using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls; // Nécessaire pour ComboBox, ComboBoxItem
using Microsoft.Win32;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Services;
// NOUVEAU : Référence explicite pour piloter Excel en arrière-plan
using Excel = Microsoft.Office.Interop.Excel;

namespace AutoActivator.Gui
{

    public partial class MainWindow : Window
    {

        // RENOMMÉ : Formatage visuel pour l'interface de l'Extraction
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

        // NOUVEAU : Met à jour ou vide le chemin réseau selon le bouton radio et l'environnement choisis
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

                    if (envValue == "D")
                        TxtBatchExtCsv.Text = @"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_C01ComparisonsDB_URL_ELIA_LoginPage_D000.xls";
                    else if (envValue == "Q")
                        TxtBatchExtCsv.Text = @"\\jafile01\Automated_Testing\IS_QCRUNS\00_GENERICS\KEY_C01ComparisonsDB_URL_ELIA_LoginPage_Q000.xls";
                }
            }
        }

        private void BatchInputType_Checked(object sender, RoutedEventArgs e) => UpdateBatchExtCsvPath();

        // NOUVEAU : Événement lié au changement du menu déroulant
        private void CmbExtEnv_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchExtCsvPath();

        // SINGLE EXTRACTION TAB LOGIC

        private async void BtnRunSingle_Click(object sender, RoutedEventArgs e)
        {
            string rawInput = TxtExtContract?.Text.Trim();
            string envValue = "D";

            // On regarde si la recherche se fait par Demand ID via le bouton radio de l'interface
            bool isDemandId = RbSearchDemand?.IsChecked == true;

            // Récupère la valeur de l'environnement depuis la ComboBox
            if (CmbExtEnv?.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";

            if (string.IsNullOrEmpty(rawInput))
            {
                TxtStatus.Text = "Please enter a value.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            IProgress<ExtractionItem> progress = new Progress<ExtractionItem>(item => ExtractionHistory.Add(item));

            await RunProcessAsync(async () =>
            {
                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = $"Extracting Environment {envValue}000...");
                await Task.Run(() => PerformSingleExtraction(rawInput, envValue + "000", progress, isDemandId));

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Single extraction completed successfully.");
            });
        }

        private void PerformSingleExtraction(string targetValue, string env, IProgress<ExtractionItem> progress, bool isDemandId)
        {
            try
            {
                // On passe "true" pour "saveIndividualFile" et isDemandId à la fin pour le service d'extraction
                ExtractionResult result = _extractionService.PerformExtraction(targetValue, env, true, isDemandId);
                _lastGeneratedPath = Settings.OutputDir;

                // On utilise result.ContractReference qui contient le Contract Extended (ex: 582-2735865-77) renvoyé par le service
                // Le préfixe [DMD] a été retiré, le numéro s'affiche proprement dans tous les cas.
                string displayContract = isDemandId ? FormatContractForDisplay(result.ContractReference) : FormatContractForDisplay(targetValue);

                progress.Report(new ExtractionItem
                {
                    ContractId = displayContract,
                    InternalId = result.InternalId,
                    Product = env,
                    Premium = string.IsNullOrWhiteSpace(result.Premium) ? "0" : result.Premium,
                    Ucon = result.UconId,
                    Hdmd = result.DemandId,
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Test = "OK",
                    FilePath = result.FilePath
                });
            }
            catch (Exception ex)
            {
                // GESTION DE L'ERREUR : on n'interrompt pas l'application
                progress.Report(new ExtractionItem
                {
                    ContractId = targetValue,
                    InternalId = "Erreur",
                    Product = env,
                    Premium = "0",
                    Ucon = "N/A",
                    Hdmd = "N/A",
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Test = ex.Message.Contains("No associated contract") ? "Non trouvé/Mauvais ID" : "Erreur SQL",
                    FilePath = string.Empty
                });
            }
        }


        // BATCH EXTRACTION TAB LOGIC

        private void BtnBrowseExtCsv_Click(object sender, RoutedEventArgs e) => TxtBatchExtCsv.Text = OpenFileDialogHybrid();

        // NOUVEAU : Un dialogue hybride qui accepte CSV, XLS et XLSX
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

        // NOUVEAU : Convertisseur automatique Excel -> CSV en arrière-plan avec sauvegarde propre
        private string PrepareCsvFromExcel(string excelFilePath, string env)
        {
            if (!File.Exists(excelFilePath))
                throw new FileNotFoundException($"Le fichier Excel est introuvable sur le réseau ou en local : {excelFilePath}");

            Directory.CreateDirectory(Settings.OutputDir);

            // CHANGEMENT : On récupère le nom du fichier Excel d'origine (sans l'extension .xls/.xlsx)
            string originalFileName = Path.GetFileNameWithoutExtension(excelFilePath);

            // On crée un nom clair pour le fichier CSV converti qui sera sauvegardé
            string csvFileName = $"Converti_{originalFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string savedCsvPath = Path.Combine(Settings.OutputDir, csvFileName);

            Excel.Application excelApp = new Excel.Application();
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;

            Excel.Workbook workbook = null;
            try
            {
                // On ouvre en ReadOnly pour ne pas bloquer les collègues sur le réseau
                workbook = excelApp.Workbooks.Open(excelFilePath, ReadOnly: true);
                Excel.Worksheet worksheet = workbook.Sheets[1];

                // On cherche la colonne "Value" (ou modifiez selon le vrai nom de votre colonne)
                Excel.Range firstRow = worksheet.Rows[2];
                Excel.Range searchRange = firstRow.Find("Value");

                if (searchRange != null)
                {
                    int colIndex = searchRange.Column;
                    Excel.Range valueCol = worksheet.Columns[colIndex];
                    valueCol.NumberFormat = "0"; // Force le format nombre entier (évite les 1.23E+11)
                }

                // Sauvegarde du CSV définitif dans votre dossier Output
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
            string filePath = TxtBatchExtCsv?.Text.Trim();
            string envValue = "D";

            // NOUVEAU : Vérification de la méthode de recherche pour le batch (Contract vs Demand ID)
            bool isDemandId = RbBatchSearchDemand?.IsChecked == true;

            // Récupère la valeur de l'environnement depuis la ComboBox
            if (CmbExtEnv?.SelectedItem is ComboBoxItem eItem) envValue = eItem.Tag?.ToString() ?? "D";

            if (string.IsNullOrEmpty(filePath))
            {
                TxtStatus.Text = "Please select a file or use the default network paths.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            var batchService = new BatchExtractionService(_extractionService);

            IProgress<BatchProgressInfo> progress = new Progress<BatchProgressInfo>(info =>
            {
                ExtractionHistory.Add(new ExtractionItem
                {
                    ContractId = FormatContractForDisplay(info.ContractId),
                    InternalId = info.InternalId,
                    Product = info.Product,
                    Premium = info.Premium,
                    Ucon = info.UconId,
                    Hdmd = info.DemandId,
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Test = info.Status,
                    FilePath = string.Empty
                });
            });

            await RunProcessAsync(async () =>
            {
                string actualFile = filePath;
                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = $"Preparing Environment {envValue}000...");

                // Détection intelligente : Si c'est un Excel, on le convertit, sinon on l'utilise tel quel
                if (filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = $"Converting Network Excel to CSV for {envValue}000...");
                    actualFile = await Task.Run(() => PrepareCsvFromExcel(filePath, envValue + "000"));
                }

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = $"Batch Extracting Environment {envValue}000...");
                await Task.Run(() => batchService.PerformBatchExtraction(actualFile, envValue + "000", progress.Report, isDemandId));

                _lastGeneratedPath = Settings.OutputDir;
                Application.Current.Dispatcher.Invoke(() => {
                    TxtStatus.Text = "Batch extraction completed! Global files saved in Output folder.";
                    TxtStatus.Foreground = System.Windows.Media.Brushes.Green;
                });
            });
        }
    }
}