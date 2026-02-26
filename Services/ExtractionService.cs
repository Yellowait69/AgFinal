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
                throw new ArgumentException("Contract number is empty.");

            // Nettoyage BOM + espaces insécables
            targetContract = targetContract
                .Replace("\u00A0", "")
                .Replace("\uFEFF", "")
                .Trim();

            if (!_db.TestConnection())
                throw new Exception("Unable to establish SQL connection.");

            var parameters = new Dictionary<string, object>
            {
                { "@ContractNumber", targetContract }
            };

            var dtLisa = _db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = _db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {targetContract} not found.");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();

            string generatedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

            string eliaUconId = "Not found";
            string eliaDemandId = "Not found";
            string internalIdString = "Not found";

            #region LISA SECTION

            if (dtLisa.Rows.Count > 0 && dtLisa.Columns.Contains("NO_CNT"))
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                internalIdString = internalId.ToString();

                var lisaTables = new[]
                {
                    "LV.SCNTT0","LV.SAVTT0","LV.SWBGT0","LV.PCONT0","LV.ELIAT0","LV.ELIHT0",
                    "LV.ADMDT0","LV.SPERT0",
                    "FJ1.TB5LPPL","FJ1.TB5LPPR","FJ1.TB5LGDR","LV.XRSTT0",
                    "LV.PRIST0","LV.PECHT0","LV.PFIET0","LV.PMNTT0","LV.PRCTT0","LV.PSUMT0","LV.SELTT0",
                    "FJ1.TB5LPPF","LV.FMVGT0","LV.FMVDT0","LV.SFTS","LV.PINCT0",
                    "LV.SCLST0","LV.SCLRT0","LV.SCLDT0",
                    "LV.BSPDT0","LV.BSPGT0","LV.BPBAT0","LV.BPPAT0",
                    "LV.MWBGT0"
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

            #endregion

            #region ELIA SECTION

            if (dtElia.Rows.Count > 0 && dtElia.Columns.Contains("IT5UCONAIDN"))
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

                var eliaTables = new[]
                {
                    "FJ1.TB5HELT","FJ1.TB5UCON","FJ1.TB5UGAR","FJ1.TB5UASU","FJ1.TB5UCCR",
                    "FJ1.TB5UAVE","FJ1.TB5UPNR","FJ1.TB5UPRP","FJ1.TB5UPRS","FJ1.TB5UPMP",
                    "FJ1.TB5URPP","FJ1.TB5UPRF","FJ1.TB5UFML",
                    "FJ1.TB5UCRB","FJ1.TB5UDCR","FJ1.TB5UBEN"
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
                    var demandTables = new[]
                    {
                        "FJ1.TB5HDMD","FJ1.TB5HDGM","FJ1.TB5HDGD",
                        "FJ1.TB5HPRO","FJ1.TB5HDIC","FJ1.TB5HEPT"
                    };

                    foreach (var dId in demandIds)
                    {
                        sbElia.AppendLine("--------------------------------------------------------------------------------");
                        sbElia.AppendLine($"### --- DEMAND ID : {dId} ---");
                        sbElia.AppendLine("--------------------------------------------------------------------------------");

                        foreach (var table in demandTables)
                        {
                            if (SqlQueries.Queries.ContainsKey(table))
                            {
                                var dt = _db.GetData(
                                    SqlQueries.Queries[table],
                                    new Dictionary<string, object>
                                    {
                                        { "@DemandId", dId },
                                        { "@DemandIds", dId }
                                    });

                                AddTableToBuffer(sbElia, table, dt);
                            }
                        }
                    }
                }
            }

            #endregion

            if (saveIndividualFile)
            {
                try
                {
                    Directory.CreateDirectory(Settings.OutputDir);
                    string fullContent = sbLisa + Environment.NewLine + sbElia.ToString();
                    File.WriteAllText(generatedPath, fullContent, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    throw new Exception($"File generation failed: {ex.Message}");
                }
            }

            return new ExtractionResult
            {
                FilePath = saveIndividualFile ? generatedPath : string.Empty,
                StatusMessage = $"UCONAIDN: {eliaUconId} | HDMDAIDN: {eliaDemandId}",
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
            sb.AppendLine($"### TABLE : {tableName} | Rows : {dt?.Rows.Count ?? 0}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            if (dt == null || dt.Rows.Count == 0)
            {
                sb.AppendLine("NO DATA FOUND");
                sb.AppendLine();
                return;
            }

            var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
            sb.AppendLine(string.Join(";", columns));

            foreach (DataRow row in dt.Rows)
            {
                var fields = row.ItemArray.Select(f =>
                    f == DBNull.Value
                        ? ""
                        : f.ToString()
                            .Replace(";", " ")
                            .Replace("\r", " ")
                            .Replace("\n", " ")
                            .Trim());

                sb.AppendLine(string.Join(";", fields));
            }

            sb.AppendLine();
        }
    }
}