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

namespace AutoActivator.Gui.Views
{
    public partial class ActivationView : UserControl
    {
        // Instantiation of business services
        private readonly ActivationDataService _activationDataService = new ActivationDataService();

        // Cancellation token to stop asynchronous tasks
        private CancellationTokenSource _cts;

        public ActivationView()
        {
            InitializeComponent();
        }

        // -- UI NAVIGATION & MANAGEMENT --

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                // 1 corresponds to the Activation Tab index in the Help module
                mainWindow.OpenHelpTargetingTab(1);
            }
        }

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

        // Updates the network path based on the selected radio button, environment, and channel
        private void UpdateBatchActCsvPath()
        {
            // Safety check (UI elements can be null during XAML initialization)
            if (TxtBatchActCsv == null || RbBatchActSearchDemand == null) return;

            if (RbBatchActSearchDemand.IsChecked == true)
            {
                string envValue = CmbActEnv?.SelectedItem is ComboBoxItem eItem ? eItem.Tag?.ToString() ?? "D" : "D";

                // If the channel is not yet initialized, default to "C01"
                string channelValue = CmbActChannel?.SelectedItem is ComboBoxItem cItem ? cItem.Tag?.ToString() ?? "C01" : "C01";

                // Dynamic path generation using the channel and environment
                TxtBatchActCsv.Text = $@"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_{channelValue}ComparisonsDB_URL_ELIA_LoginPage_{envValue}000.xls";
            }
            else if (RbBatchActSearchContract != null && RbBatchActSearchContract.IsChecked == true)
            {
                // If searching by Contract, clear the field since the network file is only for Demand IDs
                TxtBatchActCsv.Text = string.Empty;
            }
        }

        // Triggered when clicking on "Contract" or "Demand ID" in the Batch tab
        private void BatchActInputType_Checked(object sender, RoutedEventArgs e) => UpdateBatchActCsvPath();

        // Triggered when changing "Env (D/Q/...)"
        private void CmbActEnv_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchActCsvPath();

        // Triggered when changing "Channel (C01/C03/C05)"
        private void CmbActChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBatchActCsvPath();

            // NOUVEAUTÉ : Gestion de la visibilité des boutons "Skip Prime"
            if (CmbActChannel?.SelectedItem is ComboBoxItem cItem)
            {
                string channelValue = cItem.Tag?.ToString() ?? "C01";

                // On affiche le bouton uniquement pour C01 et C03
                Visibility skipPrimeVisibility = (channelValue == "C01" || channelValue == "C03") ? Visibility.Visible : Visibility.Collapsed;

                if (BtnSkipPrimeSingleAct != null) BtnSkipPrimeSingleAct.Visibility = skipPrimeVisibility;
                if (BtnSkipPrimeBatchAct != null) BtnSkipPrimeBatchAct.Visibility = skipPrimeVisibility;
            }
        }

        private void BtnCancelActivation_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();

                // Retrieve the MainWindow to update the global status
                if (Window.GetWindow(this) is MainWindow mainWindow)
                {
                    mainWindow.TxtStatus.Text = "Canceling... The sequence will stop shortly.";
                    mainWindow.TxtStatus.Foreground = Brushes.DarkOrange;
                }
            }
        }

        // -- EXECUTION: SINGLE ACTIVATION --

        private void BtnRunSingleActivation_Click(object sender, RoutedEventArgs e) => RunSingleActivation(false);
        private void BtnSkipPrimeSingleAct_Click(object sender, RoutedEventArgs e) => RunSingleActivation(true);

        private async void RunSingleActivation(bool skipPrime)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            _cts = new CancellationTokenSource();

            await mainWindow.RunProcessAsync(async () =>
            {
                // NOUVEAUTÉ : Ajout de la variable channel
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

                    // NOUVEAUTÉ : Récupération du canal
                    if (CmbActChannel.SelectedItem is ComboBoxItem chItem) channel = chItem.Tag?.ToString() ?? "C01";
                });

                if (string.IsNullOrEmpty(rawInput)) throw new Exception("Please enter a contract value or Demand ID.");

                string resolvedContract = rawInput;

                if (isDemandId)
                {
                    Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Searching for the contract associated with the Demand ID...");
                    resolvedContract = await _activationDataService.GetContractFromDemandAsync(rawInput, envValue + "000");
                    if (string.IsNullOrEmpty(resolvedContract))
                        throw new Exception($"Unable to find a contract associated with Demand ID {rawInput} in the {envValue}000 database.");
                }

                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Fetching premium from the database...");
                string amount = await _activationDataService.FetchPremiumAsync(resolvedContract, envValue + "000");

                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Preparing activation sequence...");
                string formattedContract = _activationDataService.FormatContractForJcl(resolvedContract);

                StringBuilder report = new StringBuilder();
                report.AppendLine("=== SINGLE ACTIVATION REPORT ===");
                report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                if (skipPrime) report.AppendLine("MODE: SKIP PRIME (Bypassing ADDPRCT, LVPP06U, LVPG22U)\n");

                try
                {
                    // NOUVEAUTÉ : On passe la variable "channel" et "skipPrime"
                    await _activationDataService.ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, channel, skipPrime, username, password,
                        msg => Application.Current.Dispatcher.InvokeAsync(() => mainWindow.TxtStatus.Text = msg), _cts.Token);

                    report.AppendLine($"Original Input: {rawInput} | Contract Found: {resolvedContract} | JCL Contract: {formattedContract} | Env: {envValue} | Channel: {channel} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Status: SUCCESS");

                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Activation sequence completed successfully. Please check the generated report.", "Activation Successful", MessageBoxButton.OK, MessageBoxImage.Information));
                }
                catch (Exception ex) when (ex.Message == "ALREADY_ACTIVE") // Catches the specific 008 Exception
                {
                    report.AppendLine($"Original Input: {rawInput} | Contract Found: {resolvedContract} | JCL Contract: {formattedContract} | Env: {envValue} | Channel: {channel} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Status: ALREADY ACTIVE (Error 008)");

                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("This contract is already active (Error 008 - premium already assigned).", "Contract Already Active", MessageBoxButton.OK, MessageBoxImage.Information));
                }
                catch (Exception ex)
                {
                    report.AppendLine($"Original Input: {rawInput} | Contract Found: {resolvedContract} | JCL Contract: {formattedContract} | Env: {envValue} | Channel: {channel} | CUS: {cus} | BUCP: {bucp} | CMDPMT: {cmdpmt} | Amount: {amount} | Status: FAILED ({ex.Message})");
                    throw; // Rethrow the error for global UI handling (RunProcessAsync will display it)
                }
                finally
                {
                    string skipSuffix = skipPrime ? "_SKIP_PRIME" : "";
                    string path = Path.Combine(Settings.OutputDir, $"Single_Activation_{formattedContract}{skipSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(path, report.ToString());

                    // Pass the path to the main window for the clickable link
                    mainWindow.LastGeneratedPath = path;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (mainWindow.PrgLoading != null) mainWindow.PrgLoading.Visibility = Visibility.Collapsed;
                    });
                }
            });
        }

        // -- EXECUTION: BATCH ACTIVATION --

        private void BtnRunBatchActivation_Click(object sender, RoutedEventArgs e) => RunBatchActivation(false);
        private void BtnSkipPrimeBatchAct_Click(object sender, RoutedEventArgs e) => RunBatchActivation(true);

        private async void RunBatchActivation(bool skipPrime)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            _cts = new CancellationTokenSource();

            await mainWindow.RunProcessAsync(async () =>
            {
                // NOUVEAUTÉ : Ajout de la variable channel
                string filePath = "", envValue = "D", cus = "XXX", bucp = "382", cmdpmt = "8", channel = "C01";
                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;
                bool isDemandId = false;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new Exception("Credentials are not configured. Please log in again.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    filePath = TxtBatchActCsv.Text.Trim();
                    isDemandId = RbBatchActSearchDemand?.IsChecked == true;
                    if (CmbActEnv.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";
                    cus = TxtActCus.Text.Trim();
                    if (CmbActBucp.SelectedItem is ComboBoxItem bItem) bucp = bItem.Content?.ToString() ?? "382";
                    if (CmbActCmdpmt.SelectedItem is ComboBoxItem cItem) cmdpmt = cItem.Content?.ToString() ?? "8";

                    // NOUVEAUTÉ : Récupération du canal
                    if (CmbActChannel.SelectedItem is ComboBoxItem chItem) channel = chItem.Tag?.ToString() ?? "C01";
                });

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) throw new Exception("Please select a valid file.");

                // Use the PrepareCsvFromExcel method if an Excel file is provided
                if (filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Converting network Excel file to local CSV...");

                    // Note: Ensure PrepareCsvFromExcel is set to "public" in MainWindow.xaml.cs
                    // or moved to a utility class (e.g., ExcelHelper.PrepareCsvFromExcel)
                    // filePath = await Task.Run(() => mainWindow.PrepareCsvFromExcel(filePath, envValue + "000"));
                }

                var batchService = new BatchActivationService(_activationDataService);

                // NOUVEAUTÉ : On passe la variable "channel" et "skipPrime" à la méthode RunBatchAsync
                var result = await batchService.RunBatchAsync(
                    filePath, isDemandId, envValue, cus, bucp, cmdpmt, channel, skipPrime, username, password, Settings.OutputDir,
                    msg => Application.Current.Dispatcher.InvokeAsync(() => mainWindow.TxtStatus.Text = msg),
                    _cts.Token
                );

                // Pass the path to the main window for the clickable link
                mainWindow.LastGeneratedPath = result.reportPath;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (mainWindow.PrgLoading != null) mainWindow.PrgLoading.Visibility = Visibility.Collapsed;

                    // Final result popup including the "Already Active" counter
                    MessageBox.Show($"Batch completed.\nSuccess: {result.successCount} \nAlready Active: {result.alreadyActiveCount} \nFailed: {result.errorCount}\n\nPlease open the log file for detailed results.", "Batch Activation", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }
    }
}