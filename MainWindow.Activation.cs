using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoActivator.Config;
using AutoActivator.Services;
using AutoActivator.Sql;

namespace AutoActivator.Gui
{
    public partial class MainWindow
    {
        private void BtnBrowseActCsv_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV Files|*.csv",
                Title = "Select CSV file for batch activation",
                InitialDirectory = Path.GetFullPath(Settings.InputDir)
            };
            if (openFileDialog.ShowDialog() == true) TxtBatchActCsv.Text = openFileDialog.FileName;
        }

        private void BtnCancelActivation_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                TxtStatus.Text = "Annulation en cours... La séquence va s'arrêter au prochain point de contrôle.";
                TxtStatus.Foreground = Brushes.DarkOrange;
            }
        }

        /// <summary>
        /// Formate le numéro de contrat : supprime les tirets. Si longueur == 12, on retire le 1er et les 2 derniers.
        /// Exemple : "123-4567899-10" -> "123456789910" -> "234567899"
        /// </summary>
        private string FormatContractNumber(string rawContract)
        {
            string cleaned = rawContract.Replace("-", "").Replace(" ", "").Trim();
            if (cleaned.Length == 12)
            {
                return cleaned.Substring(1, 9);
            }
            return cleaned;
        }

        /// <summary>
        /// Interroge directement la DB pour récupérer la prime liée au contrat.
        /// </summary>
        private async Task<string> FetchPremiumAsync(string contract, string envSuffix)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var db = new DatabaseManager(envSuffix);
                    var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], new Dictionary<string, object> { { "@ContractNumber", contract } });

                    if (dtElia.Rows.Count > 0)
                    {
                        string eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(eliaUconId) && SqlQueries.Queries.ContainsKey("FJ1.TB5UPRP"))
                        {
                            var dtPremium = db.GetData(SqlQueries.Queries["FJ1.TB5UPRP"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                            if (dtPremium.Rows.Count > 0 && dtPremium.Columns.Contains("IT5UPRPUBRU"))
                            {
                                return dtPremium.Rows[0]["IT5UPRPUBRU"]?.ToString()?.Trim() ?? "0";
                            }
                        }
                    }
                }
                catch { }
                return "0";
            });
        }

        // -- ACTIVATION UNITAIRE --
        private async void BtnRunSingleActivation_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            await RunProcessAsync(async () =>
            {
                string rawContract = "", envValue = "D", cus = "XXX", bucp = "382", cmdpmt = "8";
                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new Exception("Les identifiants ne sont pas configurés. Veuillez vous reconnecter.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    rawContract = TxtActContract.Text.Trim();
                    if (CmbActEnv.SelectedItem is System.Windows.Controls.ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";
                    cus = TxtActCus.Text.Trim();
                    if (CmbActBucp.SelectedItem is System.Windows.Controls.ComboBoxItem bItem) bucp = bItem.Content?.ToString() ?? "382";
                    if (CmbActCmdpmt.SelectedItem is System.Windows.Controls.ComboBoxItem cItem) cmdpmt = cItem.Content?.ToString() ?? "8";
                });

                if (string.IsNullOrEmpty(rawContract)) throw new Exception("Veuillez entrer un numéro de contrat.");

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Recherche de la prime en base de données...");
                string amount = await FetchPremiumAsync(rawContract, envValue + "000");

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Préparation de l'activation...");
                string formattedContract = FormatContractNumber(rawContract);

                StringBuilder report = new StringBuilder();
                report.AppendLine("=== RAPPORT D'ACTIVATION UNITAIRE ===");
                report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

                try
                {
                    await ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, username, password, _cts.Token);
                    report.AppendLine($"Contrat Original: {rawContract} | Contrat Modifié: {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Statut: SUCCÈS");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"Contrat Original: {rawContract} | Contrat Modifié: {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Statut: ÉCHEC ({ex.Message})");
                    throw; // On relance l'erreur pour l'afficher à l'utilisateur
                }
                finally
                {
                    string path = Path.Combine(Settings.OutputDir, $"Activation_Unitaire_{formattedContract}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(path, report.ToString());
                    _lastGeneratedPath = path;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (PrgLoading != null) PrgLoading.Visibility = Visibility.Collapsed;
                    MessageBox.Show("Séquence d'activation terminée. Consultez le rapport généré.", "Activation", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }

        // -- ACTIVATION PAR LOT (CSV) --
        private async void BtnRunBatchActivation_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            await RunProcessAsync(async () =>
            {
                string csvPath = "", envValue = "D", cus = "XXX", bucp = "382", cmdpmt = "8";
                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new Exception("Les identifiants ne sont pas configurés.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    csvPath = TxtBatchActCsv.Text.Trim();
                    if (CmbActEnv.SelectedItem is System.Windows.Controls.ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";
                    cus = TxtActCus.Text.Trim();
                    if (CmbActBucp.SelectedItem is System.Windows.Controls.ComboBoxItem bItem) bucp = bItem.Content?.ToString() ?? "382";
                    if (CmbActCmdpmt.SelectedItem is System.Windows.Controls.ComboBoxItem cItem) cmdpmt = cItem.Content?.ToString() ?? "8";
                });

                if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath)) throw new Exception("Veuillez sélectionner un fichier CSV valide.");

                StringBuilder report = new StringBuilder();
                report.AppendLine("=== RAPPORT D'ACTIVATION BATCH ===");
                report.AppendLine($"Date de lancement: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine($"Configuration Globale -> Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt}\n");
                report.AppendLine("---------------------------------------------------------------------------------------------------------");

                int successCount = 0, errorCount = 0;

                using (var reader = new StreamReader(csvPath, Encoding.UTF8))
                {
                    string headerLine = reader.ReadLine();
                    if (headerLine != null && headerLine.StartsWith("\uFEFF")) headerLine = headerLine.Substring(1);

                    char delimiter = headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';
                    var headers = ParseCsvLine(headerLine, delimiter);

                    int contractIdx = -1, premiumIdx = -1;
                    for (int i = 0; i < headers.Count; i++)
                    {
                        string h = headers[i].Trim().ToLower();
                        if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa")) contractIdx = i;
                        if (h.Contains("premium") || h.Contains("prime") || h.Contains("amount")) premiumIdx = i;
                    }

                    if (contractIdx == -1) throw new Exception("Colonne de contrat introuvable dans le CSV.");

                    string line;
                    int rowNum = 1;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (_cts.Token.IsCancellationRequested) break;

                        var columns = ParseCsvLine(line, delimiter);
                        if (columns.Count <= contractIdx) continue;

                        string rawContract = columns[contractIdx].Replace("=", "").Trim();
                        string amount = (premiumIdx != -1 && columns.Count > premiumIdx) ? columns[premiumIdx].Replace("=", "").Trim() : "0";
                        if (string.IsNullOrEmpty(rawContract)) continue;

                        string formattedContract = FormatContractNumber(rawContract);

                        Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = $"Batch en cours: Activation de {formattedContract} (Ligne {rowNum++})...");

                        try
                        {
                            await ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, username, password, _cts.Token);
                            // Rapport avec toutes les données demandées (Succès)
                            report.AppendLine($"[SUCCÈS] Contrat: {rawContract} -> {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount}");
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            // Rapport avec toutes les données demandées (Échec)
                            report.AppendLine($"[ÉCHEC]  Contrat: {rawContract} -> {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Erreur: {ex.Message}");
                            errorCount++;
                            // On "avale" l'erreur pour continuer la boucle sur le contrat suivant
                        }
                    }
                }

                report.AppendLine("---------------------------------------------------------------------------------------------------------");
                report.AppendLine($"FIN DU TRAITEMENT. Succès: {successCount} | Échecs: {errorCount}");

                string path = Path.Combine(Settings.OutputDir, $"Activation_Batch_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, report.ToString());
                _lastGeneratedPath = path;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (PrgLoading != null) PrgLoading.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Batch terminé.\nSuccès: {successCount} \nÉchecs: {errorCount}\n\nOuvrez le fichier de log pour les détails.", "Activation Batch", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }

        // -- LOGIQUE CENTRALE D'ACTIVATION (Injecte les variables et lance JCLs) --
        private async Task ExecuteActivationSequenceAsync(string contract, string amount, string envValue, string cus, string bucp, string cmdpmt, string username, string password, CancellationToken token)
        {
            string q2 = envValue == "D" ? "Q2T" : "Q2C";
            string fastCtrl = envValue == "D" ? "I0T.DB.CA.FIB.FASTCTRL" : "I10.DB.CA.FIB.FASTCTRL";
            string envImsValue = envValue == "D" ? "T" : "C";

            // Formatage de la prime
            string paddedAmount = amount.PadLeft(10, '0');
            string paddedBucp = bucp.PadLeft(5, '0');

            var generalVariables = new Dictionary<string, string>
            {
                { "ENV", envValue }, { "ENVIMS", envImsValue }, { "CUS", cus },
                { "YYMMDD", DateTime.Now.ToString("yyMMdd") }, { "YYYY", DateTime.Now.ToString("yyyy") },
                { "MM", DateTime.Now.ToString("MM") }, { "DD", DateTime.Now.ToString("dd") },
                { "CLASS", "A" }, { "CNTBEG", contract }, { "CNTEND", contract },
                { "MMDD", DateTime.Now.ToString("MMdd") }, { "CYMD", DateTime.Now.ToString("yyyyMMdd") },
                { "STE", "A" }, { "Q2", q2 }, { "CM.", "     " },
                { "DRUN", DateTime.Now.ToString("yyyyMMdd") }, { "NREMB", "20" },
                { "CONTR-EX", "Y" }, { "CONTR-RE", "Y" }, { "CONTR-UN", "Y" },
                { "NJJART72", "5" }, { "FASTCTRL", fastCtrl }
            };

            var addprctVariables = new Dictionary<string, string>
            {
                { "CMDPMT", cmdpmt }, { "AMOUNT", paddedAmount },
                { "BUCP", paddedBucp }, { "USERNAME", username }
            };

            string jclFolder = @"\\Jafile02\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\LVCHAIN\JCL";

            if (!Directory.Exists(jclFolder))
                throw new Exception($"Dossier réseau des JCL inaccessible: {jclFolder}");

            var orchestrator = new ActivationOrchestrator(jclFolder);

            await orchestrator.RunActivationSequenceAsync(
                generalVariables, addprctVariables, username, password,
                msg => Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = msg),
                token
            );
        }

        // -- PARSER CSV INTELLIGENT (Utilisé pour le batch) --
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