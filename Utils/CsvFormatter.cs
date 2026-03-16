using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoActivator.Utils
{
    public static class CsvFormatter
    {
        public static void AddTableToBuffer(StringBuilder sb, string tableName, DataTable dt)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"### TABLE : {tableName} | Lignes : {dt?.Rows.Count ?? 0}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            if (dt == null || dt.Rows.Count == 0)
            {
                sb.AppendLine("AUCUNE DONNÉE TROUVÉE\n");
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

        // --- NOUVELLES MÉTHODES POUR LE FILTRAGE INTELLIGENT LORS DE LA COMPARAISON ---

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
        /// Découpe le fichier complet et ne garde QUE le bloc de texte du contrat le plus récent pour chaque Test ID.
        /// </summary>
        public static Dictionary<string, string> SplitAndFilterMostRecentByTestId(string filePath)
        {
            var blocks = new Dictionary<string, (DateTime date, StringBuilder content)>();
            if (!File.Exists(filePath)) return new Dictionary<string, string>();

            string[] lines = File.ReadAllLines(filePath);
            string currentRootTestId = "UNKNOWN";
            DateTime currentDate = DateTime.MinValue;
            StringBuilder currentBlock = new StringBuilder();
            long fallbackCounter = 0; // Si pas de date, on prend le dernier dans l'ordre de lecture

            foreach (var line in lines)
            {
                // À chaque nouveau contrat trouvé dans le fichier...
                if (line.StartsWith("### GLOBAL CONTRACT REPORT:"))
                {
                    // 1. Sauvegarder le bloc du contrat PRÉCÉDENT s'il est plus récent que celui déjà en mémoire
                    if (currentBlock.Length > 0 && currentRootTestId != "UNKNOWN")
                    {
                        if (!blocks.ContainsKey(currentRootTestId) || currentDate >= blocks[currentRootTestId].date)
                        {
                            blocks[currentRootTestId] = (currentDate, currentBlock);
                        }
                    }

                    // 2. Préparer le NOUVEAU bloc
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

                            // On extrait "ID501" de "ID501_FIB_ProcessID:130326145957"
                            currentRootTestId = rawTestId.Contains("_") ? rawTestId.Split('_')[0].Trim() : rawTestId;

                            // On extrait la date pour pouvoir comparer
                            DateTime parsedDate = ParseProcessIdToDate(rawTestId);
                            if (parsedDate != DateTime.MinValue) currentDate = parsedDate;
                        }
                    }
                }

                currentBlock.AppendLine(line);
            }

            // N'oublions pas d'enregistrer le tout dernier contrat lu du fichier !
            if (currentBlock.Length > 0 && currentRootTestId != "UNKNOWN")
            {
                if (!blocks.ContainsKey(currentRootTestId) || currentDate >= blocks[currentRootTestId].date)
                {
                    blocks[currentRootTestId] = (currentDate, currentBlock);
                }
            }

            // On retourne le dictionnaire propre avec juste le texte
            return blocks.ToDictionary(k => k.Key, v => v.Value.content.ToString());
        }

        public static List<string> GetAllTableNamesFromContent(string fileContent)
        {
            var tableNames = new HashSet<string>();
            using (var reader = new StringReader(fileContent))
            {
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
            }
            return tableNames.ToList();
        }

        public static DataTable LoadTableFromContent(string fileContent, string tableName)
        {
            var dt = new DataTable(tableName);
            string searchString = $"### TABLE : {tableName} |";
            int tableStartIndex = fileContent.IndexOf(searchString);

            if (tableStartIndex == -1) return dt;

            int firstNewLine = fileContent.IndexOf('\n', tableStartIndex);
            int secondNewLine = fileContent.IndexOf('\n', firstNewLine + 1);
            if (firstNewLine == -1 || secondNewLine == -1) return dt;

            int dataStartIndex = secondNewLine + 1;

            bool isHeader = true;
            bool inQuotes = false;
            StringBuilder currentField = new StringBuilder();
            List<string> currentLine = new List<string>();

            for (int i = dataStartIndex; i < fileContent.Length; i++)
            {
                char c = fileContent[i];
                char nextC = i + 1 < fileContent.Length ? fileContent[i + 1] : '\0';

                if (c == '"')
                {
                    if (inQuotes && nextC == '"')
                    {
                        currentField.Append('"');
                        i++;
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
                    if (c == '\r' && nextC == '\n') i++;

                    currentLine.Add(currentField.ToString());
                    currentField.Clear();

                    if (currentLine.Count == 1 && (string.IsNullOrWhiteSpace(currentLine[0]) || currentLine[0].StartsWith("---") || currentLine[0] == "AUCUNE DONNÉE TROUVÉE"))
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

            return dt;
        }

        // --- ANCIENNES MÉTHODES POUR CONSERVER LA COMPATIBILITÉ AVEC LE RESTE DE L'APPLICATION ---
        public static List<string> GetAllTableNames(string filePath)
        {
            if (!File.Exists(filePath)) return new List<string>();
            return GetAllTableNamesFromContent(File.ReadAllText(filePath));
        }

        public static DataTable LoadTableFromCsv(string filePath, string tableName)
        {
            if (!File.Exists(filePath)) return new DataTable(tableName);
            return LoadTableFromContent(File.ReadAllText(filePath), tableName);
        }
    }
}