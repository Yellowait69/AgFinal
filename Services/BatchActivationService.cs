using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActivator.Services
{
    public class BatchActivationService
    {
        private readonly ActivationDataService _dataService;

        public BatchActivationService(ActivationDataService dataService)
        {
            _dataService = dataService;
        }

        public async Task<(int successCount, int errorCount, string reportPath)> RunBatchAsync(
            string filePath, bool isDemandId, string envValue, string cus, string bucp, string cmdpmt,
            string username, string password, string outputDir, Action<string> onProgress, CancellationToken token)
        {
            // DÉBLOCAGE RÉSEAU. Autorise plus de 2 requêtes HTTP simultanées.
            // C'est vital car sinon .NET bloque la 3ème requête indéfiniment.
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;

            StringBuilder report = new StringBuilder();
            report.AppendLine("=== RAPPORT D'ACTIVATION BATCH (MODE PARALLÈLE HAUTE VITESSE) ===");
            report.AppendLine($"Date de lancement: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Configuration Globale -> Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt}\n");
            report.AppendLine("---------------------------------------------------------------------------------------------------------");

            int successCount = 0;
            int errorCount = 0;

            // Structure Thread-Safe pour stocker les résultats dans le désordre, puis les trier à la fin
            var globalReport = new ConcurrentBag<(int RowNum, string Message)>();
            var contractsToProcess = new List<(string rawInput, int rowNum)>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                char delimiter = ';';
                int contractIdx = -1;
                bool headerFound = false;

                // --- 1. RECHERCHE DE L'EN-TÊTE ---
                // Ajout de ConfigureAwait(false) pour éviter les deadlocks
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (line.StartsWith("\uFEFF")) line = line.Substring(1); // Nettoyage BOM
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.Replace("\"", "").TrimStart().StartsWith("Contract in", StringComparison.OrdinalIgnoreCase))
                        continue;

                    delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
                    var headers = ParseCsvLine(line, delimiter);

                    for (int i = 0; i < headers.Count; i++)
                    {
                        string h = headers[i].Trim().ToLower();
                        if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa") || h.Contains("value") || h.Contains("demand"))
                        {
                            contractIdx = i;
                        }
                    }

                    if (contractIdx != -1) { headerFound = true; break; }
                }

                if (!headerFound)
                    throw new Exception("Colonne de contrat ou de Demand introuvable dans le fichier.");

                // --- 2. CHARGEMENT EN MÉMOIRE ---
                int currentRow = 1;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = ParseCsvLine(line, delimiter);
                    if (columns.Count <= contractIdx) continue;

                    string rawInput = columns[contractIdx].Replace("=", "").Replace("\"", "").Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

                    // On ignore les lignes vides OU la ligne parasite "End of File"
                    if (string.IsNullOrEmpty(rawInput) || rawInput.Equals("End of File", StringComparison.OrdinalIgnoreCase))
                        continue;

                    contractsToProcess.Add((rawInput, currentRow++));
                }
            }

            // --- 3. TRAITEMENT PARALLÈLE MASSIF ---

            // ACCÉLÉRATION : On passe à 15 requêtes simultanées
            var semaphore = new SemaphoreSlim(15);

            // Initialisation des compteurs
            int totalItems = contractsToProcess.Count;
            int processedItems = 0;

            onProgress($"Lancement du traitement parallèle très haute vitesse... (0 / {totalItems} contrats)");

            // Utilisation de l'index dans le Select pour créer un micro-décalage au lancement
            var tasks = contractsToProcess.Select((item, index) => Task.Run(async () =>
            {
                // Micro-décalage : Évite que 15 requêtes frappent le serveur ou la base de données à la même milliseconde.
                // Plafonné à 2 secondes (2000ms). L'ajout de ConfigureAwait(false) est CRUCIAL ici.
                await Task.Delay(Math.Min(index * 100, 2000), token).ConfigureAwait(false);

                await semaphore.WaitAsync(token).ConfigureAwait(false); // Attente d'un "ticket" d'exécution
                try
                {
                    if (token.IsCancellationRequested) return;

                    string resolvedContract = item.rawInput;

                    // Ajout du compteur dynamique au début du message
                    onProgress($"[{processedItems} / {totalItems} terminés] Recherche DB : {item.rawInput}...");

                    // A. Résolution Demand ID (Si nécessaire)
                    if (isDemandId)
                    {
                        resolvedContract = await _dataService.GetContractFromDemandAsync(item.rawInput, envValue + "000").ConfigureAwait(false);

                        if (string.IsNullOrEmpty(resolvedContract))
                        {
                            globalReport.Add((item.rowNum, $"[ÉCHEC]  Ligne {item.rowNum,-4} | Input: {item.rawInput} | Erreur: Aucun contrat associé à ce Demand ID en base."));
                            Interlocked.Increment(ref errorCount); // Thread-safe incrémentation

                            int currentErr = Interlocked.Increment(ref processedItems);
                            onProgress($"[{currentErr} / {totalItems} terminés] Échec Demand ID : {item.rawInput}");
                            return;
                        }
                    }

                    // B. Recherche de la prime
                    string amount = await _dataService.FetchPremiumAsync(resolvedContract, envValue + "000").ConfigureAwait(false);
                    string formattedContract = _dataService.FormatContractForJcl(resolvedContract);

                    // Ajout du compteur dynamique au début du message
                    onProgress($"[{processedItems} / {totalItems} terminés] Envoi Mainframe API : {formattedContract}...");

                    // C. Exécution de la séquence d'activation
                    await _dataService.ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, username, password, msg => { }, token).ConfigureAwait(false);

                    globalReport.Add((item.rowNum, $"[SUCCÈS] Ligne {item.rowNum,-4} | Input: {item.rawInput} -> Contrat JCL: {formattedContract} | Env: {envValue} | Amount: {amount}"));
                    Interlocked.Increment(ref successCount);

                    int currentOk = Interlocked.Increment(ref processedItems);
                    onProgress($"[{currentOk} / {totalItems} terminés] Succès : {formattedContract}");
                }
                catch (Exception ex)
                {
                    globalReport.Add((item.rowNum, $"[ÉCHEC]  Ligne {item.rowNum,-4} | Input: {item.rawInput} | Erreur: {ex.Message}"));
                    Interlocked.Increment(ref errorCount);

                    int currentFail = Interlocked.Increment(ref processedItems);
                    onProgress($"[{currentFail} / {totalItems} terminés] Échec : {item.rawInput}");
                }
                finally
                {
                    semaphore.Release(); // Libère le "ticket" pour le contrat suivant
                }
            }, token)).ToList(); // .ToList() est CRUCIAL ici pour démarrer l'exécution.

            // On attend que TOUTES les activations soient terminées
            // Le ConfigureAwait(false) empêche le blocage final (deadlock) de l'interface !
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Message de transition pour l'utilisateur
            onProgress("Tous les contrats ont été traités. Génération du rapport...");

            // --- 4. ASSEMBLAGE DU RAPPORT FINAL ---
            // Les contrats ont été traités dans le désordre (parallélisme), on les trie par numéro de ligne pour la lisibilité
            foreach (var log in globalReport.OrderBy(x => x.RowNum))
            {
                report.AppendLine(log.Message);
            }

            report.AppendLine("---------------------------------------------------------------------------------------------------------");
            report.AppendLine($"FIN DU TRAITEMENT. Succès: {successCount} | Échecs: {errorCount}");

            string reportPath = string.Empty;

            try
            {
                // CRUCIAL : S'assure que le dossier d'export existe avant d'écrire, sinon l'application crashe silencieusement
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                reportPath = Path.Combine(outputDir, $"Activation_Batch_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(reportPath, report.ToString());
                onProgress("Rapport généré avec succès !");
            }
            catch (Exception ex)
            {
                onProgress($"ERREUR GRAVE : Impossible de sauvegarder le rapport ({ex.Message})");
                throw;
            }

            return (successCount, errorCount, reportPath);
        }

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
                    else { inQuotes = !inQuotes; }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else { currentField.Append(c); }
            }
            result.Add(currentField.ToString());
            return result;
        }
    }
}