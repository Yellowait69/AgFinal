using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Sql;
using AutoActivator.Utils;

namespace AutoActivator.Services
{
    // 1. FACTORY INTERFACE (Pour créer le DBManager selon l'environnement dynamiquement)
    public interface IDatabaseManagerFactory
    {
        IDatabaseManager Create(string envSuffix);
    }

    // 2. CONFIGURATION (Pour retirer les tableaux de tables codés en dur)
    public class ExtractionSettings
    {
        public string[] LisaTables { get; set; } = { "LV.PCONT0", "LV.ELIAT0", "LV.ELIHT0", "LV.SCNTT0", "LV.SWBGT0", "LV.SAVTT0", "LV.XRSTT0", "LV.SPERT0", "LV.ADMDT0", "FJ1.TB5LPPL", "FJ1.TB5LPPR", "FJ1.TB5LGDR", "LV.PRIST0", "LV.PECHT0", "LV.PFIET0", "LV.PMNTT0", "LV.PRCTT0", "LV.PSUMT0", "LV.SELTT0", "FJ1.TB5LPPF", "LV.FMVGT0", "LV.FMVDT0", "LV.SFTS", "LV.PINCT0", "LV.SCLST0", "LV.SCLRT0", "LV.SCLDT0", "LV.BSPDT0", "LV.BSPGT0", "LV.BPBAT0", "LV.BPPAT0", "LV.MWBGT0" };
        public string[] EliaTables { get; set; } = { "FJ1.TB5HELT", "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UCCR", "FJ1.TB5UAVE", "FJ1.TB5UPNR", "FJ1.TB5UPRP", "FJ1.TB5UPRS", "FJ1.TB5UPMP", "FJ1.TB5URPP", "FJ1.TB5UPRF", "FJ1.TB5UFML", "FJ1.TB5UCRB", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
        public string[] DemandTables { get; set; } = { "FJ1.TB5HDMD", "FJ1.TB5HDGM", "FJ1.TB5HDGD", "FJ1.TB5HPRO", "FJ1.TB5HEPT", "FJ1.TB5HDIC" };
    }

    public class ExtractionService
    {
        private readonly IDatabaseManagerFactory _dbManagerFactory;
        private readonly ILogger<ExtractionService> _logger;
        private readonly ExtractionSettings _settings;

        // 3. INJECTION DES DÉPENDANCES
        public ExtractionService(
            IDatabaseManagerFactory dbManagerFactory,
            ILogger<ExtractionService> logger,
            IOptions<ExtractionSettings> settings)
        {
            _dbManagerFactory = dbManagerFactory ?? throw new ArgumentNullException(nameof(dbManagerFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        // 4. AJOUT DU CANCELLATION TOKEN POUR L'ANNULATION
        public async Task<ExtractionResult> PerformExtractionAsync(
            string targetContract,
            string envSuffix,
            bool saveIndividualFile = true,
            bool isDemandId = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetContract))
                throw new ArgumentException("The input value is empty.", nameof(targetContract));

            string cleanedContract = targetContract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

            // Utilisation de la Factory au lieu de "new DatabaseManager()"
            var dbManager = _dbManagerFactory.Create(envSuffix);

            _logger.LogInformation("Démarrage de l'extraction pour le contrat {Contract} sur l'environnement {Env}", cleanedContract, envSuffix);

            if (isDemandId)
            {
                cleanedContract = await ResolveDemandIdAsync(dbManager, cleanedContract, envSuffix, cancellationToken).ConfigureAwait(false);
            }

            var (dtLisa, dtElia) = await GetCoreContractsAsync(dbManager, cleanedContract, cancellationToken).ConfigureAwait(false);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {cleanedContract} not found. Verify that you are targeting the correct database ({envSuffix}).");

            var sbLisa = new StringBuilder();
            var sbElia = new StringBuilder();
            string internalIdString = "Not found", eliaUconId = "Not found", eliaDemandId = "Not found", premiumAmount = "0";

            // Extraction LISA
            if (dtLisa.Rows.Count > 0)
            {
                internalIdString = await ExtractLisaSectionAsync(dbManager, dtLisa, cleanedContract, sbLisa, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                sbLisa.AppendLine($"### DIAGNOSTIC : Contract {cleanedContract} missing ###");
                _logger.LogWarning("Contrat LISA {Contract} non trouvé.", cleanedContract);
            }

            // Extraction ELIA
            if (dtElia.Rows.Count > 0)
            {
                (eliaUconId, eliaDemandId, premiumAmount) = await ExtractEliaSectionAsync(dbManager, dtElia, sbElia, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                sbElia.AppendLine("### ELIA SECTION : NO DATA FOUND ###");
                _logger.LogWarning("Section ELIA non trouvée pour le contrat {Contract}.", cleanedContract);
            }

            // Sauvegarde de fichier déléguée à une méthode dédiée
            if (saveIndividualFile)
            {
                await SaveExtractionToFileAsync(cleanedContract, envSuffix, internalIdString, eliaUconId, sbLisa, sbElia, cancellationToken).ConfigureAwait(false);
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

        #region Méthodes privées d'orchestration

        private async Task<string> ResolveDemandIdAsync(IDatabaseManager dbManager, string demandId, string envSuffix, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object> { { "@DemandId", demandId } };

            // Remarque : Votre GetDataAsync actuel ne prend pas de CancellationToken.
            // Pensez à l'ajouter dans la signature de l'interface IDatabaseManager.
            var dtDemand = await dbManager.GetDataAsync(SqlQueries.Queries["GET_CONTRACT_BY_DEMAND"], parameters).ConfigureAwait(false);

            if (dtDemand.Rows.Count > 0 && dtDemand.Rows[0]["IT5UCONLREFEXN"] != DBNull.Value)
            {
                return dtDemand.Rows[0]["IT5UCONLREFEXN"].ToString().Trim();
            }

            throw new Exception($"No associated contract found for Demand ID {demandId} in environment {envSuffix}.");
        }

        private async Task<(DataTable dtLisa, DataTable dtElia)> GetCoreContractsAsync(IDatabaseManager dbManager, string contractNumber, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object> { { "@ContractNumber", contractNumber } };

            var lisaTask = dbManager.GetDataAsync(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var eliaTask = dbManager.GetDataAsync(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            await Task.WhenAll(lisaTask, eliaTask).ConfigureAwait(false);

            return (await lisaTask, await eliaTask);
        }

        private async Task<string> ExtractLisaSectionAsync(IDatabaseManager dbManager, DataTable dtLisa, string cleanedContract, StringBuilder sbLisa, CancellationToken cancellationToken)
        {
            if (dtLisa.Rows[0]["NO_CNT"] == DBNull.Value)
                throw new Exception($"Le contrat {cleanedContract} a été trouvé mais son ID interne (NO_CNT) est NULL en base de données.");

            long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
            string internalIdString = internalId.ToString();

            // Utilisation des tables depuis _settings
            await ExtractAndAppendTablesAsync(dbManager, _settings.LisaTables, "@InternalId", internalId, sbLisa, cancellationToken).ConfigureAwait(false);

            return internalIdString;
        }

        private async Task<(string uconId, string demandId, string premium)> ExtractEliaSectionAsync(IDatabaseManager dbManager, DataTable dtElia, StringBuilder sbElia, CancellationToken cancellationToken)
        {
            string eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim() ?? "Not found";
            string premiumAmount = "0";
            string eliaDemandId = "Not found";

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
                    // Remplacement de Console.WriteLine par le Logger
                    _logger.LogWarning(ex, "Erreur lors de la récupération de la prime pour UconId {UconId}", eliaUconId);
                }
            }

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

            await ExtractAndAppendTablesAsync(dbManager, _settings.EliaTables, "@EliaId", eliaUconId, sbElia, cancellationToken).ConfigureAwait(false);

            if (demandIds.Count > 0)
            {
                await ExtractAndAppendTablesAsync(dbManager, _settings.DemandTables, "@DemandIds", string.Join(",", demandIds), sbElia, cancellationToken).ConfigureAwait(false);
            }

            return (eliaUconId, eliaDemandId, premiumAmount);
        }

        #endregion

        #region Méthodes IO et Utilitaires

        private async Task SaveExtractionToFileAsync(string contract, string envSuffix, string internalId, string uconId, StringBuilder sbLisa, StringBuilder sbElia, CancellationToken cancellationToken)
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
                    // Possibilité d'annuler l'écriture si l'utilisateur l'a demandé
                    await writer.WriteAsync(combinedContent.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Fichier verrouillé pour {Contract}, tentative avec un nom alternatif.", contract);
                string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{envLetter}_Uniq_{contract}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                using (StreamWriter writer = new StreamWriter(alternativePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(combinedContent.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task ExtractAndAppendTablesAsync(IDatabaseManager dbManager, IEnumerable<string> tables, string parameterName, object parameterValue, StringBuilder sb, CancellationToken cancellationToken)
        {
            var resultsDictionary = new ConcurrentDictionary<string, string>();
            var semaphore = new SemaphoreSlim(10);
            var tasks = new List<Task>();
            var tablesList = tables.ToList();

            foreach (var table in tablesList)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        // Vérifie si une annulation a été demandée
                        cancellationToken.ThrowIfCancellationRequested();

                        if (SqlQueries.Queries.ContainsKey(table))
                        {
                            var dt = await dbManager.GetDataAsync(SqlQueries.Queries[table], new Dictionary<string, object> { { parameterName, parameterValue } }).ConfigureAwait(false);

                            var tempSb = new StringBuilder();
                            CsvFormatter.AddTableToBuffer(tempSb, table, dt);

                            resultsDictionary.TryAdd(table, tempSb.ToString());
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("L'extraction de la table {Table} a été annulée.", table);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erreur SQL lors de l'extraction de la table {Table}", table);
                        resultsDictionary.TryAdd(table, $"### TABLE : {table} | EXTRACTION ERROR\nSQL Error: {ex.Message}\n");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Reconstitution dans le bon ordre
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