using System;
using System.Collections.Concurrent;
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

        private const int MAX_PARALLEL = 10;

        public BatchActivationService(ActivationDataService dataService)
        {
            _dataService = dataService;
        }

        public async Task<(int successCount, int errorCount, string reportPath)> RunBatchAsync(
            string filePath,
            bool isDemandId,
            string envValue,
            string cus,
            string bucp,
            string cmdpmt,
            string username,
            string password,
            string outputDir,
            Action<string> onProgress,
            CancellationToken token)
        {
            var reportBuilder = CreateReportHeader(envValue, cus, bucp, cmdpmt);

            int successCount = 0;
            int errorCount = 0;
            int processedItems = 0;

            var globalReport = new ConcurrentBag<(int RowNum, string Message)>();

            var contracts = await LoadContractsFromFile(filePath);

            int totalItems = contracts.Count;

            await ValidateSession(username, password, envValue, token, onProgress);

            onProgress($"Session active. Démarrage du batch (0 / {totalItems})...");

            using var semaphore = new SemaphoreSlim(MAX_PARALLEL);

            var tasks = contracts.Select(async item =>
            {
                await semaphore.WaitAsync(token);

                try
                {
                    await ProcessContract(
                        item,
                        isDemandId,
                        envValue,
                        cus,
                        bucp,
                        cmdpmt,
                        username,
                        password,
                        globalReport,
                        () => Interlocked.Increment(ref successCount),
                        () => Interlocked.Increment(ref errorCount),
                        token);
                }
                finally
                {
                    int current = Interlocked.Increment(ref processedItems);
                    onProgress($"Activation en cours : {current} / {totalItems}...");
                    semaphore.Release();
                }

            });

            await Task.WhenAll(tasks);

            BuildFinalReport(reportBuilder, globalReport, successCount, errorCount);

            string reportPath = SaveReport(reportBuilder, outputDir);

            return (successCount, errorCount, reportPath);
        }

        private async Task ProcessContract(
            (string rawInput, int rowNum) item,
            bool isDemandId,
            string envValue,
            string cus,
            string bucp,
            string cmdpmt,
            string username,
            string password,
            ConcurrentBag<(int RowNum, string Message)> report,
            Action incrementSuccess,
            Action incrementError,
            CancellationToken token)
        {
            try
            {
                string resolvedContract = item.rawInput;

                if (isDemandId)
                {
                    resolvedContract = await _dataService.GetContractFromDemandAsync(
                        item.rawInput,
                        envValue + "000");

                    if (string.IsNullOrEmpty(resolvedContract))
                    {
                        report.Add((item.rowNum,
                            $"[ÉCHEC]  Ligne {item.rowNum,-4} | Input: {item.rawInput} | Aucun contrat associé."));
                        incrementError();
                        return;
                    }
                }

                string amount = await _dataService.FetchPremiumAsync(resolvedContract, envValue + "000");

                string formattedContract = _dataService.FormatContractForJcl(resolvedContract);

                await _dataService.ExecuteActivationSequenceAsync(
                    formattedContract,
                    amount,
                    envValue,
                    cus,
                    bucp,
                    cmdpmt,
                    username,
                    password,
                    msg => { },
                    token,
                    true);

                report.Add((item.rowNum,
                    $"[SUCCÈS] Ligne {item.rowNum,-4} | Input: {item.rawInput} -> JCL: {formattedContract} | Amount: {amount}"));

                incrementSuccess();
            }
            catch (Exception ex)
            {
                report.Add((item.rowNum,
                    $"[ÉCHEC]  Ligne {item.rowNum,-4} | Input: {item.rawInput} | Erreur: {ex.Message}"));

                incrementError();
            }
        }

        private async Task<List<(string rawInput, int rowNum)>> LoadContractsFromFile(string filePath)
        {
            var contracts = new List<(string rawInput, int rowNum)>();

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            string line;
            bool headerFound = false;
            int contractIndex = -1;
            char delimiter = ';';
            int row = 1;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("\uFEFF"))
                    line = line[1..];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';

                var headers = ParseCsvLine(line, delimiter);

                for (int i = 0; i < headers.Count; i++)
                {
                    string h = headers[i].Trim().ToLower();

                    if (h.Contains("contract") ||
                        h.Contains("contrat") ||
                        h.Contains("lisa") ||
                        h.Contains("value") ||
                        h.Contains("demand"))
                    {
                        contractIndex = i;
                    }
                }

                if (contractIndex != -1)
                {
                    headerFound = true;
                    break;
                }
            }

            if (!headerFound)
                throw new Exception("Colonne contrat introuvable.");

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var columns = ParseCsvLine(line, delimiter);

                if (columns.Count <= contractIndex)
                    continue;

                string value = columns[contractIndex]
                    .Replace("=", "")
                    .Replace("\"", "")
                    .Replace("\u00A0", "")
                    .Replace("\uFEFF", "")
                    .Trim();

                if (string.IsNullOrEmpty(value) ||
                    value.Equals("End of File", StringComparison.OrdinalIgnoreCase))
                    continue;

                contracts.Add((value, row++));
            }

            return contracts;
        }

        private async Task ValidateSession(
            string username,
            string password,
            string envValue,
            CancellationToken token,
            Action<string> progress)
        {
            progress("Connexion au mainframe...");

            var api = new MicroFocusApiService();

            bool success = await api.LogonAsync(username, password, envValue, msg => { }, token);

            if (!success)
                throw new Exception("Échec authentification MicroFocus.");
        }

        private StringBuilder CreateReportHeader(string env, string cus, string bucp, string cmdpmt)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== RAPPORT D'ACTIVATION BATCH ===");
            sb.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Env: {env} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt}");
            sb.AppendLine("------------------------------------------------------------------");

            return sb;
        }

        private void BuildFinalReport(
            StringBuilder report,
            ConcurrentBag<(int RowNum, string Message)> logs,
            int success,
            int errors)
        {
            foreach (var log in logs.OrderBy(x => x.RowNum))
                report.AppendLine(log.Message);

            report.AppendLine("------------------------------------------------------------------");
            report.AppendLine($"TERMINÉ | Succès: {success} | Échecs: {errors}");
        }

        private string SaveReport(StringBuilder report, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string path = Path.Combine(
                outputDir,
                $"Activation_Batch_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            File.WriteAllText(path, report.ToString());

            return path;
        }

        private List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var field = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            result.Add(field.ToString());

            return result;
        }
    }
}