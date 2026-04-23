using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActivator.Services
{
    /// <summary>
    /// Service responsible for processing bulk contract activations from a CSV file.
    /// It orchestrates parallel database lookups while throttling Mainframe API submissions
    /// to prevent overloading the host system.
    /// </summary>
    public class BatchActivationService
    {
        private readonly ActivationDataService _dataService;

        public BatchActivationService(ActivationDataService dataService)
        {
            _dataService = dataService;
        }

        public async Task<(int successCount, int alreadyActiveCount, int errorCount, string reportPath)> RunBatchAsync(
            string filePath, bool isDemandId, string envValue, string cus, string bucp, string cmdpmt, string channel, bool skipPrime,
            string username, string password, string outputDir, Action<string> onProgress, CancellationToken token)
        {
            ServicePointManager.DefaultConnectionLimit = 500;
            ServicePointManager.Expect100Continue = false;

            int successCount = 0;
            int alreadyActiveCount = 0;
            int errorCount = 0;

            var globalReport = new ConcurrentBag<(int RowNum, string Message)>();
            var contractsToProcess = new List<(string rawInput, int rowNum)>();

            // 1. OPTIMISATION : ARRÊT DE LA LECTURE DU FICHIER SI ANNULÉ
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIdx = -1;
                bool headerFound = false;

                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    // Vérification de l'annulation pendant l'analyse des en-têtes
                    token.ThrowIfCancellationRequested();

                    if (line.StartsWith("\uFEFF")) line = line.Substring(1);
                    if (string.IsNullOrWhiteSpace(line)) continue;

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

                    if (contractIdx != -1) { headerFound = true; break; }
                }

                if (!headerFound)
                    throw new Exception("Contract or Demand column not found in the input file.");

                int currentRow = 1;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    // Vérification de l'annulation pendant le chargement des données massives
                    token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line, delimiter);
                    if (columns.Count <= contractIdx) continue;

                    string rawInput = columns[contractIdx].Replace("=", "").Replace("\"", "").Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

                    if (string.IsNullOrEmpty(rawInput) || rawInput.Equals("End of File", StringComparison.OrdinalIgnoreCase))
                        continue;

                    contractsToProcess.Add((rawInput, currentRow++));
                }
            }

            var semaphore = new SemaphoreSlim(1);

            int totalItems = contractsToProcess.Count;
            int processedItems = 0;

            onProgress($"Starting parallel DB processing... (0 / {totalItems} contracts)");

            var tasks = contractsToProcess.Select((item, index) => Task.Run(async () =>
            {
                try
                {
                    // Utilisation stricte du ThrowIfCancellationRequested au lieu du return silencieux
                    token.ThrowIfCancellationRequested();

                    string resolvedContract = item.rawInput;

                    onProgress($"[{processedItems} / {totalItems} completed] DB Search : {item.rawInput}...");

                    if (isDemandId)
                    {
                        resolvedContract = await _dataService.GetContractFromDemandAsync(item.rawInput, envValue + "000").ConfigureAwait(false);

                        if (string.IsNullOrEmpty(resolvedContract))
                        {
                            globalReport.Add((item.rowNum, $"[FAILED]  Line {item.rowNum,-4} | Input: {item.rawInput} | Error: No contract associated with this Demand ID in the database."));
                            Interlocked.Increment(ref errorCount);

                            int currentErr = Interlocked.Increment(ref processedItems);
                            onProgress($"[{currentErr} / {totalItems} completed] DB Failure : {item.rawInput}");
                            return;
                        }
                    }

                    string amount = await _dataService.FetchPremiumAsync(resolvedContract, envValue + "000").ConfigureAwait(false);
                    string formattedContract = _dataService.FormatContractForJcl(resolvedContract);

                    await semaphore.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        token.ThrowIfCancellationRequested();

                        onProgress($"[{processedItems} / {totalItems} completed] Sending to Mainframe API : {formattedContract}...");

                        await _dataService.ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, channel, skipPrime, username, password, msg => { }, token).ConfigureAwait(false);

                        globalReport.Add((item.rowNum, $"[SUCCESS] Line {item.rowNum,-4} | Input: {item.rawInput} -> JCL Contract: {formattedContract} | Env: {envValue} | Amount: {amount}"));
                        Interlocked.Increment(ref successCount);

                        int currentSuccess = Interlocked.Increment(ref processedItems);
                        onProgress($"[{currentSuccess} / {totalItems} completed] Success : {formattedContract}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    // 2. CAPTURE DE L'ANNULATION POUR ÉVITER LES FAUX RAPPORTS D'ERREURS
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex.Message == "ALREADY_ACTIVE")
                    {
                        globalReport.Add((item.rowNum, $"[ALREADY ACTIVE] Line {item.rowNum,-4} | Input: {item.rawInput} | The contract is already activated (Error 008)."));
                        Interlocked.Increment(ref alreadyActiveCount);

                        int currentAct = Interlocked.Increment(ref processedItems);
                        onProgress($"[{currentAct} / {totalItems} completed] Already active : {item.rawInput}");
                    }
                    else
                    {
                        globalReport.Add((item.rowNum, $"[FAILED]  Line {item.rowNum,-4} | Input: {item.rawInput} | Error: {ex.Message}"));
                        Interlocked.Increment(ref errorCount);

                        int currentFail = Interlocked.Increment(ref processedItems);
                        onProgress($"[{currentFail} / {totalItems} completed] Failed : {item.rawInput}");
                    }
                }
            }, token)).ToList();

            // 3. CAPTURE DE L'ANNULATION GLOBALE (POUR GÉNÉRER UN RAPPORT PARTIEL)
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                onProgress("All contracts have been processed. Generating the report...");
            }
            catch (OperationCanceledException)
            {
                onProgress("Activation batch cancelled by user. Generating partial report...");
                globalReport.Add((0, "\n[WARNING] BATCH ACTIVATION WAS CANCELLED BY THE USER. THE FOLLOWING REPORT IS INCOMPLETE.\n"));
            }

            string skipSuffix = skipPrime ? "_SKIP_PRIME" : "";
            string cancelSuffix = token.IsCancellationRequested ? "_PARTIAL_CANCELLED" : "";
            string reportPath = Path.Combine(outputDir, $"Activation_Batch{skipSuffix}{cancelSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            try
            {
                using (StreamWriter writer = new StreamWriter(reportPath, false, Encoding.UTF8))
                {
                    await writer.WriteLineAsync("=== BATCH ACTIVATION REPORT (SECURE PARALLEL MODE) ===").ConfigureAwait(false);
                    await writer.WriteLineAsync($"Launch date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}").ConfigureAwait(false);

                    if (skipPrime) await writer.WriteLineAsync("MODE: SKIP PRIME ACTIVE (Bypassing ADDPRCT, LVPP06U, LVPG22U)").ConfigureAwait(false);

                    await writer.WriteLineAsync($"Global Configuration -> Env: {envValue} | Channel: {channel} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt}\n").ConfigureAwait(false);
                    await writer.WriteLineAsync("---------------------------------------------------------------------------------------------------------").ConfigureAwait(false);

                    foreach (var log in globalReport.OrderBy(x => x.RowNum))
                    {
                        await writer.WriteLineAsync(log.Message).ConfigureAwait(false);
                    }

                    await writer.WriteLineAsync("---------------------------------------------------------------------------------------------------------").ConfigureAwait(false);

                    if (token.IsCancellationRequested)
                        await writer.WriteLineAsync($"PROCESSING INTERRUPTED. Successes: {successCount} | Already active: {alreadyActiveCount} | Failures: {errorCount}").ConfigureAwait(false);
                    else
                        await writer.WriteLineAsync($"END OF PROCESSING. Successes: {successCount} | Already active: {alreadyActiveCount} | Failures: {errorCount}").ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                reportPath = Path.Combine(outputDir, $"Activation_Batch{skipSuffix}{cancelSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 4)}.txt");

                using (StreamWriter fallbackWriter = new StreamWriter(reportPath, false, Encoding.UTF8))
                {
                    await fallbackWriter.WriteLineAsync("=== BATCH ACTIVATION REPORT (FALLBACK MODE) ===").ConfigureAwait(false);
                    foreach (var log in globalReport.OrderBy(x => x.RowNum))
                    {
                        await fallbackWriter.WriteLineAsync(log.Message).ConfigureAwait(false);
                    }
                    await fallbackWriter.WriteLineAsync($"PROCESSING RESULTS. Successes: {successCount} | Failures: {errorCount}").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                onProgress($"CRITICAL ERROR: Unable to save the report ({ex.Message})");
                throw;
            }

            // Remonter l'annulation à l'UI une fois le rapport de sauvegarde effectué
            token.ThrowIfCancellationRequested();

            onProgress("Report generated successfully!");

            return (successCount, alreadyActiveCount, errorCount, reportPath);
        }

        /// <summary>
        /// Custom CSV parser to handle fields that contain the delimiter character inside quotes.
        /// </summary>
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