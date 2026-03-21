using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AutoActivator.Config;
using AutoActivator.Models;

namespace AutoActivator.Services
{
    // Interface pour faciliter les tests unitaires et l'injection de dépendances
    public interface IBatchExtractionService
    {
        Task PerformBatchExtractionAsync(string filePath, string env, Action<BatchProgressInfo> onProgressUpdate, bool isDemandId = false, CancellationToken cancellationToken = default);
    }

    public class BatchExtractionService : IBatchExtractionService
    {
        private readonly ExtractionService _extractionService;
        private readonly ILogger<BatchExtractionService> _logger;

        // Injection des dépendances (ExtractionService + Logger)
        public BatchExtractionService(ExtractionService extractionService, ILogger<BatchExtractionService> logger)
        {
            _extractionService = extractionService ?? throw new ArgumentNullException(nameof(extractionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task PerformBatchExtractionAsync(
            string filePath,
            string env,
            Action<BatchProgressInfo> onProgressUpdate,
            bool isDemandId = false,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("Le fichier CSV spécifié est introuvable : {FilePath}", filePath);
                throw new FileNotFoundException("The specified CSV file could not be found.", filePath);
            }

            _logger.LogInformation("Démarrage du batch d'extraction depuis {FilePath} sur l'environnement {Env}", filePath, env);

            var globalCombinedResults = new ConcurrentDictionary<int, string>();
            var contractsToProcess = new List<(int rowNum, string contractNumber, string rawTestId)>();

            // --- 1 & 2. LECTURE INTELLIGENTE ET CHARGEMENT (Avec gestion d'annulation) ---
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIndex = -1, testIdIndex = -1;
                bool headerFound = false;
                int lineNumber = 0;

                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested(); // Arrêt possible pendant la lecture

                    lineNumber++;
                    if (line.StartsWith("\uFEFF")) line = line.Substring(1);
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.Replace("\"", "").TrimStart().StartsWith("Contract in", StringComparison.OrdinalIgnoreCase))
                        continue;

                    delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
                    var cols = ParseCsvLine(line, delimiter);

                    for (int i = 0; i < cols.Count; i++)
                    {
                        string h = cols[i].Trim().ToLower();
                        if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa") || h.Contains("value") || h.Contains("demand")) contractIndex = i;
                        if (h.Contains("test") || h.Contains("id test") || h.Contains("idtest") || h == "key") testIdIndex = i;
                    }

                    if (contractIndex != -1)
                    {
                        headerFound = true;
                        break;
                    }
                }

                if (!headerFound)
                    throw new Exception("Unable to find 'Value', 'Demand', or 'Contract' column in the CSV file.");

                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line, delimiter);

                    if (columns.Count > contractIndex)
                    {
                        string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();

                        if (contractNumber.Equals("End of File", StringComparison.OrdinalIgnoreCase)) continue;

                        string rawTestId = (testIdIndex != -1 && columns.Count > testIdIndex)
                            ? columns[testIdIndex].Replace("=", "").Replace("\"", "").Trim()
                            : contractNumber;

                        if (!string.IsNullOrEmpty(contractNumber))
                        {
                            contractsToProcess.Add((lineNumber, contractNumber, rawTestId));
                        }
                    }
                }
            }

            // --- 3. TRAITEMENT PARALLÈLE MASSIF ---
            int totalItems = contractsToProcess.Count;
            int processedItems = 0;

            _logger.LogInformation("Lancement du traitement parallèle pour {Count} contrats.", totalItems);

            var semaphore = new SemaphoreSlim(10); // Limite de concurrence préservée
            var tasks = contractsToProcess.Select((item, index) => Task.Run(async () =>
            {
                await Task.Delay(Math.Min(index * 20, 2000), cancellationToken).ConfigureAwait(false);

                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Utilisation de l'ExtractionService mis à jour (qui supporte maintenant le CancellationToken)
                    ExtractionResult result = await _extractionService.PerformExtractionAsync(item.contractNumber, env, false, isDemandId, cancellationToken).ConfigureAwait(false);

                    string displayContract = isDemandId && !string.IsNullOrEmpty(result.ContractReference) ? result.ContractReference : item.contractNumber;

                    if (!string.IsNullOrWhiteSpace(result.LisaContent) || !string.IsNullOrWhiteSpace(result.EliaContent))
                    {
                        var localReport = new StringBuilder();
                        localReport.AppendLine(new string('=', 80));
                        localReport.AppendLine($"### GLOBAL CONTRACT REPORT: {displayContract} | TEST ID: {item.rawTestId} | ENV: {env} ###");
                        localReport.AppendLine(new string('=', 80));

                        if (!string.IsNullOrWhiteSpace(result.LisaContent))
                        {
                            localReport.AppendLine($"--- LISA SECTION ---");
                            localReport.Append(result.LisaContent).AppendLine();
                        }

                        if (!string.IsNullOrWhiteSpace(result.EliaContent))
                        {
                            localReport.AppendLine($"--- ELIA SECTION (UCON: {result.UconId}) ---");
                            localReport.Append(result.EliaContent).AppendLine();
                        }

                        globalCombinedResults.TryAdd(index, localReport.ToString());
                    }

                    int current = Interlocked.Increment(ref processedItems);

                    onProgressUpdate?.Invoke(new BatchProgressInfo
                    {
                        RowNum = item.rowNum,
                        ContractId = displayContract,
                        InternalId = result.InternalId,
                        Product = env,
                        Premium = string.IsNullOrWhiteSpace(result.Premium) ? "0" : result.Premium,
                        UconId = result.UconId,
                        DemandId = result.DemandId,
                        Status = "OK",
                        CurrentItem = current,
                        TotalItems = totalItems
                    });
                }
                catch (OperationCanceledException)
                {
                    // Tâche annulée silencieusement pour ne pas spammer les logs
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'extraction du contrat {Contract} à la ligne {Line}", item.contractNumber, item.rowNum);
                    int current = Interlocked.Increment(ref processedItems);
                    onProgressUpdate?.Invoke(new BatchProgressInfo
                    {
                        RowNum = item.rowNum,
                        ContractId = $"{item.contractNumber} (FAILED)",
                        InternalId = "Error",
                        Product = env,
                        Premium = "0",
                        UconId = "Error",
                        DemandId = "Error",
                        Status = ex.Message.ToLower().Contains("not found") ? "Not found in DB" : "SQL Error",
                        CurrentItem = current,
                        TotalItems = totalItems
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken)).ToList();

            // Attendre la fin ou l'annulation
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // --- 4. SAUVEGARDE OPTIMISÉE DU RAPPORT GLOBAL ---
            _logger.LogInformation("Génération du fichier final...");
            string origFileName = Path.GetFileNameWithoutExtension(filePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(Settings.OutputDir);

            char envLetter = !string.IsNullOrEmpty(env) ? char.ToUpper(env[0]) : 'U';
            string combinedPath = Path.Combine(Settings.OutputDir, $"Extraction_{origFileName}_{envLetter}_{timestamp}.csv");

            try
            {
                using (var writer = new StreamWriter(combinedPath, false, Encoding.UTF8))
                {
                    if (globalCombinedResults.IsEmpty)
                    {
                        await writer.WriteAsync("NO CONTRACT FOUND.").ConfigureAwait(false);
                    }
                    else
                    {
                        // OPTIMISATION MÉMOIRE : Au lieu de créer un immense StringBuilder final,
                        // on écrit directement dans le Stream.
                        for (int i = 0; i < totalItems; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (globalCombinedResults.TryGetValue(i, out var report))
                            {
                                await writer.WriteAsync(report).ConfigureAwait(false);
                            }
                        }
                    }
                }
                _logger.LogInformation("Fichier batch sauvegardé avec succès : {Path}", combinedPath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Le fichier principal est verrouillé, création d'une alternative...");
                string alternativePath = Path.Combine(Settings.OutputDir, $"Extraction_{origFileName}_{envLetter}_{timestamp}_{Guid.NewGuid().ToString().Substring(0, 4)}.csv");
                using (var writer = new StreamWriter(alternativePath, false, Encoding.UTF8))
                {
                    for (int i = 0; i < totalItems; i++)
                    {
                        if (globalCombinedResults.TryGetValue(i, out var report))
                        {
                            await writer.WriteAsync(report).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        // Remarque : Pour un projet encore plus professionnel, cette méthode ParseCsvLine pourrait être
        // totalement remplacée par l'utilisation de la bibliothèque NuGet "CsvHelper".
        private List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            result.Add(currentField.ToString());
            return result;
        }
    }
}