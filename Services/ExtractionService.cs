using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        }

        // 1. AJOUT DU CancellationToken
        public async Task<ExtractionResult> PerformExtractionAsync(string targetContract, string envSuffix, bool saveIndividualFile = true, bool isDemandId = false, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(targetContract))
                throw new ArgumentException("The input value is empty.");

            // Vérification initiale
            token.ThrowIfCancellationRequested();

            string cleanedContract = targetContract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

            if (isDemandId)
            {
                var demandDb = new DatabaseManager(envSuffix);
                var dtDemand = await demandDb.GetDataAsync(SqlQueries.Queries["GET_CONTRACT_BY_DEMAND"], new Dictionary<string, object> { { "@DemandId", cleanedContract } }).ConfigureAwait(false);

                if (dtDemand.Rows.Count > 0 && dtDemand.Rows[0]["IT5UCONLREFEXN"] != DBNull.Value)
                {
                    cleanedContract = dtDemand.Rows[0]["IT5UCONLREFEXN"].ToString().Trim();
                }
                else
                {
                    throw new Exception($"No associated contract found for Demand ID {cleanedContract} in environment {envSuffix}.");
                }
            }

            token.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, object> { { "@ContractNumber", cleanedContract } };

            var lisaDb = new DatabaseManager(envSuffix);
            var eliaDb = new DatabaseManager(envSuffix);

            var lisaTask = lisaDb.GetDataAsync(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var eliaTask = eliaDb.GetDataAsync(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            await Task.WhenAll(lisaTask, eliaTask).ConfigureAwait(false);

            var dtLisa = await lisaTask.ConfigureAwait(false);
            var dtElia = await eliaTask.ConfigureAwait(false);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {cleanedContract} not found. Verify that you are targeting the correct database ({envSuffix}).");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();

            string eliaUconId = "Not found", eliaDemandId = "Not found", internalIdString = "Not found";
            string premiumAmount = "0";

            #region LISA SECTION
            token.ThrowIfCancellationRequested();

            if (dtLisa.Rows.Count > 0)
            {
                if (dtLisa.Rows[0]["NO_CNT"] == DBNull.Value)
                    throw new Exception($"The contract {cleanedContract} was found but its internal ID (NO_CNT) is NULL in the database.");

                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                internalIdString = internalId.ToString();

                var lisaTables = new[] { "LV.PCONT0", "LV.ELIAT0", "LV.ELIHT0", "LV.SCNTT0", "LV.SWBGT0", "LV.SAVTT0", "LV.XRSTT0", "LV.SPERT0", "LV.ADMDT0", "FJ1.TB5LPPL", "FJ1.TB5LPPR", "FJ1.TB5LGDR", "LV.PRIST0", "LV.PECHT0", "LV.PFIET0", "LV.PMNTT0", "LV.PRCTT0", "LV.PSUMT0", "LV.SELTT0", "FJ1.TB5LPPF", "LV.FMVGT0", "LV.FMVDT0", "LV.SFTS", "LV.PINCT0", "LV.SCLST0", "LV.SCLRT0", "LV.SCLDT0", "LV.BSPDT0", "LV.BSPGT0", "LV.BPBAT0", "LV.BPPAT0", "LV.MWBGT0" };

                // Transmission du token
                await ExtractAndAppendTablesAsync(envSuffix, lisaTables, "@InternalId", internalId, sbLisa, token).ConfigureAwait(false);
            }
            else sbLisa.AppendLine($"### DIAGNOSTIC : Contract {cleanedContract} missing ###");
            #endregion

            #region ELIA SECTION
            token.ThrowIfCancellationRequested();

            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim() ?? "Not found";

                if (SqlQueries.Queries.ContainsKey("FJ1.TB5UPRP"))
                {
                    try
                    {
                        var premiumDb = new DatabaseManager(envSuffix);
                        var dtPremium = await premiumDb.GetDataAsync(SqlQueries.Queries["FJ1.TB5UPRP"], new Dictionary<string, object> { { "@EliaId", eliaUconId } }).ConfigureAwait(false);
                        if (dtPremium.Rows.Count > 0 && dtPremium.Columns.Contains("IT5UPRPUBRU"))
                        {
                            premiumAmount = dtPremium.Rows[0]["IT5UPRPUBRU"]?.ToString()?.Trim() ?? "0";
                        }
                    }
                    catch { /* Silently ignore the error and keep "0" */ }
                }

                token.ThrowIfCancellationRequested();

                var eliaDemandDb = new DatabaseManager(envSuffix);
                var dtDemand = await eliaDemandDb.GetDataAsync(SqlQueries.Queries["GET_ELIA_DEMAND_IDS"], new Dictionary<string, object> { { "@EliaId", eliaUconId } }).ConfigureAwait(false);

                var demandIds = new List<string>();
                if (dtDemand.Columns.Contains("IT5HDMDAIDN"))
                    foreach (DataRow row in dtDemand.Rows)
                        if (!string.IsNullOrWhiteSpace(row["IT5HDMDAIDN"]?.ToString())) demandIds.Add(row["IT5HDMDAIDN"].ToString().Trim());

                if (demandIds.Count > 0) eliaDemandId = string.Join(", ", demandIds);

                var eliaTables = new[] { "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR", "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP", "FJ1.TB5URPP", "FJ1.TB5UPRF", "FJ1.TB5UFML", "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };

                // Transmission du token
                await ExtractAndAppendTablesAsync(envSuffix, eliaTables, "@EliaId", eliaUconId, sbElia, token).ConfigureAwait(false);

                if (demandIds.Count > 0)
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO", "FJ1.TB5HEPT", "FJ1.TB5HDIC" };
                    // Transmission du token
                    await ExtractAndAppendTablesAsync(envSuffix, demandTables, "@DemandIds", string.Join(",", demandIds), sbElia, token).ConfigureAwait(false);
                }
            }
            else sbElia.AppendLine("### ELIA SECTION : NO DATA FOUND ###");
            #endregion

            token.ThrowIfCancellationRequested();

            string finalLisaContent = sbLisa.ToString();
            string finalEliaContent = sbElia.ToString();

            if (saveIndividualFile)
            {
                Directory.CreateDirectory(Settings.OutputDir);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                char envLetter = !string.IsNullOrEmpty(envSuffix) ? char.ToUpper(envSuffix[0]) : 'U';
                string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_Uniq_{cleanedContract}_{timestamp}.csv");

                string lisaHeader = $"================================================================================\n=== SECTION LISA (INTERNAL ID: {internalIdString} | ENV: {envSuffix}) ===\n================================================================================\n";
                string eliaHeader = $"================================================================================\n=== SECTION ELIA (UCON ID: {eliaUconId} | ENV: {envSuffix}) ===\n================================================================================\n";

                try
                {
                    using (StreamWriter writer = new StreamWriter(combinedPath, false, Encoding.UTF8))
                    {
                        await writer.WriteAsync(lisaHeader).ConfigureAwait(false);
                        await writer.WriteAsync(finalLisaContent).ConfigureAwait(false);
                        await writer.WriteLineAsync().ConfigureAwait(false);
                        await writer.WriteAsync(eliaHeader).ConfigureAwait(false);
                        await writer.WriteAsync(finalEliaContent).ConfigureAwait(false);
                    }
                }
                catch (IOException)
                {
                    string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_Uniq_{cleanedContract}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                    using (StreamWriter writer = new StreamWriter(alternativePath, false, Encoding.UTF8))
                    {
                        await writer.WriteAsync(lisaHeader).ConfigureAwait(false);
                        await writer.WriteAsync(finalLisaContent).ConfigureAwait(false);
                        await writer.WriteLineAsync().ConfigureAwait(false);
                        await writer.WriteAsync(eliaHeader).ConfigureAwait(false);
                        await writer.WriteAsync(finalEliaContent).ConfigureAwait(false);
                    }
                }
            }

            return new ExtractionResult
            {
                ContractReference = cleanedContract,
                FilePath = Settings.OutputDir,
                StatusMessage = $"Extraction saved | ID: {internalIdString}",
                InternalId = internalIdString, UconId = eliaUconId, DemandId = eliaDemandId,
                LisaContent = finalLisaContent, EliaContent = finalEliaContent,
                Premium = premiumAmount
            };
        }

        // 2. AJOUT DU CancellationToken
        private async Task ExtractAndAppendTablesAsync(string envSuffix, IEnumerable<string> tables, string parameterName, object parameterValue, StringBuilder sb, CancellationToken token = default)
        {
            var tablesList = tables.ToList();

            string[] resultsArray = new string[tablesList.Count];
            var semaphore = new SemaphoreSlim(10);
            var tasks = new List<Task>();

            for (int i = 0; i < tablesList.Count; i++)
            {
                int index = i;
                string table = tablesList[index];

                // 3. PASSAGE DU TOKEN A TASK.RUN
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Vérification avant d'attendre le thread pool SQL
                        token.ThrowIfCancellationRequested();

                        // 4. PASSAGE DU TOKEN AU SEMAPHORE
                        await semaphore.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            // Vérification juste avant l'exécution SQL
                            token.ThrowIfCancellationRequested();

                            if (SqlQueries.Queries.ContainsKey(table))
                            {
                                var threadSafeDb = new DatabaseManager(envSuffix);
                                var dt = await threadSafeDb.GetDataAsync(SqlQueries.Queries[table], new Dictionary<string, object> { { parameterName, parameterValue } }).ConfigureAwait(false);

                                // Vérification avant le traitement lourd du CsvFormatter
                                token.ThrowIfCancellationRequested();

                                var tempSb = new StringBuilder();
                                CsvFormatter.AddTableToBuffer(tempSb, table, dt);
                                resultsArray[index] = tempSb.ToString();

                                dt.Clear();
                                dt.Dispose();
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // On laisse l'exception remonter proprement, ce n'est pas une "erreur technique"
                        throw;
                    }
                    catch (Exception ex)
                    {
                        resultsArray[index] = $"### TABLE : {table} | EXTRACTION ERROR\nSQL Error: {ex.Message}\n";
                    }
                }, token));
            }

            // Si une des tâches lance une OperationCanceledException, WhenAll la remontera et annulera le reste
            await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var content in resultsArray)
            {
                if (!string.IsNullOrEmpty(content))
                {
                    sb.Append(content);
                }
            }
        }
    }
}