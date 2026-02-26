using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using AutoActivator.Config;
using AutoActivator.Sql;

namespace AutoActivator.Services
{
    public class ExtractionResult
    {
        public string FilePath { get; set; }
        public string StatusMessage { get; set; }
        public string InternalId { get; set; }
        public string UconId { get; set; }
        public string DemandId { get; set; }
        public string LisaContent { get; set; }
        public string EliaContent { get; set; }
    }

    public class ExtractionService
    {
        private readonly DatabaseManager _db;

        public ExtractionService()
        {
            _db = new DatabaseManager();
        }

        public ExtractionResult PerformExtraction(string targetContract, bool saveIndividualFile = true)
        {
            if (string.IsNullOrWhiteSpace(targetContract))
                throw new ArgumentException("Le numéro de contrat est vide.");

            // Nettoyage (BOM, espaces insécables)
            string cleanedContract = targetContract
                .Replace("\u00A0", "")
                .Replace("\uFEFF", "")
                .Trim();

            if (!_db.TestConnection())
                throw new Exception("Impossible d'établir la connexion SQL.");

            var parameters = new Dictionary<string, object>
            {
                { "@ContractNumber", cleanedContract }
            };

            // 1. Récupération des identifiants pivots (LISA et ELIA)
            var dtLisa = _db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = _db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            // Diagnostic si aucune clé n'est trouvée
            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contrat {cleanedContract} introuvable. Vérifiez que vous ciblez la bonne base (Q000 vs D000).");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();

            string generatedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{cleanedContract}.csv");

            string eliaUconId = "Not found";
            string eliaDemandId = "Not found";
            string internalIdString = "Not found";

            #region SECTION LISA

            if (dtLisa.Rows.Count > 0)
            {
                // CORRECTION : On convertit explicitement en Int64 (long) comme dans le script de test
                // pour éviter les erreurs de typage lors du passage de paramètre SQL.
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                internalIdString = internalId.ToString();

                // CORRECTION : Utilisation de la liste exacte et validée du script de test
                var lisaTables = new[]
                {
                    "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0",
                    "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0",
                    "LV.MWBGT0", "LV.PRIST0", "LV.FMVGT0"
                };

                foreach (var table in lisaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = _db.GetData(
                            SqlQueries.Queries[table],
                            new Dictionary<string, object> { { "@InternalId", internalId } });

                        AddTableToBuffer(sbLisa, table, dt);
                    }
                }
            }
            else
            {
                sbLisa.AppendLine($"### DIAGNOSTIC : Contrat {cleanedContract} absent de LV.SCNTT0 dans {Settings.DbConfig.Database} ###");
            }

            #endregion

            #region SECTION ELIA

            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim() ?? "Not found";

                var dtDemand = _db.GetData(
                    SqlQueries.Queries["GET_ELIA_DEMAND_IDS"],
                    new Dictionary<string, object> { { "@EliaId", eliaUconId } });

                var demandIds = new List<string>();
                if (dtDemand.Columns.Contains("IT5HDMDAIDN"))
                {
                    foreach (DataRow row in dtDemand.Rows)
                    {
                        var dId = row["IT5HDMDAIDN"]?.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(dId))
                            demandIds.Add(dId);
                    }
                }

                if (demandIds.Count > 0)
                    eliaDemandId = string.Join(", ", demandIds);

                // Note : Je n'ai pas touché à tes tables ELIA car tu as précisé que seule
                // l'extraction LISA posait souci et que ta logique ELIA ici est plus complète.
                var eliaTables = new[]
                {
                    "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR",
                    "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP",
                    "FJ1.TB5UPRF", "FJ1.TB5UFML", "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN"
                };

                foreach (var table in eliaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = _db.GetData(
                            SqlQueries.Queries[table],
                            new Dictionary<string, object> { { "@EliaId", eliaUconId } });

                        AddTableToBuffer(sbElia, table, dt);
                    }
                }

                if (demandIds.Count > 0)
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO" };
                    string demandIdsString = string.Join(",", demandIds);

                    foreach (var table in demandTables)
                    {
                        if (SqlQueries.Queries.ContainsKey(table))
                        {
                            var dt = _db.GetData(
                                SqlQueries.Queries[table],
                                new Dictionary<string, object> { { "@DemandIds", demandIdsString } });

                            AddTableToBuffer(sbElia, table, dt);
                        }
                    }
                }
            }
            else
            {
                sbElia.AppendLine("### ELIA SECTION : AUCUNE DONNEE TROUVÉE DANS FJ1.TB5UCON ###");
            }

            #endregion

            if (saveIndividualFile)
            {
                try
                {
                    Directory.CreateDirectory(Settings.OutputDir);
                    string fullContent = "================================================================================\n" +
                                       "=== SECTION LISA (INTERNAL ID: " + internalIdString + ") ===\n" +
                                       "================================================================================\n" +
                                       sbLisa.ToString() + "\n\n" +
                                       "================================================================================\n" +
                                       "=== SECTION ELIA (UCON ID: " + eliaUconId + ") ===\n" +
                                       "================================================================================\n" +
                                       sbElia.ToString();

                    File.WriteAllText(generatedPath, fullContent, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Échec de génération du fichier : {ex.Message}");
                }
            }

            return new ExtractionResult
            {
                FilePath = saveIndividualFile ? generatedPath : string.Empty,
                StatusMessage = $"LISA: {internalIdString} | ELIA: {eliaUconId}",
                InternalId = internalIdString,
                UconId = eliaUconId,
                DemandId = eliaDemandId,
                LisaContent = sbLisa.ToString(),
                EliaContent = sbElia.ToString()
            };
        }

        private void AddTableToBuffer(StringBuilder sb, string tableName, DataTable dt)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"### TABLE : {tableName} | Lignes : {dt?.Rows.Count ?? 0}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            if (dt == null || dt.Rows.Count == 0)
            {
                sb.AppendLine("AUCUNE DONNÉE TROUVÉE");
                sb.AppendLine();
                return;
            }

            var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
            sb.AppendLine(string.Join(";", columns));

            foreach (DataRow row in dt.Rows)
            {
                // Excellente pratique conservée ici : le fallback DBNull.Value
                var fields = row.ItemArray.Select(f =>
                    f == DBNull.Value ? "" : f.ToString().Replace(";", " ").Replace("\n", " ").Trim());
                sb.AppendLine(string.Join(";", fields));
            }
            sb.AppendLine();
        }
    }
}