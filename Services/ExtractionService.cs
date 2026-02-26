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
    // Classe de résultat enrichie pour séparer les contenus LISA et ELIA
    public class ExtractionResult
    {
        public string FilePath { get; set; }
        public string StatusMessage { get; set; }
        public string InternalId { get; set; } // NOUVEAU : ID Interne (NO_CNT)
        public string UconId { get; set; }
        public string DemandId { get; set; }
        public string LisaContent { get; set; } // Nouveau : Contenu texte des tables LISA (et assimilées)
        public string EliaContent { get; set; } // Nouveau : Contenu texte des tables ELIA
    }

    public class ExtractionService
    {
        private readonly DatabaseManager _db;

        public ExtractionService()
        {
            _db = new DatabaseManager();
        }

        public ExtractionResult PerformExtraction(string targetContract)
        {
            // CORRECTION MAJEURE ICI : Nettoyage impératif des espaces insécables ET du BOM (\uFEFF)
            targetContract = targetContract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

            if (!_db.TestConnection())
                throw new Exception("Unable to establish SQL connection.");

            var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };

            // Récupération des IDs initiaux
            var dtLisa = _db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = _db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {targetContract} not found.");

            // Initialisation des tampons séparés
            StringBuilder sbLisa = new StringBuilder();
            StringBuilder sbElia = new StringBuilder();

            string generatedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

            string eliaUconId = "Not found";
            string eliaDemandId = "Not found";
            string internalIdString = "Not found"; // Initialisation de l'Internal ID

            // --- SECTION LISA ET TABLES ASSIMILEES (@InternalId) ---
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                internalIdString = internalId.ToString(); // Sauvegarde de l'Internal ID pour l'historique

                // Toutes les requêtes qui s'exécutent avec @InternalId (Synchronisé avec SqlQueries)
                var lisaTables = new[] {
                    "LV.SCNTT0", "LV.SAVTT0", "LV.SWBGT0", "LV.PCONT0", "LV.ELIAT0", "LV.ELIHT0",
                    "LV.ADMDT0", "LV.SPERT0", // Nouvelles tables ajoutées
                    "FJ1.TB5LPPL", "FJ1.TB5LPPR", "FJ1.TB5LGDR", "LV.XRSTT0",
                    "LV.PRIST0", "LV.PECHT0", "LV.PFIET0", "LV.PMNTT0", "LV.PRCTT0", "LV.PSUMT0", "LV.SELTT0",
                    "FJ1.TB5LPPF", "LV.FMVGT0", "LV.FMVDT0", "LV.SFTS", "LV.PINCT0",
                    "LV.SCLST0", "LV.SCLRT0", "LV.SCLDT0",
                    "LV.BSPDT0", "LV.BSPGT0", "LV.BPBAT0", "LV.BPPAT0",
                    "LV.MWBGT0"
                };

                foreach (var table in lisaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = _db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", internalId } });
                        AddTableToBuffer(sbLisa, table, dt); // Ajout au tampon LISA
                    }
                }
            }

            // --- SECTION ELIA (@EliaId et @DemandId) ---
            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"].ToString().Trim();

                // Récupération d'une liste de TOUTES les DemandIds associées
                // (Nom de requête mis à jour : GET_ELIA_DEMAND_IDS)
                var dtDemand = _db.GetData(SqlQueries.Queries["GET_ELIA_DEMAND_IDS"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                List<string> demandIds = new List<string>();
                foreach (DataRow row in dtDemand.Rows)
                {
                    string dId = row["IT5HDMDAIDN"].ToString().Trim();
                    if (!string.IsNullOrEmpty(dId)) demandIds.Add(dId);
                }

                if (demandIds.Count > 0)
                {
                    // Met à jour le status pour voir toutes les demandes
                    eliaDemandId = string.Join(", ", demandIds);
                }

                // Toutes les requêtes qui s'exécutent avec @EliaId (Synchronisé avec SqlQueries)
                var eliaTables = new[] {
                    "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR",
                    "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP",
                    "FJ1.TB5URPP", // Nouvelle table ajoutée
                    "FJ1.TB5UPRF", "FJ1.TB5UFML",
                    "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN"
                };

                foreach (var table in eliaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = _db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                        AddTableToBuffer(sbElia, table, dt); // Ajout au tampon ELIA
                    }
                }

                // On itère sur toutes les demandes trouvées pour les requêtes qui nécessitent @DemandId
                if (demandIds.Count > 0)
                {
                    var demandTables = new[] {
                        "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO", "FJ1.TB5HDIC", "FJ1.TB5HEPT"
                    };

                    foreach (var dId in demandIds)
                    {
                        sbElia.AppendLine($"--------------------------------------------------------------------------------");
                        sbElia.AppendLine($"### --- DEMAND ID : {dId} ---");
                        sbElia.AppendLine($"--------------------------------------------------------------------------------");

                        foreach (var table in demandTables)
                        {
                            if (SqlQueries.Queries.ContainsKey(table))
                            {
                                // Passage des paramètres de façon sécurisée (supporte @DemandId ET @DemandIds selon ton fichier SQL)
                                var dt = _db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> {
                                    { "@DemandId", dId },
                                    { "@DemandIds", dId }
                                });
                                AddTableToBuffer(sbElia, table, dt); // Ajout au tampon ELIA
                            }
                        }
                    }
                }
            }

            // Sauvegarde du fichier individuel combiné (LISA + ELIA)
            string fullContent = sbLisa.ToString() + Environment.NewLine + sbElia.ToString();
            File.WriteAllText(generatedPath, fullContent, Encoding.UTF8);

            return new ExtractionResult
            {
                FilePath = generatedPath,
                StatusMessage = $"UCONAIDN: {eliaUconId} | HDMDAIDN: {eliaDemandId}",
                InternalId = internalIdString, // Transmission de l'ID Interne à l'interface
                UconId = eliaUconId,
                DemandId = eliaDemandId,
                LisaContent = sbLisa.ToString(),
                EliaContent = sbElia.ToString()
            };
        }

        private void AddTableToBuffer(StringBuilder sb, string tableName, DataTable dt)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"### TABLE : {tableName} | Rows : {dt.Rows.Count}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            if (dt.Rows.Count > 0)
            {
                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                sb.AppendLine(string.Join(";", columns));

                foreach (DataRow row in dt.Rows)
                {
                    var fields = row.ItemArray.Select(f => f?.ToString().Replace(";", " ").Replace("\n", " ").Trim());
                    sb.AppendLine(string.Join(";", fields));
                }
            }
            else
            {
                sb.AppendLine("NO DATA FOUND");
            }
            sb.AppendLine();
        }
    }
}