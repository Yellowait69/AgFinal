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
        /// Lance la comparaison complète. Ne garde en mémoire que le contrat le plus récent pour chaque Test ID.
        /// </summary>
        public ComparisonReport RunFullComparison(string baseFile, string targetFile)
        {
            var report = new ComparisonReport();

            // 1. Lire et filtrer les fichiers : on ne garde en mémoire que le texte du contrat LE PLUS RÉCENT pour chaque ID (ex: ID501)
            var baseBlocks = CsvFormatter.SplitAndFilterMostRecentByTestId(baseFile);
            var targetBlocks = CsvFormatter.SplitAndFilterMostRecentByTestId(targetFile);

            // Modification : on s'assure qu'au moins un des deux fichiers contient des données
            if (baseBlocks.Count == 0 && targetBlocks.Count == 0)
            {
                throw new Exception("Aucune donnée trouvée dans aucun des deux fichiers. Le format est-il correct ?");
            }

            // 2. Extraire les listes d'IDs de chaque fichier
            var baseIds = baseBlocks.Keys.ToList();
            var targetIds = targetBlocks.Keys.ToList();

            // 3. Trouver les IDs communs et les différences (tests manquants d'un côté ou de l'autre)
            var commonTestIds = baseIds.Intersect(targetIds).ToList();

            // On remplit le rapport avec les informations sur les tests
            report.TotalBaseTests = baseIds.Count;
            report.TotalTargetTests = targetIds.Count;
            report.ComparedTestsCount = commonTestIds.Count;
            report.MissingInTarget = baseIds.Except(targetIds).ToList(); // Présents dans Base, absents dans Target
            report.MissingInBase = targetIds.Except(baseIds).ToList();   // Présents dans Target, absents dans Base

            if (commonTestIds.Count == 0)
            {
                throw new Exception($"Aucun ID de Test commun n'a été trouvé. Comparaison impossible. (Base: {baseIds.Count} tests, Target: {targetIds.Count} tests)");
            }

            // 4. Boucle de comparaison uniquement sur les IDs communs
            foreach (var testId in commonTestIds)
            {
                string baseContent = baseBlocks[testId];
                string targetContent = targetBlocks[testId];

                // --- NOUVEAU : Récupération du Produit depuis la table FJ1.TB5UCON ---
                string product = "INCONNU";
                try
                {
                    DataTable uconTable = CsvFormatter.LoadTableFromContent(baseContent, "FJ1.TB5UCON");
                    if (uconTable != null && uconTable.Rows.Count > 0 && uconTable.Columns.Contains("IT5UCONCASU"))
                    {
                        product = uconTable.Rows[0]["IT5UCONCASU"]?.ToString();
                        if (string.IsNullOrWhiteSpace(product)) product = "INCONNU";
                    }
                }
                catch { /* Ignorer si la table n'existe pas ou n'a pas la colonne */ }

                // Initialisation des métriques
                if (!report.TestMetrics.ContainsKey(testId)) report.TestMetrics[testId] = new ComparisonMetrics();
                if (!report.ProductMetrics.ContainsKey(product)) report.ProductMetrics[product] = new ComparisonMetrics();
                // ---------------------------------------------------------------------

                // Obtenir toutes les tables impliquées pour ce contrat précis
                List<string> tablesToCompare = CsvFormatter.GetAllTableNamesFromContent(baseContent);

                foreach (var tableName in tablesToCompare)
                {
                    // Déduction automatique de la section (LISA ou ELIA)
                    string tableType = tableName.StartsWith("LV.") ? "LISA" : (tableName.StartsWith("FJ1.") ? "ELIA" : "COMBINED");

                    // Nom formaté pour l'affichage (ex: "[ID501] LV.PCONT0")
                    string displayTableName = $"[{testId}] {tableName}";

                    // Ajout de testId et product dans les paramètres de l'appel
                    CompareAndAppendToReport(baseContent, targetContent, tableName, displayTableName, tableType, report, Path.GetFileName(baseFile), Path.GetFileName(targetFile), testId, product);
                }
            }

            // Calcul du pourcentage de réussite global
            CalculateGlobalScore(report);

            return report;
        }

        private void CompareAndAppendToReport(string baseContent, string targetContent, string actualTableName, string displayTableName, string type, ComparisonReport report, string baseFileName, string targetFileName, string testId, string product)
        {
            // Charge la table spécifiée depuis les textes filtrés en mémoire
            DataTable df1 = CsvFormatter.LoadTableFromContent(baseContent, actualTableName);
            DataTable df2 = CsvFormatter.LoadTableFromContent(targetContent, actualTableName);

            // Comparaison avec affichage formaté (incluant l'ID de Test)
            var (Status, Details) = Comparator.CompareDataTables(df1, df2, displayTableName);

            var fileResult = new FileComparisonResult
            {
                FileType = type,
                BaseFileName = baseFileName,
                TargetFileName = targetFileName,
                TableName = displayTableName,
                Status = Status,
                ErrorDetails = Details
            };

            report.FileResults.Add(fileResult);

            // Mise à jour des compteurs pour le score global
            int rowsCount = Math.Max(df1?.Rows.Count ?? 0, df2?.Rows.Count ?? 0);
            int currentErrors = 0; // Variable pour compter spécifiquement les erreurs de cette table

            if (Status != "OK" && Status != "OK_EMPTY")
            {
                // Compte le nombre de lignes remontées avec des erreurs depuis "Details"
                if (!string.IsNullOrEmpty(Details))
                {
                    int diffCount = Details.Split(new[] { "Row #" }, StringSplitOptions.None).Length - 1;

                    // Si aucune ligne spécifique n'est pointée, on considère que toute la table a échoué
                    currentErrors = diffCount > 0 ? diffCount : rowsCount;
                }
                else
                {
                    currentErrors = rowsCount;
                }
            }

            // Mise à jour des scores globaux du rapport
            report.TotalRowsCompared += rowsCount;
            report.TotalDifferencesFound += currentErrors;

            // --- NOUVEAU : Mise à jour des statistiques ciblées (Test & Produit) ---
            if (rowsCount > 0)
            {
                report.TestMetrics[testId].TotalRows += rowsCount;
                report.TestMetrics[testId].ErrorRows += currentErrors;

                report.ProductMetrics[product].TotalRows += rowsCount;
                report.ProductMetrics[product].ErrorRows += currentErrors;
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