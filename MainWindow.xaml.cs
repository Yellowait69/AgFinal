using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
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

            // Définir "Activation" comme onglet par défaut au lancement
            BtnActivation_Click(null, null);
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

        // --- GESTION DES COULEURS DES ONGLETS ---
        private void SetActiveTabColor(System.Windows.Controls.Button activeButton)
        {
            var inactiveColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#34495E");
            var activeColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#1ABC9C");

            // Remettre tous les boutons en bleu foncé
            if (BtnActivation != null) BtnActivation.Background = inactiveColor;
            if (BtnExtraction != null) BtnExtraction.Background = inactiveColor;
            if (BtnComparison != null) BtnComparison.Background = inactiveColor;

            // Mettre le bouton actif en vert/turquoise
            if (activeButton != null) activeButton.Background = activeColor;
        }

        private void BtnExtraction_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Visible;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Collapsed;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Collapsed;

            SetActiveTabColor(BtnExtraction);
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Collapsed;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Collapsed;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Visible;

            SetActiveTabColor(BtnActivation);
        }

        private void BtnComparison_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Collapsed;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Visible;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Collapsed;

            SetActiveTabColor(BtnComparison);
        }

        // --- MOTEUR ASYNCHRONE PRINCIPAL ---
        private async Task RunProcessAsync(Func<Task> action)
        {
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;

            // Désactivation des boutons pendant le traitement
            if (BtnRunSingle != null) BtnRunSingle.IsEnabled = false;
            if (BtnRunBatch != null) BtnRunBatch.IsEnabled = false;
            if (BtnRunSingleAct != null) BtnRunSingleAct.IsEnabled = false;
            if (BtnRunBatchAct != null) BtnRunBatchAct.IsEnabled = false;
            if (BtnRunComparison != null) BtnRunComparison.IsEnabled = false;

            if (BtnCancelSingleAct != null) BtnCancelSingleAct.IsEnabled = true;
            if (BtnCancelBatchAct != null) BtnCancelBatchAct.IsEnabled = true;

            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                await action();
                LnkFile.Visibility = Visibility.Visible;
                TxtStatus.Foreground = Brushes.Green;
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Opération annulée par l'utilisateur.";
                TxtStatus.Foreground = Brushes.DarkOrange;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Erreur critique: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
                MessageBox.Show(ex.Message, "Erreur d'exécution", MessageBoxButton.OK, MessageBoxImage.Error);
                LnkFile.Visibility = Visibility.Visible;
            }
            finally
            {
                PrgLoading.Visibility = Visibility.Collapsed;

                // Réactivation des boutons
                if (BtnRunSingle != null) BtnRunSingle.IsEnabled = true;
                if (BtnRunBatch != null) BtnRunBatch.IsEnabled = true;
                if (BtnRunSingleAct != null) BtnRunSingleAct.IsEnabled = true;
                if (BtnRunBatchAct != null) BtnRunBatchAct.IsEnabled = true;

                if (BtnCancelSingleAct != null) BtnCancelSingleAct.IsEnabled = false;
                if (BtnCancelBatchAct != null) BtnCancelBatchAct.IsEnabled = false;

                if (BtnRunComparison != null)
                {
                    BtnRunComparison.IsEnabled = !string.IsNullOrWhiteSpace(TxtBaseFile?.Text) && !string.IsNullOrWhiteSpace(TxtTargetFile?.Text);
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