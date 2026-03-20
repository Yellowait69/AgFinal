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
// Référence explicite pour piloter Excel en arrière-plan
using Excel = Microsoft.Office.Interop.Excel;

namespace AutoActivator.Gui.Views
{
    public partial class ExtractionView : UserControl
    {
        private readonly ExtractionService _extractionService;

        // Collection observable pour mettre à jour l'UI en temps réel
        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        // Compteur global des KO
        private int _koExtractionCount = 0;

        public ExtractionView()
        {
            InitializeComponent();

            // Lier l'historique visuel (ListView) à notre collection
            ListHistory.ItemsSource = ExtractionHistory;

            _extractionService = new ExtractionService();
        }

        // --- NOUVEAU : Fonction utilitaire pour convertir le numéro de ligne en entier ---
        private int GetRowNumValue(string rowNumStr)
        {
            if (int.TryParse(rowNumStr, out int val))
                return val;

            // Si c'est "-" (extraction unique) ou un texte invalide, on retourne 0
            // pour qu'il s'affiche tout en haut de son groupe
            return 0;
        }

        // --- NOUVEAU : Fonction centralisée avec tri intelligent (Ascendant) ---
        private void AddExtractionItemToHistory(ExtractionItem item)
        {
            int newItemRow = GetRowNumValue(item.RowNum);
            bool isKo = (item.Test == "KO");

            // On s'assure que la modification de la collection se fait bien sur le thread de l'interface graphique
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (isKo)
                {
                    // On cherche la bonne position dans le bloc des KO (de l'index 0 jusqu'à _koExtractionCount)
                    int insertIndex = 0;
                    while (insertIndex < _koExtractionCount)
                    {
                        int currentItemRow = GetRowNumValue(ExtractionHistory[insertIndex].RowNum);
                        if (currentItemRow > newItemRow)
                        {
                            break; // On a trouvé un numéro plus grand, on s'insère juste avant
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
                else // Statut OK
                {
                    // On cherche la bonne position dans le bloc des OK (de _koExtractionCount jusqu'à la fin de la liste)
                    int insertIndex = _koExtractionCount;
                    while (insertIndex < ExtractionHistory.Count)
                    {
                        int currentItemRow = GetRowNumValue(ExtractionHistory[insertIndex].RowNum);
                        if (currentItemRow > newItemRow)
                        {
                            break; // On a trouvé un numéro plus grand, on s'insère juste avant
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

            // On nettoie les éventuels tirets ou espaces déjà présents
            string clean = contract.Replace("-", "").Replace(" ", "");

            // Si c'est un numéro standard à 12 chiffres, on le formate en XXX-XXXXXXX-XX
            if (clean.Length == 12)
            {
                return $"{clean.Substring(0, 3)}-{clean.Substring(3, 7)}-{clean.Substring(10, 2)}";
            }

            // Sinon (ex: Demand ID plus long ou format non standard), on le retourne tel quel
            return contract;
        }

        // -- GESTION DE L'INTERFACE UTILISATEUR --

        private void InputType_Checked(object sender, RoutedEventArgs e)
        {
            // On s'assure que l'élément visuel est bien initialisé avant de vider le texte
            if (TxtExtContract != null)
            {
                TxtExtContract.Text = string.Empty;
            }
        }

        // Met à jour ou vide le chemin réseau selon le bouton radio, l'environnement et le canal choisis
        private void UpdateBatchExtCsvPath()
        {
            if (TxtBatchExtCsv != null)
            {
                // Si la recherche par Contract Number est sélectionnée, on vide le champ
                if (RbBatchSearchContract != null && RbBatchSearchContract.IsChecked == true)
                {
                    TxtBatchExtCsv.Text = string.Empty;
                }
                // Si la recherche par Demand ID est sélectionnée, on met les chemins réseau par défaut
                else if (RbBatchSearchDemand != null && RbBatchSearchDemand.IsChecked == true)
                {
                    string envValue = CmbExtEnv?.SelectedItem is ComboBoxItem eItem ? eItem.Tag?.ToString() ?? "D" : "D";
                    string channelValue = CmbExtChannel?.SelectedItem is ComboBoxItem cItem ? cItem.Tag?.ToString() ?? "C01" : "C01";

                    // Génération dynamique du chemin avec le canal et l'environnement
                    TxtBatchExtCsv.Text = $@"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_{channelValue}ComparisonsDB_URL_ELIA_LoginPage_{envValue}000.xls";
                }
            }
        }

        private void BatchInputType_Checked(object sender, RoutedEventArgs e) => UpdateBatchExtCsvPath();

        // Événements liés au changement des menus déroulants
        private void CmbExtEnv_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchExtCsvPath();
        private void CmbExtChannel_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchExtCsvPath();

        // SINGLE EXTRACTION TAB LOGIC

        private async void BtnRunSingle_Click(object sender, RoutedEventArgs e)
        {
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            string rawInput = TxtExtContract?.Text.Trim();
            string envValue = "D";

            // On regarde si la recherche se fait par Demand ID via le bouton radio de l'interface
            bool isDemandId = RbSearchDemand?.IsChecked == true;

            // Récupère la valeur de l'environnement depuis la ComboBox
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

                // Mettre à jour le chemin global sur la MainWindow
                mainWindow.LastGeneratedPath = Settings.OutputDir;

                string displayContract = isDemandId ? FormatContractForDisplay(result.ContractReference) : FormatContractForDisplay(targetValue);

                // Détection de KO si l'internal ID n'est pas trouvé
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
                    InternalId = "Erreur",
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
                throw new FileNotFoundException($"Le fichier Excel est introuvable sur le réseau ou en local : {excelFilePath}");

            Directory.CreateDirectory(Settings.OutputDir);

            string originalFileName = Path.GetFileNameWithoutExtension(excelFilePath);
            string csvFileName = $"Converti_{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
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

            // Progress gère désormais le tri et le comptage des KO via la nouvelle fonction AddExtractionItemToHistory
            IProgress<BatchProgressInfo> progress = new Progress<BatchProgressInfo>(info =>
            {
                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = $"Extraction par lot en cours : {info.CurrentItem} / {info.TotalItems} contrats traités...");

                // Détection de KO
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
                    // Excel COM object creation doit rester dans un Task.Run car synchrone et très lourd
                    actualFile = await Task.Run(() => PrepareCsvFromExcel(filePath, envValue + "000"));
                }

                Application.Current.Dispatcher.Invoke(() => mainWindow.TxtStatus.Text = $"Lancement de l'extraction par lot ({envValue}000)...");

                // APPEL ASYNCHRONE DU BATCH (Parallélisme massif)
                await batchService.PerformBatchExtractionAsync(actualFile, envValue + "000", progress.Report, isDemandId);

                // Mettre à jour le chemin global sur la MainWindow
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
                _koExtractionCount = 0; // Remise à zéro du compteur KO
                if (TxtKoCount != null) TxtKoCount.Text = "0";
            }
        }
    }
}