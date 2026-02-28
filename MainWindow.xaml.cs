using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using AutoActivator.Config;
using AutoActivator.Models;
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

        // Services
        private readonly ExtractionService _extractionService;

        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
            ListHistory.ItemsSource = ExtractionHistory;

            // Sort history by Product (Environment in our updated logic) by default
            ICollectionView view = CollectionViewSource.GetDefaultView(ExtractionHistory);
            view.SortDescriptions.Add(new SortDescription("Product", ListSortDirection.Ascending));

            // Initialize extraction service
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
                MessageBox.Show($"[ERROR] Failed to create directories: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================================
        // SINGLE EXTRACTION TAB LOGIC
        // =========================================================================

        private async void BtnRunSingle_Click(object sender, RoutedEventArgs e)
        {
            string contractD = TxtSingleD?.Text.Trim();
            string contractQ = TxtSingleQ?.Text.Trim();

            if (string.IsNullOrEmpty(contractD) && string.IsNullOrEmpty(contractQ))
            {
                TxtStatus.Text = "Please enter at least one contract number (D000 or Q000).";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            // Utilisation de IProgress pour gérer la mise à jour de l'UI de manière asynchrone et fluide
            var progress = new Progress<ExtractionItem>(item => ExtractionHistory.Add(item));

            await RunProcessAsync(async () =>
            {
                if (!string.IsNullOrEmpty(contractD))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Extracting Environment D000..."));
                    await Task.Run(() => PerformSingleExtraction(contractD, "D000", progress));
                }

                if (!string.IsNullOrEmpty(contractQ))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Extracting Environment Q000..."));
                    await Task.Run(() => PerformSingleExtraction(contractQ, "Q000", progress));
                }

                Application.Current.Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Single extraction completed successfully."));
            });
        }

        private void PerformSingleExtraction(string contract, string env, IProgress<ExtractionItem> progress)
        {
            ExtractionResult result = _extractionService.PerformExtraction(contract, env, true);
            _lastGeneratedPath = Settings.OutputDir;

            // Report transmet l'objet au thread UI sans bloquer l'exécution
            progress.Report(new ExtractionItem
            {
                ContractId = contract,
                InternalId = result.InternalId,
                Product = env, // Using Product column to show Environment
                Premium = "0",
                Ucon = result.UconId,
                Hdmd = result.DemandId,
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Test = "OK",
                FilePath = result.FilePath
            });
        }

        // =========================================================================
        // BATCH EXTRACTION TAB LOGIC
        // =========================================================================

        private void BtnBrowseD_Click(object sender, RoutedEventArgs e) => TxtBatchD.Text = OpenCsvDialog();
        private void BtnBrowseQ_Click(object sender, RoutedEventArgs e) => TxtBatchQ.Text = OpenCsvDialog();

        private string OpenCsvDialog()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV Files|*.csv",
                Title = "Select a CSV file containing contracts"
            };

            return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : string.Empty;
        }

        private async void BtnRunBatch_Click(object sender, RoutedEventArgs e)
        {
            string fileD = TxtBatchD?.Text.Trim();
            string fileQ = TxtBatchQ?.Text.Trim();

            if (string.IsNullOrEmpty(fileD) && string.IsNullOrEmpty(fileQ))
            {
                TxtStatus.Text = "Please select at least one CSV file.";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            var batchService = new BatchExtractionService(_extractionService);

            // Utilisation de IProgress pour reporter l'avancée sans bloquer (freeze) l'UI
            var progress = new Progress<BatchProgressInfo>(UpdateHistoryGrid);

            await RunProcessAsync(async () =>
            {
                if (!string.IsNullOrEmpty(fileD))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Batch Extracting Environment D000..."));
                    await Task.Run(() => batchService.PerformBatchExtraction(fileD, "D000", info => progress.Report(info)));
                }

                if (!string.IsNullOrEmpty(fileQ))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Batch Extracting Environment Q000..."));
                    await Task.Run(() => batchService.PerformBatchExtraction(fileQ, "Q000", info => progress.Report(info)));
                }

                _lastGeneratedPath = Settings.OutputDir;
                Application.Current.Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Batch extraction completed! Global files saved in Output folder."));
            });
        }

        /// <summary>
        /// Callback method used by IProgress to update UI thread-safely
        /// </summary>
        private void UpdateHistoryGrid(BatchProgressInfo info)
        {
            // Grâce à Progress<T>, ce code est DÉJÀ exécuté sur le thread UI (Dispatcher)
            // Plus besoin de Invoke ou BeginInvoke ici, ce qui empêche le gel !
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
        }

        // =========================================================================
        // UTILITY METHODS
        // =========================================================================

        /// <summary>
        /// Wraps UI state management (Progress bar, buttons disabling) and exception handling
        /// </summary>
        private async Task RunProcessAsync(Func<Task> action)
        {
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;
            if (BtnRunSingle != null) BtnRunSingle.IsEnabled = false;
            if (BtnRunBatch != null) BtnRunBatch.IsEnabled = false;

            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                await action();
                LnkFile.Visibility = Visibility.Visible;
                TxtStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
                MessageBox.Show(ex.Message, "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                PrgLoading.Visibility = Visibility.Collapsed;
                if (BtnRunSingle != null) BtnRunSingle.IsEnabled = true;
                if (BtnRunBatch != null) BtnRunBatch.IsEnabled = true;
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_lastGeneratedPath))
                {
                    Process.Start("explorer.exe", _lastGeneratedPath);
                }
                else if (File.Exists(_lastGeneratedPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}