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
        private readonly ActivationDataService _activationDataService = new ActivationDataService();

        private CancellationTokenSource _cts;

        public ActivationView()
        {
            InitializeComponent();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
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
                if (item.Content?.ToString() == content)
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void BtnRunSingleActivation_Click(object sender, RoutedEventArgs e) => RunSingleActivation(false);
        private void BtnSkipPrimeSingleAct_Click(object sender, RoutedEventArgs e) => RunSingleActivation(true);

        private async void RunSingleActivation(bool skipPrime)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            _cts = new CancellationTokenSource();

            // ACTIVER LE BOUTON D'ANNULATION
            BtnCancelSingleAct.IsEnabled = true;

            await mainWindow.RunProcessAsync(async () =>
            {
                try
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
                        await _activationDataService.ExecuteActivationSequenceAsync(formattedContract, amount, envValue, cus, bucp, cmdpmt, channel, skipPrime, false, username, password,
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
                        throw;
                    }
                    finally
                    {
                        string skipSuffix = skipPrime ? "_SKIP_PRIME" : "";
                        string path = Path.Combine(Settings.OutputDir, $"Single_Activation_{formattedContract}{skipSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                        File.WriteAllText(path, report.ToString());

                        mainWindow.LastGeneratedPath = path;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (mainWindow.PrgLoading != null) mainWindow.PrgLoading.Visibility = Visibility.Collapsed;
                        });
                    }
                }
                finally
                {
                    // DESACTIVER LE BOUTON D'ANNULATION À LA FIN
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        BtnCancelSingleAct.IsEnabled = false;
                    });
                }
            });
        }


        private void BtnRunBatchActivation_Click(object sender, RoutedEventArgs e) => RunBatchActivation(false);
        private void BtnSkipPrimeBatchAct_Click(object sender, RoutedEventArgs e) => RunBatchActivation(true);

        public string PrepareCsvFromExcel(string excelFilePath)
        {
            if (!File.Exists(excelFilePath))
                throw new FileNotFoundException($"Excel file not found: {excelFilePath}");

            Directory.CreateDirectory(Settings.OutputDir);

            string originalFileName = Path.GetFileNameWithoutExtension(excelFilePath);
            string savedCsvPath = Path.Combine(Settings.OutputDir, $"Converted_Act_{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            Excel.Application excelApp = null;
            Excel.Workbook workbook = null;
            Excel.Worksheet worksheet = null;
            Excel.Range firstRow = null;
            Excel.Range searchRange = null;
            Excel.Range valueCol = null;

            try
            {
                excelApp = new Excel.Application { Visible = false, DisplayAlerts = false };
                workbook = excelApp.Workbooks.Open(excelFilePath, ReadOnly: true);
                worksheet = workbook.Sheets[1];

                firstRow = worksheet.Rows[2];
                searchRange = firstRow.Find("Value");

                if (searchRange != null)
                {
                    valueCol = worksheet.Columns[searchRange.Column];
                    valueCol.NumberFormat = "0";
                }

                workbook.SaveAs(savedCsvPath, Excel.XlFileFormat.xlCSV, Type.Missing, Type.Missing,
                                Type.Missing, Type.Missing, Excel.XlSaveAsAccessMode.xlNoChange,
                                Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
            }
            finally
            {
                if (valueCol != null) Marshal.ReleaseComObject(valueCol);
                if (searchRange != null) Marshal.ReleaseComObject(searchRange);
                if (firstRow != null) Marshal.ReleaseComObject(firstRow);
                if (worksheet != null) Marshal.ReleaseComObject(worksheet);
                if (workbook != null)
                {
                    workbook.Close(false);
                    Marshal.ReleaseComObject(workbook);
                }
                if (excelApp != null)
                {
                    excelApp.Quit();
                    Marshal.ReleaseComObject(excelApp);
                }
            }

            return savedCsvPath;
        }

        private async void RunBatchActivation(bool skipPrime)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            _cts = new CancellationTokenSource();

            // ACTIVER LE BOUTON D'ANNULATION
            BtnCancelBatchAct.IsEnabled = true;

            await mainWindow.RunProcessAsync(async () =>
            {
                try
                {
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
                        if (CmbActChannel.SelectedItem is ComboBoxItem chItem) channel = chItem.Tag?.ToString() ?? "C01";
                    });

                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) throw new Exception("Please select a valid file.");

                    if (filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Converting network Excel file to local CSV via Excel Interop...");

                        filePath = await Task.Run(() => PrepareCsvFromExcel(filePath));
                    }

                    var batchService = new BatchActivationService(_activationDataService);

                    var result = await batchService.RunBatchAsync(
                        filePath, isDemandId, envValue, cus, bucp, cmdpmt, channel, skipPrime, username, password, Settings.OutputDir,
                        msg => Application.Current.Dispatcher.InvokeAsync(() => mainWindow.TxtStatus.Text = msg),
                        _cts.Token
                    );

                    mainWindow.LastGeneratedPath = result.reportPath;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (mainWindow.PrgLoading != null) mainWindow.PrgLoading.Visibility = Visibility.Collapsed;

                        MessageBox.Show($"Batch completed.\nSuccess: {result.successCount} \nAlready Active: {result.alreadyActiveCount} \nFailed: {result.errorCount}\n\nPlease open the log file for detailed results.", "Batch Activation", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                finally
                {
                    // DESACTIVER LE BOUTON D'ANNULATION À LA FIN
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        BtnCancelBatchAct.IsEnabled = false;
                    });
                }
            });
        }
    }
}