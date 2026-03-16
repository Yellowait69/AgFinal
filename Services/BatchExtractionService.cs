using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoActivator.Config;
using AutoActivator.Models;

namespace AutoActivator.Services
{
    public class BatchExtractionService
    {
        private readonly ExtractionService _extractionService;

        public BatchExtractionService(ExtractionService extractionService)
        {
            _extractionService = extractionService;
        }

        public async Task PerformBatchExtractionAsync(string filePath, string env, Action<BatchProgressInfo> onProgressUpdate, bool isDemandId = false)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("The specified CSV file could not be found.", filePath);

            // Collections "Thread-Safe" pour le parallélisme
            ConcurrentQueue<string> globalCombinedQueue = new ConcurrentQueue<string>();
            ConcurrentBag<string> processedTestIds = new ConcurrentBag<string>();

            // Liste pour stocker les contrats à traiter avant de lancer le parallélisme
            List<(string contractNumber, string testId)> contractsToProcess = new List<(string, string)>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIndex = -1, testIdIndex = -1;
                bool headerFound = false;

                // --- 1. RECHERCHE INTELLIGENTE DE L'EN-TÊTE ---
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("\uFEFF")) line = line.Substring(1);
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.Replace("\"", "").TrimStart().StartsWith("Contract in", StringComparison.OrdinalIgnoreCase))
                        continue;

                    delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
                    var cols = ParseCsvLine(line, delimiter);

                    for (int i = 0; i < cols.Count; i++)
                    {
                        string h = cols[i].Trim().ToLower();
                        if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa") || h.Contains("value") || h.Contains("demand")) contractIndex = i;
                        if (h.Contains("test") || h.Contains("id test") || h.Contains("idtest")) testIdIndex = i;
                    }

                    if (contractIndex != -1)
                    {
                        headerFound = true;
                        break;
                    }
                }

                if (!headerFound)
                    throw new Exception("Impossible de trouver la colonne 'Value', 'Demand' ou 'Contract' dans le fichier CSV.");

                // --- 2. LECTURE DES DONNÉES EN MÉMOIRE ---
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line, delimiter);

                    if (columns.Count > contractIndex)
                    {
                        string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();
                        string testId = (testIdIndex != -1 && columns.Count > testIdIndex)
                            ? columns[testIdIndex].Replace("=", "").Trim()
                            : contractNumber;

                        if (!string.IsNullOrEmpty(contractNumber))
                        {
                            contractsToProcess.Add((contractNumber, testId));
                        }
                    }
                }
            }

            // --- 3. TRAITEMENT PARALLÈLE MASSIF ---
            // MaxDegreeOfParallelism = 15 signifie que 15 contrats sont extraits SIMULTANÉMENT.
            // Vous pouvez augmenter ce chiffre à 20 ou 30 si votre base de données SQL le supporte sans ralentir.
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 15 };

            await Parallel.ForEachAsync(contractsToProcess, parallelOptions, async (item, token) =>
            {
                try
                {
                    // L'appel est maintenant ASYNCHRONE pour ne pas bloquer les threads
                    ExtractionResult result = await _extractionService.PerformExtractionAsync(item.contractNumber, env, false, isDemandId);

                    string displayContract = isDemandId && !string.IsNullOrEmpty(result.ContractReference)
                        ? result.ContractReference
                        : item.contractNumber;

                    if (!string.IsNullOrWhiteSpace(result.LisaContent) || !string.IsNullOrWhiteSpace(result.EliaContent))
                    {
                        StringBuilder localReport = new StringBuilder();
                        localReport.AppendLine(new string('=', 80));
                        localReport.AppendLine($"### GLOBAL CONTRACT REPORT: {displayContract} | TEST ID: {item.testId} | ENV: {env} ###");
                        localReport.AppendLine(new string('=', 80));

                        if (!string.IsNullOrWhiteSpace(result.LisaContent))
                        {
                            localReport.AppendLine($"--- LISA SECTION ---");
                            localReport.Append(result.LisaContent).AppendLine();
                        }

                        if (!string.IsNullOrWhiteSpace(result.EliaContent))
                        {
                            localReport.AppendLine($"--- ELIA SECTION (UCON: {result.UconId}) ---");
                            localReport.Append(result.EliaContent).AppendLine();
                        }

                        // Ajout sécurisé (Thread-Safe)
                        globalCombinedQueue.Enqueue(localReport.ToString());
                    }

                    processedTestIds.Add(item.testId);

                    onProgressUpdate?.Invoke(new BatchProgressInfo
                    {
                        ContractId = displayContract,
                        InternalId = result.InternalId,
                        Product = env,
                        Premium = string.IsNullOrWhiteSpace(result.Premium) ? "0" : result.Premium,
                        UconId = result.UconId,
                        DemandId = result.DemandId,
                        Status = "OK"
                    });
                }
                catch (Exception ex)
                {
                    onProgressUpdate?.Invoke(new BatchProgressInfo
                    {
                        ContractId = $"{item.contractNumber} (FAILED)",
                        InternalId = "Error",
                        Product = env,
                        Premium = "0",
                        UconId = "Error",
                        DemandId = "Error",
                        Status = ex.Message.ToLower().Contains("not found") ? "Not found in DB" : "SQL Error"
                    });
                }
            });

            // --- 4. SAUVEGARDE DU RAPPORT GLOBAL ---
            StringBuilder finalCombinedReport = new StringBuilder();
            foreach (var report in globalCombinedQueue)
            {
                finalCombinedReport.Append(report);
            }

            string fileSignature = "NoContract";
            string sizeTag = "Big";
            var idList = processedTestIds.ToList();

            if (idList.Count > 0)
            {
                var firstThree = idList.Take(3).Select(c => c.Replace(" ", ""));
                fileSignature = string.Join("_", firstThree);

                if (idList.Count > 3)
                    fileSignature += $"_#{idList.Count - 3}other";
                else if (idList.Count == 1)
                    sizeTag = "Uniq";
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(Settings.OutputDir);

            char envLetter = !string.IsNullOrEmpty(env) ? char.ToUpper(env[0]) : 'U';
            string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_{sizeTag}_{fileSignature}_{timestamp}.csv");

            string contentToWrite = finalCombinedReport.Length > 0 ? finalCombinedReport.ToString() : "NO CONTRACT FOUND.";
            try
            {
                File.WriteAllText(combinedPath, contentToWrite, Encoding.UTF8);
            }
            catch (IOException)
            {
                string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_{sizeTag}_{fileSignature}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                File.WriteAllText(alternativePath, contentToWrite, Encoding.UTF8);
            }
        }

        private List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            result.Add(currentField.ToString());
            return result;
        }
    }
}