using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using AutoActivator.Config;
using AutoActivator.Services;
using AutoActivator.Sql;

namespace AutoActivator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       AUTO-ACTIVATOR L.I.S.A (.NET VERSION)      ");
            Console.WriteLine("==================================================");
            Console.WriteLine("1. Run Single Extraction (LISA & ELIA)");
            Console.WriteLine("2. Run Activation");
            Console.WriteLine("3. Run Comparison");
            Console.WriteLine("0. Exit");
            Console.WriteLine("==================================================");
            Console.Write("Your choice: ");

            var choice = Console.ReadLine();

            try
            {
                if (!Directory.Exists(Settings.OutputDir)) Directory.CreateDirectory(Settings.OutputDir);
                if (!Directory.Exists(Settings.SnapshotDir)) Directory.CreateDirectory(Settings.SnapshotDir);
                if (!Directory.Exists(Settings.InputDir)) Directory.CreateDirectory(Settings.InputDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Unable to create directories: {ex.Message}");
            }

            switch (choice)
            {
                case "1":
                    RunTestExtraction();
                    break;
                case "2":
                    RunActivation();
                    break;
                case "3":
                    RunComparison();
                    break;
                case "0":
                    return;
                default:
                    Console.WriteLine("Invalid choice.");
                    break;
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        // =========================================================================
        // 1. EXTRACTION
        // =========================================================================
        private static void RunTestExtraction()
        {
            Console.WriteLine("\n--- Starting Extraction ---");
            Console.Write("Enter the contract number (e.g., 182-2728195-31): ");
            string input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) return;

            // Demander l'environnement pour utiliser la nouvelle logique de DatabaseManager
            Console.Write("Environment suffix (e.g., D000 or Q000): ");
            string envSuffix = Console.ReadLine() ?? "D000";
            if (string.IsNullOrWhiteSpace(envSuffix)) envSuffix = "D000";

            var extractionService = new ExtractionService();

            try
            {
                var result = extractionService.PerformExtraction(input, envSuffix, true);
                Console.WriteLine($"\n🎉 Extraction completed! Files generated in:\n--> {result.FilePath}");
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"[ERROR] {ex.Message}");
            }
        }

        // =========================================================================
        // 2. ACTIVATION
        // =========================================================================
        private static void RunActivation()
        {
            Console.WriteLine("\n--- Starting Activation Script ---");
            Console.Write("Environment suffix (e.g., D000 or Q000): ");
            string envSuffix = Console.ReadLine() ?? "D000";
            if (string.IsNullOrWhiteSpace(envSuffix)) envSuffix = "D000";

            // CORRECTION DE L'ERREUR ICI : On passe bien l'environnement au DatabaseManager
            var db = new DatabaseManager(envSuffix);
            if (!db.TestConnection()) return;

            // Simulation: Replace with your actual list if needed
            var contratsSources = new List<string> { "182-2728195-31" };

            foreach (var oldContractExt in contratsSources)
            {
                Console.WriteLine($"\n--- Processing: {oldContractExt} ---");
                var parameters = new Dictionary<string, object> { { "@ContractNumber", oldContractExt } };
                var dtId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);

                if (dtId.Rows.Count > 0)
                {
                    long idIntSource = Convert.ToInt64(dtId.Rows[0]["NO_CNT"]);
                    // Snapshot logic...
                    Console.WriteLine($"   [OK] Contract identified for activation.");
                }
                else
                {
                    Console.WriteLine($"   [SKIP] Contract {oldContractExt} not found.");
                }
            }
        }

        // =========================================================================
        // 3. COMPARISON
        // =========================================================================
        private static void RunComparison()
        {
            Console.WriteLine("\n--- Starting Comparator ---");
            Console.Write("Path to Base File (e.g., ExtractionLISA_...): ");
            string baseFile = Console.ReadLine()?.Trim('\"'); // Trim des guillemets si glissé-déposé

            Console.Write("Path to Target File (e.g., snapshot/ExtractionLISA_...): ");
            string targetFile = Console.ReadLine()?.Trim('\"');

            Console.Write("Table to compare (e.g., LV.PRCTT0): ");
            string tableName = Console.ReadLine();

            if (!File.Exists(baseFile) || !File.Exists(targetFile))
            {
                Console.WriteLine("[ERROR] One or both files do not exist.");
                return;
            }

            var orchestrator = new ComparisonOrchestrator();
            try
            {
                var report = orchestrator.RunFullComparison(baseFile, targetFile, tableName);

                Console.WriteLine($"\n=== COMPARISON REPORT ===");
                Console.WriteLine($"Global Success: {report.GlobalSuccessPercentage}%");
                Console.WriteLine($"Total Rows Compared: {report.TotalRowsCompared}");
                Console.WriteLine($"Total Differences: {report.TotalDifferencesFound}\n");

                foreach (var fileResult in report.FileResults)
                {
                    Console.WriteLine($"[{fileResult.Status}] {fileResult.FileType} - {fileResult.TableName}");
                    if (!fileResult.IsMatch && !string.IsNullOrEmpty(fileResult.ErrorDetails))
                    {
                        Console.WriteLine(fileResult.ErrorDetails);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Comparison failed: {ex.Message}");
            }
        }
    }
}