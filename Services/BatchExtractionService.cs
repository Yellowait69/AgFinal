using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        public void PerformBatchExtraction(string filePath, string env, Action<BatchProgressInfo> onProgressUpdate, bool isDemandId = false)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("The specified CSV file could not be found.", filePath);

            StringBuilder globalCombined = new StringBuilder();
            List<string> processedTestIds = new List<string>();

            // NOUVEAU : Ouverture avec FileShare.ReadWrite pour permettre la lecture même si le fichier est ouvert dans Excel
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIndex = -1, testIdIndex = -1; // premiumIndex a été supprimé
                bool headerFound = false;

                // --- 1. RECHERCHE INTELLIGENTE DE L'EN-TÊTE ---
                // Le programme scanne chaque ligne jusqu'à trouver la vraie ligne d'en-têtes
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("\uFEFF")) line = line.Substring(1); // Nettoyage BOM
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Si c'est la ligne de titre parasite générée par votre export, on l'ignore de force !
                    if (line.Replace("\"", "").TrimStart().StartsWith("Contract in", StringComparison.OrdinalIgnoreCase))
                        continue;

                    delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
                    var cols = ParseCsvLine(line, delimiter);

                    for (int i = 0; i < cols.Count; i++)
                    {
                        string h = cols[i].Trim().ToLower();

                        // On vérifie si la colonne contient nos mots clés (value, demand, contract...)
                        if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa") || h.Contains("value") || h.Contains("demand")) contractIndex = i;
                        // La recherche de la colonne "premium" a été supprimée ici
                        if (h.Contains("test") || h.Contains("id test") || h.Contains("idtest")) testIdIndex = i;
                    }

                    // Si on a trouvé la colonne d'identifiant principal, c'est qu'on a le vrai en-tête ! On arrête la recherche.
                    if (contractIndex != -1)
                    {
                        headerFound = true;
                        break;
                    }
                }

                if (!headerFound)
                    throw new Exception("Impossible de trouver la colonne 'Value', 'Demand' ou 'Contract' dans le fichier CSV.");

                // --- 2. LECTURE DES DONNÉES ---
                // Maintenant qu'on connait la bonne colonne, on lit le reste du fichier (les vraies données)
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line, delimiter);

                    if (columns.Count > contractIndex)
                    {
                        // On nettoie les éventuels caractères parasites (comme les guillemets ou le =)
                        string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();
                        // La lecture de premiumAmount depuis le CSV a été supprimée ici

                        string testId = (testIdIndex != -1 && columns.Count > testIdIndex)
                            ? columns[testIdIndex].Replace("=", "").Trim()
                            : contractNumber;

                        // Si la case contient bien un numéro (et pas juste du vide), on lance l'extraction
                        if (!string.IsNullOrEmpty(contractNumber))
                        {
                            try
                            {
                                // On transmet le booléen isDemandId au service d'extraction
                                ExtractionResult result = _extractionService.PerformExtraction(contractNumber, env, false, isDemandId);

                                // Si c'était un Demand ID, on récupère le vrai numéro de contrat renvoyé par le service
                                string displayContract = isDemandId && !string.IsNullOrEmpty(result.ContractReference)
                                    ? result.ContractReference
                                    : contractNumber;

                                if (!string.IsNullOrWhiteSpace(result.LisaContent) || !string.IsNullOrWhiteSpace(result.EliaContent))
                                {
                                    globalCombined.AppendLine(new string('=', 80));
                                    globalCombined.AppendLine($"### GLOBAL CONTRACT REPORT: {displayContract} | TEST ID: {testId} | ENV: {env} ###");
                                    globalCombined.AppendLine(new string('=', 80));

                                    if (!string.IsNullOrWhiteSpace(result.LisaContent))
                                    {
                                        globalCombined.AppendLine($"--- LISA SECTION ---");
                                        globalCombined.Append(result.LisaContent).AppendLine();
                                    }

                                    if (!string.IsNullOrWhiteSpace(result.EliaContent))
                                    {
                                        globalCombined.AppendLine($"--- ELIA SECTION (UCON: {result.UconId}) ---");
                                        globalCombined.Append(result.EliaContent).AppendLine();
                                    }
                                }

                                processedTestIds.Add(testId);

                                onProgressUpdate?.Invoke(new BatchProgressInfo
                                {
                                    ContractId = displayContract,
                                    InternalId = result.InternalId,
                                    Product = env, // On affiche bien D000 ou Q000
                                    // NOUVEAU: On va chercher la prime TOUJOURS dans le résultat SQL (result.Premium)
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
                                    ContractId = $"{contractNumber} (FAILED)",
                                    InternalId = "Error",
                                    Product = env, // On affiche l'environnement même en cas d'erreur
                                    Premium = "0", // 0 par défaut en cas d'erreur
                                    UconId = "Error",
                                    DemandId = "Error",
                                    Status = ex.Message.ToLower().Contains("not found") ? "Not found in DB" : "SQL Error"
                                });
                            }
                        }
                    }
                }
            }

            // --- 3. SAUVEGARDE DU RAPPORT GLOBAL ---
            string fileSignature = "NoContract";
            string sizeTag = "Big";

            if (processedTestIds.Count > 0)
            {
                var firstThree = processedTestIds.Take(3).Select(c => c.Replace(" ", ""));
                fileSignature = string.Join("_", firstThree);

                if (processedTestIds.Count > 3)
                    fileSignature += $"_#{processedTestIds.Count - 3}other";
                else if (processedTestIds.Count == 1)
                    sizeTag = "Uniq";
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(Settings.OutputDir);

            char envLetter = !string.IsNullOrEmpty(env) ? char.ToUpper(env[0]) : 'U';
            string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_{sizeTag}_{fileSignature}_{timestamp}.csv");

            // NOUVEAU : On gère l'erreur au cas où un rapport portant le même nom serait déjà ouvert dans Excel
            string contentToWrite = globalCombined.Length > 0 ? globalCombined.ToString() : "NO CONTRACT FOUND.";
            try
            {
                File.WriteAllText(combinedPath, contentToWrite, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Si le fichier est bloqué, on ajoute un suffixe aléatoire pour forcer la sauvegarde sans crasher
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