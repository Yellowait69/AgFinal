using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoActivator.Config;
using AutoActivator.Services;
using AutoActivator.Sql;

namespace AutoActivator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       AUTO-ACTIVATOR L.I.S.A (.NET VERSION)      ");
            Console.WriteLine("==================================================");
            Console.WriteLine("1. Run Single Extraction (LISA & ELIA)");
            Console.WriteLine("2. Run Massive Batch Activation");
            Console.WriteLine("3. Run Comparison");
            Console.WriteLine("0. Exit");
            Console.WriteLine("==================================================");
            Console.Write("Your choice: ");

            var choice = Console.ReadLine();

            // Création des dossiers de base s'ils n'existent pas
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
                    await RunTestExtractionAsync();
                    break;
                case "2":
                    await RunActivationAsync();
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

        // -----------------------------------------------------------
        // 1. EXTRACTION
        // -----------------------------------------------------------
        private static async Task RunTestExtractionAsync()
        {
            Console.WriteLine("\n--- Starting Extraction ---");
            Console.Write("Enter the contract number (e.g., 182-2728195-31): ");
            string input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) return;

            Console.Write("Environment suffix (e.g., D000 or Q000): ");
            string envSuffix = Console.ReadLine()?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(envSuffix)) envSuffix = "D000";

            var extractionService = new ExtractionService();

            try
            {
                var result = await extractionService.PerformExtractionAsync(input, envSuffix, true, false);
                Console.WriteLine($"\n🎉 Extraction completed! Files generated in:\n--> {result.FilePath}");
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"[ERROR] Extraction failed:\n{ex.ToString()}");
            }
        }

        // -----------------------------------------------------------
        // 2. ACTIVATION (Mode MASSIVE BATCH)
        // -----------------------------------------------------------
        private static async Task RunActivationAsync()
        {
            Console.WriteLine("\n--- Starting Massive Batch Activation ---");
            Console.Write("Environment suffix (e.g., D000 or Q000): ");
            string envSuffix = Console.ReadLine()?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(envSuffix)) envSuffix = "D000";

            // Extraction de la lettre de l'environnement (ex: 'D' pour D000)
            string envLetter = envSuffix.Substring(0, 1);

            // L'API Mainframe requiert des identifiants
            Console.Write("Username Mainframe (ex: XA3894): ");
            string username = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(username)) username = "XA3894";

            Console.Write("Password Mainframe : ");
            string password = Console.ReadLine()?.Trim();

            // Menu pour charger les contrats
            Console.WriteLine("\nHow do you want to load the contracts?");
            Console.WriteLine("1. Enter a single contract manually");
            Console.WriteLine($"2. Load a list from a text file (located in '{Settings.InputDir}')");
            Console.Write("Choice: ");
            string inputChoice = Console.ReadLine()?.Trim();

            var contractsToProcess = new List<string>();

            if (inputChoice == "1")
            {
                Console.Write("Enter contract number (e.g., 182-2728195-31): ");
                string contract = Console.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(contract))
                {
                    contractsToProcess.Add(contract);
                }
            }
            else if (inputChoice == "2")
            {
                Console.Write("Enter the file name (e.g., contrats.txt): ");
                string fileName = Console.ReadLine()?.Trim() ?? "";
                string filePath = Path.Combine(Settings.InputDir, fileName);

                if (File.Exists(filePath))
                {
                    contractsToProcess = File.ReadAllLines(filePath)
                                             .Where(line => !string.IsNullOrWhiteSpace(line))
                                             .Select(line => line.Trim())
                                             .ToList();
                    Console.WriteLine($"\n[INFO] Successfully loaded {contractsToProcess.Count} contracts from {fileName}.");
                }
                else
                {
                    Console.WriteLine($"[ERROR] File not found: {filePath}");
                    return;
                }
            }
            else
            {
                Console.WriteLine("[ERROR] Invalid choice. Returning to main menu.");
                return;
            }

            if (contractsToProcess.Count == 0)
            {
                Console.WriteLine("[WARNING] No contracts to process. Aborting activation.");
                return;
            }

            // CRÉATION D'UN FICHIER CSV TEMPORAIRE
            // Permet d'utiliser la vraie méthode RunBatchAsync sans modifier BatchActivationService.cs
            string tempCsvPath = Path.Combine(Settings.InputDir, $"temp_batch_{Guid.NewGuid():N}.csv");

            try
            {
                File.WriteAllText(tempCsvPath, "Contract\n" + string.Join("\n", contractsToProcess), Encoding.UTF8);

                // FIX CS7036 : Instanciation avec l'injection de dépendance requise
                var dataService = new ActivationDataService();
                var batchActivationService = new BatchActivationService(dataService);

                Console.WriteLine($"\n[INFO] Launching Bulk Activation for {contractsToProcess.Count} item(s)...");

                // FIX CS1061 : Appel à la vraie méthode d'orchestration existante
                var result = await batchActivationService.RunBatchAsync(
                    filePath: tempCsvPath,
                    isDemandId: false,
                    envValue: envLetter,
                    cus: "000",             // Valeurs métier par défaut pour la console
                    bucp: "00000",
                    cmdpmt: "1",
                    channel: "C05",
                    skipPrime: false,
                    username: username,
                    password: password,
                    outputDir: Settings.OutputDir,
                    onProgress: msg => Console.WriteLine(msg), // Redirige les logs vers la console
                    token: CancellationToken.None
                );

                Console.WriteLine("\n==================================================");
                Console.WriteLine("🎉 BATCH ACTIVATION PROCESS FINISHED!");
                Console.WriteLine($"Success: {result.successCount} | Already Active: {result.alreadyActiveCount} | Errors: {result.errorCount}");
                Console.WriteLine($"Report saved at: {result.reportPath}");
                Console.WriteLine("==================================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL ERROR] Bulk Activation failed:\n{ex.ToString()}");
            }
            finally
            {
                // Nettoyage propre du fichier temporaire
                if (File.Exists(tempCsvPath))
                {
                    File.Delete(tempCsvPath);
                }
            }
        }

        // -----------------------------------------------------------
        // 3. COMPARISON
        // -----------------------------------------------------------
        private static void RunComparison()
        {
            Console.WriteLine("\n--- Starting Comparator ---");
            Console.Write("Path to Base File (e.g., ExtractionLISA_...): ");
            string baseFile = Console.ReadLine()?.Trim('\"');

            Console.Write("Path to Target File (e.g., snapshot/ExtractionLISA_...): ");
            string targetFile = Console.ReadLine()?.Trim('\"');

            if (!File.Exists(baseFile) || !File.Exists(targetFile))
            {
                Console.WriteLine("[ERROR] One or both files do not exist.");
                return;
            }

            var orchestrator = new ComparisonOrchestrator();
            try
            {
                var report = orchestrator.RunFullComparison(baseFile, targetFile);

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
                Console.WriteLine($"[ERROR] Comparison failed:\n{ex.ToString()}");
            }
        }
    }
}