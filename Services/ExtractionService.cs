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
        public ExtractionService()
        {
            // Constructor is kept empty, the DB is now initialized per extraction
            // to support switching between D000 and Q000 dynamically.
        }

        public ExtractionResult PerformExtraction(string targetContract, string envSuffix, bool saveIndividualFile = true)
        {
            if (string.IsNullOrWhiteSpace(targetContract))
                throw new ArgumentException("The contract number is empty.");

            string cleanedContract = targetContract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

            // Instantiate the database manager for the specific environment (D000 or Q000)
            var db = new DatabaseManager(envSuffix);

            if (!db.TestConnection())
                throw new Exception($"Unable to establish SQL connection to environment {envSuffix}.");

            var parameters = new Dictionary<string, object> { { "@ContractNumber", cleanedContract } };

            var dtLisa = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {cleanedContract} not found. Verify that you are targeting the correct database ({envSuffix}).");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();

            // Output file name now includes the environment suffix to avoid overwrites
            string generatedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{envSuffix}_{cleanedContract}.csv");

            string eliaUconId = "Not found", eliaDemandId = "Not found", internalIdString = "Not found";

            #region SECTION LISA
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                internalIdString = internalId.ToString();

                var lisaTables = new[] { "LV.PCONT0", "LV.ELIAT0", "LV.ELIHT0", "LV.SCNTT0", "LV.SWBGT0", "LV.SAVTT0", "LV.XRSTT0", "LV.SPERT0", "LV.ADMDT0", "FJ1.TB5LPPL", "FJ1.TB5LPPR", "FJ1.TB5LGDR", "LV.PRIST0", "LV.PECHT0", "LV.PFIET0", "LV.PMNTT0", "LV.PRCTT0", "LV.PSUMT0", "LV.SELTT0", "FJ1.TB5LPPF", "LV.FMVGT0", "LV.FMVDT0", "LV.SFTS", "LV.PINCT0", "LV.SCLST0", "LV.SCLRT0", "LV.SCLDT0", "LV.BSPDT0", "LV.BSPGT0", "LV.BPBAT0", "LV.BPPAT0", "LV.MWBGT0" };

                // Pass the db instance to the method
                ExtractAndAppendTables(db, lisaTables, "@InternalId", internalId, sbLisa);
            }
            else sbLisa.AppendLine($"### DIAGNOSTIC : Contract {cleanedContract} missing ###");
            #endregion

            #region SECTION ELIA
            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim() ?? "Not found";
                var dtDemand = db.GetData(SqlQueries.Queries["GET_ELIA_DEMAND_IDS"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });

                var demandIds = new List<string>();
                if (dtDemand.Columns.Contains("IT5HDMDAIDN"))
                    foreach (DataRow row in dtDemand.Rows)
                        if (!string.IsNullOrWhiteSpace(row["IT5HDMDAIDN"]?.ToString())) demandIds.Add(row["IT5HDMDAIDN"].ToString().Trim());

                if (demandIds.Count > 0) eliaDemandId = string.Join(", ", demandIds);

                var eliaTables = new[] { "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR", "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP", "FJ1.TB5URPP", "FJ1.TB5UPRF", "FJ1.TB5UFML", "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
                ExtractAndAppendTables(db, eliaTables, "@EliaId", eliaUconId, sbElia);

                if (demandIds.Count > 0)
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO", "FJ1.TB5HEPT", "FJ1.TB5HDIC" };
                    ExtractAndAppendTables(db, demandTables, "@DemandIds", string.Join(",", demandIds), sbElia);
                }
            }
            else sbElia.AppendLine("### ELIA SECTION : NO DATA FOUND ###");
            #endregion

            if (saveIndividualFile)
            {
                Directory.CreateDirectory(Settings.OutputDir);
                string fullContent = $"================================================================================\n=== SECTION LISA (INTERNAL ID: {internalIdString} | ENV: {envSuffix}) ===\n================================================================================\n{sbLisa}\n\n================================================================================\n=== SECTION ELIA (UCON ID: {eliaUconId} | ENV: {envSuffix}) ===\n================================================================================\n{sbElia}";
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

        // The method now takes 'DatabaseManager db' as a parameter to use the right environment
        private void ExtractAndAppendTables(DatabaseManager db, IEnumerable<string> tables, string parameterName, object parameterValue, StringBuilder sb)
        {
            foreach (var table in tables)
            {
                if (SqlQueries.Queries.ContainsKey(table))
                {
                    try
                    {
                        var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { parameterName, parameterValue } });
                        // CSV Formatting Call
                        CsvFormatter.AddTableToBuffer(sb, table, dt);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"### TABLE : {table} | EXTRACTION ERROR\nSQL Error: {ex.Message}\n");
                    }
                }
            }
        }
    }
}