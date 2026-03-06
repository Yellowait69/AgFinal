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

        // The method takes 'env' (e.g., "D000" or "Q000") to target the correct database
        public void PerformBatchExtraction(string filePath, string env, Action<BatchProgressInfo> onProgressUpdate)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("The specified CSV file could not be found.", filePath);

            // Replacing the two StringBuilders with a single one
            StringBuilder globalCombined = new StringBuilder();

            List<string> processedTestIds = new List<string>();

            // LINE-BY-LINE READING WITH STREAMREADER (Resolves RAM issue)
            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                string headerLine = reader.ReadLine();

                // Cleaning up any potential BOM (Byte Order Mark) at the beginning of the file
                if (headerLine != null && headerLine.StartsWith("\uFEFF"))
                    headerLine = headerLine.Substring(1);

                if (string.IsNullOrWhiteSpace(headerLine))
                    throw new Exception("The CSV file is empty or contains only the header.");

                // Automatic detection of the most frequent delimiter in the header
                char delimiter = headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';

                // Parsing the header with the new robust function
                var headers = ParseCsvLine(headerLine, delimiter);
                int contractIndex = 0, premiumIndex = -1, productIndex = -1, testIdIndex = -1;

                for (int i = 0; i < headers.Count; i++)
                {
                    string h = headers[i].Trim().ToLower();
                    if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa")) contractIndex = i;
                    if (h.Contains("premium") || h.Contains("prime")) premiumIndex = i;
                    if (h.Contains("product") || h.Contains("produit")) productIndex = i;

                    // Detection of "id test" or "test" column
                    if (h.Contains("test") || h.Contains("id test") || h.Contains("idtest")) testIdIndex = i;
                }

                string line;
                // Reading the rest of the file, line by line
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Using the smart parser instead of a simple .Split()
                    var columns = ParseCsvLine(line, delimiter);

                    if (columns.Count > contractIndex)
                    {
                        // The parser already removes quotes, we keep Replace("=", "") just in case Excel injects ="value"
                        string contractNumber = columns[contractIndex].Replace("=", "").Trim();
                        string premiumAmount = (premiumIndex != -1 && columns.Count > premiumIndex) ? columns[premiumIndex].Replace("=", "").Trim() : "0";
                        string productValue = (productIndex != -1 && columns.Count > productIndex) ? columns[productIndex].Replace("=", "").Trim() : "N/A";

                        // Get Test ID (fallback to contract number if no test column is found)
                        string testId = (testIdIndex != -1 && columns.Count > testIdIndex)
                            ? columns[testIdIndex].Replace("=", "").Trim()
                            : contractNumber;

                        if (!string.IsNullOrEmpty(contractNumber))
                        {
                            try
                            {
                                // Passing the environment parameter down to the ExtractionService
                                ExtractionResult result = _extractionService.PerformExtraction(contractNumber, env, false);

                                // If we have data, we add it to the combined StringBuilder
                                if (!string.IsNullOrWhiteSpace(result.LisaContent) || !string.IsNullOrWhiteSpace(result.EliaContent))
                                {
                                    globalCombined.AppendLine(new string('=', 80));
                                    globalCombined.AppendLine($"### GLOBAL CONTRACT REPORT: {contractNumber} | TEST ID: {testId} | ENV: {env} ###");
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

                                // Keep track of the successfully processed Test IDs
                                processedTestIds.Add(testId);

                                onProgressUpdate?.Invoke(new BatchProgressInfo
                                {
                                    ContractId = contractNumber,
                                    InternalId = result.InternalId,
                                    Product = productValue,
                                    Premium = premiumAmount,
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
                                    Product = productValue,
                                    Premium = premiumAmount,
                                    UconId = "Error",
                                    DemandId = "Error",
                                    Status = ex.Message.ToLower().Contains("not found") ? "Not found in DB" : "SQL Error"
                                });
                            }
                        }
                    }
                }
            }

            // --- SMART NAMING LOGIC ---
            string fileSignature = "NoContract";
            string sizeTag = "Big"; // Default to "Big" for a CSV extraction

            if (processedTestIds.Count > 0)
            {
                // Take the first 3 Test IDs and remove spaces to keep the filename compact
                var firstThree = processedTestIds.Take(3).Select(c => c.Replace(" ", ""));
                fileSignature = string.Join("_", firstThree);

                if (processedTestIds.Count > 3)
                {
                    fileSignature += $"_#{processedTestIds.Count - 3}other";
                }
                else if (processedTestIds.Count == 1)
                {
                    // If the CSV resulted in only 1 valid contract, switch to "Uniq"
                    sizeTag = "Uniq";
                }
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(Settings.OutputDir);

            // Get the first uppercase letter of the environment (e.g., "D" for "D000")
            char envLetter = !string.IsNullOrEmpty(env) ? char.ToUpper(env[0]) : 'U';

            // Generate the single combined output file
            string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_{sizeTag}_{fileSignature}_{timestamp}.csv");

            File.WriteAllText(
                combinedPath,
                globalCombined.Length > 0 ? globalCombined.ToString() : "NO CONTRACT FOUND.",
                Encoding.UTF8);
        }

        /// <summary>
        /// Robust native CSV parser handling delimiters present inside quotes.
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
                    // If we encounter a double quote inside quotes, it's an escaped quote ("")
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // We skip the 2nd quote
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