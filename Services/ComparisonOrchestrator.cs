using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using AutoActivator.Models;

namespace AutoActivator.Services
{
    public class ComparisonOrchestrator
    {
        // Assumes the file format is "Type_ID1_ID2_ID3_RestOfName.csv"
        // Example: "Elia_1001_2002_3003_v1.csv"

        /// <summary>
        /// Extracts the first 3 IDs from the file name for filtering.
        /// </summary>
        public string[] GetFileIds(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parts = fileName.Split('_');

            // Expects at least: Type(0)_Id1(1)_Id2(2)_Id3(3)
            if (parts.Length >= 4)
            {
                return new[] { parts[1], parts[2], parts[3] };
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// Returns the list of compatible target files (having the same first 3 IDs).
        /// </summary>
        public List<string> GetCompatibleTargetFiles(string baseFilePath, IEnumerable<string> allAvailableFiles)
        {
            var baseIds = GetFileIds(baseFilePath);
            if (baseIds.Length != 3) return new List<string>();

            return allAvailableFiles.Where(file =>
            {
                if (file == baseFilePath) return false;
                var targetIds = GetFileIds(file);
                return targetIds.Length == 3 &&
                       targetIds[0] == baseIds[0] &&
                       targetIds[1] == baseIds[1] &&
                       targetIds[2] == baseIds[2];
            }).ToList();
        }

        /// <summary>
        /// Launches the full comparison. Automatically handles the Elia/Lisa mirror comparison.
        /// </summary>
        public ComparisonReport RunFullComparison(string baseFile, string targetFile, string tableName)
        {
            var report = new ComparisonReport();

            // 1. Comparison of the main selected file
            string baseType = Path.GetFileName(baseFile).StartsWith("Elia", StringComparison.OrdinalIgnoreCase) ? "ELIA" : "LISA";
            CompareAndAppendToReport(baseFile, targetFile, tableName, baseType, report);

            // 2. Automatic deduction of the twin file (If Elia -> look for Lisa, and vice versa)
            string mirrorType = baseType == "ELIA" ? "Lisa" : "Elia";
            string baseMirrorFile = baseFile.Replace(baseType, mirrorType, StringComparison.OrdinalIgnoreCase);
            string targetMirrorFile = targetFile.Replace(baseType, mirrorType, StringComparison.OrdinalIgnoreCase);

            // If mirror files exist, compare them automatically as well
            if (File.Exists(baseMirrorFile) && File.Exists(targetMirrorFile))
            {
                CompareAndAppendToReport(baseMirrorFile, targetMirrorFile, tableName, mirrorType.ToUpper(), report);
            }

            // 3. Calculation of the global success percentage
            CalculateGlobalScore(report);

            return report;
        }

        private void CompareAndAppendToReport(string file1, string file2, string tableName, string type, ComparisonReport report)
        {
            // NOTE: Replace with your actual CSV -> DataTable conversion method
            DataTable df1 = LoadCsvToDataTable(file1);
            DataTable df2 = LoadCsvToDataTable(file2);

            var (Status, Details) = Comparator.CompareDataTables(df1, df2, tableName);

            var fileResult = new FileComparisonResult
            {
                FileType = type,
                BaseFileName = Path.GetFileName(file1),
                TargetFileName = Path.GetFileName(file2),
                TableName = tableName,
                Status = Status,
                ErrorDetails = Details
            };

            report.FileResults.Add(fileResult);

            // Update counters for the global score
            int rowsCount = Math.Max(df1?.Rows.Count ?? 0, df2?.Rows.Count ?? 0);
            report.TotalRowsCompared += rowsCount;

            if (Status != "OK" && Status != "OK_EMPTY")
            {
                // Approximation: Count the number of rows reported with errors from "Details"
                if (!string.IsNullOrEmpty(Details))
                {
                    // Count the number of occurrences of "Row #" in the report to know how many rows failed
                    int diffCount = Details.Split(new[] { "Row #" }, StringSplitOptions.None).Length - 1;
                    report.TotalDifferencesFound += diffCount > 0 ? diffCount : rowsCount; // If structural error, everything is marked as failed
                }
                else
                {
                    report.TotalDifferencesFound += rowsCount;
                }
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

            // Prevent negative values if global errors skew the calculation
            if (successfulRows < 0) successfulRows = 0;

            report.GlobalSuccessPercentage = Math.Round(((double)successfulRows / report.TotalRowsCompared) * 100, 2);
        }

        // --- Utility method (Mock) to load a CSV into a DataTable ---
        // (To be adapted if you already have CsvFormatter.cs configured for this)
        private DataTable LoadCsvToDataTable(string filePath)
        {
            var dt = new DataTable();
            if (!File.Exists(filePath)) return dt;

            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0) return dt;

            var headers = lines[0].Split(';'); // or ',' depending on your format
            foreach (var header in headers) dt.Columns.Add(header.Trim());

            for (int i = 1; i < lines.Length; i++)
            {
                var row = dt.NewRow();
                var cols = lines[i].Split(';');
                for (int j = 0; j < headers.Length && j < cols.Length; j++)
                {
                    row[j] = cols[j].Trim();
                }
                dt.Rows.Add(row);
            }
            return dt;
        }
    }
}