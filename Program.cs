using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        // 1. EXTRACTION TO A SINGLE FILE
        // =========================================================================
        private static void RunTestExtraction()
        {
            Console.WriteLine("\n--- Starting Batch Extraction ---");
            Console.Write("Enter the contract number (e.g., 182-2728195-31): ");
            string input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) return;

            string targetContract = input.Trim().Replace("\u00A0", "");
            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            // 1. Retrieve IDs
            var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };
            var dtLisa = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
            {
                Console.WriteLine($"[ERROR] Contract {targetContract} not found.");
                return;
            }

            // 2. Prepare content for the single file
            StringBuilder sbFullContent = new StringBuilder();
            string finalPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

            // Local function to format tables in the single buffer
            void AddTableToBuffer(string tableName, DataTable dt)
            {
                sbFullContent.AppendLine("################################################################################");
                sbFullContent.AppendLine($"### TABLE : {tableName} | Rows : {dt.Rows.Count}");
                sbFullContent.AppendLine("################################################################################");

                if (dt.Rows.Count > 0)
                {
                    var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                    sbFullContent.AppendLine(string.Join(";", columns));

                    foreach (DataRow row in dt.Rows)
                    {
                        var fields = row.ItemArray.Select(f => f?.ToString().Replace(";", " ").Replace("\n", " ").Trim());
                        sbFullContent.AppendLine(string.Join(";", fields));
                    }
                }
                else
                {
                    sbFullContent.AppendLine("NO DATA FOUND");
                }
                sbFullContent.AppendLine(); // Line break between tables
            }

            // 3. LISA Extraction
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                Console.WriteLine("[INFO] Extracting LISA tables...");

                var lisaTables = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0", "LV.MWBGT0", "LV.PRIST0", "LV.FMVGT0" };
                foreach (var table in lisaTables)
                {
                    var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", internalId } });
                    AddTableToBuffer(table, dt);
                }
            }

            // 4. ELIA Extraction
            if (dtElia.Rows.Count > 0)
            {
                string eliaId = dtElia.Rows[0]["IT5UCONAIDN"].ToString();
                Console.WriteLine("[INFO] Extracting ELIA tables...");

                var eliaTables = new[] { "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UPRP", "FJ1.TB5UAVE", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
                foreach (var table in eliaTables)
                {
                    var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@EliaId", eliaId } });
                    AddTableToBuffer(table, dt);
                }
            }

            // 5. Write final file
            File.WriteAllText(finalPath, sbFullContent.ToString(), Encoding.UTF8);
            Console.WriteLine($"\n🎉 Extraction completed! Single file generated:\n--> {finalPath}");
        }

        // =========================================================================
        // 2. ACTIVATION
        // =========================================================================
        private static void RunActivation()
        {
            Console.WriteLine("\n--- Starting Activation Script ---");
            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            // Simulation: Replace with your actual list if needed
            var contratsSources = new List<string> { "182-2728195-31" };
            var resultats = new List<Dictionary<string, object>>();

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
            // Existing comparison logic, using the new Queries access
            // ... (identical to your original code but using DatabaseManager)
        }

        private static void WriteDataTableToCsv(DataTable dataTable, string filePath)
        {
            if (dataTable == null) return;
            var sb = new StringBuilder();

            // Header
            var columns = dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
            sb.AppendLine(string.Join(";", columns));

            // Data
            foreach (DataRow row in dataTable.Rows)
            {
                var fields = row.ItemArray.Select(f => {
                    string val = f?.ToString() ?? "";
                    return val.Replace(";", " ").Replace("\n", " ").Trim();
                });
                sb.AppendLine(string.Join(";", fields));
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}