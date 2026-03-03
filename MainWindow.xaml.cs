using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using AutoActivator.Config;
using AutoActivator.Models;
using AutoActivator.Services;

namespace AutoActivator.Gui
{
    public partial class MainWindow : Window
    {
        private string _lastGeneratedPath = "";
        private readonly ExtractionService _extractionService;

        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
            ListHistory.ItemsSource = ExtractionHistory;

            ICollectionView view = CollectionViewSource.GetDefaultView(ExtractionHistory);
            view.SortDescriptions.Add(new SortDescription("Product", ListSortDirection.Ascending));

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
        // NAVIGATION MENU LOGIC
        // =========================================================================
        private void BtnExtraction_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Visible;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Collapsed;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Collapsed;
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Collapsed;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Collapsed;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Visible;
        }

        private void BtnComparison_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Collapsed;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Visible;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Collapsed;
        }

        // =========================================================================
        // ACTIVATION MODULE LOGIC
        // =========================================================================

        private async void BtnRunActivation_Click(object sender, RoutedEventArgs e)
        {
            await RunProcessAsync(async () =>
            {
                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Préparation de l'activation (JCLs)...");

                string envValue = "D";
                string contract = "", cus = "", amount = "", bucp = "";

                // Récupération sécurisée des valeurs depuis l'interface graphique (Thread UI)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CmbEnvironment.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
                    {
                        envValue = selectedItem.Tag?.ToString() ?? "D";
                    }

                    contract = TxtActContract.Text.Trim();
                    cus = TxtActCus.Text.Trim();
                    amount = TxtActAmount.Text.Trim();
                    bucp = TxtActBucp.Text.Trim();
                });

                // --- UTILISATION DES IDENTIFIANTS DEPUIS LES SETTINGS ---
                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    throw new Exception("Les identifiants (Uid/Pwd) ne sont pas configurés. Veuillez vous reconnecter.");
                }

                // 1. Définition des variables générales
                var generalVariables = new Dictionary<string, string>
                {
                    { "ENVIMS", envValue },
                    { "CUS", cus },
                    { "YYMMDD", DateTime.Now.ToString("yyMMdd") },
                    { "YYYY", DateTime.Now.ToString("yyyy") },
                    { "MM", DateTime.Now.ToString("MM") },
                    { "DD", DateTime.Now.ToString("dd") },
                    { "CLASS", "A" },
                    { "CNTBEG", contract },
                    { "CNTEND", contract }
                };

                // 2. Définition des variables spécifiques à ADDPRCT
                var addprctVariables = new Dictionary<string, string>
                {
                    { "STE", "A" },
                    { "CMDPMT", "6" },
                    { "AMOUNT", amount },
                    { "BUCP", bucp },
                    { "USERNAME", username }
                };

                // 3. Initialisation du service d'activation avec le bon chemin réseau
                string jclFolder = @"\\Jafile02\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\LVCHAIN\JCL";

                if (!Directory.Exists(jclFolder))
                {
                    throw new Exception($"Impossible d'accéder au dossier réseau contenant les JCLs :\n{jclFolder}\nVérifiez votre connexion au réseau de l'entreprise ou vos droits d'accès.");
                }

                // On instancie le service dans un bloc 'using' pour libérer les ressources réseau à la fin
                using (var activationService = new ActivationService(jclFolder))
                {
                    // 4. Exécution de la séquence
                    await activationService.RunActivationSequenceAsync(
                        generalVariables,
                        addprctVariables,
                        username,
                        password,
                        message =>
                        {
                            // Cette fonction est appelée depuis le service pour mettre à jour l'interface
                            Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = message);
                        },
                        CancellationToken.None // Token par défaut, à modifier si on ajoute un bouton "Annuler"
                    );
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = "Séquence d'activation terminée avec succès !";
                    MessageBox.Show("Les 5 Jobs ont été soumis et acceptés par le serveur.", "Activation Réussie", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }

        // =========================================================================
        // UTILITY METHODS
        // =========================================================================
        private async Task RunProcessAsync(Func<Task> action)
        {
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;

            // Désactiver les boutons pour éviter les doubles clics
            if (BtnRunSingle != null) BtnRunSingle.IsEnabled = false;
            if (BtnRunBatch != null) BtnRunBatch.IsEnabled = false;
            if (BtnRunActivation != null) BtnRunActivation.IsEnabled = false;
            if (BtnRunComparison != null) BtnRunComparison.IsEnabled = false;

            TxtStatus.Foreground = System.Windows.Media.Brushes.Blue;

            try
            {
                await action();
                LnkFile.Visibility = Visibility.Visible;
                TxtStatus.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                PrgLoading.Visibility = Visibility.Collapsed;

                // Réactiver les boutons
                if (BtnRunSingle != null) BtnRunSingle.IsEnabled = true;
                if (BtnRunBatch != null) BtnRunBatch.IsEnabled = true;
                if (BtnRunActivation != null) BtnRunActivation.IsEnabled = true;

                // Le bouton de comparaison dépend des champs de texte
                if (BtnRunComparison != null)
                {
                    BtnRunComparison.IsEnabled = !string.IsNullOrWhiteSpace(TxtBaseFile?.Text) &&
                                                 !string.IsNullOrWhiteSpace(TxtTargetFile?.Text);
                }
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_lastGeneratedPath)) Process.Start("explorer.exe", _lastGeneratedPath);
                else if (File.Exists(_lastGeneratedPath)) Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}