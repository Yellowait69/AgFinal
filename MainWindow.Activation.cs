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
        private readonly ActivationDataService _activationDataService = new();
        private CancellationTokenSource _cts;

        // ===============================
        // UI HELPERS
        // ===============================

        private void UpdateStatus(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (TxtStatus != null)
                    TxtStatus.Text = message;
            });
        }

        private void ResetStatus(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = message;
                TxtStatus.Foreground = Brushes.Black;
            });
        }

        private (string env, string cus, string bucp, string cmdpmt) GetActivationParameters()
        {
            string envValue = "D";
            string cus = TxtActCus.Text.Trim();
            string bucp = "382";
            string cmdpmt = "8";

            if (CmbActEnv.SelectedItem is ComboBoxItem eItem)
                envValue = eItem.Tag?.ToString() ?? "D";

            if (CmbActBucp.SelectedItem is ComboBoxItem bItem)
                bucp = bItem.Content?.ToString() ?? "382";

            if (CmbActCmdpmt.SelectedItem is ComboBoxItem cItem)
                cmdpmt = cItem.Content?.ToString() ?? "8";

            return (envValue, cus, bucp, cmdpmt);
        }

        private void EnsureCredentials()
        {
            if (string.IsNullOrWhiteSpace(Settings.DbConfig.Uid) ||
                string.IsNullOrWhiteSpace(Settings.DbConfig.Pwd))
            {
                throw new Exception("Les identifiants ne sont pas configurés.");
            }
        }

        // ===============================
        // FILE BROWSER
        // ===============================

        private void BtnBrowseActCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Data Files (*.csv;*.xls;*.xlsx)|*.csv;*.xls;*.xlsx",
                Title = "Select file for batch activation",
                InitialDirectory = Path.GetFullPath(Settings.InputDir)
            };

            if (dialog.ShowDialog() == true)
                TxtBatchActCsv.Text = dialog.FileName;
        }

        // ===============================
        // UI EVENTS
        // ===============================

        private void ActInputType_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtActContract != null)
                TxtActContract.Text = string.Empty;
        }

        private void BatchActInputType_Checked(object sender, RoutedEventArgs e)
        {
            UpdateBatchActCsvPath();
        }

        private void CmbActEnv_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBatchActCsvPath();
        }

        private void CmbActChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBatchActCsvPath();
        }

        private void UpdateBatchActCsvPath()
        {
            if (TxtBatchActCsv == null || RbBatchActSearchDemand == null)
                return;

            if (RbBatchActSearchDemand.IsChecked == true)
            {
                string env = (CmbActEnv?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "D";
                string channel = (CmbActChannel?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "C01";

                TxtBatchActCsv.Text =
                    $@"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_{channel}ComparisonsDB_URL_ELIA_LoginPage_{env}000.xls";
            }
            else
            {
                TxtBatchActCsv.Text = string.Empty;
            }
        }

        private void BtnCancelActivation_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                TxtStatus.Text = "Annulation en cours...";
                TxtStatus.Foreground = Brushes.DarkOrange;
            }
        }

        // ===============================
        // SINGLE ACTIVATION
        // ===============================

        private async void BtnRunSingleActivation_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();

            await RunProcessAsync(async () =>
            {
                EnsureCredentials();

                string rawInput = TxtActContract.Text.Trim();
                bool isDemandId = RbActSearchDemand?.IsChecked == true;

                if (string.IsNullOrWhiteSpace(rawInput))
                    throw new Exception("Veuillez entrer un contrat ou Demand ID.");

                var (envValue, cus, bucp, cmdpmt) = GetActivationParameters();

                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;

                string resolvedContract = rawInput;

                if (isDemandId)
                {
                    UpdateStatus("Recherche du contrat via Demand ID...");

                    resolvedContract = await _activationDataService
                        .GetContractFromDemandAsync(rawInput, envValue + "000");

                    if (string.IsNullOrEmpty(resolvedContract))
                        throw new Exception("Contrat introuvable.");
                }

                UpdateStatus("Recherche du premium...");

                string amount = await _activationDataService
                    .FetchPremiumAsync(resolvedContract, envValue + "000");

                string formattedContract =
                    _activationDataService.FormatContractForJcl(resolvedContract);

                StringBuilder report = new();

                report.AppendLine("=== RAPPORT ACTIVATION UNITAIRE ===");
                report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine();

                try
                {
                    await _activationDataService.ExecuteActivationSequenceAsync(
                        formattedContract,
                        amount,
                        envValue,
                        cus,
                        bucp,
                        cmdpmt,
                        username,
                        password,
                        UpdateStatus,
                        _cts.Token
                    );

                    report.AppendLine($"Input: {rawInput} | Contract: {formattedContract} | Amount: {amount} | SUCCESS");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"Input: {rawInput} | ERROR: {ex.Message}");
                    throw;
                }
                finally
                {
                    Directory.CreateDirectory(Settings.OutputDir);

                    string path = Path.Combine(
                        Settings.OutputDir,
                        $"Activation_{formattedContract}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                    File.WriteAllText(path, report.ToString());

                    _lastGeneratedPath = path;
                }

                MessageBox.Show(
                    "Activation terminée. Consultez le rapport.",
                    "Activation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
        }

        // ===============================
        // BATCH ACTIVATION
        // ===============================

        private async void BtnRunBatchActivation_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();

            await RunProcessAsync(async () =>
            {
                EnsureCredentials();

                string filePath = TxtBatchActCsv.Text.Trim();

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    throw new Exception("Fichier invalide.");

                bool isDemandId = RbBatchActSearchDemand?.IsChecked == true;

                var (envValue, cus, bucp, cmdpmt) = GetActivationParameters();

                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;

                if (filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("Conversion Excel -> CSV...");

                    filePath = await Task.Run(() =>
                        PrepareCsvFromExcel(filePath, envValue + "000"));
                }

                var batchService = new BatchActivationService(_activationDataService);

                var result = await batchService.RunBatchAsync(
                    filePath,
                    isDemandId,
                    envValue,
                    cus,
                    bucp,
                    cmdpmt,
                    username,
                    password,
                    Settings.OutputDir,
                    UpdateStatus,
                    _cts.Token);

                _lastGeneratedPath = result.reportPath;

                MessageBox.Show(
                    $"Batch terminé.\nSuccès: {result.successCount}\nÉchecs: {result.errorCount}",
                    "Batch Activation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
        }
    }
}