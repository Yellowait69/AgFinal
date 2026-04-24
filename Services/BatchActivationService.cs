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
    /// PHASE 1: Orchestrates parallel database lookups (SQL Server).
    /// PHASE 2: Consolidates valid contracts into a single file and executes ONE Mainframe sequence via FTP.
    /// </summary>
    public class BatchActivationService
    {
        private readonly ActivationDataService _dataService;
        private readonly MicroFocusApiService _apiService;
        private readonly JclProcessorService _jclProcessor;

        // DSN Mainframe de destination pour le transfert de masse (Bulk)
        private readonly string _mainframeDatasetName = "FILES.FIBE.FORTIS.BULK.INPUT";

        public BatchActivationService(ActivationDataService dataService)
        {
            _dataService = dataService;
            _apiService = new MicroFocusApiService();

            // CORRECTION : On pointe vers le lecteur réseau contenant les JCL
            string jclDir = @"\\Jafile02\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\LVCHAIN\JCL";

            if (!Directory.Exists(jclDir))
            {
                throw new Exception($"[CRITICAL] JCL network folder is inaccessible: {jclDir}. Please check your VPN or network connection.");
            }

            _jclProcessor = new JclProcessorService(jclDir);
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

            // Liste Thread-Safe pour les contrats validés par la base de données (prêts pour le Bulk Mainframe)
            var validContractsForBulk = new ConcurrentBag<(int RowNum, string RawInput, string FormattedContract, string Amount)>();

            // =========================================================================================
            // 1. OPTIMISATION : LECTURE DU FICHIER CSV
            // =========================================================================================
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIdx = -1;
                bool headerFound = false;

                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
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

            int totalItems = contractsToProcess.Count;
            int processedItems = 0;

            // =========================================================================================
            // 2. PHASE 1 : INTERROGATION BASE DE DONNÉES EN PARALLÈLE
            // =========================================================================================
            var dbSemaphore = new SemaphoreSlim(30);

            onProgress($"[PHASE 1] Starting parallel DB validation... (0 / {totalItems} contracts)");

            var tasks = contractsToProcess.Select((item, index) => Task.Run(async () =>
            {
                await dbSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    string resolvedContract = item.rawInput;

                    if (isDemandId)
                    {
                        resolvedContract = await _dataService.GetContractFromDemandAsync(item.rawInput, envValue + "000").ConfigureAwait(false);

                        if (string.IsNullOrEmpty(resolvedContract))
                        {
                            globalReport.Add((item.rowNum, $"[FAILED]  Line {item.rowNum,-4} | Input: {item.rawInput} | DB Error: No contract associated with this Demand ID."));
                            Interlocked.Increment(ref errorCount);

                            int currentErr = Interlocked.Increment(ref processedItems);
                            onProgress($"[{currentErr} / {totalItems} completed] DB Failure : {item.rawInput}");
                            return;
                        }
                    }

                    string amount = await _dataService.FetchPremiumAsync(resolvedContract, envValue + "000").ConfigureAwait(false);
                    string formattedContract = _dataService.FormatContractForJcl(resolvedContract);

                    // Au lieu d'appeler le Mainframe, on stocke les données validées par la BDD
                    validContractsForBulk.Add((item.rowNum, item.rawInput, formattedContract, amount));

                    int currentSuccess = Interlocked.Increment(ref processedItems);
                    onProgress($"[{currentSuccess} / {totalItems} completed] DB Validated : {formattedContract}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    globalReport.Add((item.rowNum, $"[FAILED]  Line {item.rowNum,-4} | Input: {item.rawInput} | DB Error: {ex.Message}"));
                    Interlocked.Increment(ref errorCount);

                    int currentFail = Interlocked.Increment(ref processedItems);
                    onProgress($"[{currentFail} / {totalItems} completed] Failed : {item.rawInput}");
                }
                finally
                {
                    dbSemaphore.Release();
                }
            }, token)).ToList();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                onProgress("Activation batch cancelled by user. Generating partial report...");
                globalReport.Add((0, "\n[WARNING] BATCH ACTIVATION WAS CANCELLED BY THE USER. THE FOLLOWING REPORT IS INCOMPLETE.\n"));
            }

            // =========================================================================================
            // 3. PHASE 2 : EXÉCUTION BATCH MAINFRAME
            // =========================================================================================
            string localTempFilePath = null;
            var contractsToUpload = validContractsForBulk.ToList();

            if (contractsToUpload.Any() && !token.IsCancellationRequested)
            {
                try
                {
                    onProgress($"[PHASE 2] Connecting to Mainframe for Bulk Execution ({contractsToUpload.Count} valid contracts)...");
                    bool isLogged = await _apiService.LogonAsync(username, password, envValue, onProgress, token).ConfigureAwait(false);
                    if (!isLogged) throw new Exception("Unable to connect to the MicroFocus server.");

                    // 3.A Création du fichier physique
                    localTempFilePath = GenerateLocalBulkFile(contractsToUpload);

                    // 3.B Transfert FTP vers FILES.FIBE.FORTIS
                    onProgress("[PHASE 2] Uploading massive data file via FTP...");
                    await UploadBulkFileToMainframeAsync(localTempFilePath, token).ConfigureAwait(false);

                    // 3.C Lancement de la séquence JCL globale
                    onProgress("[PHASE 2] Executing Single Batch JCL Sequence...");
                    var variables = new Dictionary<string, string>
                    {
                        { "ENV", envValue }, { "CUS", cus }, { "BUCP", bucp },
                        { "CMDPMT", cmdpmt }, { "CHANNEL", channel },
                        { "USERNAME", username }, { "CLASS", "A" }
                    };

                    await RunBatchActivationSequenceAsync(variables, skipPrime, onProgress, token).ConfigureAwait(false);

                    successCount += contractsToUpload.Count;
                    foreach (var c in contractsToUpload)
                    {
                        globalReport.Add((c.RowNum, $"[SUCCESS] Line {c.RowNum,-4} | Input: {c.RawInput} -> Activated via Bulk. Env: {envValue} | Amount: {c.Amount}"));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    foreach (var c in contractsToUpload)
                    {
                        globalReport.Add((c.RowNum, $"[FAILED]  Line {c.RowNum,-4} | Input: {c.RawInput} | BATCH MAINFRAME CRASH: {ex.Message}"));
                        errorCount++;
                    }
                    onProgress($"[CRITICAL ERROR] Batch Mainframe execution failed: {ex.Message}");
                }
                finally
                {
                    if (localTempFilePath != null && File.Exists(localTempFilePath))
                        File.Delete(localTempFilePath);
                }
            }

            // =========================================================================================
            // 4. GÉNÉRATION DU RAPPORT FINAL
            // =========================================================================================
            if (!token.IsCancellationRequested)
            {
                onProgress("All contracts have been processed. Generating the report...");
            }

            string skipSuffix = skipPrime ? "_SKIP_PRIME" : "";
            string cancelSuffix = token.IsCancellationRequested ? "_PARTIAL_CANCELLED" : "";
            string reportPath = Path.Combine(outputDir, $"Activation_Batch{skipSuffix}{cancelSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            try
            {
                using (StreamWriter writer = new StreamWriter(reportPath, false, Encoding.UTF8))
                {
                    await writer.WriteLineAsync("=== BATCH ACTIVATION REPORT (SECURE PARALLEL & BULK MODE) ===").ConfigureAwait(false);
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

            token.ThrowIfCancellationRequested();
            onProgress("Report generated successfully!");

            return (successCount, alreadyActiveCount, errorCount, reportPath);
        }

        // =========================================================================================
        // MÉTHODES UTILITAIRES POUR LE MAINFRAME ET LE CSV
        // =========================================================================================

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

        private string GenerateLocalBulkFile(List<(int RowNum, string RawInput, string FormattedContract, string Amount)> contracts)
        {
            string tempFileName = $"BULK_ACT_{Guid.NewGuid():N}.TXT";
            string filePath = Path.Combine(Path.GetTempPath(), tempFileName);

            var sb = new StringBuilder();
            foreach (var c in contracts)
            {
                sb.AppendLine($"{c.FormattedContract.PadRight(20)};{c.Amount.PadLeft(15, '0')}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }

        private async Task UploadBulkFileToMainframeAsync(string localFilePath, CancellationToken cancellationToken)
        {
            string ftpJcl = _jclProcessor.GenerateFtpJcl(
                DsnDirection.Read, _mainframeDatasetName, localFilePath, TransferMode.Text);

            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(ftpJcl, cancellationToken).ConfigureAwait(false);
            if (!Success) throw new Exception($"FTP Upload Error: {Error}");

            for (int i = 0; i < 60; i++) // Timeout 2 min
            {
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken).ConfigureAwait(false);

                if (Status == "Complete" || Status == "Done" || Status == "Output") return;
                if (Status == "JCLError" || Status == "Abend") throw new Exception("Crash of FTP Transfer Job.");
            }
            throw new Exception("Timeout waiting for FTP.");
        }

        private async Task RunBatchActivationSequenceAsync(Dictionary<string, string> variables, bool skipPrime, Action<string> onProgress, CancellationToken cancellationToken)
        {
            int jobCounter = 1;

            if (!skipPrime)
            {
                await ProcessSingleJobWaitAsync("ADDPRCT", variables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
                await ProcessSingleJobWaitAsync("LVPP06U", variables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
                await ProcessSingleJobWaitAsync("LVPG22U", variables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
            }

            await ProcessSingleJobWaitAsync("LI1J04D0", variables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
            await ProcessSingleJobWaitAsync("LI1J04D2", variables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> ProcessSingleJobWaitAsync(string jobName, Dictionary<string, string> variables, int count, Action<string> onProgress, CancellationToken cancellationToken)
        {
            variables["JOBNAM"] = jobName;
            variables["AP"] = (jobName.StartsWith("LV")) ? "LV" : "LI";

            string readyContent = await _jclProcessor.GetPreparedJclAsync(jobName, variables, count).ConfigureAwait(false);

            if (jobName == "LVPG22U" && readyContent.Contains("//ICEGENER IF"))
                readyContent = readyContent.Substring(0, readyContent.IndexOf("//ICEGENER IF"));

            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(readyContent, cancellationToken).ConfigureAwait(false);
            if (!Success) throw new Exception($"JCL {jobName} Submission Error: {Error}");

            onProgress($"   -> [Running] {jobName} ({JobNum}) ...");

            for (int i = 0; i < 150; i++) // Timeout 5 min par Job
            {
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken).ConfigureAwait(false);

                if (Status == "JCLError" || Status == "Abend" || Status == "Failed")
                    throw new Exception($"System Crash on {jobName}. Chain interrupted.");

                if (Status == "Complete" || Status == "Done" || Status == "Output")
                {
                    string cleanRC = ReturnCode.TrimStart('0');
                    if (string.IsNullOrEmpty(cleanRC)) cleanRC = "0";

                    if (cleanRC != "0" && cleanRC != "4")
                        throw new Exception($"Business error (RC={ReturnCode}) on {jobName}.");

                    onProgress($"      [OK] {jobName} completed (RC={ReturnCode}).");
                    return JobNum;
                }
            }
            throw new Exception($"Timeout on {jobName}.");
        }
    }
}