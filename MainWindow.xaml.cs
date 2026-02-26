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
        public string InternalId { get; set; } // NOUVEAU : Numéro interne (NO_CNT)
        public string Product { get; set; }
        public string Premium { get; set; }
        public string Ucon { get; set; }
        public string Hdmd { get; set; }
        public string Time { get; set; }
        public string Test { get; set; } // NOUVEAU : Statut ou valeur de test
        public string FilePath { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string _lastGeneratedPath = "";
        private readonly ExtractionService _extractionService;
        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
            ListHistory.ItemsSource = ExtractionHistory;

            // Tri automatique de l'historique par la colonne "Product"
            ICollectionView view = CollectionViewSource.GetDefaultView(ExtractionHistory);
            view.SortDescriptions.Add(new SortDescription("Product", ListSortDirection.Ascending));

            _extractionService = new ExtractionService();
        }

        private void InitializeDirectories()
        {
            try
            {
                if (!Directory.Exists(Settings.OutputDir)) Directory.CreateDirectory(Settings.OutputDir);
                if (!Directory.Exists(Settings.SnapshotDir)) Directory.CreateDirectory(Settings.SnapshotDir);
                if (!Directory.Exists(Settings.InputDir)) Directory.CreateDirectory(Settings.InputDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[ERROR] Directories: {ex.Message}");
            }
        }

        private void TxtContract_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtFilePath != null && !string.IsNullOrEmpty(TxtContract.Text))
            {
                TxtFilePath.Text = string.Empty;
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Fichiers CSV|*.csv",
                Title = "Sélectionnez un fichier CSV contenant les contrats"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtFilePath.Text = openFileDialog.FileName;
                TxtContract.Text = string.Empty;
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string singleInput = TxtContract.Text.Trim();
            string fileInput = TxtFilePath?.Text.Trim();

            if (string.IsNullOrEmpty(singleInput) && string.IsNullOrEmpty(fileInput))
            {
                TxtStatus.Text = "Please enter a contract number or select a CSV file.";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;
            BtnRun.IsEnabled = false;
            if (BtnBrowse != null) BtnBrowse.IsEnabled = false;

            TxtStatus.Text = string.IsNullOrEmpty(fileInput) ? "Extraction in progress..." : "Batch extraction in progress...";
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                if (!string.IsNullOrEmpty(fileInput))
                {
                    await Task.Run(() => PerformBatchExtraction(fileInput));
                    TxtStatus.Text = "Batch extraction completed! Global files saved in Output folder.";
                    TxtStatus.Foreground = Brushes.Green;

                    // Pour le batch, le lien ouvrira le dossier de sortie
                    _lastGeneratedPath = Settings.OutputDir;
                    LnkFile.Visibility = Visibility.Visible;
                }
                else
                {
                    ExtractionResult result = await Task.Run(() => _extractionService.PerformExtraction(singleInput));

                    _lastGeneratedPath = result.FilePath;
                    TxtStatus.Text = $"Completed! {result.StatusMessage}";
                    TxtStatus.Foreground = Brushes.Green;
                    LnkFile.Visibility = Visibility.Visible;

                    ExtractionHistory.Add(new ExtractionItem
                    {
                        ContractId = singleInput,
                        InternalId = result.InternalId, // Ajout de l'Internal ID
                        Product = "N/A",
                        Premium = "0",
                        Ucon = result.UconId,
                        Hdmd = result.DemandId,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Test = "À définir", // Ajout de la valeur de Test
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
                PrgLoading.Visibility = Visibility.Collapsed;
                BtnRun.IsEnabled = true;
                if (BtnBrowse != null) BtnBrowse.IsEnabled = true;
            }
        }

        private void PerformBatchExtraction(string filePath)
        {
            string rawText = File.ReadAllText(filePath);
            string[] lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1) return;

            // Buffers pour les fichiers globaux
            StringBuilder globalLisa = new StringBuilder();
            StringBuilder globalElia = new StringBuilder();

            string[] headers = lines[0].Split(new[] { ';', ',' });
            int contractIndex = 4;
            int premiumIndex = 5;
            int productIndex = 3;
            // Si tu as une colonne "Test" dans ton CSV, tu pourrais aussi récupérer son index ici

            for (int i = 0; i < headers.Length; i++)
            {
                // Nettoyage des guillemets potentiels autour des en-têtes
                string h = headers[i].Trim().Trim('"');
                if (h.Equals("LISA Contract", StringComparison.OrdinalIgnoreCase)) contractIndex = i;
                if (h.Equals("Premium", StringComparison.OrdinalIgnoreCase)) premiumIndex = i;
                if (h.Equals("Product", StringComparison.OrdinalIgnoreCase)) productIndex = i;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(new[] { ';', ',' });

                if (columns.Length > contractIndex)
                {
                    // Suppression impérative des guillemets autour des valeurs (essentiel pour la BDD)
                    string contractNumber = columns[contractIndex].Trim().Trim('"');
                    string premiumAmount = columns.Length > premiumIndex ? columns[premiumIndex].Trim().Trim('"') : "0";
                    string productValue = columns.Length > productIndex ? columns[productIndex].Trim().Trim('"') : "N/A";

                    if (!string.IsNullOrEmpty(contractNumber))
                    {
                        try
                        {
                            ExtractionResult result = _extractionService.PerformExtraction(contractNumber);

                            // Accumulation LISA uniquement si le contenu n'est pas vide
                            if (!string.IsNullOrWhiteSpace(result.LisaContent))
                            {
                                globalLisa.AppendLine("------------------------------------------------------------");
                                globalLisa.AppendLine($"### CONTRACT: {contractNumber} | PRODUCT: {productValue}");
                                globalLisa.AppendLine("------------------------------------------------------------");
                                globalLisa.Append(result.LisaContent);
                                globalLisa.AppendLine();
                            }

                            // Accumulation ELIA uniquement si le contenu n'est pas vide
                            if (!string.IsNullOrWhiteSpace(result.EliaContent))
                            {
                                globalElia.AppendLine("------------------------------------------------------------");
                                globalElia.AppendLine($"### CONTRACT: {contractNumber} | UCON: {result.UconId}");
                                globalElia.AppendLine("------------------------------------------------------------");
                                globalElia.Append(result.EliaContent);
                                globalElia.AppendLine();
                            }

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ExtractionHistory.Add(new ExtractionItem
                                {
                                    ContractId = contractNumber,
                                    InternalId = result.InternalId, // Ajout de l'Internal ID
                                    Product = productValue,
                                    Premium = premiumAmount,
                                    Ucon = result.UconId,
                                    Hdmd = result.DemandId,
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
                                    Test = "À définir", // Ajout de la valeur de Test
                                    FilePath = result.FilePath
                                });
                            });
                        }
                        catch (Exception)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ExtractionHistory.Add(new ExtractionItem
                                {
                                    ContractId = $"{contractNumber} (FAILED)",
                                    InternalId = "Error", // Valeur par défaut en cas d'erreur
                                    Product = productValue,
                                    Premium = premiumAmount,
                                    Ucon = "Error",
                                    Hdmd = "Error",
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
                                    Test = "Error", // Valeur par défaut en cas d'erreur
                                    FilePath = string.Empty
                                });
                            });
                        }
                    }
                }
            }

            // Sauvegarde des fichiers globaux
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            File.WriteAllText(Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_LISA_{timestamp}.csv"), globalLisa.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_ELIA_{timestamp}.csv"), globalElia.ToString(), Encoding.UTF8);
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_lastGeneratedPath))
            {
                // Si c'est un dossier (Batch), on l'ouvre
                Process.Start("explorer.exe", _lastGeneratedPath);
            }
            else if (File.Exists(_lastGeneratedPath))
            {
                // Si c'est un fichier unique, on le sélectionne
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
            }
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Activation Module selected."; }
        private void BtnComparison_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Comparison Module selected."; }
    }
}