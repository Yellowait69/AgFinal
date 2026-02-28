using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
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
                Title = "Select the base CSV file"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtBaseFile.Text = openFileDialog.FileName;
                PopulateTargetFiles(openFileDialog.FileName);
            }
        }

        private void PopulateTargetFiles(string baseFilePath)
        {
            ComboTargetFile.Items.Clear();
            var orchestrator = new ComparisonOrchestrator();

            string directory = Path.GetDirectoryName(baseFilePath);
            var allFiles = Directory.GetFiles(directory, "*.csv");
            var compatibleFiles = orchestrator.GetCompatibleTargetFiles(baseFilePath, allFiles);

            foreach (var file in compatibleFiles)
            {
                ComboTargetFile.Items.Add(Path.GetFileName(file));
            }

            if (ComboTargetFile.Items.Count > 0)
            {
                ComboTargetFile.SelectedIndex = 0;
                ComboTargetFile.IsEnabled = true;
                BtnRunComparison.IsEnabled = true;
            }
            else
            {
                ComboTargetFile.IsEnabled = false;
                BtnRunComparison.IsEnabled = false;
                MessageBox.Show("No compatible target files (sharing the same 3 IDs) found in this directory.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnRunComparisonAction_Click(object sender, RoutedEventArgs e)
        {
            string baseFile = TxtBaseFile.Text;
            string targetFileName = ComboTargetFile.SelectedItem?.ToString();
            string tableName = TxtTableName.Text.Trim();

            if (string.IsNullOrEmpty(targetFileName)) return;

            string targetFile = Path.Combine(Path.GetDirectoryName(baseFile), targetFileName);

            BtnRunComparison.IsEnabled = false;
            TxtComparisonResults.Text = "Running deep comparison... Please wait.";

            await Task.Run(() =>
            {
                var orchestrator = new ComparisonOrchestrator();
                try
                {
                    var report = orchestrator.RunFullComparison(baseFile, targetFile, tableName);
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
                sb.AppendLine("All files, including Elia/Lisa mirror files, are perfectly identical.");
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