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
        public string InternalId { get; set; } // Numéro interne (NO_CNT)
        public string Product { get; set; }
        public string Premium { get; set; }
        public string Ucon { get; set; }
        public string Hdmd { get; set; }
        public string Time { get; set; }
        public string Test { get; set; } // Statut ou valeur de test
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
                        Test = "OK", // Ajout de la valeur de Test
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
            // CORRECTION MAJEURE ICI : Nettoyage du BOM (\uFEFF) généré par Excel
            string rawText = File.ReadAllText(filePath).Replace("\uFEFF", "");
            string[] lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1)
            {
                MessageBox.Show("Le fichier CSV est vide ou ne contient que l'en-tête.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Buffers pour les fichiers globaux
            StringBuilder globalLisa = new StringBuilder();
            StringBuilder globalElia = new StringBuilder();

            string[] headers = lines[0].Split(new[] { ';', ',' });

            // On met -1 par défaut pour vérifier si on a bien trouvé la colonne
            int contractIndex = -1;
            int premiumIndex = -1;
            int productIndex = -1;

            for (int i = 0; i < headers.Length; i++)
            {
                // Nettoyage des guillemets potentiels autour des en-têtes et passage en minuscules
                string h = headers[i].Trim().Trim('"').ToLower();

                // Détection plus souple des colonnes
                if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa")) contractIndex = i;
                if (h.Contains("premium") || h.Contains("prime")) premiumIndex = i;
                if (h.Contains("product") || h.Contains("produit")) productIndex = i;
            }

            // Si on n'a pas trouvé de colonne contrat, on prend la première (index 0) par défaut
            if (contractIndex == -1) contractIndex = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(new[] { ';', ',' });

                if (columns.Length > contractIndex)
                {
                    // Nettoyage agressif : supprime les guillemets ET les signes égal (ex: ="182-272...")
                    string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();

                    string premiumAmount = (premiumIndex != -1 && columns.Length > premiumIndex)
                        ? columns[premiumIndex].Replace("=", "").Replace("\"", "").Trim() : "0";

                    string productValue = (productIndex != -1 && columns.Length > productIndex)
                        ? columns[productIndex].Replace("=", "").Replace("\"", "").Trim() : "N/A";

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
                                    InternalId = result.InternalId,
                                    Product = productValue,
                                    Premium = premiumAmount,
                                    Ucon = result.UconId,
                                    Hdmd = result.DemandId,
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
                                    Test = "OK", // Indique que tout s'est bien passé
                                    FilePath = result.FilePath
                                });
                            });
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ExtractionHistory.Add(new ExtractionItem
                                {
                                    ContractId = $"{contractNumber} (FAILED)",
                                    InternalId = "Error",
                                    Product = productValue,
                                    Premium = premiumAmount,
                                    Ucon = "Error",
                                    Hdmd = "Error",
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
                                    Test = ex.Message.Contains("not found") ? "Non trouvé en BDD" : "Erreur SQL",
                                    FilePath = string.Empty
                                });
                            });
                        }
                    }
                }
            }

            // Sauvegarde des fichiers globaux (avec sécurité au cas où aucun contrat ne ressortirait)
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");

            string finalLisaContent = globalLisa.Length > 0 ? globalLisa.ToString() : "AUCUN CONTRAT LISA TROUVE LORS DE L'EXTRACTION.\n\nVeuillez vérifier que les numéros de contrats dans votre CSV sont au bon format (ex: 182-1234567-89).";
            File.WriteAllText(Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_LISA_{timestamp}.csv"), finalLisaContent, Encoding.UTF8);

            string finalEliaContent = globalElia.Length > 0 ? globalElia.ToString() : "AUCUN CONTRAT ELIA TROUVE LORS DE L'EXTRACTION.";
            File.WriteAllText(Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_ELIA_{timestamp}.csv"), finalEliaContent, Encoding.UTF8);
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