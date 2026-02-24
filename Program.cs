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
            Console.WriteLine("1. Lancer le Test d'Extraction (LISA & ELIA)");
            Console.WriteLine("2. Lancer l'Activation");
            Console.WriteLine("3. Lancer la Comparaison");
            Console.WriteLine("0. Quitter");
            Console.WriteLine("==================================================");
            Console.Write("Votre choix : ");

            var choice = Console.ReadLine();

            try
            {
                if (!Directory.Exists(Settings.OutputDir)) Directory.CreateDirectory(Settings.OutputDir);
                if (!Directory.Exists(Settings.SnapshotDir)) Directory.CreateDirectory(Settings.SnapshotDir);
                if (!Directory.Exists(Settings.InputDir)) Directory.CreateDirectory(Settings.InputDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Impossible de créer les répertoires : {ex.Message}");
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
                    Console.WriteLine("Choix invalide.");
                    break;
            }

            Console.WriteLine("\nAppuyez sur une touche pour quitter...");
            Console.ReadKey();
        }

        // =========================================================================
        // 1. TEST D'EXTRACTION (LISA & ELIA)
        // =========================================================================
        private static void RunTestExtraction()
        {
            Console.WriteLine("\n--- Démarrage de l'Extraction Complète ---");
            Console.Write("Entrez le numéro de contrat (ex: 182-2728195-31) : ");
            string input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("[ERROR] Aucun numéro de contrat saisi.");
                return;
            }

            // Nettoyage de l'input (suppression espaces insécables et trim)
            string targetContract = input.Trim().Replace("\u00A0", "");

            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            // 1. Récupération des IDs (LISA et ELIA)
            var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };

            var dtLisa = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
            {
                Console.WriteLine($"[ERROR] Le contrat {targetContract} est introuvable dans toutes les bases.");
                return;
            }

            string extractionDir = Path.Combine(Settings.OutputDir, $"extraction_{targetContract}_{DateTime.Now:yyyyMMdd_HHmmss}");
            if (!Directory.Exists(extractionDir)) Directory.CreateDirectory(extractionDir);

            // 2. Extraction LISA (si ID trouvé)
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                Console.WriteLine($"[INFO] Contrat LISA trouvé (ID: {internalId}). Extraction des tables LV...");

                var lisaTables = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0", "LV.MWBGT0" };
                foreach (var table in lisaTables)
                {
                    var p = new Dictionary<string, object> { { "@InternalId", internalId } };
                    var dt = db.GetData(SqlQueries.Queries[table], p);
                    WriteDataTableToCsv(dt, Path.Combine(extractionDir, $"{table.Replace("LV.", "")}.csv"));
                }
            }

            // 3. Extraction ELIA (si ID trouvé)
            if (dtElia.Rows.Count > 0)
            {
                string eliaId = dtElia.Rows[0]["IT5UCONAIDN"].ToString();
                Console.WriteLine($"[INFO] Contrat ELIA trouvé (ID: {eliaId}). Extraction des tables FJ1...");

                var eliaTables = new[] { "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UPRP", "FJ1.TB5UAVE", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
                foreach (var table in eliaTables)
                {
                    var p = new Dictionary<string, object> { { "@EliaId", eliaId } };
                    var dt = db.GetData(SqlQueries.Queries[table], p);
                    WriteDataTableToCsv(dt, Path.Combine(extractionDir, $"{table.Replace("FJ1.", "")}.csv"));
                }
            }

            Console.WriteLine($"\n🎉 Extraction terminée avec succès !");
            Console.WriteLine($"Dossier : {extractionDir}");
        }

        // =========================================================================
        // 2. ACTIVATION
        // =========================================================================
        private static void RunActivation()
        {
            Console.WriteLine("\n--- Démarrage du Script d'Activation ---");
            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            // Simulation : Remplace par ta liste réelle si besoin
            var contratsSources = new List<string> { "182-2728195-31" };
            var resultats = new List<Dictionary<string, object>>();

            foreach (var oldContractExt in contratsSources)
            {
                Console.WriteLine($"\n--- Traitement : {oldContractExt} ---");
                var parameters = new Dictionary<string, object> { { "@ContractNumber", oldContractExt } };
                var dtId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);

                if (dtId.Rows.Count > 0)
                {
                    long idIntSource = Convert.ToInt64(dtId.Rows[0]["NO_CNT"]);
                    // Logic de snapshot...
                    Console.WriteLine($"   [OK] Contrat identifié pour activation.");
                }
                else
                {
                    Console.WriteLine($"   [SKIP] Contrat {oldContractExt} non trouvé.");
                }
            }
        }

        // =========================================================================
        // 3. COMPARAISON
        // =========================================================================
        private static void RunComparison()
        {
            Console.WriteLine("\n--- Démarrage du Comparateur ---");
            // Logique de comparaison telle qu'existante, en utilisant les nouveaux accès Queries
            // ... (identique à ton code original mais utilisant DatabaseManager)
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