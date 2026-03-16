using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoActivator.Config;
using AutoActivator.Services;

namespace AutoActivator.Gui
{
    public partial class MainWindow
    {
        // Instanciation des services métier
        private readonly ActivationDataService _activationDataService = new ActivationDataService();

        // -- GESTION DE L'INTERFACE UTILISATEUR --

        private void BtnBrowseActCsv_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Data Files (*.csv;*.xls;*.xlsx)|*.csv;*.xls;*.xlsx|CSV Files (*.csv)|*.csv|Excel Files (*.xls;*.xlsx)|*.xls;*.xlsx",
                Title = "Select file for batch activation",
                InitialDirectory = Path.GetFullPath(Settings.InputDir)
            };
            if (openFileDialog.ShowDialog() == true) TxtBatchActCsv.Text = openFileDialog.FileName;
        }

        private void ActInputType_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtActContract != null) TxtActContract.Text = string.Empty;
        }

        // NOUVEAU : Met à jour le chemin réseau selon le bouton radio, l'environnement et le canal choisis
        private void UpdateBatchActCsvPath()
        {
            // Vérification de sécurité (les éléments de l'UI peuvent être nulls pendant l'initialisation XAML)
            if (TxtBatchActCsv == null || RbBatchActSearchDemand == null) return;

            if (RbBatchActSearchDemand.IsChecked == true)
            {
                string envValue = CmbActEnv?.SelectedItem is ComboBoxItem eItem ? eItem.Tag?.ToString() ?? "D" : "D";

                // Si le canal n'est pas encore initialisé, on prend "C01" par défaut
                string channelValue = CmbActChannel?.SelectedItem is ComboBoxItem cItem ? cItem.Tag?.ToString() ?? "C01" : "C01";

                // Génération dynamique du chemin avec le canal et l'environnement
                TxtBatchActCsv.Text = $@"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_{channelValue}ComparisonsDB_URL_ELIA_LoginPage_{envValue}000.xls";
            }
            else if (RbBatchActSearchContract != null && RbBatchActSearchContract.IsChecked == true)
            {
                // Si on cherche par Contrat, on vide la case car le fichier réseau ne concerne que les Demands ID
                TxtBatchActCsv.Text = string.Empty;
            }
        }

        // Appelé quand on clique sur "Contrat" ou "Demand ID" dans l'onglet Batch
        private void BatchActInputType_Checked(object sender, RoutedEventArgs e) => UpdateBatchActCsvPath();

        // NOUVEAU : Appelé quand on change "Env (D/Q/...)" ou "Canal (C01/C02/...)"
        private void CmbActEnv_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchActCsvPath();
        private void CmbActChannel_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchActCsvPath();

        private void BtnCancelActivation_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                TxtStatus.Text = "Annulation en cours... La séquence va s'arrêter.";
                TxtStatus.Foreground = Brushes.DarkOrange;
            }
        }

        // -- EXECUTION : ACTIVATION UNITAIRE --

        private async void BtnRunSingleActivation_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            await RunProcessAsync(async () =>
            {
                string rawInput = "", envValue = "D", cus = "XXX", bucp = "382", cmdpmt = "8";
                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;
                bool isDemandId = false;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new Exception("Les identifiants ne sont pas configurés. Veuillez vous reconnecter.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    rawInput = TxtActContract.Text.Trim();
                    isDemandId = RbActSearchDemand?.IsChecked == true;
                    if (CmbActEnv.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";
                    cus = TxtActCus.Text.Trim();
                    if (CmbActBucp.SelectedItem is ComboBoxItem bItem) bucp = bItem.Content?.ToString() ?? "382";
                    if (CmbActCmdpmt.SelectedItem is ComboBoxItem cItem) cmdpmt = cItem.Content?.ToString() ?? "8";
                });

                if (string.IsNullOrEmpty(rawInput)) throw new Exception("Veuillez entrer une valeur de contrat ou de Demand ID.");

                string resolvedContract = rawInput;

                if (isDemandId)
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Recherche du contrat associé au Demand ID...");
                    resolvedContract = await _activationDataService.GetContractFromDemandAsync(rawInput, envValue + "000");
                    if (string.IsNullOrEmpty(resolvedContract))
                        throw new Exception($"Impossible de trouver un contrat associé au Demand ID {rawInput} dans la base {envValue}000.");
                }

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Recherche de la prime en base de données...");
                string amount = await _activationDataService.FetchPremiumAsync(resolvedContract, envValue + "000");

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Préparation de l'activation...");
                string formattedContract = _activationDataService.FormatContractForJcl(resolvedContract);

                StringBuilder report = new StringBuilder();
                report.AppendLine("=== RAPPORT D'ACTIVATION UNITAIRE ===");
                report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

                try
                {
                    // L'UI Thread est mis à jour proprement via InvokeAsync (non-bloquant)
                    await _activationDataService.ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, username, password, msg => Application.Current.Dispatcher.InvokeAsync(() => TxtStatus.Text = msg), _cts.Token);
                    report.AppendLine($"Input Original: {rawInput} | Contrat Trouvé: {resolvedContract} | Contrat JCL: {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Statut: SUCCÈS");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"Input Original: {rawInput} | Contrat Trouvé: {resolvedContract} | Contrat JCL: {formattedContract} | Env: {envValue} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Statut: ÉCHEC ({ex.Message})");
                    throw;
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

        // -- EXECUTION : ACTIVATION PAR LOT --

        private async void BtnRunBatchActivation_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            await RunProcessAsync(async () =>
            {
                string filePath = "", envValue = "D", cus = "XXX", bucp = "382", cmdpmt = "8";
                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;
                bool isDemandId = false;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new Exception("Les identifiants ne sont pas configurés.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    filePath = TxtBatchActCsv.Text.Trim();
                    isDemandId = RbBatchActSearchDemand?.IsChecked == true;
                    if (CmbActEnv.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";
                    cus = TxtActCus.Text.Trim();
                    if (CmbActBucp.SelectedItem is ComboBoxItem bItem) bucp = bItem.Content?.ToString() ?? "382";
                    if (CmbActCmdpmt.SelectedItem is ComboBoxItem cItem) cmdpmt = cItem.Content?.ToString() ?? "8";
                });

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) throw new Exception("Veuillez sélectionner un fichier valide.");

                // Utilisation de la méthode présente dans MainWindow.Extraction.cs
                if (filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Conversion du fichier Excel réseau en CSV local...");
                    filePath = await Task.Run(() => PrepareCsvFromExcel(filePath, envValue + "000"));
                }

                var batchService = new BatchActivationService(_activationDataService);

                // CORRECTION : L'UI Thread est mis à jour proprement via InvokeAsync pour ne pas bloquer les tâches parallèles
                var result = await batchService.RunBatchAsync(
                    filePath, isDemandId, envValue, cus, bucp, cmdpmt, username, password, Settings.OutputDir,
                    msg => Application.Current.Dispatcher.InvokeAsync(() => TxtStatus.Text = msg),
                    _cts.Token
                );

                _lastGeneratedPath = result.reportPath;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (PrgLoading != null) PrgLoading.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Batch terminé.\nSuccès: {result.successCount} \nÉchecs: {result.errorCount}\n\nOuvrez le fichier de log pour les détails.", "Activation Batch", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }
    }
}