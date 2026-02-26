using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using AutoActivator.Services;
using AutoActivator.Config;

namespace AutoActivator.Gui
{
    public class ExtractionItem
    {
        public string ContractId { get; set; }
        public string InternalId { get; set; }
        public string Product { get; set; }
        public string Premium { get; set; }
        public string Ucon { get; set; }
        public string Hdmd { get; set; }
        public string Time { get; set; }
        public string Test { get; set; }
        public string FilePath { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string _lastGeneratedPath = "";
        private readonly ExtractionService _extractionService;
        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();

            ListHistory.ItemsSource = ExtractionHistory;

            ICollectionView view = CollectionViewSource.GetDefaultView(ExtractionHistory);
            view.SortDescriptions.Add(new SortDescription("Product", ListSortDirection.Ascending));

            _extractionService = new ExtractionService();
        }

        private void InitializeDirectories()
        {
            try
            {
                Directory.CreateDirectory(Settings.OutputDir);
                Directory.CreateDirectory(Settings.SnapshotDir);
                Directory.CreateDirectory(Settings.InputDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[ERROR] Directories: {ex.Message}");
            }
        }

        private void TxtContract_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtFilePath != null && !string.IsNullOrWhiteSpace(TxtContract.Text))
                TxtFilePath.Text = string.Empty;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Fichiers CSV|*.csv",
                Title = "Sélectionnez un fichier CSV contenant les contrats"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtFilePath.Text = dialog.FileName;
                TxtContract.Text = string.Empty;
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string singleInput = TxtContract.Text?.Trim();
            string fileInput = TxtFilePath?.Text?.Trim();

            if (string.IsNullOrEmpty(singleInput) && string.IsNullOrEmpty(fileInput))
            {
                TxtStatus.Text = "Please enter a contract number or select a CSV file.";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            ToggleUi(false);

            try
            {
                if (!string.IsNullOrEmpty(fileInput))
                {
                    TxtStatus.Text = "Batch extraction in progress...";
                    TxtStatus.Foreground = Brushes.Blue;

                    await Task.Run(() => PerformBatchExtraction(fileInput));

                    TxtStatus.Text = "Batch extraction completed!";
                    TxtStatus.Foreground = Brushes.Green;

                    _lastGeneratedPath = Settings.OutputDir;
                    LnkFile.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtStatus.Text = "Extraction in progress...";
                    TxtStatus.Foreground = Brushes.Blue;

                    var result = await Task.Run(() =>
                        _extractionService.PerformExtraction(singleInput, true));

                    _lastGeneratedPath = result.FilePath;

                    TxtStatus.Text = $"Completed! {result.StatusMessage}";
                    TxtStatus.Foreground = Brushes.Green;
                    LnkFile.Visibility = Visibility.Visible;

                    ExtractionHistory.Add(new ExtractionItem
                    {
                        ContractId = singleInput,
                        InternalId = result.InternalId,
                        Product = "N/A",
                        Premium = "0",
                        Ucon = result.UconId,
                        Hdmd = result.DemandId,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Test = "OK",
                        FilePath = _lastGeneratedPath
                    });
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
            finally
            {
                ToggleUi(true);
            }
        }

        private void ToggleUi(bool enabled)
        {
            PrgLoading.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            BtnRun.IsEnabled = enabled;
            if (BtnBrowse != null) BtnBrowse.IsEnabled = enabled;
        }

        private void PerformBatchExtraction(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("CSV file not found.");

            string rawText = File.ReadAllText(filePath).Replace("\uFEFF", "");
            string[] lines = rawText
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1)
                throw new Exception("CSV file is empty or contains only header.");

            StringBuilder globalLisa = new();
            StringBuilder globalElia = new();

            string[] headers = lines[0].Split(';', ',');

            int contractIndex = 0;
            int premiumIndex = -1;
            int productIndex = -1;

            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim().Trim('"').ToLower();
                if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa"))
                    contractIndex = i;
                if (h.Contains("premium") || h.Contains("prime"))
                    premiumIndex = i;
                if (h.Contains("product") || h.Contains("produit"))
                    productIndex = i;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] columns = line.Split(';', ',');

                if (columns.Length <= contractIndex)
                    continue;

                string contractNumber = CleanCsvValue(columns[contractIndex]);
                if (string.IsNullOrWhiteSpace(contractNumber))
                    continue;

                string premium = premiumIndex >= 0 && columns.Length > premiumIndex
                    ? CleanCsvValue(columns[premiumIndex])
                    : "0";

                string product = productIndex >= 0 && columns.Length > productIndex
                    ? CleanCsvValue(columns[productIndex])
                    : "N/A";

                try
                {
                    var result = _extractionService.PerformExtraction(contractNumber, false);

                    if (!string.IsNullOrWhiteSpace(result.LisaContent))
                    {
                        globalLisa.AppendLine($"### CONTRACT: {contractNumber} | PRODUCT: {product}");
                        globalLisa.Append(result.LisaContent);
                        globalLisa.AppendLine();
                    }

                    if (!string.IsNullOrWhiteSpace(result.EliaContent))
                    {
                        globalElia.AppendLine($"### CONTRACT: {contractNumber} | UCON: {result.UconId}");
                        globalElia.Append(result.EliaContent);
                        globalElia.AppendLine();
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ExtractionHistory.Add(new ExtractionItem
                        {
                            ContractId = contractNumber,
                            InternalId = result.InternalId,
                            Product = product,
                            Premium = premium,
                            Ucon = result.UconId,
                            Hdmd = result.DemandId,
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            Test = "OK"
                        });
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ExtractionHistory.Add(new ExtractionItem
                        {
                            ContractId = contractNumber,
                            InternalId = "Error",
                            Product = product,
                            Premium = premium,
                            Ucon = "Error",
                            Hdmd = "Error",
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            Test = ex.Message.Contains("not found")
                                ? "Non trouvé en BDD"
                                : "Erreur SQL"
                        });
                    });
                }
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");

            File.WriteAllText(
                Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_LISA_{timestamp}.csv"),
                globalLisa.Length > 0 ? globalLisa.ToString() : "AUCUN CONTRAT LISA TROUVE.",
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_ELIA_{timestamp}.csv"),
                globalElia.Length > 0 ? globalElia.ToString() : "AUCUN CONTRAT ELIA TROUVE.",
                Encoding.UTF8);
        }

        private static string CleanCsvValue(string value)
        {
            return value?
                .Replace("=", "")
                .Replace("\"", "")
                .Trim() ?? string.Empty;
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastGeneratedPath))
                return;

            if (Directory.Exists(_lastGeneratedPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _lastGeneratedPath,
                    UseShellExecute = true
                });
            }
            else if (File.Exists(_lastGeneratedPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_lastGeneratedPath}\"",
                    UseShellExecute = true
                });
            }
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e)
            => TxtStatus.Text = "Activation Module selected.";

        private void BtnComparison_Click(object sender, RoutedEventArgs e)
            => TxtStatus.Text = "Comparison Module selected.";
    }
}