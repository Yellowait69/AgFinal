using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using AutoActivator.Config; // Ajustez selon votre namespace pour accéder à Exclusions

namespace AutoActivator.Services
{
    public static class Comparator
    {
        /// <summary>
        /// Fonction centrale de comparaison entre deux jeux de données (DataTables).
        /// </summary>
        /// <param name="dfRef">Les données extraites du contrat source.</param>
        /// <param name="dfNew">Les données extraites du nouveau contrat.</param>
        /// <param name="tableName">Le nom de la table analysée (ex: 'LV.SCNTT0').</param>
        /// <returns>Un tuple contenant le statut (OK/KO) et les détails des différences.</returns>
        public static (string Status, string Details) CompareDataTables(DataTable dfRef, DataTable dfNew, string tableName)
        {
            // ÉTAPE 1 : Contrôles de validité initiaux
            if (dfRef.Rows.Count == 0 && dfNew.Rows.Count == 0)
            {
                return ("OK_EMPTY", null);
            }

            if (dfRef.Rows.Count == 0 || dfNew.Rows.Count == 0)
            {
                return ("KO_MISSING_DATA", $"L'un des deux DataFrames est vide pour la table {tableName}.");
            }

            // ÉTAPE 2 : Isolation des données (On travaille sur des copies)
            var df1 = dfRef.Copy();
            var df2 = dfNew.Copy();

            try
            {
                // ÉTAPE 3 : Application des règles d'exclusion
                var colsToDrop = Exclusions.GetExclusionsForTable(tableName);

                foreach (var col in colsToDrop)
                {
                    if (df1.Columns.Contains(col)) df1.Columns.Remove(col);
                    if (df2.Columns.Contains(col)) df2.Columns.Remove(col);
                }

                // ÉTAPE 4 : Alignement des schémas de données (Intersection)
                var commonCols = df1.Columns.Cast<DataColumn>()
                    .Select(c => c.ColumnName)
                    .Intersect(df2.Columns.Cast<DataColumn>().Select(c => c.ColumnName))
                    .OrderBy(c => c)
                    .ToList();

                if (!commonCols.Any())
                {
                    return ("KO_NO_COMMON_COLS", "Aucune colonne commune trouvée après l'application des filtres d'exclusion.");
                }

                // Suppression des colonnes non communes pour aligner parfaitement les DataTables
                for (int i = df1.Columns.Count - 1; i >= 0; i--)
                {
                    if (!commonCols.Contains(df1.Columns[i].ColumnName)) df1.Columns.RemoveAt(i);
                }
                for (int i = df2.Columns.Count - 1; i >= 0; i--)
                {
                    if (!commonCols.Contains(df2.Columns[i].ColumnName)) df2.Columns.RemoveAt(i);
                }

                // ÉTAPE 5 : Normalisation et formatage des données
                NormalizeDataTable(df1, commonCols);
                NormalizeDataTable(df2, commonCols);

                // ÉTAPE 6 : Alignement des enregistrements (Tri)
                // On crée une chaîne de tri "Col1, Col2, Col3..." pour stabiliser l'ordre des lignes
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
                    Console.WriteLine($"[WARNING] Le tri technique a échoué sur la table {tableName}. Raison : {e.Message}");
                }

                // ÉTAPE 7 : Comparaison finale
                if (df1.Rows.Count != df2.Rows.Count)
                {
                    return ("KO_ROW_COUNT", $"Écart sur le volume de données : Source = {df1.Rows.Count} lignes vs Cible = {df2.Rows.Count} lignes.");
                }

                return GenerateDiffReport(df1, df2, commonCols);
            }
            catch (Exception e)
            {
                return ("KO_ERROR", $"Erreur technique lors de la génération du différentiel : {e.Message}");
            }
        }

        /// <summary>
        /// Nettoie et normalise les données pour éviter les faux positifs (espaces, décimales, nulls).
        /// </summary>
        private static void NormalizeDataTable(DataTable dt, List<string> columns)
        {
            foreach (DataRow row in dt.Rows)
            {
                foreach (var col in columns)
                {
                    if (row[col] == DBNull.Value || row[col] == null)
                    {
                        row[col] = string.Empty; // Normaliser les nulls
                        continue;
                    }

                    string value = row[col].ToString().Trim();

                    // Traitement des NaN ou None textuels (héritage de certains imports ou comportements Python)
                    if (value.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        row[col] = string.Empty;
                    }
                    // Si c'est un nombre (Float), on l'arrondit à 4 décimales
                    else if (double.TryParse(value, out double floatValue))
                    {
                        row[col] = Math.Round(floatValue, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        row[col] = value; // String propre
                    }
                }
            }
        }

        /// <summary>
        /// Compare cellule par cellule et génère un rapport similaire à pandas.compare().
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
                        rowDiffs.AppendLine($"    -> {commonCols[j]} : Source = '{val1}' | Cible = '{val2}'");
                    }
                }

                if (rowHasDiff)
                {
                    diffReport.AppendLine($"Ligne #{i + 1} différente :");
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