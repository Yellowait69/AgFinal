using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActivator.Services
{
    public class BatchActivationService
    {
        private readonly ActivationDataService _dataService;

        public BatchActivationService(ActivationDataService dataService)
        {
            _dataService = dataService;
        }

        public async Task<(int successCount, int errorCount, string reportPath)> RunBatchAsync(
            string filePath, bool isDemandId, string envValue, string cus, string bucp, string cmdpmt,
            string username, string password, string outputDir, Action<string> onProgress, CancellationToken token)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("=== RAPPORT D'ACTIVATION BATCH ===");
            report.AppendLine($"Date de lancement: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Configuration Globale -> Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt}\n");
            report.AppendLine("---------------------------------------------------------------------------------------------------------");

            int successCount = 0, errorCount = 0;

            // Utilisation de FileStream avec FileShare.ReadWrite pour pouvoir lire même si ouvert dans Excel
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIdx = -1;
                bool headerFound = false;

                // --- 1. RECHERCHE INTELLIGENTE DE L'EN-TÊTE ---
                // Le programme scanne chaque ligne jusqu'à trouver la vraie ligne d'en-têtes
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("\uFEFF")) line = line.Substring(1); // Nettoyage BOM
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Si c'est la ligne de titre parasite générée par un export, on l'ignore de force !
                    if (line.Replace("\"", "").TrimStart().StartsWith("Contract in", StringComparison.OrdinalIgnoreCase))
                        continue;

                    delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
                    var headers = ParseCsvLine(line, delimiter);

                    for (int i = 0; i < headers.Count; i++)
                    {
                        string h = headers[i].Trim().ToLower();
                        if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa") || h.Contains("value") || h.Contains("demand"))
                        {
                            contractIdx = i;
                        }
                    }

                    // Si on a trouvé la colonne d'identifiant principal, on arrête la recherche.
                    if (contractIdx != -1)
                    {
                        headerFound = true;
                        break;
                    }
                }

                if (!headerFound)
                    throw new Exception("Colonne de contrat ou de Demand introuvable dans le fichier.");

                // --- 2. LECTURE DES DONNÉES ---
                int rowNum = 1;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (token.IsCancellationRequested) break;

                    var columns = ParseCsvLine(line, delimiter);
                    if (columns.Count <= contractIdx) continue;

                    string rawInput = columns[contractIdx].Replace("=", "").Replace("\"", "").Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

                    // NOUVEAU : On ignore les lignes vides OU la ligne parasite "End of File"
                    if (string.IsNullOrEmpty(rawInput) || rawInput.Equals("End of File", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string resolvedContract = rawInput;

                    if (isDemandId)
                    {
                        onProgress($"Batch en cours: Résolution Demand ID {rawInput} (Ligne {rowNum})...");
                        resolvedContract = await _dataService.GetContractFromDemandAsync(rawInput, envValue + "000");

                        if (string.IsNullOrEmpty(resolvedContract))
                        {
                            report.AppendLine($"[ÉCHEC]  Input: {rawInput} | Env: {envValue} | Erreur: Aucun contrat associé à ce Demand ID en base.");
                            errorCount++;
                            rowNum++;
                            continue;
                        }
                    }

                    onProgress($"Batch en cours: Recherche prime pour {resolvedContract} (Ligne {rowNum})...");
                    string amount = await _dataService.FetchPremiumAsync(resolvedContract, envValue + "000");
                    string formattedContract = _dataService.FormatContractForJcl(resolvedContract);

                    onProgress($"Batch en cours: Activation de {formattedContract} (Ligne {rowNum++})...");

                    try
                    {
                        await _dataService.ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, username, password, onProgress, token);
                        report.AppendLine($"[SUCCÈS] Input: {rawInput} -> Contrat JCL: {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount}");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"[ÉCHEC]  Input: {rawInput} -> Contrat JCL: {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Erreur: {ex.Message}");
                        errorCount++;
                    }
                }
            }

            report.AppendLine("---------------------------------------------------------------------------------------------------------");
            report.AppendLine($"FIN DU TRAITEMENT. Succès: {successCount} | Échecs: {errorCount}");

            string reportPath = Path.Combine(outputDir, $"Activation_Batch_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(reportPath, report.ToString());

            return (successCount, errorCount, reportPath);
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
                    else { inQuotes = !inQuotes; }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else { currentField.Append(c); }
            }
            result.Add(currentField.ToString());
            return result;
        }
    }
}