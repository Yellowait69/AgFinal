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
        // 1. EXTRACTION DES DONNÉES EN DUR (Hardcoding évité)
        private static readonly string[] LisaTables = { "LV.PCONT0", "LV.ELIAT0", "LV.ELIHT0", "LV.SCNTT0", "LV.SWBGT0", "LV.SAVTT0", "LV.XRSTT0", "LV.SPERT0", "LV.ADMDT0", "FJ1.TB5LPPL", "FJ1.TB5LPPR", "FJ1.TB5LGDR", "LV.PRIST0", "LV.PECHT0", "LV.PFIET0", "LV.PMNTT0", "LV.PRCTT0", "LV.PSUMT0", "LV.SELTT0", "FJ1.TB5LPPF", "LV.FMVGT0", "LV.FMVDT0", "LV.SFTS", "LV.PINCT0", "LV.SCLST0", "LV.SCLRT0", "LV.SCLDT0", "LV.BSPDT0", "LV.BSPGT0", "LV.BPBAT0", "LV.BPPAT0", "LV.MWBGT0" };
        private static readonly string[] EliaTables = { "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR", "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP", "FJ1.TB5URPP", "FJ1.TB5UPRF", "FJ1.TB5UFML", "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
        private static readonly string[] DemandTables = { "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO", "FJ1.TB5HEPT", "FJ1.TB5HDIC" };

        // Note pour l'avenir : Idéalement, injectez une interface (ex: IDatabaseManagerFactory) via le constructeur.
        public ExtractionService()
        {
        }

        // 2. REFACTORING : La méthode principale coordonne désormais le flux de travail au lieu de tout faire elle-même (SRP)
        public async Task<ExtractionResult> PerformExtractionAsync(string targetContract, string envSuffix, bool saveIndividualFile = true, bool isDemandId = false)
        {
            if (string.IsNullOrWhiteSpace(targetContract))
                throw new ArgumentException("The input value is empty.");

            string cleanedContract = targetContract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

            // Création d'une instance UNIQUE de DatabaseManager pour cet environnement.
            // Comme GetDataAsync crée sa propre SqlConnection, cette instance est thread-safe et réutilisable.
            var dbManager = new DatabaseManager(envSuffix);

            if (isDemandId)
            {
                cleanedContract = await ResolveDemandIdAsync(dbManager, cleanedContract, envSuffix).ConfigureAwait(false);
            }

            var (dtLisa, dtElia) = await GetCoreContractsAsync(dbManager, cleanedContract).ConfigureAwait(false);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {cleanedContract} not found. Verify that you are targeting the correct database ({envSuffix}).");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();
            string internalIdString = "Not found", eliaUconId = "Not found", eliaDemandId = "Not found", premiumAmount = "0";

            // Extraction LISA
            if (dtLisa.Rows.Count > 0)
            {
                internalIdString = await ExtractLisaSectionAsync(dbManager, dtLisa, cleanedContract, sbLisa).ConfigureAwait(false);
            }
            else
            {
                sbLisa.AppendLine($"### DIAGNOSTIC : Contract {cleanedContract} missing ###");
            }

            // Extraction ELIA
            if (dtElia.Rows.Count > 0)
            {
                (eliaUconId, eliaDemandId, premiumAmount) = await ExtractEliaSectionAsync(dbManager, dtElia, sbElia).ConfigureAwait(false);
            }
            else
            {
                sbElia.AppendLine("### ELIA SECTION : NO DATA FOUND ###");
            }

            // Sauvegarde de fichier déléguée à une méthode dédiée
            if (saveIndividualFile)
            {
                await SaveExtractionToFileAsync(cleanedContract, envSuffix, internalIdString, eliaUconId, sbLisa, sbElia).ConfigureAwait(false);
            }

            return new ExtractionResult
            {
                ContractReference = cleanedContract,
                FilePath = Settings.OutputDir,
                StatusMessage = $"Extraction saved | ID: {internalIdString}",
                InternalId = internalIdString,
                UconId = eliaUconId,
                DemandId = eliaDemandId,
                LisaContent = sbLisa.ToString(),
                EliaContent = sbElia.ToString(),
                Premium = premiumAmount
            };
        }

        #region Méthodes privées d'orchestration (SRP)

        private async Task<string> ResolveDemandIdAsync(DatabaseManager dbManager, string demandId, string envSuffix)
        {
            var parameters = new Dictionary<string, object> { { "@DemandId", demandId } };
            var dtDemand = await dbManager.GetDataAsync(SqlQueries.Queries["GET_CONTRACT_BY_DEMAND"], parameters).ConfigureAwait(false);

            if (dtDemand.Rows.Count > 0 && dtDemand.Rows[0]["IT5UCONLREFEXN"] != DBNull.Value)
            {
                return dtDemand.Rows[0]["IT5UCONLREFEXN"].ToString().Trim();
            }

            throw new Exception($"No associated contract found for Demand ID {demandId} in environment {envSuffix}.");
        }

        private async Task<(DataTable dtLisa, DataTable dtElia)> GetCoreContractsAsync(DatabaseManager dbManager, string contractNumber)
        {
            var parameters = new Dictionary<string, object> { { "@ContractNumber", contractNumber } };

            var lisaTask = dbManager.GetDataAsync(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var eliaTask = dbManager.GetDataAsync(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            await Task.WhenAll(lisaTask, eliaTask).ConfigureAwait(false);

            return (await lisaTask, await eliaTask);
        }

        private async Task<string> ExtractLisaSectionAsync(DatabaseManager dbManager, DataTable dtLisa, string cleanedContract, StringBuilder sbLisa)
        {
            if (dtLisa.Rows[0]["NO_CNT"] == DBNull.Value)
                throw new Exception($"Le contrat {cleanedContract} a été trouvé mais son ID interne (NO_CNT) est NULL en base de données.");

            long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
            string internalIdString = internalId.ToString();

            await ExtractAndAppendTablesAsync(dbManager, LisaTables, "@InternalId", internalId, sbLisa).ConfigureAwait(false);

            return internalIdString;
        }

        private async Task<(string uconId, string demandId, string premium)> ExtractEliaSectionAsync(DatabaseManager dbManager, DataTable dtElia, StringBuilder sbElia)
        {
            string eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim() ?? "Not found";
            string premiumAmount = "0";
            string eliaDemandId = "Not found";

            // 3. CORRECTION : Ne plus ignorer silencieusement l'exception pour la prime
            if (SqlQueries.Queries.ContainsKey("FJ1.TB5UPRP"))
            {
                try
                {
                    var dtPremium = await dbManager.GetDataAsync(SqlQueries.Queries["FJ1.TB5UPRP"], new Dictionary<string, object> { { "@EliaId", eliaUconId } }).ConfigureAwait(false);
                    if (dtPremium.Rows.Count > 0 && dtPremium.Columns.Contains("IT5UPRPUBRU"))
                    {
                        premiumAmount = dtPremium.Rows[0]["IT5UPRPUBRU"]?.ToString()?.Trim() ?? "0";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Erreur lors de la récupération de la prime pour UconId {eliaUconId} : {ex.Message}");
                    // On log l'erreur mais on continue avec "0"
                }
            }

            // Récupération des Demands
            var dtDemand = await dbManager.GetDataAsync(SqlQueries.Queries["GET_ELIA_DEMAND_IDS"], new Dictionary<string, object> { { "@EliaId", eliaUconId } }).ConfigureAwait(false);
            var demandIds = new List<string>();

            if (dtDemand.Columns.Contains("IT5HDMDAIDN"))
            {
                foreach (DataRow row in dtDemand.Rows)
                {
                    if (!string.IsNullOrWhiteSpace(row["IT5HDMDAIDN"]?.ToString()))
                        demandIds.Add(row["IT5HDMDAIDN"].ToString().Trim());
                }
            }

            if (demandIds.Count > 0) eliaDemandId = string.Join(", ", demandIds);

            // Extraction parallèle des tables ELIA
            await ExtractAndAppendTablesAsync(dbManager, EliaTables, "@EliaId", eliaUconId, sbElia).ConfigureAwait(false);

            if (demandIds.Count > 0)
            {
                await ExtractAndAppendTablesAsync(dbManager, DemandTables, "@DemandIds", string.Join(",", demandIds), sbElia).ConfigureAwait(false);
            }

            return (eliaUconId, eliaDemandId, premiumAmount);
        }

        #endregion

        #region Méthodes IO et Utilitaires

        private async Task SaveExtractionToFileAsync(string contract, string envSuffix, string internalId, string uconId, StringBuilder sbLisa, StringBuilder sbElia)
        {
            Directory.CreateDirectory(Settings.OutputDir);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            char envLetter = !string.IsNullOrEmpty(envSuffix) ? char.ToUpper(envSuffix[0]) : 'U';

            string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_Uniq_{contract}_{timestamp}.csv");

            string lisaHeader = $"================================================================================\n=== SECTION LISA (INTERNAL ID: {internalId} | ENV: {envSuffix}) ===\n================================================================================\n";
            string eliaHeader = $"================================================================================\n=== SECTION ELIA (UCON ID: {uconId} | ENV: {envSuffix}) ===\n================================================================================\n";

            string combinedContent = lisaHeader + sbLisa.ToString() + "\n" + eliaHeader + sbElia.ToString();

            try
            {
                using (StreamWriter writer = new StreamWriter(combinedPath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(combinedContent).ConfigureAwait(false);
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[WARNING] File locked, trying alternative name. Erreur: {ex.Message}");
                string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_Uniq_{contract}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                using (StreamWriter writer = new StreamWriter(alternativePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(combinedContent).ConfigureAwait(false);
                }
            }
        }

        // 4. CORRECTION : Passage de DatabaseManager en paramètre plutôt que de créer des new() en boucle
        private async Task ExtractAndAppendTablesAsync(DatabaseManager dbManager, IEnumerable<string> tables, string parameterName, object parameterValue, StringBuilder sb)
        {
            var resultsDictionary = new ConcurrentDictionary<string, string>();
            var semaphore = new SemaphoreSlim(10);
            var tasks = new List<Task>();
            var tablesList = tables.ToList();

            foreach (var table in tablesList)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (SqlQueries.Queries.ContainsKey(table))
                        {
                            var dt = await dbManager.GetDataAsync(SqlQueries.Queries[table], new Dictionary<string, object> { { parameterName, parameterValue } }).ConfigureAwait(false);

                            var tempSb = new StringBuilder();
                            CsvFormatter.AddTableToBuffer(tempSb, table, dt);

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

            await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var table in tablesList)
            {
                if (resultsDictionary.TryGetValue(table, out var content))
                {
                    sb.Append(content);
                }
            }
        }

        #endregion
    }
}