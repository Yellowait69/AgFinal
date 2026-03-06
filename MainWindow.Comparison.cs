using System;
using System.IO;
using System.Text;
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

        // COMPARISON MODULE LOGIC


        private void BtnBrowseBase_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV Files|*.csv",
                Title = "Select the base extraction CSV file",
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
                Title = "Select the target extraction CSV file",
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
            BtnRunComparison.IsEnabled = !string.IsNullOrWhiteSpace(TxtBaseFile.Text) &&
                                         !string.IsNullOrWhiteSpace(TxtTargetFile.Text);
        }

        private async void BtnRunComparisonAction_Click(object sender, RoutedEventArgs e)
        {
            string baseFile = TxtBaseFile.Text;
            string targetFile = TxtTargetFile.Text;

            if (string.IsNullOrEmpty(baseFile) || string.IsNullOrEmpty(targetFile)) return;

            BtnRunComparison.IsEnabled = false;
            TxtComparisonResults.Text = "Running deep comparison on all tables... Please wait.";

            await Task.Run(() =>
            {
                var orchestrator = new ComparisonOrchestrator();
                try
                {
                    //  Lancer la comparaison
                    var report = orchestrator.RunFullComparison(baseFile, targetFile);

                    //  Générer le texte du rapport
                    string reportContent = GenerateReportText(report);

                    //  Créer un nom de fichier avec un ID unique

                    string[] fileIds = orchestrator.GetFileIds(baseFile);
                    string uniqueId = fileIds.Length >= 3 ? fileIds[2] : "UnknownID";
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                    Directory.CreateDirectory(Settings.OutputDir);
                    string reportFileName = $"ComparisonReport_{uniqueId}_{timestamp}.txt";
                    string reportFilePath = Path.Combine(Settings.OutputDir, reportFileName);

                    //  Sauvegarder le rapport sur le disque
                    File.WriteAllText(reportFilePath, reportContent, Encoding.UTF8);

                    //  Permettre à l'UI d'ouvrir le dossier
                    _lastGeneratedPath = Settings.OutputDir;

                    //  Afficher à l'écran avec le chemin d'accès
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TxtComparisonResults.Text = reportContent;
                        TxtComparisonResults.Text += $"\n\n📂 Rapport sauvegardé avec succès sous :\n{reportFilePath}\n";
                        TxtComparisonResults.Text += "(Vous pouvez utiliser le bouton d'ouverture de dossier pour y accéder)";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => TxtComparisonResults.Text = $"CRITICAL ERROR:\n{ex.Message}\n\n{ex.StackTrace}");
                }
            });

            BtnRunComparison.IsEnabled = true;
        }


        private string GenerateReportText(ComparisonReport report)
        {
            var sb = new StringBuilder();

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
                sb.AppendLine("All tables in the files are perfectly identical.");
            }
            else
            {
                foreach (var result in report.FileResults)
                {
                    sb.AppendLine($"--- TABLE : {result.TableName} ({result.FileType}) ---");

                    if (result.IsMatch)
                    {
                        sb.AppendLine("Status : ✅ OK (No differences)\n");
                    }
                    else
                    {
                        sb.AppendLine($"Status : ❌ {result.Status}");
                        sb.AppendLine("Details:");
                        sb.AppendLine(result.ErrorDetails);
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString();
        }


        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastGeneratedPath) && Directory.Exists(_lastGeneratedPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = _lastGeneratedPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                MessageBox.Show("Aucun dossier de génération n'est disponible pour le moment.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}