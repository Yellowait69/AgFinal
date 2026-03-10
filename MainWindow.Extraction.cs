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

        // SINGLE EXTRACTION TAB LOGIC

        private void InputType_Checked(object sender, RoutedEventArgs e)
        {
            // On s'assure que les éléments visuels sont bien initialisés avant de vider le texte
            if (TxtSingleD != null)
            {
                TxtSingleD.Text = string.Empty;
            }

            if (TxtSingleQ != null)
            {
                TxtSingleQ.Text = string.Empty;
            }
        }

        private async void BtnRunSingle_Click(object sender, RoutedEventArgs e)
        {
            string valueD = TxtSingleD?.Text.Trim();
            string valueQ = TxtSingleQ?.Text.Trim();

            // On regarde si la recherche se fait par Demand ID via le bouton radio de l'interface
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

            // NOUVEAU : Vérification de la méthode de recherche pour le batch (Contract vs Demand ID)
            bool isDemandId = RbBatchSearchDemand.IsChecked == true;

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
                    // Info.ContractId contiendra le VRAI numéro de contrat formaté si on a cherché par Demand ID
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
                if (!string.IsNullOrEmpty(fileD))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Batch Extracting Environment D000...");
                    // NOUVEAU : on passe isDemandId à la méthode
                    await Task.Run(() => batchService.PerformBatchExtraction(fileD, "D000", progress.Report, isDemandId));
                }

                if (!string.IsNullOrEmpty(fileQ))
                {
                    Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Batch Extracting Environment Q000...");
                    // NOUVEAU : on passe isDemandId à la méthode
                    await Task.Run(() => batchService.PerformBatchExtraction(fileQ, "Q000", progress.Report, isDemandId));
                }

                _lastGeneratedPath = Settings.OutputDir;
                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Batch extraction completed! Global files saved in Output folder.");
            });
        }
    }
}