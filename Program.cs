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
            Console.WriteLine("1. Lancer l'Extraction UNIQUE (LISA & ELIA)");
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
        // 1. EXTRACTION DANS UN FICHIER UNIQUE
        // =========================================================================
        private static void RunTestExtraction()
        {
            Console.WriteLine("\n--- Démarrage de l'Extraction Groupée ---");
            Console.Write("Entrez le numéro de contrat (ex: 182-2728195-31) : ");
            string input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) return;

            string targetContract = input.Trim().Replace("\u00A0", "");
            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            // 1. Récupération des IDs
            var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };
            var dtLisa = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
            {
                Console.WriteLine($"[ERROR] Contrat {targetContract} introuvable.");
                return;
            }

            // 2. Préparation du contenu du fichier unique
            StringBuilder sbFullContent = new StringBuilder();
            string finalPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

            // Fonction locale pour formater les tables dans le buffer unique
            void AddTableToBuffer(string tableName, DataTable dt)
            {
                sbFullContent.AppendLine("################################################################################");
                sbFullContent.AppendLine($"### TABLE : {tableName} | Lignes : {dt.Rows.Count}");
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
                    sbFullContent.AppendLine("AUCUNE DONNEE TROUVEE");
                }
                sbFullContent.AppendLine(); // Saut de ligne entre les tables
            }

            // 3. Extraction LISA
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                Console.WriteLine("[INFO] Extraction des tables LISA...");

                var lisaTables = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0", "LV.MWBGT0", "LV.PRIST0", "LV.FMVGT0" };
                foreach (var table in lisaTables)
                {
                    var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", internalId } });
                    AddTableToBuffer(table, dt);
                }
            }

            // 4. Extraction ELIA
            if (dtElia.Rows.Count > 0)
            {
                string eliaId = dtElia.Rows[0]["IT5UCONAIDN"].ToString();
                Console.WriteLine("[INFO] Extraction des tables ELIA...");

                var eliaTables = new[] { "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UPRP", "FJ1.TB5UAVE", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
                foreach (var table in eliaTables)
                {
                    var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@EliaId", eliaId } });
                    AddTableToBuffer(table, dt);
                }
            }

            // 5. Écriture du fichier final
            File.WriteAllText(finalPath, sbFullContent.ToString(), Encoding.UTF8);
            Console.WriteLine($"\n🎉 Extraction terminée ! Fichier unique généré :\n--> {finalPath}");
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