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
    // Petite classe pour renvoyer le résultat à l'interface utilisateur
    public class ExtractionResult
    {
        public string FilePath { get; set; }
        public string StatusMessage { get; set; }
        public string UconId { get; set; }
        public string DemandId { get; set; }
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
            targetContract = targetContract.Replace("\u00A0", "").Trim();

            if (!_db.TestConnection())
                throw new Exception("Unable to establish SQL connection.");

            var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };

            // Initial IDs retrieval via optimized queries in SqlQueries.cs
            var dtLisa = _db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = _db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {targetContract} not found.");

            StringBuilder sb = new StringBuilder();
            string generatedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

            string eliaUconId = "Not found";
            string eliaDemandId = "Not found";
            string lisaId = "Not found";

            // --- LISA SECTION ---
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                lisaId = internalId.ToString();

                var lisaTables = new[] {
                    "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0",
                    "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0",
                    "LV.MWBGT0", "LV.PRIST0", "LV.FMVGT0", "LV.ELIAT0",
                    "LV.ELIHT0", "LV.PCONT0", "LV.XRSTT0"
                };

                foreach (var table in lisaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = _db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", internalId } });
                        AddTableToBuffer(sb, table, dt);
                    }
                }
            }

            // --- ELIA SECTION ---
            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"].ToString().Trim();

                var dtDemand = _db.GetData(SqlQueries.Queries["GET_ELIA_DEMAND_ID"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                if (dtDemand.Rows.Count > 0)
                {
                    eliaDemandId = dtDemand.Rows[0]["IT5HDMDAIDN"].ToString().Trim();
                }

                var eliaTables = new[] {
                    "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UPRP",
                    "FJ1.TB5UAVE", "FJ1.TB5UDCR", "FJ1.TB5UBEN", "FJ1.TB5UPRS",
                    "FJ1.TB5URPP", "FJ1.TB5HELT", "FJ1.TB5UCCR", "FJ1.TB5UPNR"
                };

                foreach (var table in eliaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = _db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                        AddTableToBuffer(sb, table, dt);
                    }
                }

                if (eliaDemandId != "Not found")
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HPRO", "FJ1.TB5HDIC", "FJ1.TB5HEPT", "FJ1.TB5HDGM", "FJ1.TB5HDGD" };
                    foreach (var table in demandTables)
                    {
                        if (SqlQueries.Queries.ContainsKey(table))
                        {
                            var dt = _db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@DemandId", eliaDemandId } });
                            AddTableToBuffer(sb, table, dt);
                        }
                    }
                }
            }

            File.WriteAllText(generatedPath, sb.ToString(), Encoding.UTF8);

            return new ExtractionResult
            {
                FilePath = generatedPath,
                StatusMessage = $"UCONAIDN: {eliaUconId} | HDMDAIDN: {eliaDemandId}",
                UconId = eliaUconId,
                DemandId = eliaDemandId
            };
        }

        private void AddTableToBuffer(StringBuilder sb, string tableName, DataTable dt)
        {
            sb.AppendLine("################################################################################");
            sb.AppendLine($"### TABLE : {tableName} | Rows : {dt.Rows.Count}");
            sb.AppendLine("################################################################################");

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
            else { sb.AppendLine("NO DATA FOUND"); }
            sb.AppendLine();
        }
    }
}