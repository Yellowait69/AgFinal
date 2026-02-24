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
        /// Fonction centrale de comparaison entre deux jeux de donnees (DataTables).

        /// <param name="dfRef">Les donnees extraites du contrat source.</param>
        /// <param name="dfNew">Les donnees extraites du nouveau contrat.</param>
        /// <param name="tableName">Le nom de la table analysee (ex: 'LV.SCNTT0').</param>
        /// <returns>Un tuple contenant le statut (OK/KO) et les details des differences.</returns>
        public static (string Status, string Details) CompareDataTables(DataTable dfRef, DataTable dfNew, string tableName)
        {
            // ETAPE 1 : Controles de validite initiaux
            if (dfRef.Rows.Count == 0 && dfNew.Rows.Count == 0)
            {
                return ("OK_EMPTY", null);
            }

            if (dfRef.Rows.Count == 0 || dfNew.Rows.Count == 0)
            {
                return ("KO_MISSING_DATA", $"L'un des deux DataFrames est vide pour la table {tableName}.");
            }

            // ETAPE 2 : Isolation des donnees (On travaille sur des copies)
            var df1 = dfRef.Copy();
            var df2 = dfNew.Copy();

            try
            {
                // ETAPE 3 : Application des regles d'exclusion
                var colsToDrop = Exclusions.GetExclusionsForTable(tableName);

                foreach (var col in colsToDrop)
                {
                    if (df1.Columns.Contains(col)) df1.Columns.Remove(col);
                    if (df2.Columns.Contains(col)) df2.Columns.Remove(col);
                }

                // ETAPE 4 : Alignement des schemas de donnees
                var commonCols = df1.Columns.Cast<DataColumn>()
                    .Select(c => c.ColumnName)
                    .Intersect(df2.Columns.Cast<DataColumn>().Select(c => c.ColumnName))
                    .OrderBy(c => c)
                    .ToList();

                if (!commonCols.Any())
                {
                    return ("KO_NO_COMMON_COLS", "Aucune colonne commune trouv�e apr�s l'application des filtres d'exclusion.");
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

                // ETAPE 5 : Normalisation et formatage des donnees
                NormalizeDataTable(df1, commonCols);
                NormalizeDataTable(df2, commonCols);

                // ETAPE 6 : Alignement des enregistrements (Tri)
                // On cree une chaine de tri "Col1, Col2, Col3..." pour stabiliser l'ordre des lignes
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
                    Console.WriteLine($"[WARNING] Le tri technique a �chou� sur la table {tableName}. Raison : {e.Message}");
                }

                // ETAPE 7 : Comparaison finale
                if (df1.Rows.Count != df2.Rows.Count)
                {
                    return ("KO_ROW_COUNT", $"�cart sur le volume de donn�es : Source = {df1.Rows.Count} lignes vs Cible = {df2.Rows.Count} lignes.");
                }

                return GenerateDiffReport(df1, df2, commonCols);
            }
            catch (Exception e)
            {
                return ("KO_ERROR", $"Erreur technique lors de la g�n�ration du diff�rentiel : {e.Message}");
            }
        }


        /// Nettoie et normalise les donnees pour eviter les faux positifs (espaces, decimales, nulls).

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

                    // Traitement des NaN ou None textuels
                    if (value.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        row[col] = string.Empty;
                    }
                    // Si c'est un nombre (Float), on l'arrondit a 4 decimales
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


        /// Compare cellule par cellule et genere un rapport

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
                    diffReport.AppendLine($"Ligne #{i + 1} diff�rente :");
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