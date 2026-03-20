using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Services;
// Explicit reference to drive Excel in the background
using Excel = Microsoft.Office.Interop.Excel;

namespace AutoActivator.Gui.Views
{
    public partial class ExtractionView : UserControl
    {
        private readonly ExtractionService _extractionService;

        // Observable collection to update the UI in real-time
        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        // Global KO counter
        private int _koExtractionCount = 0;

        public ExtractionView()
        {
            InitializeComponent();

            // Bind the visual history (ListView) to our collection
            ListHistory.ItemsSource = ExtractionHistory;

            _extractionService = new ExtractionService();
        }

        // -- UI NAVIGATION & MANAGEMENT --

        private void BtnHelp_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                // 0 corresponds to the Extraction Tab index in the Help module
                mainWindow.OpenHelpTargetingTab(0);
            }
        }

        // --- NEW: Utility function to convert row number to integer ---
        private int GetRowNumValue(string rowNumStr)
        {
            if (int.TryParse(rowNumStr, out int val))
                return val;

            // If it is "-" (single extraction) or invalid text, return 0
            // so it stays at the top of its group
            return 0;
        }

        // --- NEW: Centralized function with smart sorting (Ascending) ---
        private void AddExtractionItemToHistory(ExtractionItem item)
        {
            int newItemRow = GetRowNumValue(item.RowNum);
            bool isKo = (item.Test == "KO");

            // Ensure collection modification happens on the UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (isKo)
                {
                    // Find the correct position in the KO block (from index 0 to _koExtractionCount)
                    int insertIndex = 0;
                    while (insertIndex < _koExtractionCount)
                    {
                        int currentItemRow = GetRowNumValue(ExtractionHistory[insertIndex].RowNum);
                        if (currentItemRow > newItemRow)
                        {
                            break; // Found a larger number, insert just before it
                        }
                        insertIndex++;
                    }

                    ExtractionHistory.Insert(insertIndex, item);
                    _koExtractionCount++;

                    if (TxtKoCount != null)
                    {
                        TxtKoCount.Text = _koExtractionCount.ToString();
                    }
                }
                else // Status OK
                {
                    // Find the correct position in the OK block (from _koExtractionCount to the end of the list)
                    int insertIndex = _koExtractionCount;
                    while (insertIndex < ExtractionHistory.Count)
                    {
                        int currentItemRow = GetRowNumValue(ExtractionHistory[insertIndex].RowNum);
                        if (currentItemRow > newItemRow)
                        {
                            break; // Found a larger number, insert just before it
                        }
                        insertIndex++;
                    }

                    ExtractionHistory.Insert(insertIndex, item);
                }
            });
        }

        private string FormatContractForDisplay(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract)) return contract;

            // Clean up any existing dashes or spaces
            string clean = contract.Replace("-", "").Replace(" ", "");

            // If it's a standard 12-digit number, format it as XXX-XXXXXXX-XX
            if (clean.Length == 12)
            {
                return $"{clean.Substring(0, 3)}-{clean.Substring(3, 7)}-{clean.Substring(10, 2)}";
            }

            // Otherwise (e.g., longer Demand ID or non-standard format), return as is
            return contract;
        }

        // -- USER INTERFACE MANAGEMENT --

        private void InputType_Checked(object sender, RoutedEventArgs e)
        {
            // Ensure the UI element is initialized before clearing text
            if (TxtExtContract != null)
            {
                TxtExtContract.Text = string.Empty;
            }
        }

        // Updates or clears the network path based on the selected radio button, environment, and channel
        private void UpdateBatchExtCsvPath()
        {
            if (TxtBatchExtCsv != null)
            {
                // If searching by Contract Number, clear the field
                if (RbBatchSearchContract != null && RbBatchSearchContract.IsChecked == true)
                {
                    TxtBatchExtCsv.Text = string.Empty;
                }
                // If searching by Demand ID, set the default network paths
                else if (RbBatchSearchDemand != null && RbBatchSearchDemand.IsChecked == true)
                {
                    string envValue = CmbExtEnv?.SelectedItem is ComboBoxItem eItem ? eItem.Tag?.ToString() ?? "D" : "D";
                    string channelValue = CmbExtChannel?.SelectedItem is ComboBoxItem cItem ? cItem.Tag?.ToString() ?? "C01" : "C01";

                    // Dynamic path generation using the channel and environment
                    TxtBatchExtCsv.Text = $@"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_{channelValue}ComparisonsDB_URL_ELIA_LoginPage_{envValue}000.xls";
                }
            }
        }

        private void BatchInputType_Checked(object sender, RoutedEventArgs e) => UpdateBatchExtCsvPath();

        // Events triggered on dropdown menu changes
        private void CmbExtEnv_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchExtCsvPath();
        private void CmbExtChannel_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchExtCsvPath();

        // SINGLE EXTRACTION TAB LOGIC

        private async void BtnRunSingle_Click(object sender, RoutedEventArgs e)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            string rawInput = TxtExtContract?.Text.Trim();
            string envValue = "D";

            // Check if the search is by Demand ID via the radio button
            bool isDemandId = RbSearchDemand?.IsChecked == true;

            // Retrieve the environment value from the ComboBox
            if (CmbExtEnv?.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";

            if (string.IsNullOrEmpty(rawInput))
            {
                mainWindow.TxtStatus.Text = "Please enter a value.";
                mainWindow.TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            IProgress<ExtractionItem> progress = new Progress<ExtractionItem>(item => AddExtractionItemToHistory(item));

            await mainWindow.RunProcessAsync(async () =>
            {
                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = $"Extracting Environment {envValue}000...");

                await PerformSingleExtractionAsync(rawInput, envValue + "000", progress, isDemandId, mainWindow);

                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = "Single extraction completed successfully.");
            });
        }

        private async Task PerformSingleExtractionAsync(string targetValue, string env, IProgress<ExtractionItem> progress, bool isDemandId, MainWindow mainWindow)
        {
            try
            {
                ExtractionResult result = await _extractionService.PerformExtractionAsync(targetValue, env, true, isDemandId);

                // Update the global path on the MainWindow
                mainWindow.LastGeneratedPath = Settings.OutputDir;

                string displayContract = isDemandId ? FormatContractForDisplay(result.ContractReference) : FormatContractForDisplay(targetValue);

                // KO detection if the internal ID is not found
                string finalTest = (result.InternalId == "Not found" || result.InternalId == "Error") ? "KO" : "OK";

                progress.Report(new ExtractionItem
                {
                    RowNum = "-",
                    ContractId = displayContract,
                    InternalId = result.InternalId,
                    Product = env,
                    Premium = string.IsNullOrWhiteSpace(result.Premium) ? "0" : result.Premium,
                    Ucon = result.UconId,
                    Hdmd = result.DemandId,
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Test = finalTest,
                    FilePath = result.FilePath
                });
            }
            catch (Exception)
            {
                progress.Report(new ExtractionItem
                {
                    RowNum = "-",
                    ContractId = targetValue,
                    InternalId = "Error",
                    Product = env,
                    Premium = "0",
                    Ucon = "N/A",
                    Hdmd = "N/A",
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Test = "KO",
                    FilePath = string.Empty
                });
            }
        }

        // BATCH EXTRACTION TAB LOGIC

        private void BtnBrowseExtCsv_Click(object sender, RoutedEventArgs e) => TxtBatchExtCsv.Text = OpenFileDialogHybrid();

        private string OpenFileDialogHybrid()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Data Files (*.csv;*.xls;*.xlsx)|*.csv;*.xls;*.xlsx|CSV Files (*.csv)|*.csv|Excel Files (*.xls;*.xlsx)|*.xls;*.xlsx",
                Title = "Select a file containing contracts",
                InitialDirectory = Path.GetFullPath(Settings.InputDir)
            };
            return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : string.Empty;
        }

        public string PrepareCsvFromExcel(string excelFilePath, string env)
        {
            if (!File.Exists(excelFilePath))
                throw new FileNotFoundException($"The Excel file cannot be found on the network or locally: {excelFilePath}");

            Directory.CreateDirectory(Settings.OutputDir);

            string originalFileName = Path.GetFileNameWithoutExtension(excelFilePath);
            string csvFileName = $"Converted_{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string savedCsvPath = Path.Combine(Settings.OutputDir, csvFileName);

            Excel.Application excelApp = new Excel.Application();
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;

            Excel.Workbook workbook = null;
            try
            {
                workbook = excelApp.Workbooks.Open(excelFilePath, ReadOnly: true);
                Excel.Worksheet worksheet = workbook.Sheets[1];

                Excel.Range firstRow = worksheet.Rows[2];
                Excel.Range searchRange = firstRow.Find("Value");

                if (searchRange != null)
                {
                    int colIndex = searchRange.Column;
                    Excel.Range valueCol = worksheet.Columns[colIndex];
                    valueCol.NumberFormat = "0";
                }

                workbook.SaveAs(savedCsvPath, Excel.XlFileFormat.xlCSV, Type.Missing, Type.Missing,
                                Type.Missing, Type.Missing, Excel.XlSaveAsAccessMode.xlNoChange,
                                Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
            }
            finally
            {
                if (workbook != null) workbook.Close(false);
                excelApp.Quit();
                System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);
            }

            return savedCsvPath;
        }

        private async void BtnRunBatch_Click(object sender, RoutedEventArgs e)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            string filePath = TxtBatchExtCsv?.Text.Trim();
            string envValue = "D";

            bool isDemandId = RbBatchSearchDemand?.IsChecked == true;

            if (CmbExtEnv?.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";

            if (string.IsNullOrEmpty(filePath))
            {
                mainWindow.TxtStatus.Text = "Please select a file or use the default network paths.";
                mainWindow.TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            var batchService = new BatchExtractionService(_extractionService);

            // Progress now handles sorting and KO counting via the new AddExtractionItemToHistory function
            IProgress<BatchProgressInfo> progress = new Progress<BatchProgressInfo>(info =>
            {
                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = $"Batch extraction in progress: {info.CurrentItem} / {info.TotalItems} contracts processed...");

                // KO Detection
                string status = info.Status;
                if (info.InternalId == "Not found" || info.InternalId == "Error" || status.Contains("Error") || status.Contains("Not found"))
                {
                    status = "KO";
                }

                AddExtractionItemToHistory(new ExtractionItem
                {
                    RowNum = info.RowNum.ToString(),
                    ContractId = FormatContractForDisplay(info.ContractId),
                    InternalId = info.InternalId,
                    Product = info.Product,
                    Premium = info.Premium,
                    Ucon = info.UconId,
                    Hdmd = info.DemandId,
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Test = status,
                    FilePath = string.Empty
                });
            });

            await mainWindow.RunProcessAsync(async () =>
            {
                string actualFile = filePath;
                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = $"Preparing Environment {envValue}000...");

                if (filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = $"Converting Network Excel to CSV for {envValue}000...");
                    // Excel COM object creation must remain in a Task.Run because it is synchronous and heavy
                    actualFile = await Task.Run(() => PrepareCsvFromExcel(filePath, envValue + "000"));
                }

                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = $"Launching batch extraction ({envValue}000)...");

                // ASYNCHRONOUS BATCH CALL (Massive parallelism)
                await batchService.PerformBatchExtractionAsync(actualFile, envValue + "000", progress.Report, isDemandId);

                // Update the global path on the MainWindow
                mainWindow.LastGeneratedPath = Settings.OutputDir;

                Application.Current.Dispatcher.Invoke(() => {
                    mainWindow.TxtStatus.Text = "Batch extraction completed! Global files saved in Output folder.";
                    mainWindow.TxtStatus.Foreground = System.Windows.Media.Brushes.Green;
                });
            });
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (ExtractionHistory != null)
            {
                ExtractionHistory.Clear();
                _koExtractionCount = 0; // Reset KO counter
                if (TxtKoCount != null) TxtKoCount.Text = "0";
            }
        }
    }
}