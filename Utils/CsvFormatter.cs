using System;
using System.Collections.Generic;
using System.Data;
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

        /// <summary>
        /// Extrait une table spécifique d'un fichier multi-tables généré par l'ExtractionService.
        /// Gère correctement les points-virgules et retours à la ligne inclus dans les champs entre guillemets.
        /// </summary>
        public static DataTable LoadTableFromCsv(string filePath, string tableName)
        {
            var dt = new DataTable(tableName);
            if (!File.Exists(filePath)) return dt;

            string fileContent = File.ReadAllText(filePath);

            // 1. Localiser le début de la table demandée
            string searchString = $"### TABLE : {tableName} |";
            int tableStartIndex = fileContent.IndexOf(searchString);

            if (tableStartIndex == -1) return dt; // Table introuvable

            // 2. Sauter l'en-tête de la table et la ligne de tirets "----"
            int firstNewLine = fileContent.IndexOf('\n', tableStartIndex);
            int secondNewLine = fileContent.IndexOf('\n', firstNewLine + 1);
            if (firstNewLine == -1 || secondNewLine == -1) return dt;

            int dataStartIndex = secondNewLine + 1; // Début de la ligne des noms de colonnes

            bool isHeader = true;
            bool inQuotes = false;
            StringBuilder currentField = new StringBuilder();
            List<string> currentLine = new List<string>();

            // 3. Analyser le texte caractère par caractère (Machine à états)
            for (int i = dataStartIndex; i < fileContent.Length; i++)
            {
                char c = fileContent[i];
                char nextC = i + 1 < fileContent.Length ? fileContent[i + 1] : '\0';

                if (c == '"')
                {
                    if (inQuotes && nextC == '"') // Cas d'un guillemet échappé (Ex: "")
                    {
                        currentField.Append('"');
                        i++; // Ignorer le guillemet suivant
                    }
                    else
                    {
                        inQuotes = !inQuotes; // Entrer ou sortir d'un champ texte
                    }
                }
                else if (c == ';' && !inQuotes) // Changement de colonne
                {
                    currentLine.Add(currentField.ToString());
                    currentField.Clear();
                }
                else if ((c == '\r' || c == '\n') && !inQuotes) // Fin de ligne (en dehors des guillemets)
                {
                    if (c == '\r' && nextC == '\n') i++; // Gérer les retours chariots Windows (\r\n)

                    currentLine.Add(currentField.ToString());
                    currentField.Clear();

                    // Condition d'arrêt : fin de la table (ligne vide, tirets ou message d'erreur)
                    if (currentLine.Count == 1 && (string.IsNullOrWhiteSpace(currentLine[0]) || currentLine[0].StartsWith("---") || currentLine[0] == "AUCUNE DONNÉE TROUVÉE"))
                    {
                        break;
                    }

                    // Traitement de la ligne extraite
                    if (isHeader)
                    {
                        foreach (var field in currentLine)
                        {
                            string colName = field.Trim();
                            // Sécurité pour éviter que le DataTable plante si 2 colonnes ont le même nom
                            int suffix = 1;
                            while (dt.Columns.Contains(colName)) colName = $"{field.Trim()}_{suffix++}";

                            dt.Columns.Add(colName);
                        }
                        isHeader = false;
                    }
                    else if (currentLine.Any(x => !string.IsNullOrWhiteSpace(x))) // Ajouter les données si la ligne n'est pas complètement vide
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
                    currentField.Append(c); // Ajouter le caractère au champ en cours
                }
            }

            return dt;
        }
    }
}