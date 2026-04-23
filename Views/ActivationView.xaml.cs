using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoActivator.Config;
using AutoActivator.Services;
using Excel = Microsoft.Office.Interop.Excel;

namespace AutoActivator.Gui.Views
{
    public partial class ActivationView : UserControl
    {
        // Utilisation du service de données pour l'activation
        private readonly ActivationDataService _activationDataService = new ActivationDataService();
        private CancellationTokenSource _cts;

        public ActivationView()
        {
            InitializeComponent();
        }

        // Navigation vers l'aide ciblée sur l'activation
        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.OpenHelpTargetingTab(1);
            }
        }

        // Sélection de fichiers CSV ou Excel pour le batch
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

        // Mise à jour dynamique du chemin réseau si "Demand ID" est coché
        private void UpdateBatchActCsvPath()
        {
            if (TxtBatchActCsv == null || RbBatchActSearchDemand == null) return;

            if (RbBatchActSearchDemand.IsChecked == true)
            {
                string envValue = CmbActEnv?.SelectedItem is ComboBoxItem eItem ? eItem.Tag?.ToString() ?? "D" : "D";
                string channelValue = CmbActChannel?.SelectedItem is ComboBoxItem cItem ? cItem.Tag?.ToString() ?? "C01" : "C01";
                TxtBatchActCsv.Text = $@"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_{channelValue}ComparisonsDB_URL_ELIA_LoginPage_{envValue}000.xls";
            }
            else if (RbBatchActSearchContract != null && RbBatchActSearchContract.IsChecked == true)
            {
                TxtBatchActCsv.Text = string.Empty;
            }
        }

        private void BatchActInputType_Checked(object sender, RoutedEventArgs e) => UpdateBatchActCsvPath();
        private void CmbActEnv_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchActCsvPath();

        // Gestion de la visibilité des boutons "Skip Prime" selon le canal
        private void CmbActChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBatchActCsvPath();
            if (CmbActChannel?.SelectedItem is ComboBoxItem cItem)
            {
                string channelValue = cItem.Tag?.ToString() ?? "C01";
                Visibility skipPrimeVisibility = (channelValue == "C01" || channelValue == "C03") ? Visibility.Visible : Visibility.Collapsed;

                if (BtnSkipPrimeSingleAct != null) BtnSkipPrimeSingleAct.Visibility = skipPrimeVisibility;
                if (BtnSkipPrimeBatchAct != null) BtnSkipPrimeBatchAct.Visibility = skipPrimeVisibility;
            }
        }

        // Annulation sécurisée de la séquence en cours
        private void BtnCancelActivation_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                if (Window.GetWindow(this) is MainWindow mainWindow)
                {
                    mainWindow.TxtStatus.Text = "Canceling... The sequence will stop shortly.";
                    mainWindow.TxtStatus.Foreground = Brushes.DarkOrange;
                }
            }
        }

        // Configuration automatique des paramètres selon le profil métier
        private void Preset_Checked(object sender, RoutedEventArgs e)
        {
            if (CmbActBucp == null || CmbActCmdpmt == null) return;
            if (sender == RbPresetBank)
            {
                SelectComboBoxItemByContent(CmbActBucp, "382");
                SelectComboBoxItemByContent(CmbActCmdpmt, "8");
            }
            else if (sender == RbPresetBrokerOld)
            {
                SelectComboBoxItemByContent(CmbActBucp, "49797");
                SelectComboBoxItemByContent(CmbActCmdpmt, "X");
            }
            else if (sender == RbPresetBrokerNew)
            {
                SelectComboBoxItemByContent(CmbActBucp, "80819");
                SelectComboBoxItemByContent(CmbActCmdpmt, "H");
            }
        }

        private void BtnClearPreset_Click(object sender, RoutedEventArgs e)
        {
            if (RbPresetBank != null) RbPresetBank.IsChecked = false;
            if (RbPresetBrokerOld != null) RbPresetBrokerOld.IsChecked = false;
            if (RbPresetBrokerNew != null) RbPresetBrokerNew.IsChecked = false;
        }

        private void SelectComboBoxItemByContent(ComboBox comboBox, string content)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content?.ToString() == content) { comboBox.SelectedItem = item; break; }
            }
        }

        // -----------------------------------------------------------
        // LOGIQUE D'ACTIVATION UNIQUE (Single)
        // -----------------------------------------------------------
        private void BtnRunSingleActivation_Click(object sender, RoutedEventArgs e) => RunSingleActivation(false);
        private void BtnSkipPrimeSingleAct_Click(object sender, RoutedEventArgs e) => RunSingleActivation(true);

        private async void RunSingleActivation(bool skipPrime)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;
            _cts = new CancellationTokenSource();

            await mainWindow.RunProcessAsync(async () =>
            {
                string rawInput = "", envValue = "D", cus = "XXX", bucp = "382", cmdpmt = "8", channel = "C01";
                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;
                bool isDemandId = false;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new Exception("Credentials are not configured. Please log in again.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    rawInput = TxtActContract.Text.Trim();
                    isDemandId = RbActSearchDemand?.IsChecked == true;
                    if (CmbActEnv.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";
                    cus = TxtActCus.Text.Trim();
                    if (CmbActBucp.SelectedItem is ComboBoxItem bItem) bucp = bItem.Content?.ToString() ?? "382";
                    if (CmbActCmdpmt.SelectedItem is ComboBoxItem cItem) cmdpmt = cItem.Content?.ToString() ?? "8";
                    if (CmbActChannel.SelectedItem is ComboBoxItem chItem) channel = chItem.Tag?.ToString() ?? "C01";
                });

                if (string.IsNullOrEmpty(rawInput)) throw new Exception("Please enter a contract value or Demand ID.");

                string resolvedContract = rawInput;

                // Résolution si Demand ID
                if (isDemandId)
                {
                    Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Searching for the contract associated with the Demand ID...");
                    resolvedContract = await _activationDataService.GetContractFromDemandAsync(rawInput, envValue + "000");
                    if (string.IsNullOrEmpty(resolvedContract))
                        throw new Exception($"Unable to find a contract associated with Demand ID {rawInput} in the {envValue}000 database.");
                }

                // Récupération prime et formatage
                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Fetching premium from the database...");
                string amount = await _activationDataService.FetchPremiumAsync(resolvedContract, envValue + "000");
                string formattedContract = _activationDataService.FormatContractForJcl(resolvedContract);

                StringBuilder report = new StringBuilder();
                report.AppendLine("=== SINGLE ACTIVATION REPORT ===");
                report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                if (skipPrime) report.AppendLine("MODE: SKIP PRIME (Bypassing ADDPRCT, LVPP06U, LVPG22U)\n");

                try
                {
                    // Exécution de la séquence Mainframe
                    await _activationDataService.ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, channel, skipPrime, username, password,
                        msg => Application.Current.Dispatcher.InvokeAsync(() => mainWindow.TxtStatus.Text = msg), _cts.Token);

                    report.AppendLine($"Input: {rawInput} | Contract: {resolvedContract} | Amount: {amount} | Status: SUCCESS");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Activation completed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
                }
                catch (Exception ex) when (ex.Message == "ALREADY_ACTIVE") // Erreur 008 spécifique ADDPRCT
                {
                    report.AppendLine($"Input: {rawInput} | Status: ALREADY ACTIVE (RC=08)");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Contract already active.", "Info", MessageBoxButton.OK, MessageBoxImage.Information));
                }
                catch (Exception ex)
                {
                    report.AppendLine($"Input: {rawInput} | Status: FAILED ({ex.Message})");
                    throw;
                }
                finally
                {
                    string path = Path.Combine(Settings.OutputDir, $"Single_Act_{formattedContract}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(path, report.ToString());
                    mainWindow.LastGeneratedPath = path;
                    Application.Current.Dispatcher.Invoke(() => { if (mainWindow.PrgLoading != null) mainWindow.PrgLoading.Visibility = Visibility.Collapsed; });
                }
            });
        }

        // -----------------------------------------------------------
        // LOGIQUE D'ACTIVATION MASSIVE (Batch)
        // -----------------------------------------------------------
        private void BtnRunBatchActivation_Click(object sender, RoutedEventArgs e) => RunBatchActivation(false);
        private void BtnSkipPrimeBatchAct_Click(object sender, RoutedEventArgs e) => RunBatchActivation(true);

        // Conversion Excel vers CSV avec nettoyage des processus Excel
        public string PrepareCsvFromExcel(string excelFilePath)
        {
            Excel.Application excelApp = null;
            Excel.Workbook workbook = null;
            try
            {
                string originalFileName = Path.GetFileNameWithoutExtension(excelFilePath);
                string savedCsvPath = Path.Combine(Settings.OutputDir, $"Converted_Act_{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                Directory.CreateDirectory(Settings.OutputDir);

                excelApp = new Excel.Application { Visible = false, DisplayAlerts = false };
                workbook = excelApp.Workbooks.Open(excelFilePath, ReadOnly: true);
                Excel.Worksheet worksheet = workbook.Sheets[1];
                Excel.Range firstRow = worksheet.Rows[2];
                Excel.Range searchRange = firstRow.Find("Value");

                if (searchRange != null)
                {
                    Excel.Range valueCol = worksheet.Columns[searchRange.Column];
                    valueCol.NumberFormat = "0";
                    Marshal.ReleaseComObject(valueCol);
                    Marshal.ReleaseComObject(searchRange);
                }

                workbook.SaveAs(savedCsvPath, Excel.XlFileFormat.xlCSV);
                return savedCsvPath;
            }
            finally
            {
                if (workbook != null) { workbook.Close(false); Marshal.ReleaseComObject(workbook); }
                if (excelApp != null) { excelApp.Quit(); Marshal.ReleaseComObject(excelApp); }
            }
        }

        private async void RunBatchActivation(bool skipPrime)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;
            _cts = new CancellationTokenSource();

            await mainWindow.RunProcessAsync(async () =>
            {
                string filePath = "", envValue = "D", cus = "XXX", bucp = "382", cmdpmt = "8", channel = "C01";
                string username = Settings.DbConfig.Uid, password = Settings.DbConfig.Pwd;
                bool isDemandId = false;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new Exception("Credentials are not configured.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    filePath = TxtBatchActCsv.Text.Trim();
                    isDemandId = RbBatchActSearchDemand?.IsChecked == true;
                    if (CmbActEnv.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";
                    cus = TxtActCus.Text.Trim();
                    if (CmbActBucp.SelectedItem is ComboBoxItem bItem) bucp = bItem.Content?.ToString() ?? "382";
                    if (CmbActCmdpmt.SelectedItem is ComboBoxItem cItem) cmdpmt = cItem.Content?.ToString() ?? "8";
                    if (CmbActChannel.SelectedItem is ComboBoxItem chItem) channel = chItem.Tag?.ToString() ?? "C01";
                });

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) throw new Exception("Invalid file path.");

                if (filePath.EndsWith(".xls") || filePath.EndsWith(".xlsx"))
                {
                    Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Converting Excel to CSV...");
                    filePath = await Task.Run(() => PrepareCsvFromExcel(filePath));
                }

                // Orchestration du batch
                var batchService = new BatchActivationService(_activationDataService);
                var result = await batchService.RunBatchAsync(
                    filePath, isDemandId, envValue, cus, bucp, cmdpmt, channel, skipPrime, username, password, Settings.OutputDir,
                    msg => Application.Current.Dispatcher.InvokeAsync(() => mainWindow.TxtStatus.Text = msg), _cts.Token);

                mainWindow.LastGeneratedPath = result.reportPath;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (mainWindow.PrgLoading != null) mainWindow.PrgLoading.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Batch complete. Success: {result.successCount} | Failed: {result.errorCount}", "Batch Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }
    }
}