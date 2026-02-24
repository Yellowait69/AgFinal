using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AutoActivator.Config;
using AutoActivator.Services;
using AutoActivator.Sql; // Ajout nécessaire pour accéder à SqlQueries

namespace AutoActivator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       AUTO-ACTIVATOR L.I.S.A (.NET VERSION)      ");
            Console.WriteLine("==================================================");
            Console.WriteLine("1. Lancer le Test d'Extraction");
            Console.WriteLine("2. Lancer l'Activation");
            Console.WriteLine("3. Lancer la Comparaison");
            Console.WriteLine("0. Quitter");
            Console.WriteLine("==================================================");
            Console.Write("Votre choix : ");

            var choice = Console.ReadLine();

            // Création sécurisée des répertoires de travail définis dans Settings
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
                    Console.WriteLine("Fin du programme.");
                    return;
                default:
                    Console.WriteLine("Choix invalide.");
                    break;
            }

            Console.WriteLine("\nAppuyez sur une touche pour quitter...");
            Console.ReadKey();
        }

        // =========================================================================
        // 1. TEST D'EXTRACTION (Génération CSV native)
        // =========================================================================
        private static void RunTestExtraction()
        {
            Console.WriteLine("\n--- Démarrage du Test d'Extraction LISA ---");
            string targetContract = "182-2728195-31";

            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            Console.WriteLine($"Recherche de l'ID interne pour le contrat : {targetContract}");

            var p1 = new Dictionary<string, object> { { "@ContractNumber", targetContract } };
            var dfId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], p1);

            if (dfId.Rows.Count == 0)
            {
                string altContract = targetContract.Replace("-", "");
                Console.WriteLine($"[WARNING] Contrat introuvable. Essai sans tirets : {altContract}");
                var p2 = new Dictionary<string, object> { { "@ContractNumber", altContract } };
                dfId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], p2);

                if (dfId.Rows.Count == 0)
                {
                    Console.WriteLine($"[ERROR] Le contrat {targetContract} est introuvable.");
                    return;
                }
                targetContract = altContract;
            }

            long internalId = Convert.ToInt64(dfId.Rows[0]["NO_CNT"]);
            Console.WriteLine($" Contrat trouvé ! ID Interne (NO_CNT) = {internalId}");

            var tablesToExtract = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0" };

            string extractionDir = Path.Combine(Settings.OutputDir, $"extraction_brute_{targetContract}");
            if (!Directory.Exists(extractionDir)) Directory.CreateDirectory(extractionDir);

            foreach (var table in tablesToExtract)
            {
                if (!SqlQueries.Queries.ContainsKey(table)) continue;

                var parameters = new Dictionary<string, object> { { "@InternalId", internalId } };
                var dfTable = db.GetData(SqlQueries.Queries[table], parameters);

                string sheetName = table.Replace("LV.", "");
                string csvPath = Path.Combine(extractionDir, $"{sheetName}.csv");

                if (dfTable.Rows.Count > 0)
                {
                    WriteDataTableToCsv(dfTable, csvPath);
                    Console.WriteLine($"  -> {table} : {dfTable.Rows.Count} lignes extraites.");
                }
                else
                {
                    File.WriteAllText(csvPath, "Info\nAucune donnee trouvee", Encoding.UTF8);
                    Console.WriteLine($"  -> {table} : Vide.");
                }
            }

            Console.WriteLine($"🎉 Extraction terminée ! Dossier : {extractionDir}");
        }

        // =========================================================================
        // 2. ACTIVATION (Export CSV Natif)
        // =========================================================================
        private static void RunActivation()
        {
            Console.WriteLine("\n--- Démarrage du Script d'Activation ---");
            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            var contratsSources = new List<string> { "12345678", "87654321" };
            var resultats = new List<Dictionary<string, object>>();

            foreach (var oldContractExt in contratsSources)
            {
                Console.WriteLine($"\n--- Traitement : {oldContractExt} ---");

                var parameters = new Dictionary<string, object> { { "@ContractNumber", oldContractExt } };
                var dtId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);

                decimal montantPrime = 100.00m;

                if (dtId.Rows.Count > 0)
                {
                    long idIntSource = Convert.ToInt64(dtId.Rows[0]["NO_CNT"]);

                    foreach (var table in new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0" })
                    {
                        var pTable = new Dictionary<string, object> { { "@InternalId", idIntSource } };
                        var dtSource = db.GetData(SqlQueries.Queries[table], pTable);
                        dtSource.TableName = table;

                        // Utilisation de WriteXml natif pour le snapshot
                        string xmlPath = Path.Combine(Settings.SnapshotDir, $"{oldContractExt}_{table}.xml");
                        dtSource.WriteXml(xmlPath, XmlWriteMode.WriteSchema);
                    }
                    Console.WriteLine($"   [Snapshot] Sauvegarde source terminée.");
                }

                // Simulation de duplication (Logique métier LISA)
                string newContractExt = "999" + (oldContractExt.Length > 6 ? oldContractExt.Substring(oldContractExt.Length - 6) : oldContractExt);
                Thread.Sleep(500);

                var pNewId = new Dictionary<string, object> { { "@ContractNumber", newContractExt } };
                var dtNewId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], pNewId);

                long idIntNew = 0;
                string status = "KO_NOT_FOUND_IN_LISA";

                if (dtNewId.Rows.Count > 0)
                {
                    idIntNew = Convert.ToInt64(dtNewId.Rows[0]["NO_CNT"]);
                    bool isPaid = db.InjectPayment(idIntNew, montantPrime);
                    status = isPaid ? "OK_PAID" : "KO_PAYMENT";
                }

                resultats.Add(new Dictionary<string, object>
                {
                    { "Ancien_Contrat", oldContractExt },
                    { "Nouveau_Contrat", newContractExt },
                    { "ID_Interne_New", idIntNew },
                    { "Montant_Paye", montantPrime },
                    { "Date_Injection", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "Statut", status }
                });
            }

            if (resultats.Any())
            {
                string csvOutput = Path.ChangeExtension(Settings.ActivationOutputFile, ".csv");
                var sb = new StringBuilder();
                var keys = resultats.First().Keys.ToList();

                sb.AppendLine(string.Join(";", keys));
                foreach (var res in resultats)
                {
                    var values = keys.Select(k => res[k]?.ToString() ?? "").ToArray();
                    sb.AppendLine(string.Join(";", values));
                }

                File.WriteAllText(csvOutput, sb.ToString(), Encoding.UTF8);
                Console.WriteLine($"\n--- Fichier généré : {csvOutput} ---");
            }
        }

        // =========================================================================
        // 3. COMPARAISON (Lecture XML et CSV native)
        // =========================================================================
        private static void RunComparison()
        {
            Console.WriteLine("\n--- Démarrage du Comparateur ---");

            string inputCsv = Path.ChangeExtension(Settings.InputFile, ".csv");
            if (!File.Exists(inputCsv))
            {
                Console.WriteLine($"[ERROR] CSV introuvable : {inputCsv}");
                return;
            }

            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            var reportData = new StringBuilder();
            reportData.AppendLine("Reference_Contract;New_Contract;Table;Status;Details");

            var statsList = new List<(string Product, string Contract, string Status)>();
            var lines = File.ReadAllLines(inputCsv).Skip(1);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var columns = line.Split(';');
                if (columns.Length < 6) continue;

                string refContract = columns[0].Trim();
                string newContract = columns[1].Trim();
                string statutJ0 = columns[5].Trim();

                if (!statutJ0.StartsWith("OK", StringComparison.OrdinalIgnoreCase)) continue;

                var pNewId = new Dictionary<string, object> { { "@ContractNumber", newContract } };
                var dtNewId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], pNewId);

                if (dtNewId.Rows.Count == 0) continue;
                long idNew = Convert.ToInt64(dtNewId.Rows[0]["NO_CNT"]);

                // Récupération code produit
                string productCode = "UNKNOWN";
                var dtProd = db.GetData("SELECT TOP 1 C_PROP_PRINC FROM LV.SCNTT0 WHERE NO_CNT = @Id", new Dictionary<string, object>{{"@Id", idNew}});
                if (dtProd.Rows.Count > 0) productCode = dtProd.Rows[0][0].ToString().Trim();

                string contractGlobalStatus = "OK";
                var tables = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0" };

                foreach (var table in tables)
                {
                    string snapshotPath = Path.Combine(Settings.SnapshotDir, $"{refContract}_{table}.xml");
                    if (!File.Exists(snapshotPath)) continue;

                    DataTable dtRef = new DataTable();
                    dtRef.ReadXml(snapshotPath);

                    var dtNew = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", idNew } });

                    var (status, details) = Comparator.CompareDataTables(dtRef, dtNew, table);
                    if (status.StartsWith("KO")) contractGlobalStatus = "KO";

                    reportData.AppendLine($"{refContract};{newContract};{table};{status};{details?.Replace("\n", " ")}");
                }
                statsList.Add((productCode, refContract, contractGlobalStatus));
            }

            File.WriteAllText(Path.Combine(Settings.OutputDir, "rapport_final.csv"), reportData.ToString(), Encoding.UTF8);
            Console.WriteLine("Rapport généré.");
        }

        private static void WriteDataTableToCsv(DataTable dataTable, string filePath)
        {
            var sb = new StringBuilder();
            var columns = dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
            sb.AppendLine(string.Join(";", columns));

            foreach (DataRow row in dataTable.Rows)
            {
                var fields = row.ItemArray.Select(f => f?.ToString().Replace(";", " ").Replace("\n", " ") ?? "");
                sb.AppendLine(string.Join(";", fields));
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}