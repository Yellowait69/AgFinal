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
        private CancellationTokenSource _cts;

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

        private async void BtnRunActivation_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();

            await RunProcessAsync(async () =>
            {
                Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = "Préparation de l'activation (JCLs)...");

                string envValue = "D";
                string contract = "", cus = "", amount = "", bucp = "";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CmbEnvironment.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
                    {
                        envValue = selectedItem.Tag?.ToString() ?? "D";
                    }

                    contract = TxtActContract.Text.Trim();
                    cus = TxtActCus.Text.Trim();
                    amount = TxtActAmount.Text.Trim().PadLeft(10, '0');
                    bucp = TxtActBucp.Text.Trim().PadLeft(5, '0');
                });

                string username = Settings.DbConfig.Uid;
                string password = Settings.DbConfig.Pwd;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    throw new Exception("Les identifiants (Uid/Pwd) ne sont pas configurés. Veuillez vous reconnecter.");
                }

                string q2 = "Q2T";
                string fastCtrl = "10T.DB.CA.FIB.FASTCTRL";


                string envImsValue = "T";

                switch (envValue)
                {
                    case "D":
                        q2 = "Q2T";
                        fastCtrl = "I0T.DB.CA.FIB.FASTCTRL";
                        envImsValue = "T";
                        break;

                    case "Q":
                        q2 = "Q2C";
                        fastCtrl = "I10.DB.CA.FIB.FASTCTRL";
                        envImsValue = "C";
                        break;
                }

                var generalVariables = new Dictionary<string, string>
                {
                    { "ENVIMS", envImsValue },
                    { "CUS", cus },
                    { "YYMMDD", DateTime.Now.ToString("yyMMdd") },
                    { "YYYY", DateTime.Now.ToString("yyyy") },
                    { "MM", DateTime.Now.ToString("MM") },
                    { "DD", DateTime.Now.ToString("dd") },
                    { "CLASS", "A" },
                    { "CNTBEG", contract },
                    { "CNTEND", contract },
                    { "MMDD", DateTime.Now.ToString("MMdd") },
                    { "CYMD", DateTime.Now.ToString("yyyyMMdd") },
                    { "STE", "A" },
                    { "Q2", q2 },
                    { "CM.", "     " },
                    { "DRUN", DateTime.Now.ToString("yyyyMMdd") },
                    { "NREMB", "20" },
                    { "CONTR-EX", "Y" },
                    { "CONTR-RE", "Y" },
                    { "CONTR-UN", "Y" },
                    { "NJJART72", "5" },
                    { "FASTCTRL", fastCtrl }
                };

                var addprctVariables = new Dictionary<string, string>
                {
                    { "CMDPMT", "6" },
                    { "AMOUNT", amount },
                    { "BUCP", bucp },
                    { "USERNAME", username }
                };

                string jclFolder = @"\\Jafile02\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\LVCHAIN\JCL";

                if (!Directory.Exists(jclFolder))
                {
                    throw new Exception($"Impossible d'accéder au dossier réseau contenant les JCLs :\n{jclFolder}\nVérifiez votre connexion au réseau de l'entreprise ou vos droits d'accès.");
                }

                var activationOrchestrator = new ActivationOrchestrator(jclFolder);

                await activationOrchestrator.RunActivationSequenceAsync(
                    generalVariables,
                    addprctVariables,
                    username,
                    password,
                    message =>
                    {
                        Application.Current.Dispatcher.Invoke(() => TxtStatus.Text = message);
                    },
                    _cts.Token
                );

                Application.Current.Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = "Séquence d'activation terminée avec succès !";
                    MessageBox.Show("Les 5 Jobs ont été soumis et acceptés par le serveur sans erreur métier.", "Activation Réussie", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }

        private void BtnCancelActivation_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                TxtStatus.Text = "Annulation en cours... La séquence va s'arrêter.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
        }

        private async Task RunProcessAsync(Func<Task> action)
        {
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;

            if (BtnRunSingle != null) BtnRunSingle.IsEnabled = false;
            if (BtnRunBatch != null) BtnRunBatch.IsEnabled = false;
            if (BtnRunActivation != null) BtnRunActivation.IsEnabled = false;
            if (BtnRunComparison != null) BtnRunComparison.IsEnabled = false;

            if (BtnCancelActivation != null) BtnCancelActivation.IsEnabled = true;

            TxtStatus.Foreground = System.Windows.Media.Brushes.Blue;

            try
            {
                await action();
                LnkFile.Visibility = Visibility.Visible;
                TxtStatus.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Opération annulée par l'utilisateur.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Erreur: {ex.Message}";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
                MessageBox.Show(ex.Message, "Erreur d'exécution", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                PrgLoading.Visibility = Visibility.Collapsed;

                if (BtnRunSingle != null) BtnRunSingle.IsEnabled = true;
                if (BtnRunBatch != null) BtnRunBatch.IsEnabled = true;
                if (BtnRunActivation != null) BtnRunActivation.IsEnabled = true;

                if (BtnCancelActivation != null) BtnCancelActivation.IsEnabled = false;

                if (BtnRunComparison != null)
                {
                    BtnRunComparison.IsEnabled =
                        !string.IsNullOrWhiteSpace(TxtBaseFile?.Text) &&
                        !string.IsNullOrWhiteSpace(TxtTargetFile?.Text);
                }
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_lastGeneratedPath))
                    Process.Start("explorer.exe", _lastGeneratedPath);
                else if (File.Exists(_lastGeneratedPath))
                    Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}