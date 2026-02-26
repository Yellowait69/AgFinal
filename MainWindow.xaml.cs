using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        public string Product { get; set; }
        public string Premium { get; set; }
        public string Ucon { get; set; }
        public string Hdmd { get; set; }
        public string Time { get; set; }
        public string FilePath { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string _lastGeneratedPath = "";
        private readonly ExtractionService _extractionService; // Instance de notre nouveau service
        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
            ListHistory.ItemsSource = ExtractionHistory;

            // Tri automatique de l'historique par la colonne "Product"
            ICollectionView view = CollectionViewSource.GetDefaultView(ExtractionHistory);
            view.SortDescriptions.Add(new SortDescription("Product", ListSortDirection.Ascending));

            // Initialisation du service
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

            TxtStatus.Text = string.IsNullOrEmpty(fileInput) ? "Extraction in progress from LISA and ELIA..." : "Batch extraction in progress...";
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                if (!string.IsNullOrEmpty(fileInput))
                {
                    await Task.Run(() => PerformBatchExtraction(fileInput));
                    TxtStatus.Text = "Batch extraction completed!";
                    TxtStatus.Foreground = Brushes.Green;
                }
                else
                {
                    // Appel simplifié au service d'extraction
                    ExtractionResult result = await Task.Run(() => _extractionService.PerformExtraction(singleInput));

                    _lastGeneratedPath = result.FilePath;
                    TxtStatus.Text = $"Completed! {result.StatusMessage}";
                    TxtStatus.Foreground = Brushes.Green;
                    LnkFile.Visibility = Visibility.Visible;

                    // Ajout avec Add (pour le tri automatique) et intégration de Ucon, Hdmd, Product
                    ExtractionHistory.Add(new ExtractionItem
                    {
                        ContractId = singleInput,
                        Product = "N/A",
                        Premium = "0",
                        Ucon = result.UconId,
                        Hdmd = result.DemandId,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
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
            // 1. Lire tout le fichier et séparer les lignes de manière universelle
            string rawText = File.ReadAllText(filePath);
            string[] lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1) return; // Si le fichier est vide ou n'a qu'un en-tête, on arrête

            // 2. Détecter automatiquement à quel index se trouve la colonne
            string[] headers = lines[0].Split(new[] { ';', ',' });
            int contractIndex = 4; // Valeur par défaut
            int premiumIndex = 5;
            int productIndex = 3;

            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].Trim().Equals("LISA Contract", StringComparison.OrdinalIgnoreCase))
                    contractIndex = i;
                if (headers[i].Trim().Equals("Premium", StringComparison.OrdinalIgnoreCase))
                    premiumIndex = i;
                if (headers[i].Trim().Equals("Product", StringComparison.OrdinalIgnoreCase))
                    productIndex = i;
            }

            // 3. Boucle d'extraction
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] columns = line.Split(new[] { ';', ',' });

                // On vérifie que la ligne contient suffisamment de colonnes pour atteindre notre index principal (contrat)
                if (columns.Length > contractIndex)
                {
                    string contractNumber = columns[contractIndex].Trim();
                    string premiumAmount = columns.Length > premiumIndex ? columns[premiumIndex].Trim() : "0";
                    string productValue = columns.Length > productIndex ? columns[productIndex].Trim() : "N/A";

                    if (!string.IsNullOrEmpty(contractNumber))
                    {
                        try
                        {
                            // Appel au service d'extraction
                            ExtractionResult result = _extractionService.PerformExtraction(contractNumber);
                            _lastGeneratedPath = result.FilePath;

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ExtractionHistory.Add(new ExtractionItem
                                {
                                    ContractId = contractNumber,
                                    Product = productValue,
                                    Premium = premiumAmount,
                                    Ucon = result.UconId,
                                    Hdmd = result.DemandId,
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
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
                                    Product = productValue,
                                    Premium = premiumAmount,
                                    Ucon = "Erreur",
                                    Hdmd = "Erreur",
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
                                    FilePath = string.Empty
                                });
                            });
                        }
                    }
                }
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_lastGeneratedPath))
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Activation Module selected."; }
        private void BtnComparison_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Comparison Module selected."; }
    }
}