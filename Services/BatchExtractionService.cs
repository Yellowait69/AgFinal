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
    /// <summary>
    /// Service responsible for processing bulk contract extractions from a CSV file.
    /// It uses massive parallel processing and streams results to temporary files
    /// to avoid OutOfMemory (RAM) issues on large datasets.
    /// </summary>
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

            string tempDirPath = Path.Combine(Path.GetTempPath(), $"AutoActivator_Batch_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDirPath);

            List<(int rowNum, string contractNumber, string rawTestId)> contractsToProcess = new List<(int, string, string)>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIndex = -1, testIdIndex = -1;
                bool headerFound = false;

                int lineNumber = 0;

                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    lineNumber++;

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
                    throw new Exception("Unable to find 'Value', 'Demand', or 'Contract' column in the CSV file.");

                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    lineNumber++;

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line, delimiter);

                    if (columns.Count > contractIndex)
                    {
                        string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();

                        if (contractNumber.Equals("End of File", StringComparison.OrdinalIgnoreCase)) continue;

                        string rawTestId = (testIdIndex != -1 && columns.Count > testIdIndex)
                            ? columns[testIdIndex].Replace("=", "").Replace("\"", "").Trim()
                            : contractNumber;

                        if (!string.IsNullOrEmpty(contractNumber))
                        {
                            contractsToProcess.Add((lineNumber, contractNumber, rawTestId));
                        }
                    }
                }
            }

            var semaphore = new SemaphoreSlim(30);

            int totalItems = contractsToProcess.Count;
            int processedItems = 0;

            var tasks = contractsToProcess.Select((item, index) => Task.Run(async () =>
            {
                await Task.Delay(Math.Min(index * 20, 2000)).ConfigureAwait(false);

                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    ExtractionResult result = await _extractionService.PerformExtractionAsync(item.contractNumber, env, false, isDemandId).ConfigureAwait(false);

                    string displayContract = isDemandId && !string.IsNullOrEmpty(result.ContractReference)
                        ? result.ContractReference
                        : item.contractNumber;

                    if (!string.IsNullOrWhiteSpace(result.LisaContent) || !string.IsNullOrWhiteSpace(result.EliaContent))
                    {
                        StringBuilder localReport = new StringBuilder();
                        localReport.AppendLine(new string('=', 80));
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

                        string tempFile = Path.Combine(tempDirPath, $"{index}.tmp");
                        using (StreamWriter writer = new StreamWriter(tempFile, false, Encoding.UTF8))
                        {
                            await writer.WriteAsync(localReport.ToString()).ConfigureAwait(false);
                        }
                    }

                    int current = Interlocked.Increment(ref processedItems);

                    onProgressUpdate?.Invoke(new BatchProgressInfo
                    {
                        RowNum = item.rowNum,
                        ContractId = displayContract,
                        InternalId = result.InternalId,
                        Product = env,
                        Premium = string.IsNullOrWhiteSpace(result.Premium) ? "0" : result.Premium,
                        UconId = result.UconId,
                        DemandId = result.DemandId,
                        Status = "OK",
                        CurrentItem = current,
                        TotalItems = totalItems
                    });
                }
                catch (Exception ex)
                {
                    int current = Interlocked.Increment(ref processedItems);
                    onProgressUpdate?.Invoke(new BatchProgressInfo
                    {
                        RowNum = item.rowNum,
                        ContractId = $"{item.contractNumber} (FAILED)",
                        InternalId = "Error",
                        Product = env,
                        Premium = "0",
                        UconId = "Error",
                        DemandId = "Error",
                        Status = ex.Message.ToLower().Contains("not found") ? "Not found in DB" : "SQL Error",
                        CurrentItem = current,
                        TotalItems = totalItems
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            })).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            string origFileName = Path.GetFileNameWithoutExtension(filePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(Settings.OutputDir);

            char envLetter = !string.IsNullOrEmpty(env) ? char.ToUpper(env[0]) : 'U';
            string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{origFileName}_{envLetter}_{timestamp}.csv");

            try
            {
                bool anyDataWritten = false;

                using (StreamWriter finalWriter = new StreamWriter(combinedPath, false, Encoding.UTF8))
                {
                    for (int i = 0; i < totalItems; i++)
                    {
                        string tempFile = Path.Combine(tempDirPath, $"{i}.tmp");

                        if (File.Exists(tempFile))
                        {
                            anyDataWritten = true;

                            using (StreamReader tempReader = new StreamReader(tempFile, Encoding.UTF8))
                            {
                                string content = await tempReader.ReadToEndAsync().ConfigureAwait(false);
                                await finalWriter.WriteAsync(content).ConfigureAwait(false);
                            }
                        }
                    }

                    if (!anyDataWritten)
                    {
                        await finalWriter.WriteAsync("NO CONTRACT FOUND.").ConfigureAwait(false);
                    }
                }
            }
            catch (IOException)
            {
                string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{origFileName}_{envLetter}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                File.Copy(combinedPath, alternativePath, true);
            }
            finally
            {
                if (Directory.Exists(tempDirPath))
                {
                    try
                    {
                        Directory.Delete(tempDirPath, true);
                    }
                    catch { /* Silently catch exceptions in case an antivirus locks the directory temporarily */ }
                }
            }
        }

        /// <summary>
        /// Custom CSV parser to safely handle fields that contain the delimiter character inside quotes.
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