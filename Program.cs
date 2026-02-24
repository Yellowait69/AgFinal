using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AutoActivator.Config;
using AutoActivator.Services;

namespace AutoActivator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       AUTO-ACTIVATOR L.I.S.A (.NET VERSION)      ");
            Console.WriteLine("==================================================");
            Console.WriteLine("1. Lancer le Test d'Extraction (test_extraction.py)");
            Console.WriteLine("2. Lancer l'Activation (run_activation.py)");
            Console.WriteLine("3. Lancer la Comparaison (run_comparison.py)");
            Console.WriteLine("0. Quitter");
            Console.WriteLine("==================================================");
            Console.Write("Votre choix : ");

            var choice = Console.ReadLine();

            Directory.CreateDirectory(Settings.OutputDir);
            Directory.CreateDirectory(Settings.SnapshotDir);
            Directory.CreateDirectory(Settings.InputDir);

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
                    break;
                default:
                    Console.WriteLine("Choix invalide.");
                    break;
            }

            Console.WriteLine("Appuyez sur une touche pour quitter...");
            Console.ReadKey();
        }

        // =========================================================================
        // 1. TEST D'EXTRACTION (Génération CSV sans ClosedXML)
        // =========================================================================
        private static void RunTestExtraction()
        {
            Console.WriteLine("\n--- Démarrage du Test d'Extraction LISA ---");
            string targetContract = "182-2728195-31";

            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            Console.WriteLine($"Recherche de l'ID interne pour le contrat externe : {targetContract}");

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

            // Création d'un dossier d'extraction pour y mettre les CSV
            string extractionDir = Path.Combine(Settings.OutputDir, $"extraction_brute_{targetContract}");
            Directory.CreateDirectory(extractionDir);

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
                    Console.WriteLine($"  -> {table} : {dfTable.Rows.Count} lignes extraites (sauvegardé dans {sheetName}.csv).");
                }
                else
                {
                    File.WriteAllText(csvPath, "Info\nAucune donnee trouvee");
                    Console.WriteLine($"  -> {table} : Vide (0 ligne).");
                }
            }

            Console.WriteLine($"🎉 Extraction terminée ! Dossier généré : {extractionDir}");
        }

        // =========================================================================
        // 2. ACTIVATION (Export CSV au lieu d'Excel)
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
                Console.WriteLine($"\n--- Traitement Source : {oldContractExt} ---");

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
                        dtSource.WriteXml(Path.Combine(Settings.SnapshotDir, $"{oldContractExt}_{table}.xml"), XmlWriteMode.WriteSchema);
                    }
                    Console.WriteLine($"   [Snapshot] 📸 Sauvegarde de l'état source pour {oldContractExt} terminée.");
                }

                string newContractExt = "999" + oldContractExt.Substring(Math.Max(0, oldContractExt.Length - 6));
                Console.WriteLine($"   [Simulation] Duplication ELIA : Source {oldContractExt} -> Cible {newContractExt}");
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
                Console.WriteLine($"\n--- Fichier de suivi généré : {csvOutput} ---");
            }
        }

        // =========================================================================
        // 3. COMPARAISON (Lecture de CSV au lieu d'Excel)
        // =========================================================================
        private static void RunComparison()
        {
            Console.WriteLine("\n--- Démarrage du Comparateur Auto-Activator ---");

            string inputCsv = Path.ChangeExtension(Settings.InputFile, ".csv");

            if (!File.Exists(inputCsv))
            {
                Console.WriteLine($"[ERROR] Fichier d'entrée CSV introuvable : {inputCsv}");
                return;
            }

            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            var reportData = new StringBuilder();
            reportData.AppendLine("Reference_Contract;New_Contract;Table;Status;Details");

            var statsList = new List<(string Product, string Contract, string Status)>();

            // Lecture du fichier CSV (Séparateur ';')
            var lines = File.ReadAllLines(inputCsv).Skip(1); // Skip header

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var columns = line.Split(';');
                if (columns.Length < 6) continue;

                string refContract = columns[0].Trim();
                string newContract = columns[1].Trim();
                string statutJ0 = columns[5].Trim();

                if (!statutJ0.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Skip {refContract} : Activation J0 en échec ({statutJ0}).");
                    statsList.Add(("UNKNOWN", refContract, "SKIP_ACTIVATION_KO"));
                    continue;
                }

                Console.WriteLine($"\nTraitement : Réf {refContract} vs Nouveau {newContract}");

                var pNewId = new Dictionary<string, object> { { "@ContractNumber", newContract } };
                var dtNewId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], pNewId);

                if (dtNewId.Rows.Count == 0) continue;
                long idNew = Convert.ToInt64(dtNewId.Rows[0]["NO_CNT"]);

                string productCode = "UNKNOWN";
                var pProd = new Dictionary<string, object> { { "@InternalId", idNew } };
                string qProd = "SELECT TOP 1 C_PROP_PRINC FROM LV.SCNTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId";
                var dtProd = db.GetData(qProd, pProd);
                if (dtProd.Rows.Count > 0 && dtProd.Columns.Contains("C_PROP_PRINC"))
                {
                    productCode = dtProd.Rows[0]["C_PROP_PRINC"]?.ToString().Trim();
                }

                string contractGlobalStatus = "OK";
                var tablesToCheck = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0" };

                foreach (var table in tablesToCheck)
                {
                    var dtRefData = new DataTable();
                    string snapshotPath = Path.Combine(Settings.SnapshotDir, $"{refContract}_{table}.xml");

                    if (File.Exists(snapshotPath))
                    {
                        dtRefData.ReadXml(snapshotPath);
                    }
                    else
                    {
                        Console.WriteLine($"   [!] Snapshot introuvable pour {table}. Impossible de comparer.");
                        continue;
                    }

                    var pTable = new Dictionary<string, object> { { "@InternalId", idNew } };
                    var dtNewData = db.GetData(SqlQueries.Queries[table], pTable);

                    var (status, details) = Comparator.CompareDataTables(dtRefData, dtNewData, table);

                    if (status.StartsWith("KO"))
                    {
                        Console.WriteLine($" ÉCHEC SUR {refContract} (Table: {table}) - Status: {status}");
                        contractGlobalStatus = "KO";
                    }

                    string cleanDetails = details?.Replace("\r", "").Replace("\n", " | ") ?? "";
                    reportData.AppendLine($"{refContract};{newContract};{table};{status};{cleanDetails}");
                }

                statsList.Add((productCode, refContract, contractGlobalStatus));
            }

            string reportPath = Path.Combine(Settings.OutputDir, $"rapport_detaille_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(reportPath, reportData.ToString(), Encoding.UTF8);
            Console.WriteLine($"\nRapport généré avec succès : {reportPath}");

            if (statsList.Any())
            {
                var summary = statsList
                    .GroupBy(s => s.Product)
                    .Select(g =>
                    {
                        int okCount = g.Count(x => x.Status == "OK");
                        int koCount = g.Count(x => x.Status != "OK");
                        int total = okCount + koCount;
                        double successRate = total > 0 ? Math.Round((double)okCount / total * 100, 1) : 0;

                        return new
                        {
                            Product = string.IsNullOrEmpty(g.Key) ? "UNKNOWN" : g.Key,
                            OK = okCount,
                            KO = koCount,
                            Total = total,
                            SuccessRate = successRate
                        };
                    })
                    .OrderBy(x => x.Product)
                    .ToList();

                Console.WriteLine("\n============================================================");
                Console.WriteLine(" SYNTHÈSE DES RÉSULTATS PAR PRODUIT (KPIs)");
                Console.WriteLine("============================================================");
                Console.WriteLine($"{"Produit",-15} | {"OK",-5} | {"KO",-5} | {"Total",-6} | {"Taux Succès (%)",-15}");
                Console.WriteLine(new string('-', 60));

                var summaryCsv = new StringBuilder();
                summaryCsv.AppendLine("Product;OK;KO;Total;Success_Rate (%)");

                foreach (var stat in summary)
                {
                    Console.WriteLine($"{stat.Product,-15} | {stat.OK,-5} | {stat.KO,-5} | {stat.Total,-6} | {stat.SuccessRate,-15:F1}");
                    summaryCsv.AppendLine($"{stat.Product};{stat.OK};{stat.KO};{stat.Total};{stat.SuccessRate}");
                }
                Console.WriteLine("============================================================\n");

                string summaryPath = Path.Combine(Settings.OutputDir, $"synthese_par_produit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(summaryPath, summaryCsv.ToString(), Encoding.UTF8);
                Console.WriteLine($"Rapport de synthèse généré avec succès : {summaryPath}");
            }
        }

        // =========================================================================
        // MÉTHODE UTILITAIRE : Convertir un DataTable en fichier CSV
        // =========================================================================
        private static void WriteDataTableToCsv(DataTable dataTable, string filePath)
        {
            var sb = new StringBuilder();

            // Titres des colonnes
            var columnNames = dataTable.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray();
            sb.AppendLine(string.Join(";", columnNames));

            // Données
            foreach (DataRow row in dataTable.Rows)
            {
                var fields = row.ItemArray.Select(field =>
                    field == null ? "" : field.ToString().Replace(";", ",").Replace("\n", " ").Replace("\r", "")
                ).ToArray();
                sb.AppendLine(string.Join(";", fields));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}