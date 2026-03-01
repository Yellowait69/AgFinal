using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using AutoActivator.Config; // Ajouté pour accéder à Settings.OutputDir
using AutoActivator.Models;
using AutoActivator.Services;

namespace AutoActivator.Gui
{
    public partial class MainWindow : Window
    {
        // =========================================================================
        // COMPARISON MODULE LOGIC
        // =========================================================================

        private void BtnBrowseBase_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV Files|*.csv",
                Title = "Select the base CSV file",
                // Ouvre directement le dossier des extractions
                InitialDirectory = Path.GetFullPath(Settings.OutputDir)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtBaseFile.Text = openFileDialog.FileName;
                CheckEnableRunButton();
            }
        }

        private void BtnBrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV Files|*.csv",
                Title = "Select the target CSV file",
                // Ouvre directement le dossier des extractions
                InitialDirectory = Path.GetFullPath(Settings.OutputDir)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtTargetFile.Text = openFileDialog.FileName;
                CheckEnableRunButton();
            }
        }

        private void CheckEnableRunButton()
        {
            // N'active le bouton "Run" que si les deux fichiers sont renseignés
            if (!string.IsNullOrWhiteSpace(TxtBaseFile.Text) && !string.IsNullOrWhiteSpace(TxtTargetFile.Text))
            {
                BtnRunComparison.IsEnabled = true;
            }
            else
            {
                BtnRunComparison.IsEnabled = false;
            }
        }

        private async void BtnRunComparisonAction_Click(object sender, RoutedEventArgs e)
        {
            string baseFile = TxtBaseFile.Text;
            string targetFile = TxtTargetFile.Text;

            // Sécurité supplémentaire au cas où
            if (string.IsNullOrEmpty(baseFile) || string.IsNullOrEmpty(targetFile)) return;

            BtnRunComparison.IsEnabled = false;
            TxtComparisonResults.Text = "Running deep comparison on all tables... Please wait.";

            await Task.Run(() =>
            {
                var orchestrator = new ComparisonOrchestrator();
                try
                {
                    // Lancement de la comparaison globale (sans spécifier de table unique)
                    var report = orchestrator.RunFullComparison(baseFile, targetFile);
                    Application.Current.Dispatcher.Invoke(() => DisplayComparisonReport(report));
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => TxtComparisonResults.Text = $"CRITICAL ERROR:\n{ex.Message}\n\n{ex.StackTrace}");
                }
            });

            BtnRunComparison.IsEnabled = true;
        }

        private void DisplayComparisonReport(ComparisonReport report)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("=====================================================");
            sb.AppendLine("                 COMPARISON REPORT                   ");
            sb.AppendLine("=====================================================");
            sb.AppendLine($"Global Success Rate : {report.GlobalSuccessPercentage} %");
            sb.AppendLine($"Total Rows Analyzed : {report.TotalRowsCompared}");
            sb.AppendLine($"Total Errors Found  : {report.TotalDifferencesFound}");
            sb.AppendLine("=====================================================\n");

            if (report.GlobalSuccessPercentage == 100)
            {
                sb.AppendLine("✅ PERFECT MATCH!");
                sb.AppendLine("All files, including Elia/Lisa mirror files and all their tables, are perfectly identical.");
            }
            else
            {
                foreach (var result in report.FileResults)
                {
                    sb.AppendLine($"--- Comparing {result.FileType} ---");
                    sb.AppendLine($"Base   : {result.BaseFileName}");
                    sb.AppendLine($"Target : {result.TargetFileName}");
                    sb.AppendLine($"Table  : {result.TableName}");

                    if (result.IsMatch) sb.AppendLine("Status : ✅ OK (No differences)");
                    else
                    {
                        sb.AppendLine($"Status : ❌ {result.Status}");
                        sb.AppendLine("Details:");
                        sb.AppendLine(result.ErrorDetails);
                    }
                    sb.AppendLine();
                }
            }

            TxtComparisonResults.Text = sb.ToString();
        }
    }
}