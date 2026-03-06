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
        // Le format de fichier actuel est "Extraction_Env_Size_Signature_Timestamp.csv"
        // Exemple : "Extraction_D_Uniq_182-2728195-31_20260228_153000.csv"

        /// <summary>
        /// Extrait l'environnement, la taille et la signature du nom de fichier pour le filtrage.
        /// </summary>
        public string[] GetFileIds(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parts = fileName.Split('_');

            // Attend au moins : Prefix(0)_Env(1)_Size(2)_Signature(3)
            if (parts.Length >= 4)
            {
                return new[] { parts[1], parts[2], parts[3] };
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// Retourne la liste des fichiers cibles compatibles (ayant le même Env, Size et Signature).
        /// </summary>
        public List<string> GetCompatibleTargetFiles(string baseFilePath, IEnumerable<string> allAvailableFiles)
        {
            var baseIds = GetFileIds(baseFilePath);
            if (baseIds.Length != 3) return new List<string>();

            return allAvailableFiles.Where(file =>
            {
                // Ignorer le fichier de base lui-même
                if (file.Equals(baseFilePath, StringComparison.OrdinalIgnoreCase)) return false;

                var targetIds = GetFileIds(file);
                return targetIds.Length == 3 &&
                       targetIds[0] == baseIds[0] &&
                       targetIds[1] == baseIds[1] &&
                       targetIds[2] == baseIds[2];
            }).ToList();
        }

        /// <summary>
        /// Lance la comparaison complète. Lit toutes les tables du fichier combiné et les compare une par une.
        /// </summary>
        public ComparisonReport RunFullComparison(string baseFile, string targetFile)
        {
            var report = new ComparisonReport();

            // Récupère automatiquement TOUTES les tables contenues dans le fichier combiné
            List<string> tablesToCompare = CsvFormatter.GetAllTableNames(baseFile);

            if (tablesToCompare.Count == 0)
            {
                throw new Exception("Aucune table trouvée dans le fichier de base. Le format est-il correct ?");
            }

            // Boucle de comparaison sur chaque table trouvée
            foreach (var tableName in tablesToCompare)
            {
                // Déduction automatique de la section (LISA ou ELIA) selon le préfixe de la table
                // "LV." correspond généralement à LISA et "FJ1." à ELIA
                string tableType = tableName.StartsWith("LV.") ? "LISA" : (tableName.StartsWith("FJ1.") ? "ELIA" : "COMBINED");

                // Comparaison de la table extraite depuis les deux fichiers globaux
                CompareAndAppendToReport(baseFile, targetFile, tableName, tableType, report);
            }

            // Calcul du pourcentage de réussite global
            CalculateGlobalScore(report);

            return report;
        }

        private void CompareAndAppendToReport(string file1, string file2, string tableName, string type, ComparisonReport report)
        {
            // Utilisation du parseur CSV pour extraire la table spécifique du fichier combiné
            DataTable df1 = CsvFormatter.LoadTableFromCsv(file1, tableName);
            DataTable df2 = CsvFormatter.LoadTableFromCsv(file2, tableName);

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

            // Mise à jour des compteurs pour le score global
            int rowsCount = Math.Max(df1?.Rows.Count ?? 0, df2?.Rows.Count ?? 0);
            report.TotalRowsCompared += rowsCount;

            if (Status != "OK" && Status != "OK_EMPTY")
            {
                //  Compte le nombre de lignes remontées avec des erreurs depuis "Details"
                if (!string.IsNullOrEmpty(Details))
                {
                    // Compte le nombre d'occurrences de "Row #" dans le rapport pour savoir combien de lignes ont échoué
                    int diffCount = Details.Split(new[] { "Row #" }, StringSplitOptions.None).Length - 1;

                    // Si aucune ligne spécifique n'est pointée (erreur structurelle), on considère que toute la table a échoué
                    report.TotalDifferencesFound += diffCount > 0 ? diffCount : rowsCount;
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

            // Empêche les valeurs négatives si des erreurs structurelles faussent le calcul global
            if (successfulRows < 0) successfulRows = 0;

            report.GlobalSuccessPercentage = Math.Round(((double)successfulRows / report.TotalRowsCompared) * 100, 2);
        }
    }
}