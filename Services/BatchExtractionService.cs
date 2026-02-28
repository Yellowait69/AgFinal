using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Indispensable pour utiliser .Take() et .Select()
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

            string rawText = File.ReadAllText(filePath).Replace("\uFEFF", "");
            string[] lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1)
                throw new Exception("The CSV file is empty or contains only the header.");

            StringBuilder globalLisa = new StringBuilder();
            StringBuilder globalElia = new StringBuilder();

            string[] headers = lines[0].Split(new[] { ';', ',' });
            int contractIndex = 0, premiumIndex = -1, productIndex = -1, testIdIndex = -1;

            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim().Trim('"').ToLower();
                if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa")) contractIndex = i;
                if (h.Contains("premium") || h.Contains("prime")) premiumIndex = i;
                if (h.Contains("product") || h.Contains("produit")) productIndex = i;

                // Detection of "id test" or "test" column
                if (h.Contains("test") || h.Contains("id test") || h.Contains("idtest")) testIdIndex = i;
            }

            // List to store the TEST IDs of successfully processed contracts
            List<string> processedTestIds = new List<string>();

            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(new[] { ';', ',' });

                if (columns.Length > contractIndex)
                {
                    string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();
                    string premiumAmount = (premiumIndex != -1 && columns.Length > premiumIndex) ? columns[premiumIndex].Replace("=", "").Replace("\"", "").Trim() : "0";
                    string productValue = (productIndex != -1 && columns.Length > productIndex) ? columns[productIndex].Replace("=", "").Replace("\"", "").Trim() : "N/A";

                    // Get Test ID (fallback to contract number if no test column is found)
                    string testId = (testIdIndex != -1 && columns.Length > testIdIndex)
                        ? columns[testIdIndex].Replace("=", "").Replace("\"", "").Trim()
                        : contractNumber;

                    if (!string.IsNullOrEmpty(contractNumber))
                    {
                        try
                        {
                            // Passing the environment parameter down to the ExtractionService
                            ExtractionResult result = _extractionService.PerformExtraction(contractNumber, env, false);

                            if (!string.IsNullOrWhiteSpace(result.LisaContent))
                            {
                                globalLisa.AppendLine(new string('-', 60));
                                globalLisa.AppendLine($"### CONTRACT: {contractNumber} | TEST ID: {testId} | ENV: {env}");
                                globalLisa.AppendLine(new string('-', 60));
                                globalLisa.Append(result.LisaContent).AppendLine();
                            }

                            if (!string.IsNullOrWhiteSpace(result.EliaContent))
                            {
                                globalElia.AppendLine(new string('-', 60));
                                globalElia.AppendLine($"### CONTRACT: {contractNumber} | TEST ID: {testId} | UCON: {result.UconId} | ENV: {env}");
                                globalElia.AppendLine(new string('-', 60));
                                globalElia.Append(result.EliaContent).AppendLine();
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

            // Generate the output files
            File.WriteAllText(
                Path.Combine(Settings.OutputDir, $"ExtractionLISA_{envLetter}_{sizeTag}_{fileSignature}_{timestamp}.csv"),
                globalLisa.Length > 0 ? globalLisa.ToString() : "NO LISA CONTRACT FOUND.",
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(Settings.OutputDir, $"ExtractionELIA_{envLetter}_{sizeTag}_{fileSignature}_{timestamp}.csv"),
                globalElia.Length > 0 ? globalElia.ToString() : "NO ELIA CONTRACT FOUND.",
                Encoding.UTF8);
        }
    }
}