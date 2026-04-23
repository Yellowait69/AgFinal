using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Services;

namespace AutoActivator.Gui.Views
{
    public class UIComparisonResult
    {
        public string TableName { get; set; }
        public string Status { get; set; }
        public string ErrorDetails { get; set; }
        public bool IsMatch { get; set; }
        public string StatusIcon => IsMatch ? "✔ OK" : "✖ KO";
        public SolidColorBrush StatusColor => IsMatch ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2ECC71"))
                                                      : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
    }

    public partial class ComparisonView : UserControl
    {
        public ComparisonView()
        {
            InitializeComponent();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.OpenHelpTargetingTab(2);
            }
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool isVisible && isVisible)
            {
                LoadBaselines();
            }
        }

        private void LoadBaselines()
        {
            if (Directory.Exists(Settings.BaselineDir))
            {
                var files = Directory.GetFiles(Settings.BaselineDir, "*.csv").Select(f => Path.GetFileName(f)).ToList();
                files.Insert(0, "-- Select a Baseline --");

                CmbBaselineSelector.SelectionChanged -= SmartMatcher_Changed;
                CmbBaselineSelector.ItemsSource = files;
                CmbBaselineSelector.SelectedIndex = 0;
                CmbBaselineSelector.SelectionChanged += SmartMatcher_Changed;
            }
        }

        private void SmartMatcher_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CmbBaselineSelector == null || CmbSmartEnv == null || TxtBaseFile == null || TxtTargetFile == null) return;

            if (CmbBaselineSelector.SelectedIndex > 0 && CmbBaselineSelector.SelectedItem is string selectedFileName)
            {
                string basePath = Path.Combine(Settings.BaselineDir, selectedFileName);
                TxtBaseFile.Text = basePath;

                string envLetter = CmbSmartEnv.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "D" : "D";

                string targetPath = FindLatestMatchingExtraction(selectedFileName, envLetter);

                if (!string.IsNullOrEmpty(targetPath))
                {
                    TxtTargetFile.Text = targetPath;
                }
                else
                {
                    TxtTargetFile.Text = "";
                    MessageBox.Show($"No recent extraction found for {selectedFileName} in environment {envLetter}000.\nPlease ensure you have run an extraction recently.", "No Match Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                CheckEnableRunButton();
            }
            else if (CmbBaselineSelector.SelectedIndex == 0)
            {
                TxtBaseFile.Text = "";
                TxtTargetFile.Text = "";
                CheckEnableRunButton();
            }
        }

        private string FindLatestMatchingExtraction(string baselineName, string envLetter)
        {
            string[] channels = { "C01", "C03", "C05" };
            string foundChannel = channels.FirstOrDefault(c => baselineName.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);

            if (foundChannel != null && Directory.Exists(Settings.OutputDir))
            {
                string searchPattern = $"*{foundChannel}*_{envLetter}_*.csv";

                var files = Directory.GetFiles(Settings.OutputDir, searchPattern);
                if (files.Any())
                {
                    return files.OrderByDescending(f => File.GetCreationTime(f)).First();
                }
            }
            return null;
        }

        // =====================================================================
        // UPDATED METHOD: Automatically add the latest extraction
        // =====================================================================
        private void BtnAddBaseline_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Check if the extraction folder exists
                if (!Directory.Exists(Settings.OutputDir))
                {
                    MessageBox.Show("The extraction folder does not exist yet. Please run an extraction first.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 2. Find the most recent CSV file in the extraction folder
                var latestExtractionFile = Directory.GetFiles(Settings.OutputDir, "*.csv")
                                                    .OrderByDescending(f => File.GetCreationTime(f))
                                                    .FirstOrDefault();

                // 3. If a file exists, copy it
                if (latestExtractionFile != null)
                {
                    if (!Directory.Exists(Settings.BaselineDir))
                        Directory.CreateDirectory(Settings.BaselineDir);

                    string fileName = Path.GetFileName(latestExtractionFile);
                    string destPath = Path.Combine(Settings.BaselineDir, fileName);

                    // Copy to the Baseline folder (true overwrites if it already exists)
                    File.Copy(latestExtractionFile, destPath, true);

                    MessageBox.Show($"The latest extraction ({fileName}) was automatically added to the baselines!", "Baseline Added", MessageBoxButton.OK, MessageBoxImage.Information);

                    // 4. Update the dropdown list in the interface
                    LoadBaselines();
                }
                else
                {
                    MessageBox.Show("No extraction was found in the output folder.", "No File", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error while copying the file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // =====================================================================

        private void BtnOpenBaselineFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(Settings.BaselineDir))
                    Directory.CreateDirectory(Settings.BaselineDir);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = Settings.BaselineDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Baseline folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBrowseBase_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV Files|*.csv",
                Title = "Select the base extraction CSV file",
                InitialDirectory = Directory.Exists(Settings.BaselineDir) ? Path.GetFullPath(Settings.BaselineDir) : Path.GetFullPath(Settings.OutputDir)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                CmbBaselineSelector.SelectionChanged -= SmartMatcher_Changed;
                CmbBaselineSelector.SelectedIndex = 0;
                CmbBaselineSelector.SelectionChanged += SmartMatcher_Changed;

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
                CmbBaselineSelector.SelectionChanged -= SmartMatcher_Changed;
                CmbBaselineSelector.SelectedIndex = 0;
                CmbBaselineSelector.SelectionChanged += SmartMatcher_Changed;

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
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            string baseFile = TxtBaseFile.Text;
            string targetFile = TxtTargetFile.Text;

            if (string.IsNullOrEmpty(baseFile) || string.IsNullOrEmpty(targetFile)) return;

            BtnRunComparison.IsEnabled = false;

            PanelDashboard.Visibility = Visibility.Collapsed;
            GridResults.Visibility = Visibility.Collapsed;
            TxtComparisonWaiting.Visibility = Visibility.Visible;
            TxtComparisonWaiting.Text = "Deep analysis in progress...\n(Filtering the latest contract by Test ID). Please wait.";

            await mainWindow.RunProcessAsync(async () =>
            {
                await Task.Run(async () =>
                {
                    var orchestrator = new ComparisonOrchestrator();
                    try
                    {
                        var report = orchestrator.RunFullComparison(baseFile, targetFile);

                        string reportContent = GenerateReportText(report);

                        string[] fileIds = orchestrator.GetFileIds(baseFile);
                        string uniqueId = fileIds.Length >= 3 ? fileIds[2] : "UnknownID";
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                        Directory.CreateDirectory(Settings.OutputDir);
                        string reportFilePath = Path.Combine(Settings.OutputDir, $"ComparisonReport_{uniqueId}_{timestamp}.txt");

                        using (StreamWriter writer = new StreamWriter(reportFilePath, false, Encoding.UTF8))
                        {
                            await writer.WriteAsync(reportContent).ConfigureAwait(false);
                        }

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            mainWindow.LastGeneratedPath = Settings.OutputDir;
                            UpdateDashboardUI(report);
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TxtComparisonWaiting.Text = $"CRITICAL ERROR:\n{ex.Message}\n\n{ex.StackTrace}";
                            TxtComparisonWaiting.Foreground = new SolidColorBrush(Colors.Red);
                        });
                    }
                });
            });

            BtnRunComparison.IsEnabled = true;
        }

        private void UpdateDashboardUI(ComparisonReport report)
        {
            TxtComparisonWaiting.Visibility = Visibility.Collapsed;
            PanelDashboard.Visibility = Visibility.Visible;
            GridResults.Visibility = Visibility.Visible;

            TxtTotalRows.Text = report.TotalRowsCompared.ToString("N0");
            TxtTotalErrors.Text = report.TotalDifferencesFound.ToString("N0");
            TxtScorePercentage.Text = $"{report.GlobalSuccessPercentage}%";

            string testSummary = string.Join(" | ", report.TestMetrics.Select(t => $"{t.Key}: {t.Value.SuccessPercentage}%"));
            string cleanTestScores = $"\n🎯 Test Scores: {testSummary}";

            string productSummary = string.Join(" | ", report.ProductMetrics.Select(p => $"{p.Key}: {p.Value.SuccessPercentage}%"));
            string cleanProductScores = report.ProductMetrics.Any() ? $"\n📦 Product Scores: {productSummary}" : "";

            string combinedScores = cleanTestScores + cleanProductScores;

            double radius = 64.0;
            double circumference = 2 * Math.PI * radius;
            double dashLength = (report.GlobalSuccessPercentage / 100.0) * circumference;

            CircleProgress.StrokeDashArray = new DoubleCollection { dashLength / 12.0, circumference };

            if (report.GlobalSuccessPercentage == 100)
            {
                CircleProgress.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2ECC71")); // Green
                TxtDashboardTitle.Text = "🎉 Perfect Match!" + combinedScores;
                TxtDashboardTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2ECC71"));
            }
            else if (report.GlobalSuccessPercentage >= 95)
            {
                CircleProgress.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1C40F")); // Yellow
                TxtDashboardTitle.Text = "⚠️ Minor Discrepancies" + combinedScores;
                TxtDashboardTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F39C12"));
            }
            else
            {
                CircleProgress.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C")); // Red
                TxtDashboardTitle.Text = "❌ Differences Detected" + combinedScores;
                TxtDashboardTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
            }

            var uiResults = new List<UIComparisonResult>();
            foreach (var r in report.FileResults)
            {
                uiResults.Add(new UIComparisonResult
                {
                    TableName = r.TableName,
                    Status = r.Status,
                    IsMatch = r.IsMatch,
                    ErrorDetails = string.IsNullOrWhiteSpace(r.ErrorDetails) ? "No differences detected." : r.ErrorDetails.Trim()
                });
            }

            var sortedResults = uiResults.OrderBy(x => x.IsMatch).ThenBy(x => x.TableName).ToList();

            GridResults.ItemsSource = sortedResults;
        }

        private string GenerateReportText(ComparisonReport report)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=====================================================");
            sb.AppendLine("                 COMPARISON REPORT                   ");
            sb.AppendLine("=====================================================");

            sb.AppendLine($"[TESTS METRICS]");
            sb.AppendLine($"Tests in Base File   : {report.TotalBaseTests}");
            sb.AppendLine($"Tests in Target File : {report.TotalTargetTests}");
            sb.AppendLine($"Tests Compared       : {report.ComparedTestsCount}");

            if (report.MissingInTarget.Any() || report.MissingInBase.Any())
            {
                sb.AppendLine("\n[DISCREPANCIES - NON BLOCKING INFO]");
                if (report.MissingInTarget.Any())
                {
                    sb.AppendLine($"- Tests found ONLY in Base (ignored): {string.Join(", ", report.MissingInTarget)}");
                }
                if (report.MissingInBase.Any())
                {
                    sb.AppendLine($"- Tests found ONLY in Target (ignored): {string.Join(", ", report.MissingInBase)}");
                }
            }

            sb.AppendLine("\n[PRODUCT SCORES]");
            if (report.ProductMetrics.Any())
            {
                foreach (var kvp in report.ProductMetrics.OrderByDescending(x => x.Value.SuccessPercentage))
                {
                    sb.AppendLine($"- Product {kvp.Key,-10} : {kvp.Value.SuccessPercentage,6}%  ({kvp.Value.TotalRows - kvp.Value.ErrorRows}/{kvp.Value.TotalRows} rows OK)");
                }
            }
            else
            {
                sb.AppendLine("- No products detected.");
            }

            sb.AppendLine("\n[TEST ID SCORES]");
            foreach (var kvp in report.TestMetrics.OrderBy(x => x.Key))
            {
                sb.AppendLine($"- Test {kvp.Key,-13} : {kvp.Value.SuccessPercentage,6}%  ({kvp.Value.TotalRows - kvp.Value.ErrorRows}/{kvp.Value.TotalRows} rows OK)");
            }

            sb.AppendLine("-----------------------------------------------------");

            sb.AppendLine($"Global Success Rate : {report.GlobalSuccessPercentage} %");
            sb.AppendLine($"Total Rows Analyzed : {report.TotalRowsCompared}");
            sb.AppendLine($"Total Errors Found  : {report.TotalDifferencesFound}");
            sb.AppendLine("=====================================================\n");

            if (report.GlobalSuccessPercentage == 100)
            {
                sb.AppendLine("✅ PERFECT MATCH!");
                sb.AppendLine("All tables for the common tested contracts are perfectly identical.");
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
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                if (!string.IsNullOrEmpty(mainWindow.LastGeneratedPath) && Directory.Exists(mainWindow.LastGeneratedPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = mainWindow.LastGeneratedPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                else
                {
                    MessageBox.Show("No output folder is available at the moment.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}