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
    // Petite classe pour formater l'affichage dans le DataGrid de l'interface
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
            if (!(Window.GetWindow(this) is MainWindow mainWindow)) return;

            string baseFile = TxtBaseFile.Text;
            string targetFile = TxtTargetFile.Text;

            if (string.IsNullOrEmpty(baseFile) || string.IsNullOrEmpty(targetFile)) return;

            BtnRunComparison.IsEnabled = false;

            // Cacher le tableau de bord précédent et afficher le message d'attente
            PanelDashboard.Visibility = Visibility.Collapsed;
            GridResults.Visibility = Visibility.Collapsed;
            TxtComparisonWaiting.Visibility = Visibility.Visible;
            TxtComparisonWaiting.Text = "Analyse approfondie en cours...\n(Filtrage du contrat le plus récent par Test ID). Veuillez patienter.";

            // Appel de la méthode de chargement asynchrone sur la MainWindow
            await mainWindow.RunProcessAsync(async () =>
            {
                await Task.Run(async () =>
                {
                    var orchestrator = new ComparisonOrchestrator();
                    try
                    {
                        // Lancer la comparaison (Filtrage intelligent inclus)
                        var report = orchestrator.RunFullComparison(baseFile, targetFile);

                        // Générer le texte du rapport (pour la sauvegarde fichier texte classique)
                        string reportContent = GenerateReportText(report);

                        // Créer un nom de fichier avec un ID unique
                        string[] fileIds = orchestrator.GetFileIds(baseFile);
                        string uniqueId = fileIds.Length >= 3 ? fileIds[2] : "UnknownID";
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                        Directory.CreateDirectory(Settings.OutputDir);
                        string reportFilePath = Path.Combine(Settings.OutputDir, $"ComparisonReport_{uniqueId}_{timestamp}.txt");

                        // Sauvegarder le rapport complet sur le disque en mode Asynchrone
                        using (StreamWriter writer = new StreamWriter(reportFilePath, false, Encoding.UTF8))
                        {
                            await writer.WriteAsync(reportContent);
                        }

                        // METTRE À JOUR LA NOUVELLE INTERFACE VISUELLE SUR LE THREAD UI
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Transmission du chemin à la MainWindow pour le lien global
                            mainWindow.LastGeneratedPath = Settings.OutputDir;
                            UpdateDashboardUI(report);
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TxtComparisonWaiting.Text = $"ERREUR CRITIQUE:\n{ex.Message}\n\n{ex.StackTrace}";
                            TxtComparisonWaiting.Foreground = new SolidColorBrush(Colors.Red);
                        });
                    }
                });
            });

            BtnRunComparison.IsEnabled = true;
        }

        private void UpdateDashboardUI(ComparisonReport report)
        {
            // Basculer l'affichage
            TxtComparisonWaiting.Visibility = Visibility.Collapsed;
            PanelDashboard.Visibility = Visibility.Visible;
            GridResults.Visibility = Visibility.Visible;

            // 1. Mettre à jour les textes des statistiques
            TxtTotalRows.Text = report.TotalRowsCompared.ToString("N0");
            TxtTotalErrors.Text = report.TotalDifferencesFound.ToString("N0");
            TxtScorePercentage.Text = $"{report.GlobalSuccessPercentage}%";

            // --- NOUVEAU : Préparation du message informatif sur les tests et les produits ---
            string infoTests = "";
            string productSummary = string.Join(" | ", report.ProductMetrics.Select(p => $"Produit {p.Key} : {p.Value.SuccessPercentage}%"));

            if (report.MissingInBase.Any() || report.MissingInTarget.Any())
            {
                infoTests = $"\n(Info: {report.ComparedTestsCount} tests comparés. {report.MissingInTarget.Count} absents cible, {report.MissingInBase.Count} absents base)";
            }
            else
            {
                infoTests = $"\n({report.ComparedTestsCount} tests comparés)";
            }

            infoTests += $"\n📊 Scores Produits : {productSummary}";
            // ---------------------------------------------------------------------------------

            // 2. Animer le Cercle de Progression
            // L'Ellipse fait 140 de largeur, StrokeThickness = 12. Rayon = (140 - 12) / 2 = 64
            double radius = 64.0;
            double circumference = 2 * Math.PI * radius;

            // Calculer la longueur du trait en fonction du pourcentage
            double dashLength = (report.GlobalSuccessPercentage / 100.0) * circumference;

            // Appliquer au cercle (DashLength, EspaceVide) divisé par StrokeThickness
            CircleProgress.StrokeDashArray = new DoubleCollection { dashLength / 12.0, circumference };

            // Changer la couleur en fonction du score
            if (report.GlobalSuccessPercentage == 100)
            {
                CircleProgress.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2ECC71")); // Vert
                TxtDashboardTitle.Text = "🎉 Parfait ! Aucune différence détectée." + infoTests;
                TxtDashboardTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2ECC71"));
            }
            else if (report.GlobalSuccessPercentage >= 95)
            {
                CircleProgress.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1C40F")); // Jaune
                TxtDashboardTitle.Text = "⚠️ Presque parfait, quelques anomalies." + infoTests;
                TxtDashboardTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F39C12"));
            }
            else
            {
                CircleProgress.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C")); // Rouge
                TxtDashboardTitle.Text = "❌ Des différences majeures ont été trouvées." + infoTests;
                TxtDashboardTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
            }

            // 3. Préparer et trier la liste pour le DataGrid
            var uiResults = new List<UIComparisonResult>();
            foreach (var r in report.FileResults)
            {
                uiResults.Add(new UIComparisonResult
                {
                    TableName = r.TableName,
                    Status = r.Status,
                    IsMatch = r.IsMatch,
                    ErrorDetails = string.IsNullOrWhiteSpace(r.ErrorDetails) ? "Aucune différence détectée." : r.ErrorDetails.Trim()
                });
            }

            // MAGIE : On trie la liste pour que les erreurs (IsMatch = false) apparaissent TOUJOURS EN HAUT !
            var sortedResults = uiResults.OrderBy(x => x.IsMatch).ThenBy(x => x.TableName).ToList();

            GridResults.ItemsSource = sortedResults;
        }

        private string GenerateReportText(ComparisonReport report)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=====================================================");
            sb.AppendLine("                 COMPARISON REPORT                   ");
            sb.AppendLine("=====================================================");

            // --- SECTION D'INFORMATIONS SUR LES TESTS ---
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

            // --- NOUVEAU : SCORES PAR PRODUIT ---
            sb.AppendLine("\n[SCORES PAR PRODUIT]");
            if (report.ProductMetrics.Any())
            {
                foreach (var kvp in report.ProductMetrics.OrderByDescending(x => x.Value.SuccessPercentage))
                {
                    sb.AppendLine($"- Produit {kvp.Key,-10} : {kvp.Value.SuccessPercentage,6}%  ({kvp.Value.TotalRows - kvp.Value.ErrorRows}/{kvp.Value.TotalRows} lignes OK)");
                }
            }
            else
            {
                sb.AppendLine("- Aucun produit détecté.");
            }

            // --- NOUVEAU : SCORES PAR TEST ID ---
            sb.AppendLine("\n[SCORES PAR TEST ID]");
            foreach (var kvp in report.TestMetrics.OrderBy(x => x.Key))
            {
                sb.AppendLine($"- Test {kvp.Key,-13} : {kvp.Value.SuccessPercentage,6}%  ({kvp.Value.TotalRows - kvp.Value.ErrorRows}/{kvp.Value.TotalRows} lignes OK)");
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
                    MessageBox.Show("Aucun dossier de génération n'est disponible pour le moment.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}