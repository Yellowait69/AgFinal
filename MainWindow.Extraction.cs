using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Services;

namespace AutoActivator.Gui
{

    public partial class MainWindow : Window
    {

        // SINGLE EXTRACTION TAB LOGIC

        private async void BtnRunSingle_Click(object sender, RoutedEventArgs e)
        {
            string valueD = TxtSingleD?.Text.Trim();
            string valueQ = TxtSingleQ?.Text.Trim();

            // NOUVEAU : On regarde si la recherche se fait par Demand ID via le bouton radio de l'interface
            bool isDemandId = RbSearchDemand.IsChecked == true;

            if (string.IsNullOrEmpty(valueD) && string.IsNullOrEmpty(valueQ))
            {
                TxtStatus.Text = "Please enter at least one value (D000 or Q000).";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            IProgress<ExtractionItem> progress = new Progress<ExtractionItem>(item => ExtractionHistory.Add(item));

            await RunProcessAsync(async () =>
            {
                if (!string.IsNullOrEmpty(valueD))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Extracting Environment D000...");
                    await Task.Run(() => PerformSingleExtraction(valueD, "D000", progress, isDemandId));
                }

                if (!string.IsNullOrEmpty(valueQ))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Extracting Environment Q000...");
                    await Task.Run(() => PerformSingleExtraction(valueQ, "Q000", progress, isDemandId));
                }

                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Single extraction completed successfully.");
            });
        }

        private void PerformSingleExtraction(string targetValue, string env, IProgress<ExtractionItem> progress, bool isDemandId)
        {
            // On passe "true" pour "saveIndividualFile" et isDemandId à la fin pour le service d'extraction
            ExtractionResult result = _extractionService.PerformExtraction(targetValue, env, true, isDemandId);
            _lastGeneratedPath = Settings.OutputDir;

            progress.Report(new ExtractionItem
            {
                // Ajout d'un tag visuel [DMD] dans l'historique si c'est une recherche par Demand ID
                ContractId = isDemandId ? $"[DMD] {targetValue}" : targetValue,
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


        // BATCH EXTRACTION TAB LOGIC

        private void BtnBrowseD_Click(object sender, RoutedEventArgs e) => TxtBatchD.Text = OpenCsvDialog();
        private void BtnBrowseQ_Click(object sender, RoutedEventArgs e) => TxtBatchQ.Text = OpenCsvDialog();

        private string OpenCsvDialog()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV Files|*.csv",
                Title = "Select a CSV file containing contracts",

                InitialDirectory = Path.GetFullPath(Settings.InputDir)
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
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            var batchService = new BatchExtractionService(_extractionService);


            IProgress<BatchProgressInfo> progress = new Progress<BatchProgressInfo>(info =>
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