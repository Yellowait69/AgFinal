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
        // Limite pour éviter une saturation de la RAM (Out Of Memory) si les tables sont 100% différentes
        private const int MAX_DIFFS_TO_REPORT = 100;

        /// <summary>
        /// Central function for comparing two datasets (DataTables).
        /// </summary>
        public static (string Status, string Details) CompareDataTables(DataTable dfRef, DataTable dfNew, string tableName)
        {
            // STEP 1: Initial validity checks
            if (dfRef == null || dfNew == null)
            {
                return ("KO_NULL_DATA", $"One or both DataFrames are null for table {tableName}.");
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
                // STEP 2 & 3: Application of exclusion rules AND schema alignment efficiently
                var exclusions = Exclusions.GetExclusionsForTable(tableName) ?? new HashSet<string>();

                var allColsRef = dfRef.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                var allColsNew = dfNew.Columns.Cast<DataColumn>().Select(c => c.ColumnName);

                var commonCols = allColsRef
                    .Intersect(allColsNew)
                    .Except(exclusions)
                    .OrderBy(c => c)
                    .ToList();

                if (!commonCols.Any())
                {
                    return ("KO_NO_COMMON_COLS", "No common columns found after applying exclusion filters.");
                }

                // OPTIMIZATION: Instead of copying the whole table and manually removing columns,
                // we project only the columns we need directly into new tables.
                string[] colsArray = commonCols.ToArray();
                var df1 = new DataView(dfRef).ToTable(false, colsArray);
                var df2 = new DataView(dfNew).ToTable(false, colsArray);

                // STEP 4: Data normalization and formatting
                NormalizeDataTable(df1, commonCols);
                NormalizeDataTable(df2, commonCols);

                // STEP 5: Record alignment (Sorting)
                // Using brackets [ColName] to prevent crashes if column names contain spaces or special chars
                string sortExpression = string.Join(", ", commonCols.Select(c => $"[{c}]"));

                try
                {
                    df1.DefaultView.Sort = sortExpression;
                    df1 = df1.DefaultView.ToTable();

                    df2.DefaultView.Sort = sortExpression;
                    df2 = df2.DefaultView.ToTable();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[WARNING] Technical sort failed on table {tableName}. Proceeding without sort. Reason: {e.Message}");
                }

                // STEP 6: Final comparison
                if (df1.Rows.Count != df2.Rows.Count)
                {
                    return ("KO_ROW_COUNT", $"Discrepancy in data volume: Source = {df1.Rows.Count} rows vs Target = {df2.Rows.Count} rows.");
                }

                return GenerateDiffReport(df1, df2, commonCols);
            }
            catch (Exception e)
            {
                return ("KO_ERROR", $"Technical error while generating the differential for {tableName}: {e.Message}");
            }
        }

        /// <summary>
        /// Cleans and normalizes data to avoid false positives (spaces, decimals, nulls).
        /// </summary>
        private static void NormalizeDataTable(DataTable dt, List<string> columns)
        {
            // Suspend data events (massive performance boost for loops modifying DataTable)
            dt.BeginLoadData();

            foreach (DataRow row in dt.Rows)
            {
                foreach (var col in columns)
                {
                    object cellValue = row[col];

                    if (cellValue == DBNull.Value || cellValue == null)
                    {
                        row[col] = string.Empty;
                        continue;
                    }

                    string value = cellValue.ToString().Trim();

                    // Processing textual NaN or None (Case Insensitive)
                    if (string.Equals(value, "nan", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        row[col] = string.Empty;
                    }
                    // ROBUST PARSING: NumberStyles.Any and CultureInfo.InvariantCulture
                    else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double floatValue))
                    {
                        row[col] = Math.Round(floatValue, 4).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        // Only assign if value actually changed (saves memory allocations)
                        if (!string.Equals(cellValue.ToString(), value, StringComparison.Ordinal))
                        {
                            row[col] = value;
                        }
                    }
                }
            }

            // Resume data events
            dt.EndLoadData();
            dt.AcceptChanges();
        }

        /// <summary>
        /// Compares cell by cell and generates a report.
        /// </summary>
        private static (string Status, string Details) GenerateDiffReport(DataTable df1, DataTable df2, List<string> commonCols)
        {
            var diffReport = new StringBuilder();
            int diffCount = 0;

            for (int i = 0; i < df1.Rows.Count; i++)
            {
                bool rowHasDiff = false;
                var rowDiffs = new StringBuilder();

                for (int j = 0; j < commonCols.Count; j++)
                {
                    string val1 = df1.Rows[i][j]?.ToString() ?? string.Empty;
                    string val2 = df2.Rows[i][j]?.ToString() ?? string.Empty;

                    // Exact ordinal string comparison (fastest and most accurate after normalization)
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

                    // Failsafe limit to avoid consuming gigabytes of RAM
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