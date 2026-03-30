using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using AutoActivator.Config;

namespace AutoActivator.Services
{
    public static class Comparator
    {
        private const int MAX_DIFFS_TO_REPORT = 100;

        /// <summary>
        /// Central function to compare two datasets (DataTables).
        /// MEMORY OPTIMIZED: Uses lightweight structures instead of cloning DataTables.
        /// </summary>
        public static (string Status, string Details) CompareDataTables(DataTable dfRef, DataTable dfNew, string tableName)
        {
            if (dfRef == null || dfNew == null)
            {
                return ("KO_NULL_DATA", $"One or both DataTables are null for table {tableName}.");
            }

            if (dfRef.Rows.Count == 0 && dfNew.Rows.Count == 0)
            {
                return ("OK_EMPTY", null);
            }

            if (dfRef.Rows.Count == 0 || dfNew.Rows.Count == 0)
            {
                return ("KO_MISSING_DATA", $"Discrepancy: Source has {dfRef.Rows.Count} rows, Target has {dfNew.Rows.Count} rows for table {tableName}.");
            }

            try
            {
                string cleanTableName = tableName;
                if (cleanTableName.StartsWith("["))
                {
                    int closeBracketIdx = cleanTableName.IndexOf(']');
                    if (closeBracketIdx != -1 && closeBracketIdx + 1 < cleanTableName.Length)
                    {
                        cleanTableName = cleanTableName.Substring(closeBracketIdx + 1).Trim();
                    }
                }

                var exclusions = Exclusions.GetExclusionsForTable(cleanTableName) ?? new HashSet<string>();

                var allColsRef = dfRef.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                var allColsNew = dfNew.Columns.Cast<DataColumn>().Select(c => c.ColumnName);

                var commonCols = allColsRef
                    .Intersect(allColsNew)
                    .Except(exclusions, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();

                if (!commonCols.Any())
                {
                    return ("KO_NO_COMMON_COLS", "No common columns found after applying exclusion filters.");
                }

                int[] refIndices = commonCols.Select(c => dfRef.Columns[c].Ordinal).ToArray();
                int[] newIndices = commonCols.Select(c => dfNew.Columns[c].Ordinal).ToArray();

                List<string[]> refRows = ExtractAndNormalize(dfRef, refIndices);
                List<string[]> newRows = ExtractAndNormalize(dfNew, newIndices);

                var rowComparer = new StringArrayComparer();
                refRows.Sort(rowComparer);
                newRows.Sort(rowComparer);

                if (refRows.Count != newRows.Count)
                {
                    return ("KO_ROW_COUNT", $"Discrepancy in data volume: Source = {refRows.Count} rows vs Target = {newRows.Count} rows.");
                }

                return GenerateDiffReport(refRows, newRows, commonCols);
            }
            catch (Exception e)
            {
                return ("KO_ERROR", $"Technical error while generating the differential for {tableName}: {e.Message}");
            }
        }

        /// <summary>
        /// Extracts only the required columns and normalizes the data into a very lightweight format.
        /// </summary>
        private static List<string[]> ExtractAndNormalize(DataTable dt, int[] columnIndices)
        {
            var result = new List<string[]>(dt.Rows.Count);

            foreach (DataRow row in dt.Rows)
            {
                string[] stringRow = new string[columnIndices.Length];

                for (int i = 0; i < columnIndices.Length; i++)
                {
                    object cellValue = row[columnIndices[i]];

                    if (cellValue == DBNull.Value || cellValue == null)
                    {
                        stringRow[i] = string.Empty;
                        continue;
                    }

                    string value = cellValue.ToString().Trim();

                    if (string.Equals(value, "nan", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        stringRow[i] = string.Empty;
                    }
                    else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double floatValue))
                    {
                        stringRow[i] = Math.Round(floatValue, 4).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        stringRow[i] = value;
                    }
                }
                result.Add(stringRow);
            }

            return result;
        }

        /// <summary>
        /// Customized comparator to quickly sort a list of string arrays.
        /// </summary>
        private class StringArrayComparer : IComparer<string[]>
        {
            public int Compare(string[] x, string[] y)
            {
                for (int i = 0; i < x.Length; i++)
                {
                    int cmp = string.CompareOrdinal(x[i], y[i]);
                    if (cmp != 0) return cmp;
                }
                return 0;
            }
        }

        /// <summary>
        /// Compare cell by cell from the lightweight lists and generates a report.
        /// </summary>
        private static (string Status, string Details) GenerateDiffReport(List<string[]> refRows, List<string[]> newRows, List<string> commonCols)
        {
            var diffReport = new StringBuilder();
            int diffCount = 0;

            for (int i = 0; i < refRows.Count; i++)
            {
                bool rowHasDiff = false;
                var rowDiffs = new StringBuilder();

                for (int j = 0; j < commonCols.Count; j++)
                {
                    string val1 = refRows[i][j];
                    string val2 = newRows[i][j];

                    if (!string.Equals(val1, val2, StringComparison.Ordinal))
                    {
                        rowHasDiff = true;
                        rowDiffs.AppendLine($"    -> {commonCols[j]} : Source = '{val1}' | Target = '{val2}'");
                    }
                }

                if (rowHasDiff)
                {
                    diffCount++;
                    diffReport.AppendLine($"Row #{i + 1} is different:");
                    diffReport.Append(rowDiffs.ToString());
                    diffReport.AppendLine();

                    if (diffCount >= MAX_DIFFS_TO_REPORT)
                    {
                        diffReport.AppendLine($"\n[WARNING] Maximum number of reportable differences ({MAX_DIFFS_TO_REPORT}) reached. Truncating report...");
                        break;
                    }
                }
            }

            if (diffCount == 0)
            {
                return ("OK", null);
            }

            return ("KO", diffReport.ToString());
        }
    }
}