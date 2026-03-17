using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
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
            // Constructor is kept empty, the DB is now initialized per extraction
            // to support switching between D000 and Q000 dynamically.
        }

        public async Task<ExtractionResult> PerformExtractionAsync(string targetContract, string envSuffix, bool saveIndividualFile = true, bool isDemandId = false)
        {
            if (string.IsNullOrWhiteSpace(targetContract))
                throw new ArgumentException("The input value is empty.");

            string cleanedContract = targetContract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

            // Instantiate the database manager for the specific environment (D000 or Q000)
            var db = new DatabaseManager(envSuffix);

            // OPTIMISATION 1 : Suppression du test de connexion (TestConnectionAsync)
            // Cela évite un aller-retour réseau inutile par contrat.

            // --- Résolution du Demand ID en Numéro de contrat ---
            if (isDemandId)
            {
                // Ajout de ConfigureAwait(false) pour éviter les deadlocks
                var dtDemand = await db.GetDataAsync(SqlQueries.Queries["GET_CONTRACT_BY_DEMAND"], new Dictionary<string, object> { { "@DemandId", cleanedContract } }).ConfigureAwait(false);

                if (dtDemand.Rows.Count > 0 && dtDemand.Rows[0]["IT5UCONLREFEXN"] != DBNull.Value)
                {
                    cleanedContract = dtDemand.Rows[0]["IT5UCONLREFEXN"].ToString().Trim();
                }
                else
                {
                    throw new Exception($"No associated contract found for Demand ID {cleanedContract} in environment {envSuffix}.");
                }
            }

            var parameters = new Dictionary<string, object> { { "@ContractNumber", cleanedContract } };

            // OPTIMISATION : Lancement des requêtes LISA et ELIA EN MÊME TEMPS
            var lisaTask = db.GetDataAsync(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var eliaTask = db.GetDataAsync(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            // On attend que les deux requêtes soient terminées (sans bloquer l'UI)
            await Task.WhenAll(lisaTask, eliaTask).ConfigureAwait(false);

            var dtLisa = await lisaTask.ConfigureAwait(false);
            var dtElia = await eliaTask.ConfigureAwait(false);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {cleanedContract} not found. Verify that you are targeting the correct database ({envSuffix}).");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();

            string eliaUconId = "Not found", eliaDemandId = "Not found", internalIdString = "Not found";
            string premiumAmount = "0";

            #region SECTION LISA
            if (dtLisa.Rows.Count > 0)
            {
                if (dtLisa.Rows[0]["NO_CNT"] == DBNull.Value)
                    throw new Exception($"Le contrat {cleanedContract} a été trouvé mais son ID interne (NO_CNT) est NULL en base de données.");

                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                internalIdString = internalId.ToString();

                var lisaTables = new[] { "LV.PCONT0", "LV.ELIAT0", "LV.ELIHT0", "LV.SCNTT0", "LV.SWBGT0", "LV.SAVTT0", "LV.XRSTT0", "LV.SPERT0", "LV.ADMDT0", "FJ1.TB5LPPL", "FJ1.TB5LPPR", "FJ1.TB5LGDR", "LV.PRIST0", "LV.PECHT0", "LV.PFIET0", "LV.PMNTT0", "LV.PRCTT0", "LV.PSUMT0", "LV.SELTT0", "FJ1.TB5LPPF", "LV.FMVGT0", "LV.FMVDT0", "LV.SFTS", "LV.PINCT0", "LV.SCLST0", "LV.SCLRT0", "LV.SCLDT0", "LV.BSPDT0", "LV.BSPGT0", "LV.BPBAT0", "LV.BPPAT0", "LV.MWBGT0" };

                // Extraction parallèle de toutes les tables LISA
                await ExtractAndAppendTablesAsync(db, lisaTables, "@InternalId", internalId, sbLisa).ConfigureAwait(false);
            }
            else sbLisa.AppendLine($"### DIAGNOSTIC : Contract {cleanedContract} missing ###");
            #endregion

            #region SECTION ELIA
            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim() ?? "Not found";

                // Récupération spécifique de la prime (Premium)
                if (SqlQueries.Queries.ContainsKey("FJ1.TB5UPRP"))
                {
                    try
                    {
                        var dtPremium = await db.GetDataAsync(SqlQueries.Queries["FJ1.TB5UPRP"], new Dictionary<string, object> { { "@EliaId", eliaUconId } }).ConfigureAwait(false);
                        if (dtPremium.Rows.Count > 0 && dtPremium.Columns.Contains("IT5UPRPUBRU"))
                        {
                            premiumAmount = dtPremium.Rows[0]["IT5UPRPUBRU"]?.ToString()?.Trim() ?? "0";
                        }
                    }
                    catch { /* Ignore l'erreur silencieusement et garde "0" */ }
                }

                // Récupération des Demands
                var dtDemand = await db.GetDataAsync(SqlQueries.Queries["GET_ELIA_DEMAND_IDS"], new Dictionary<string, object> { { "@EliaId", eliaUconId } }).ConfigureAwait(false);

                var demandIds = new List<string>();
                if (dtDemand.Columns.Contains("IT5HDMDAIDN"))
                    foreach (DataRow row in dtDemand.Rows)
                        if (!string.IsNullOrWhiteSpace(row["IT5HDMDAIDN"]?.ToString())) demandIds.Add(row["IT5HDMDAIDN"].ToString().Trim());

                if (demandIds.Count > 0) eliaDemandId = string.Join(", ", demandIds);

                var eliaTables = new[] { "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR", "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP", "FJ1.TB5URPP", "FJ1.TB5UPRF", "FJ1.TB5UFML", "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };

                // Extraction parallèle de toutes les tables ELIA
                await ExtractAndAppendTablesAsync(db, eliaTables, "@EliaId", eliaUconId, sbElia).ConfigureAwait(false);

                if (demandIds.Count > 0)
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO", "FJ1.TB5HEPT", "FJ1.TB5HDIC" };
                    await ExtractAndAppendTablesAsync(db, demandTables, "@DemandIds", string.Join(",", demandIds), sbElia).ConfigureAwait(false);
                }
            }
            else sbElia.AppendLine("### ELIA SECTION : NO DATA FOUND ###");
            #endregion

            if (saveIndividualFile)
            {
                Directory.CreateDirectory(Settings.OutputDir);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                char envLetter = !string.IsNullOrEmpty(envSuffix) ? char.ToUpper(envSuffix[0]) : 'U';
                string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_Uniq_{cleanedContract}_{timestamp}.csv");

                string lisaHeader = $"================================================================================\n=== SECTION LISA (INTERNAL ID: {internalIdString} | ENV: {envSuffix}) ===\n================================================================================\n";
                string eliaHeader = $"================================================================================\n=== SECTION ELIA (UCON ID: {eliaUconId} | ENV: {envSuffix}) ===\n================================================================================\n";

                string combinedContent = lisaHeader + sbLisa.ToString() + "\n" + eliaHeader + sbElia.ToString();

                // COMPATIBILITÉ .NET ANCIEN + ConfigureAwait(false)
                try
                {
                    using (StreamWriter writer = new StreamWriter(combinedPath, false, Encoding.UTF8))
                    {
                        await writer.WriteAsync(combinedContent).ConfigureAwait(false);
                    }
                }
                catch (IOException)
                {
                    string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_Uniq_{cleanedContract}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                    using (StreamWriter writer = new StreamWriter(alternativePath, false, Encoding.UTF8))
                    {
                        await writer.WriteAsync(combinedContent).ConfigureAwait(false);
                    }
                }
            }

            return new ExtractionResult
            {
                ContractReference = cleanedContract,
                FilePath = Settings.OutputDir,
                StatusMessage = $"Extraction saved | ID: {internalIdString}",
                InternalId = internalIdString, UconId = eliaUconId, DemandId = eliaDemandId,
                LisaContent = sbLisa.ToString(), EliaContent = sbElia.ToString(),
                Premium = premiumAmount
            };
        }

        private async Task ExtractAndAppendTablesAsync(DatabaseManager db, IEnumerable<string> tables, string parameterName, object parameterValue, StringBuilder sb)
        {
            var resultsDictionary = new ConcurrentDictionary<string, string>();

            // OPTIMISATION 2 : On passe à 30 requêtes de tables simultanées par contrat
            var semaphore = new SemaphoreSlim(30);
            var tasks = new List<Task>();

            foreach (var table in tables)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (SqlQueries.Queries.ContainsKey(table))
                        {
                            var dt = await db.GetDataAsync(SqlQueries.Queries[table], new Dictionary<string, object> { { parameterName, parameterValue } }).ConfigureAwait(false);

                            var tempSb = new StringBuilder();
                            CsvFormatter.AddTableToBuffer(tempSb, table, dt); // Formate le CSV en mémoire

                            resultsDictionary.TryAdd(table, tempSb.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        resultsDictionary.TryAdd(table, $"### TABLE : {table} | EXTRACTION ERROR\nSQL Error: {ex.Message}\n");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            // Attendre que toutes les tables aient été extraites
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // On réassemble le texte dans l'ordre exact du tableau d'origine (important pour la lisibilité)
            foreach (var table in tables)
            {
                if (resultsDictionary.TryGetValue(table, out var content))
                {
                    sb.Append(content);
                }
            }
        }
    }
}