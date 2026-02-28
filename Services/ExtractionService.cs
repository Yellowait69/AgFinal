using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Sql;
using AutoActivator.Utils;

namespace AutoActivator.Services
{
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

            string cleanedContract = targetContract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

            if (!_db.TestConnection())
                throw new Exception("Impossible d'établir la connexion SQL.");

            var parameters = new Dictionary<string, object> { { "@ContractNumber", cleanedContract } };

            var dtLisa = _db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = _db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contrat {cleanedContract} introuvable. Vérifiez que vous ciblez la bonne base.");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();
            string generatedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{cleanedContract}.csv");
            string eliaUconId = "Not found", eliaDemandId = "Not found", internalIdString = "Not found";

            #region SECTION LISA
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                internalIdString = internalId.ToString();

                var lisaTables = new[] { "LV.PCONT0", "LV.ELIAT0", "LV.ELIHT0", "LV.SCNTT0", "LV.SWBGT0", "LV.SAVTT0", "LV.XRSTT0", "LV.SPERT0", "LV.ADMDT0", "FJ1.TB5LPPL", "FJ1.TB5LPPR", "FJ1.TB5LGDR", "LV.PRIST0", "LV.PECHT0", "LV.PFIET0", "LV.PMNTT0", "LV.PRCTT0", "LV.PSUMT0", "LV.SELTT0", "FJ1.TB5LPPF", "LV.FMVGT0", "LV.FMVDT0", "LV.SFTS", "LV.PINCT0", "LV.SCLST0", "LV.SCLRT0", "LV.SCLDT0", "LV.BSPDT0", "LV.BSPGT0", "LV.BPBAT0", "LV.BPPAT0", "LV.MWBGT0" };
                ExtractAndAppendTables(lisaTables, "@InternalId", internalId, sbLisa);
            }
            else sbLisa.AppendLine($"### DIAGNOSTIC : Contrat {cleanedContract} absent ###");
            #endregion

            #region SECTION ELIA
            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim() ?? "Not found";
                var dtDemand = _db.GetData(SqlQueries.Queries["GET_ELIA_DEMAND_IDS"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });

                var demandIds = new List<string>();
                if (dtDemand.Columns.Contains("IT5HDMDAIDN"))
                    foreach (DataRow row in dtDemand.Rows)
                        if (!string.IsNullOrWhiteSpace(row["IT5HDMDAIDN"]?.ToString())) demandIds.Add(row["IT5HDMDAIDN"].ToString().Trim());

                if (demandIds.Count > 0) eliaDemandId = string.Join(", ", demandIds);

                var eliaTables = new[] { "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR", "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP", "FJ1.TB5URPP", "FJ1.TB5UPRF", "FJ1.TB5UFML", "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
                ExtractAndAppendTables(eliaTables, "@EliaId", eliaUconId, sbElia);

                if (demandIds.Count > 0)
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO", "FJ1.TB5HEPT", "FJ1.TB5HDIC" };
                    ExtractAndAppendTables(demandTables, "@DemandIds", string.Join(",", demandIds), sbElia);
                }
            }
            else sbElia.AppendLine("### ELIA SECTION : AUCUNE DONNEE TROUVÉE ###");
            #endregion

            if (saveIndividualFile)
            {
                Directory.CreateDirectory(Settings.OutputDir);
                string fullContent = $"================================================================================\n=== SECTION LISA (INTERNAL ID: {internalIdString}) ===\n================================================================================\n{sbLisa}\n\n================================================================================\n=== SECTION ELIA (UCON ID: {eliaUconId}) ===\n================================================================================\n{sbElia}";
                File.WriteAllText(generatedPath, fullContent, Encoding.UTF8);
            }

            return new ExtractionResult
            {
                FilePath = saveIndividualFile ? generatedPath : string.Empty,
                StatusMessage = $"LISA: {internalIdString} | ELIA: {eliaUconId}",
                InternalId = internalIdString, UconId = eliaUconId, DemandId = eliaDemandId,
                LisaContent = sbLisa.ToString(), EliaContent = sbElia.ToString()
            };
        }

        private void ExtractAndAppendTables(IEnumerable<string> tables, string parameterName, object parameterValue, StringBuilder sb)
        {
            foreach (var table in tables)
            {
                if (SqlQueries.Queries.ContainsKey(table))
                {
                    try
                    {
                        var dt = _db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { parameterName, parameterValue } });
                        // APPEL AU NOUVEAU SERVICE CSV ICI !
                        CsvFormatter.AddTableToBuffer(sb, table, dt);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"### TABLE : {table} | ERREUR D'EXTRACTION\nErreur SQL: {ex.Message}\n");
                    }
                }
            }
        }
    }
}