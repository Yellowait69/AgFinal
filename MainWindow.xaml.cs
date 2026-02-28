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
using AutoActivator.Config;
using AutoActivator.Models; // Ajout du namespace pour les modèles (DTO)
using AutoActivator.Services;

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

        // Séparation des responsabilités : on déclare nos deux services distincts
        private readonly ExtractionService _extractionService;
        private readonly BatchExtractionService _batchExtractionService;

        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
            ListHistory.ItemsSource = ExtractionHistory;

            ICollectionView view = CollectionViewSource.GetDefaultView(ExtractionHistory);
            view.SortDescriptions.Add(new SortDescription("Product", ListSortDirection.Ascending));

            // Initialisation des services
            _extractionService = new ExtractionService();
            _batchExtractionService = new BatchExtractionService(_extractionService);
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
                    // Lancement du Batch via le service dédié, avec une fonction de Callback pour mettre à jour l'UI
                    await Task.Run(() => _batchExtractionService.PerformBatchExtraction(fileInput, UpdateHistoryGrid));

                    TxtStatus.Text = "Batch extraction completed! Global files saved in Output folder.";
                    TxtStatus.Foreground = Brushes.Green;
                    _lastGeneratedPath = Settings.OutputDir;
                    LnkFile.Visibility = Visibility.Visible;
                }
                else
                {
                    // Contrat unique via le service d'extraction simple
                    ExtractionResult result = await Task.Run(() => _extractionService.PerformExtraction(singleInput, true));

                    _lastGeneratedPath = result.FilePath;
                    TxtStatus.Text = $"Completed! {result.StatusMessage}";
                    TxtStatus.Foreground = Brushes.Green;
                    LnkFile.Visibility = Visibility.Visible;

                    UpdateHistoryGrid(new BatchProgressInfo
                    {
                        ContractId = singleInput,
                        InternalId = result.InternalId,
                        Product = "N/A",
                        Premium = "0",
                        UconId = result.UconId,
                        DemandId = result.DemandId,
                        Status = "OK"
                    });
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
                MessageBox.Show(ex.Message, "Erreur d'extraction", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                PrgLoading.Visibility = Visibility.Collapsed;
                BtnRun.IsEnabled = true;
                if (BtnBrowse != null) BtnBrowse.IsEnabled = true;
            }
        }

        /// <summary>
        /// Méthode appelée par les services (Callback) pour mettre à jour la grille de l'historique de manière asynchrone (Thread-Safe).
        /// </summary>
        private void UpdateHistoryGrid(BatchProgressInfo info)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ExtractionHistory.Add(new ExtractionItem
                {
                    ContractId = info.ContractId,
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
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_lastGeneratedPath)) Process.Start("explorer.exe", _lastGeneratedPath);
                else if (File.Exists(_lastGeneratedPath)) Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
            }
            catch (Exception ex) { MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Activation Module selected."; }
        private void BtnComparison_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Comparison Module selected."; }
    }
}