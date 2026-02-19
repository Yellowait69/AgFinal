using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using AutoActivator.Config;
using AutoActivator.Services;
// Assurez-vous d'avoir accès à votre classe SqlQueries définie précédemment

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

            // Création des dossiers nécessaires s'ils n'existent pas
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
        // 1. TEST D'EXTRACTION (Équivalent de test_extraction.py)
        // =========================================================================
        private static void RunTestExtraction()
        {
            Console.WriteLine("\n--- Démarrage du Test d'Extraction LISA ---");
            string targetContract = "182-2728195-31"; // Le numéro cible

            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            Console.WriteLine($"Recherche de l'ID interne pour le contrat externe : {targetContract}");

            var p1 = new SqlParameter[] { new SqlParameter("@ContractNumber", targetContract) };
            var dfId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], p1);

            if (dfId.Rows.Count == 0)
            {
                string altContract = targetContract.Replace("-", "");
                Console.WriteLine($"[WARNING] Contrat introuvable. Essai sans tirets : {altContract}");
                var p2 = new SqlParameter[] { new SqlParameter("@ContractNumber", altContract) };
                dfId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], p2);

                if (dfId.Rows.Count == 0)
                {
                    Console.WriteLine($"[ERROR] Le contrat {targetContract} est introuvable.");
                    return;
                }
                targetContract = altContract;
            }

            long internalId = Convert.ToInt64(dfId.Rows[0]["NO_CNT"]);
            Console.WriteLine($"✅ Contrat trouvé ! ID Interne (NO_CNT) = {internalId}");

            var tablesToExtract = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0" };
            string outputPath = Path.Combine(Settings.OutputDir, $"extraction_brute_{targetContract}.xlsx");

            using var workbook = new XLWorkbook();
            foreach (var table in tablesToExtract)
            {
                if (!SqlQueries.Queries.ContainsKey(table)) continue;

                var parameters = new[] { new SqlParameter("@InternalId", internalId) };
                var dfTable = db.GetData(SqlQueries.Queries[table], parameters);

                string sheetName = table.Replace("LV.", "");
                var worksheet = workbook.Worksheets.Add(sheetName);

                if (dfTable.Rows.Count > 0)
                {
                    // ClosedXML permet d'insérer un DataTable entier facilement
                    worksheet.Cell(1, 1).InsertTable(dfTable);
                    Console.WriteLine($"  -> {table} : {dfTable.Rows.Count} lignes extraites.");
                }
                else
                {
                    worksheet.Cell(1, 1).Value = "Info";
                    worksheet.Cell(2, 1).Value = "Aucune donnée trouvée";
                    Console.WriteLine($"  -> {table} : Vide (0 ligne).");
                }
                worksheet.Columns().AdjustToContents();
            }

            workbook.SaveAs(outputPath);
            Console.WriteLine($"🎉 Extraction terminée ! Fichier généré : {outputPath}");
        }

        // =========================================================================
        // 2. ACTIVATION (Équivalent de run_activation.py)
        // =========================================================================
        private static void RunActivation()
        {
            Console.WriteLine("\n--- Démarrage du Script d'Activation ---");
            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            // Liste de contrats de tests (hardcodée comme dans le script Python de base s'il n'y a pas de fichier)
            var contratsSources = new List<string> { "12345678", "87654321" };
            var resultats = new List<Dictionary<string, object>>();

            foreach (var oldContractExt in contratsSources)
            {
                Console.WriteLine($"\n--- Traitement Source : {oldContractExt} ---");

                // 1. Récupération de l'ID
                var parameters = new[] { new SqlParameter("@ContractNumber", oldContractExt) };
                var dtId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);

                decimal montantPrime = 100.00m; // Par défaut

                if (dtId.Rows.Count > 0)
                {
                    long idIntSource = Convert.ToInt64(dtId.Rows[0]["NO_CNT"]);

                    // 2. Snapshot XML (Remplace le Pickle)
                    foreach (var table in new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0" })
                    {
                        var pTable = new[] { new SqlParameter("@InternalId", idIntSource) };
                        var dtSource = db.GetData(SqlQueries.Queries[table], pTable);
                        dtSource.TableName = table; // Nécessaire pour l'export XML
                        dtSource.WriteXml(Path.Combine(Settings.SnapshotDir, $"{oldContractExt}_{table}.xml"), XmlWriteMode.WriteSchema);
                    }
                    Console.WriteLine($"   [Snapshot] 📸 Sauvegarde de l'état source pour {oldContractExt} terminée.");
                }

                // 3. Simulation Duplication (ELIA)
                string newContractExt = "999" + oldContractExt.Substring(Math.Max(0, oldContractExt.Length - 6));
                Console.WriteLine($"   [Simulation] Duplication ELIA : Source {oldContractExt} -> Cible {newContractExt}");
                Thread.Sleep(500);

                // 4. Injection Paiement
                var pNewId = new[] { new SqlParameter("@ContractNumber", newContractExt) };
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

            // Génération du fichier Pivot
            if (resultats.Any())
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Mapping");

                // Entêtes
                var keys = resultats.First().Keys.ToList();
                for (int i = 0; i < keys.Count; i++) ws.Cell(1, i + 1).Value = keys[i];

                // Données
                for (int r = 0; r < resultats.Count; r++)
                {
                    for (int c = 0; c < keys.Count; c++)
                    {
                        ws.Cell(r + 2, c + 1).Value = resultats[r][keys[c]]?.ToString();
                    }
                }
                wb.SaveAs(Settings.ActivationOutputFile);
                Console.WriteLine($"\n--- Fichier de suivi généré : {Settings.ActivationOutputFile} ---");
            }
        }

        // =========================================================================
        // 3. COMPARAISON (Équivalent de run_comparison.py)
        // =========================================================================
        private static void RunComparison()
        {
            Console.WriteLine("\n--- Démarrage du Comparateur Auto-Activator ---");

            if (!File.Exists(Settings.InputFile))
            {
                Console.WriteLine($"[ERROR] Fichier d'entrée introuvable : {Settings.InputFile}");
                return;
            }

            var db = new DatabaseManager();
            if (!db.TestConnection()) return;

            var reportData = new StringBuilder();
            reportData.AppendLine("Reference_Contract;New_Contract;Table;Status;Details");

            // Lecture du fichier Excel de mapping via ClosedXML
            using var wb = new XLWorkbook(Settings.InputFile);
            var ws = wb.Worksheet(1);
            var rows = ws.RangeUsed().RowsUsed().Skip(1); // Skip le header

            foreach (var row in rows)
            {
                string refContract = row.Cell(1).GetString().Trim();
                string newContract = row.Cell(2).GetString().Trim();
                string statutJ0 = row.Cell(6).GetString().Trim(); // 6eme colonne = Statut

                if (!statutJ0.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Skip {refContract} : Activation J0 en échec ({statutJ0}).");
                    continue;
                }

                Console.WriteLine($"\nTraitement : Réf {refContract} vs Nouveau {newContract}");

                var pNewId = new[] { new SqlParameter("@ContractNumber", newContract) };
                var dtNewId = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], pNewId);

                if (dtNewId.Rows.Count == 0) continue;
                long idNew = Convert.ToInt64(dtNewId.Rows[0]["NO_CNT"]);

                var tablesToCheck = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0" };

                foreach (var table in tablesToCheck)
                {
                    // 1. Charger Source (Depuis Snapshot XML)
                    var dtRefData = new DataTable();
                    string snapshotPath = Path.Combine(Settings.SnapshotDir, $"{refContract}_{table}.xml");

                    if (File.Exists(snapshotPath))
                    {
                        dtRefData.ReadXml(snapshotPath);
                    }
                    else
                    {
                        Console.WriteLine($"   [!] Snapshot introuvable pour {table}. Impossible de comparer de façon fiable.");
                        continue;
                    }

                    // 2. Charger Cible (Live DB)
                    var pTable = new[] { new SqlParameter("@InternalId", idNew) };
                    var dtNewData = db.GetData(SqlQueries.Queries[table], pTable);

                    // 3. Comparer
                    var (status, details) = Comparator.CompareDataTables(dtRefData, dtNewData, table);

                    if (status.StartsWith("KO"))
                    {
                        Console.WriteLine($" ÉCHEC SUR {refContract} (Table: {table}) - Status: {status}");
                    }

                    // Nettoyage des retours à la ligne pour le CSV
                    string cleanDetails = details?.Replace("\r", "").Replace("\n", " | ") ?? "";
                    reportData.AppendLine($"{refContract};{newContract};{table};{status};{cleanDetails}");
                }
            }

            string reportPath = Path.Combine(Settings.OutputDir, $"rapport_detaille_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(reportPath, reportData.ToString(), Encoding.UTF8);
            Console.WriteLine($"\nRapport généré avec succès : {reportPath}");
        }
    }
}