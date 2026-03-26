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

            // FIX: Use a dictionary to strictly preserve the original order via the list index
            ConcurrentDictionary<int, string> globalCombinedResults = new ConcurrentDictionary<int, string>();

            // NEW: Add the row number (rowNum) to the list to transmit it later
            List<(int rowNum, string contractNumber, string rawTestId)> contractsToProcess = new List<(int, string, string)>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIndex = -1, testIdIndex = -1;
                bool headerFound = false;

                int lineNumber = 0; // NEW: Global line counter

                // --- 1. SMART HEADER SEARCH ---
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    lineNumber++; // NEW: Increment for the first lines until the header

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

                // --- 2. READING DATA INTO MEMORY ---
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    lineNumber++; // NEW: Increment for each data line

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line, delimiter);

                    if (columns.Count > contractIndex)
                    {
                        string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();

                        // Ignore the stray line generated in some Excel exports
                        if (contractNumber.Equals("End of File", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string rawTestId = (testIdIndex != -1 && columns.Count > testIdIndex)
                            ? columns[testIdIndex].Replace("=", "").Replace("\"", "").Trim()
                            : contractNumber;

                        if (!string.IsNullOrEmpty(contractNumber))
                        {
                            // NEW: Save the exact line number
                            contractsToProcess.Add((lineNumber, contractNumber, rawTestId));
                        }
                    }
                }
            }

            // --- 3. MASSIVE PARALLEL PROCESSING ---
            // FIX: Reduced concurrency from 50 to 30 to avoid exhausting the SQL Connection Pool
            var semaphore = new SemaphoreSlim(30);

            // Initialize counters for progress tracking
            int totalItems = contractsToProcess.Count;
            int processedItems = 0;

            var tasks = contractsToProcess.Select((item, index) => Task.Run(async () =>
            {
                // Micro-delay to prevent hitting the SQL Connection Pool all at millisecond 0
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

                        // Thread-safe addition at the exact contract index (guarantees the same order as the input file)
                        globalCombinedResults.TryAdd(index, localReport.ToString());
                    }

                    int current = Interlocked.Increment(ref processedItems);

                    onProgressUpdate?.Invoke(new BatchProgressInfo
                    {
                        RowNum = item.rowNum, // NEW: Transmit the row number
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
                        RowNum = item.rowNum, // NEW: Transmit the row number in case of error
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

            // Launch all requests in parallel and wait for them to finish without blocking the UI
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // --- 4. SAVING THE GLOBAL REPORT ---
            // FIX: Reconstruct the file in the strict initial order from 0 to totalItems
            StringBuilder finalCombinedReport = new StringBuilder();
            for (int i = 0; i < totalItems; i++)
            {
                if (globalCombinedResults.TryGetValue(i, out var report))
                {
                    finalCombinedReport.Append(report);
                }
            }

            // NEW: Explicit filename based on the input file for the Smart Baseline Matcher
            string origFileName = Path.GetFileNameWithoutExtension(filePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(Settings.OutputDir);

            char envLetter = !string.IsNullOrEmpty(env) ? char.ToUpper(env[0]) : 'U';

            // Output format: "Extraction_Converted_KEY_C01..._D_20231026_143000.csv"
            string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{origFileName}_{envLetter}_{timestamp}.csv");

            string contentToWrite = finalCombinedReport.Length > 0 ? finalCombinedReport.ToString() : "NO CONTRACT FOUND.";

            try
            {
                using (StreamWriter writer = new StreamWriter(combinedPath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(contentToWrite).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{origFileName}_{envLetter}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                using (StreamWriter writer = new StreamWriter(alternativePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(contentToWrite).ConfigureAwait(false);
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