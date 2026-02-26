using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using AutoActivator.Config;

namespace AutoActivator.Services
{
    public static class Comparator
    {
        /// <summary>
        /// Central function for comparing two datasets (DataTables).
        /// </summary>
        /// <param name="dfRef">Data extracted from the source contract.</param>
        /// <param name="dfNew">Data extracted from the new contract.</param>
        /// <param name="tableName">The name of the analyzed table (e.g., 'LV.SCNTT0').</param>
        /// <returns>A tuple containing the status (OK/KO) and the details of the differences.</returns>
        public static (string Status, string Details) CompareDataTables(DataTable dfRef, DataTable dfNew, string tableName)
        {
            // STEP 1: Initial validity checks
            if (dfRef.Rows.Count == 0 && dfNew.Rows.Count == 0)
            {
                return ("OK_EMPTY", null);
            }

            if (dfRef.Rows.Count == 0 || dfNew.Rows.Count == 0)
            {
                return ("KO_MISSING_DATA", $"One of the two DataFrames is empty for table {tableName}.");
            }

            // STEP 2: Data isolation (Working on copies)
            var df1 = dfRef.Copy();
            var df2 = dfNew.Copy();

            try
            {
                // STEP 3: Application of exclusion rules
                var colsToDrop = Exclusions.GetExclusionsForTable(tableName);

                foreach (var col in colsToDrop)
                {
                    if (df1.Columns.Contains(col)) df1.Columns.Remove(col);
                    if (df2.Columns.Contains(col)) df2.Columns.Remove(col);
                }

                // STEP 4: Alignment of data schemas
                var commonCols = df1.Columns.Cast<DataColumn>()
                    .Select(c => c.ColumnName)
                    .Intersect(df2.Columns.Cast<DataColumn>().Select(c => c.ColumnName))
                    .OrderBy(c => c)
                    .ToList();

                if (!commonCols.Any())
                {
                    return ("KO_NO_COMMON_COLS", "No common column found after applying exclusion filters.");
                }

                // Removal of non-common columns to perfectly align DataTables
                for (int i = df1.Columns.Count - 1; i >= 0; i--)
                {
                    if (!commonCols.Contains(df1.Columns[i].ColumnName)) df1.Columns.RemoveAt(i);
                }
                for (int i = df2.Columns.Count - 1; i >= 0; i--)
                {
                    if (!commonCols.Contains(df2.Columns[i].ColumnName)) df2.Columns.RemoveAt(i);
                }

                // STEP 5: Data normalization and formatting
                NormalizeDataTable(df1, commonCols);
                NormalizeDataTable(df2, commonCols);

                // STEP 6: Record alignment (Sorting)
                // Creating a sort string "Col1, Col2, Col3..." to stabilize row order
                string sortExpression = string.Join(", ", commonCols);

                try
                {
                    df1.DefaultView.Sort = sortExpression;
                    df1 = df1.DefaultView.ToTable();

                    df2.DefaultView.Sort = sortExpression;
                    df2 = df2.DefaultView.ToTable();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[WARNING] Technical sort failed on table {tableName}. Reason: {e.Message}");
                }

                // STEP 7: Final comparison
                if (df1.Rows.Count != df2.Rows.Count)
                {
                    return ("KO_ROW_COUNT", $"Discrepancy in data volume: Source = {df1.Rows.Count} rows vs Target = {df2.Rows.Count} rows.");
                }

                return GenerateDiffReport(df1, df2, commonCols);
            }
            catch (Exception e)
            {
                return ("KO_ERROR", $"Technical error while generating the differential: {e.Message}");
            }
        }

        /// <summary>
        /// Cleans and normalizes data to avoid false positives (spaces, decimals, nulls).
        /// </summary>
        private static void NormalizeDataTable(DataTable dt, List<string> columns)
        {
            foreach (DataRow row in dt.Rows)
            {
                foreach (var col in columns)
                {
                    if (row[col] == DBNull.Value || row[col] == null)
                    {
                        row[col] = string.Empty;
                        continue;
                    }

                    string value = row[col].ToString().Trim();

                    // Processing textual NaN or None
                    if (value.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        row[col] = string.Empty;
                    }
                    // If it is a number (Float), round it to 4 decimals
                    else if (double.TryParse(value, out double floatValue))
                    {
                        row[col] = Math.Round(floatValue, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        row[col] = value;
                    }
                }
            }
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

                    if (val1 != val2)
                    {
                        rowHasDiff = true;
                        diffCount++;
                        rowDiffs.AppendLine($"    -> {commonCols[j]} : Source = '{val1}' | Target = '{val2}'");
                    }
                }

                if (rowHasDiff)
                {
                    diffReport.AppendLine($"Row #{i + 1} is different:");
                    diffReport.Append(rowDiffs.ToString());
                    diffReport.AppendLine();
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