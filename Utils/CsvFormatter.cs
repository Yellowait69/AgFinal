using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoActivator.Utils
{
    /// <summary>
    /// Utility class for formatting and parsing CSV data, optimized for low memory consumption.
    /// </summary>
    public static class CsvFormatter
    {
        public static void AddTableToBuffer(StringBuilder sb, string tableName, DataTable dt)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"### TABLE : {tableName} | Rows : {dt?.Rows.Count ?? 0}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            if (dt == null || dt.Rows.Count == 0)
            {
                sb.AppendLine("NO DATA FOUND\n");
                return;
            }

            var columns = dt.Columns.Cast<DataColumn>().Select(c => EscapeCsvField(c.ColumnName));
            sb.AppendLine(string.Join(";", columns));

            foreach (DataRow row in dt.Rows)
            {
                var fields = row.ItemArray.Select(f =>
                    f == DBNull.Value ? "" : EscapeCsvField(f.ToString()));
                sb.AppendLine(string.Join(";", fields));
            }
            sb.AppendLine();
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            field = field.Trim();

            if (field.Contains(";") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }
            return field;
        }


        private static DateTime ParseProcessIdToDate(string rawTestId)
        {
            try
            {
                int idx = rawTestId.IndexOf("ProcessID:");
                if (idx != -1)
                {
                    string dateStr = rawTestId.Substring(idx + 10).Trim();
                    if (dateStr.Length >= 12)
                    {
                        dateStr = dateStr.Substring(0, 12);
                        if (DateTime.TryParseExact(dateStr, "ddMMyyHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)) return dt;
                        if (DateTime.TryParseExact(dateStr, "yyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt2)) return dt2;
                    }
                }
            }
            catch { }
            return DateTime.MinValue;
        }

        /// <summary>
        /// Splits the full file and keeps ONLY the text block of the most recent contract for each Test ID.
        /// MEMORY OPTIMIZED (Line-by-line streaming).
        /// </summary>
        public static Dictionary<string, string> SplitAndFilterMostRecentByTestId(string filePath)
        {
            var blocks = new Dictionary<string, (DateTime date, StringBuilder content)>();
            if (!File.Exists(filePath)) return new Dictionary<string, string>();

            string currentRootTestId = "UNKNOWN";
            DateTime currentDate = DateTime.MinValue;
            StringBuilder currentBlock = new StringBuilder();
            long fallbackCounter = 0;

            foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
            {
                if (line.StartsWith("### GLOBAL CONTRACT REPORT:"))
                {
                    if (currentBlock.Length > 0 && currentRootTestId != "UNKNOWN")
                    {
                        if (!blocks.ContainsKey(currentRootTestId) || currentDate >= blocks[currentRootTestId].date)
                        {
                            blocks[currentRootTestId] = (currentDate, currentBlock);
                        }
                    }

                    currentBlock = new StringBuilder();
                    currentDate = new DateTime(fallbackCounter++);
                    currentRootTestId = "UNKNOWN";

                    int idx = line.IndexOf("TEST ID:");
                    if (idx != -1)
                    {
                        int endIdx = line.IndexOf("|", idx);
                        if (endIdx != -1)
                        {
                            string rawTestId = line.Substring(idx + 8, endIdx - (idx + 8)).Trim();
                            currentRootTestId = rawTestId.Contains("_") ? rawTestId.Split('_')[0].Trim() : rawTestId;

                            DateTime parsedDate = ParseProcessIdToDate(rawTestId);
                            if (parsedDate != DateTime.MinValue) currentDate = parsedDate;
                        }
                    }
                }

                currentBlock.AppendLine(line);
            }

            if (currentBlock.Length > 0 && currentRootTestId != "UNKNOWN")
            {
                if (!blocks.ContainsKey(currentRootTestId) || currentDate >= blocks[currentRootTestId].date)
                {
                    blocks[currentRootTestId] = (currentDate, currentBlock);
                }
            }

            return blocks.ToDictionary(k => k.Key, v => v.Value.content.ToString());
        }


        public static List<string> GetAllTableNamesFromContent(string fileContent)
        {
            using (var reader = new StringReader(fileContent))
            {
                return GetAllTableNamesFromReader(reader);
            }
        }

        public static List<string> GetAllTableNames(string filePath)
        {
            if (!File.Exists(filePath)) return new List<string>();

            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                return GetAllTableNamesFromReader(reader);
            }
        }

        private static List<string> GetAllTableNamesFromReader(TextReader reader)
        {
            var tableNames = new HashSet<string>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("### TABLE : "))
                {
                    int startIndex = 12;
                    int endIndex = line.IndexOf(" |", startIndex);
                    if (endIndex > startIndex)
                    {
                        tableNames.Add(line.Substring(startIndex, endIndex - startIndex).Trim());
                    }
                }
            }
            return tableNames.ToList();
        }

        public static DataTable LoadTableFromContent(string fileContent, string tableName)
        {
            using (var reader = new StringReader(fileContent))
            {
                return LoadTableFromReader(reader, tableName);
            }
        }

        public static DataTable LoadTableFromCsv(string filePath, string tableName)
        {
            if (!File.Exists(filePath)) return new DataTable(tableName);

            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                return LoadTableFromReader(reader, tableName);
            }
        }

        /// <summary>
        /// Extracts a table using a text stream (file or memory), which prevents
        /// RAM saturation when parsing very large files.
        /// </summary>
        private static DataTable LoadTableFromReader(TextReader reader, string tableName)
        {
            var dt = new DataTable(tableName);
            string searchString = $"### TABLE : {tableName} |";
            string line;
            bool tableFound = false;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith(searchString))
                {
                    tableFound = true;
                    reader.ReadLine();
                    break;
                }
            }

            if (!tableFound) return dt;

            bool isHeader = true;
            bool inQuotes = false;
            StringBuilder currentField = new StringBuilder();
            List<string> currentLine = new List<string>();

            int intChar;
            while ((intChar = reader.Read()) != -1)
            {
                char c = (char)intChar;
                int peek = reader.Peek();
                char nextC = peek != -1 ? (char)peek : '\0';

                if (c == '"')
                {
                    if (inQuotes && nextC == '"')
                    {
                        currentField.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ';' && !inQuotes)
                {
                    currentLine.Add(currentField.ToString());
                    currentField.Clear();
                }
                else if ((c == '\r' || c == '\n') && !inQuotes)
                {
                    if (c == '\r' && nextC == '\n') reader.Read();

                    currentLine.Add(currentField.ToString());
                    currentField.Clear();

                    if (currentLine.Count == 1 && (string.IsNullOrWhiteSpace(currentLine[0]) || currentLine[0].StartsWith("---") || currentLine[0] == "NO DATA FOUND"))
                    {
                        break;
                    }

                    if (isHeader)
                    {
                        foreach (var field in currentLine)
                        {
                            string colName = field.Trim();
                            int suffix = 1;
                            while (dt.Columns.Contains(colName)) colName = $"{field.Trim()}_{suffix++}";
                            dt.Columns.Add(colName);
                        }
                        isHeader = false;
                    }
                    else if (currentLine.Any(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        var row = dt.NewRow();
                        for (int j = 0; j < currentLine.Count && j < dt.Columns.Count; j++)
                        {
                            row[j] = currentLine[j];
                        }
                        dt.Rows.Add(row);
                    }

                    currentLine.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            if (currentField.Length > 0 || currentLine.Count > 0)
            {
                currentLine.Add(currentField.ToString());
                if (!isHeader && currentLine.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var row = dt.NewRow();
                    for (int j = 0; j < currentLine.Count && j < dt.Columns.Count; j++)
                    {
                        row[j] = currentLine[j];
                    }
                    dt.Rows.Add(row);
                }
            }

            return dt;
        }
    }
}