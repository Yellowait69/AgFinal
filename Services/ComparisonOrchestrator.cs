using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using AutoActivator.Models;
using AutoActivator.Utils;

namespace AutoActivator.Services
{
    public class ComparisonOrchestrator
    {

        /// <summary>
        /// Extracts the environment, size, and signature from the file name for filtering purposes.
        /// </summary>
        public string[] GetFileIds(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parts = fileName.Split('_');

            if (parts.Length >= 4)
            {
                return new[] { parts[1], parts[2], parts[3] };
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// Returns a list of compatible target files (sharing the same Env, Size, and Signature).
        /// </summary>
        public List<string> GetCompatibleTargetFiles(string baseFilePath, IEnumerable<string> allAvailableFiles)
        {
            var baseIds = GetFileIds(baseFilePath);
            if (baseIds.Length != 3) return new List<string>();

            return allAvailableFiles.Where(file =>
            {
                if (file.Equals(baseFilePath, StringComparison.OrdinalIgnoreCase)) return false;

                var targetIds = GetFileIds(file);
                return targetIds.Length == 3 &&
                       targetIds[0] == baseIds[0] &&
                       targetIds[1] == baseIds[1] &&
                       targetIds[2] == baseIds[2];
            }).ToList();
        }

        /// <summary>
        /// Runs the full comparison. Keeps only the most recent contract in memory for each Test ID to avoid duplication.
        /// </summary>
        public ComparisonReport RunFullComparison(string baseFile, string targetFile)
        {
            var report = new ComparisonReport();

            var baseBlocks = CsvFormatter.SplitAndFilterMostRecentByTestId(baseFile);
            var targetBlocks = CsvFormatter.SplitAndFilterMostRecentByTestId(targetFile);

            if (baseBlocks.Count == 0 && targetBlocks.Count == 0)
            {
                throw new Exception("No data found in either file. Is the format correct?");
            }

            var baseIds = baseBlocks.Keys.ToList();
            var targetIds = targetBlocks.Keys.ToList();

            var commonTestIds = baseIds.Intersect(targetIds).ToList();

            report.TotalBaseTests = baseIds.Count;
            report.TotalTargetTests = targetIds.Count;
            report.ComparedTestsCount = commonTestIds.Count;
            report.MissingInTarget = baseIds.Except(targetIds).ToList();
            report.MissingInBase = targetIds.Except(baseIds).ToList();

            if (commonTestIds.Count == 0)
            {
                throw new Exception($"No common Test IDs found. Comparison impossible. (Base: {baseIds.Count} tests, Target: {targetIds.Count} tests)");
            }

            foreach (var testId in commonTestIds)
            {
                string baseContent = baseBlocks[testId];
                string targetContent = targetBlocks[testId];

                string product = "UNKNOWN";
                try
                {
                    DataTable uconTable = CsvFormatter.LoadTableFromContent(baseContent, "FJ1.TB5UCON");
                    if (uconTable != null && uconTable.Rows.Count > 0 && uconTable.Columns.Contains("IT5UCONCASU"))
                    {
                        product = uconTable.Rows[0]["IT5UCONCASU"]?.ToString();
                        if (string.IsNullOrWhiteSpace(product)) product = "UNKNOWN";
                    }
                }
                catch { /* Safely ignore if the table does not exist or lacks the column */ }


                if (!report.TestMetrics.ContainsKey(testId)) report.TestMetrics[testId] = new ComparisonMetrics();
                if (!report.ProductMetrics.ContainsKey(product)) report.ProductMetrics[product] = new ComparisonMetrics();



                List<string> tablesToCompare = CsvFormatter.GetAllTableNamesFromContent(baseContent);

                foreach (var tableName in tablesToCompare)
                {
                    string tableType = tableName.StartsWith("LV.") ? "LISA" : (tableName.StartsWith("FJ1.") ? "ELIA" : "COMBINED");

                    string displayTableName = $"[{testId}] {tableName}";

                    CompareAndAppendToReport(baseContent, targetContent, tableName, displayTableName, tableType, report, Path.GetFileName(baseFile), Path.GetFileName(targetFile), testId, product);
                }
            }

            CalculateGlobalScore(report);

            return report;
        }

        private void CompareAndAppendToReport(string baseContent, string targetContent, string actualTableName, string displayTableName, string type, ComparisonReport report, string baseFileName, string targetFileName, string testId, string product)
        {
            DataTable df1 = CsvFormatter.LoadTableFromContent(baseContent, actualTableName);
            DataTable df2 = CsvFormatter.LoadTableFromContent(targetContent, actualTableName);

            var (Status, Details) = Comparator.CompareDataTables(df1, df2, displayTableName);

            var fileResult = new FileComparisonResult
            {
                FileType = type,
                BaseFileName = baseFileName,
                TargetFileName = targetFileName,
                TableName = displayTableName,
                Status = Status,
                ErrorDetails = Details
            };

            report.FileResults.Add(fileResult);

            int rowsCount = Math.Max(df1?.Rows.Count ?? 0, df2?.Rows.Count ?? 0);
            int currentErrors = 0;

            if (Status != "OK" && Status != "OK_EMPTY")
            {
                if (!string.IsNullOrEmpty(Details))
                {
                    int diffCount = Details.Split(new[] { "Row #" }, StringSplitOptions.None).Length - 1;

                    currentErrors = diffCount > 0 ? diffCount : rowsCount;
                }
                else
                {
                    currentErrors = rowsCount;
                }
            }

            report.TotalRowsCompared += rowsCount;
            report.TotalDifferencesFound += currentErrors;

            if (rowsCount > 0)
            {
                report.TestMetrics[testId].TotalRows += rowsCount;
                report.TestMetrics[testId].ErrorRows += currentErrors;

                report.ProductMetrics[product].TotalRows += rowsCount;
                report.ProductMetrics[product].ErrorRows += currentErrors;
            }
        }

        private void CalculateGlobalScore(ComparisonReport report)
        {
            if (report.TotalRowsCompared == 0)
            {
                report.GlobalSuccessPercentage = 100.0;
                return;
            }

            int successfulRows = report.TotalRowsCompared - report.TotalDifferencesFound;

            if (successfulRows < 0) successfulRows = 0;

            report.GlobalSuccessPercentage = Math.Round(((double)successfulRows / report.TotalRowsCompared) * 100, 2);
        }
    }
}