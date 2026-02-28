using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Services;

namespace AutoActivator.Gui
{
    // Le mot-clé "partial" relie ce code au MainWindow principal
    public partial class MainWindow : Window
    {
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
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            var progress = new Progress<ExtractionItem>(item => ExtractionHistory.Add(item));

            await RunProcessAsync(async () =>
            {
                if (!string.IsNullOrEmpty(contractD))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Extracting Environment D000...");
                    await Task.Run(() => PerformSingleExtraction(contractD, "D000", progress));
                }

                if (!string.IsNullOrEmpty(contractQ))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Extracting Environment Q000...");
                    await Task.Run(() => PerformSingleExtraction(contractQ, "Q000", progress));
                }

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Single extraction completed successfully.");
            });
        }

        private void PerformSingleExtraction(string contract, string env, IProgress<ExtractionItem> progress)
        {
            ExtractionResult result = _extractionService.PerformExtraction(contract, env, true);
            _lastGeneratedPath = Settings.OutputDir;

            progress.Report(new ExtractionItem
            {
                ContractId = contract,
                InternalId = result.InternalId,
                Product = env,
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
            var openFileDialog = new OpenFileDialog { Filter = "CSV Files|*.csv", Title = "Select a CSV file containing contracts" };
            return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : string.Empty;
        }

        private async void BtnRunBatch_Click(object sender, RoutedEventArgs e)
        {
            string fileD = TxtBatchD?.Text.Trim();
            string fileQ = TxtBatchQ?.Text.Trim();

            if (string.IsNullOrEmpty(fileD) && string.IsNullOrEmpty(fileQ))
            {
                TxtStatus.Text = "Please select at least one CSV file.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            var batchService = new BatchExtractionService(_extractionService);
            var progress = new Progress<BatchProgressInfo>(info =>
            {
                ExtractionHistory.Add(new ExtractionItem
                {
                    ContractId = info.ContractId, InternalId = info.InternalId, Product = info.Product,
                    Premium = info.Premium, Ucon = info.UconId, Hdmd = info.DemandId,
                    Time = DateTime.Now.ToString("HH:mm:ss"), Test = info.Status, FilePath = string.Empty
                });
            });

            await RunProcessAsync(async () =>
            {
                if (!string.IsNullOrEmpty(fileD))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Batch Extracting Environment D000...");
                    await Task.Run(() => batchService.PerformBatchExtraction(fileD, "D000", progress.Report));
                }

                if (!string.IsNullOrEmpty(fileQ))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Batch Extracting Environment Q000...");
                    await Task.Run(() => batchService.PerformBatchExtraction(fileQ, "Q000", progress.Report));
                }

                _lastGeneratedPath = Settings.OutputDir;
                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Batch extraction completed! Global files saved in Output folder.");
            });
        }
    }
}