using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

            // On garde tous les contrats sans les filtrer, en conservant le Test ID brut complet (avec ProcessID)
            List<(string contractNumber, string rawTestId)> contractsToProcess = new List<(string, string)>();

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
                        if (h.Contains("test") || h.Contains("id test") || h.Contains("idtest") || h == "key") testIdIndex = i;
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

                        // NOUVEAU : On ignore la ligne parasite générée dans certains exports Excel
                        if (contractNumber.Equals("End of File", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string rawTestId = (testIdIndex != -1 && columns.Count > testIdIndex)
                            ? columns[testIdIndex].Replace("=", "").Replace("\"", "").Trim()
                            : contractNumber;

                        if (!string.IsNullOrEmpty(contractNumber))
                        {
                            contractsToProcess.Add((contractNumber, rawTestId));
                        }
                    }
                }
            }

            // --- 3. TRAITEMENT PARALLÈLE MASSIF ---
            var semaphore = new SemaphoreSlim(15);

            var tasks = contractsToProcess.Select(async item =>
            {
                await semaphore.WaitAsync(); // Attendre son tour dans la file
                try
                {
                    ExtractionResult result = await _extractionService.PerformExtractionAsync(item.contractNumber, env, false, isDemandId);

                    string displayContract = isDemandId && !string.IsNullOrEmpty(result.ContractReference)
                        ? result.ContractReference
                        : item.contractNumber;

                    if (!string.IsNullOrWhiteSpace(result.LisaContent) || !string.IsNullOrWhiteSpace(result.EliaContent))
                    {
                        StringBuilder localReport = new StringBuilder();
                        localReport.AppendLine(new string('=', 80));
                        // On inscrit le rawTestId complet (incluant la date) dans le rapport
                        localReport.AppendLine($"### GLOBAL CONTRACT REPORT: {displayContract} | TEST ID: {item.rawTestId} | ENV: {env} ###");
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

                    processedTestIds.Add(item.rawTestId);

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
                finally
                {
                    semaphore.Release(); // Libère la place pour le contrat suivant
                }
            });

            // Lancement de toutes les requêtes en parallèle et attente de leur fin
            await Task.WhenAll(tasks);

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
                // On isole "ID501" de "ID501_FIB_Process..." pour nommer le fichier proprement
                var firstThree = idList.Select(c => c.Split('_')[0].Replace(" ", "")).Distinct().Take(3);
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

            // Écriture du fichier de manière asynchrone pour ne pas bloquer
            try
            {
                using (StreamWriter writer = new StreamWriter(combinedPath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(contentToWrite);
                }
            }
            catch (IOException)
            {
                // Si le fichier est bloqué, on crée une copie avec un GUID aléatoire
                string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_{sizeTag}_{fileSignature}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                using (StreamWriter writer = new StreamWriter(alternativePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(contentToWrite);
                }
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