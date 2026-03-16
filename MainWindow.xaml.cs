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
        // Variable partagée entre toutes les classes partielles (.Extraction.cs, .Comparison.cs, etc.)
        private string _lastGeneratedPath = "";

        private readonly ExtractionService _extractionService;
        private CancellationTokenSource _cts;

        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();

            // Lier l'historique visuel (DataGrid) à notre collection
            ListHistory.ItemsSource = ExtractionHistory;

            // Trier automatiquement l'historique d'extraction par Environnement (Product)
            ICollectionView view = CollectionViewSource.GetDefaultView(ExtractionHistory);
            view.SortDescriptions.Add(new SortDescription("Product", ListSortDirection.Ascending));

            _extractionService = new ExtractionService();

            // Définir "Activation" comme l'onglet par défaut au démarrage
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

        // --- GESTION DES COULEURS ET DE LA NAVIGATION DES ONGLETS ---
        private void SetActiveTabColor(System.Windows.Controls.Button activeButton)
        {
            var inactiveColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#34495E");
            var activeColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#1ABC9C");
            var helpColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#8E44AD");

            // Réinitialiser tous les boutons à leur couleur "inactive" (Bleu foncé)
            if (BtnActivation != null) BtnActivation.Background = inactiveColor;
            if (BtnExtraction != null) BtnExtraction.Background = inactiveColor;
            if (BtnComparison != null) BtnComparison.Background = inactiveColor;
            if (BtnHelp != null) BtnHelp.Background = inactiveColor;

            // Appliquer la couleur "active" au bouton sélectionné
            if (activeButton != null)
            {
                if (activeButton.Name == "BtnHelp")
                    activeButton.Background = helpColor; // Violet pour l'aide
                else
                    activeButton.Background = activeColor; // Turquoise pour le reste
            }
        }

        private void BtnExtraction_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Visible;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Collapsed;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Collapsed;
            if (GridHelp != null) GridHelp.Visibility = Visibility.Collapsed;

            SetActiveTabColor(BtnExtraction);
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Collapsed;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Collapsed;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Visible;
            if (GridHelp != null) GridHelp.Visibility = Visibility.Collapsed;

            SetActiveTabColor(BtnActivation);
        }

        private void BtnComparison_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Collapsed;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Visible;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Collapsed;
            if (GridHelp != null) GridHelp.Visibility = Visibility.Collapsed;

            SetActiveTabColor(BtnComparison);
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            if (GridExtraction != null) GridExtraction.Visibility = Visibility.Collapsed;
            if (GridComparison != null) GridComparison.Visibility = Visibility.Collapsed;
            if (GridActivation != null) GridActivation.Visibility = Visibility.Collapsed;
            if (GridHelp != null) GridHelp.Visibility = Visibility.Visible;

            SetActiveTabColor(BtnHelp);
        }

        // --- MOTEUR ASYNCHRONE PRINCIPAL (Gère l'UI pendant les chargements) ---
        private async Task RunProcessAsync(Func<Task> action)
        {
            // Afficher la barre de chargement et cacher le lien du précédent fichier
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;

            // Désactiver tous les boutons d'action pour empêcher l'utilisateur de lancer 2 processus en même temps
            if (BtnRunSingle != null) BtnRunSingle.IsEnabled = false;
            if (BtnRunBatch != null) BtnRunBatch.IsEnabled = false;
            if (BtnRunSingleAct != null) BtnRunSingleAct.IsEnabled = false;
            if (BtnRunBatchAct != null) BtnRunBatchAct.IsEnabled = false;
            if (BtnRunComparison != null) BtnRunComparison.IsEnabled = false;
            if (BtnOpenComparisonFolder != null) BtnOpenComparisonFolder.IsEnabled = false;

            // Activer les boutons d'annulation (si l'utilisateur veut stopper le Batch)
            if (BtnCancelSingleAct != null) BtnCancelSingleAct.IsEnabled = true;
            if (BtnCancelBatchAct != null) BtnCancelBatchAct.IsEnabled = true;

            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                // Exécuter l'action asynchrone (Extraction, Comparaison ou Activation)
                await action();

                // Si réussi, afficher le lien et passer le texte en vert
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
                TxtStatus.Text = $"Erreur critique : {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
                MessageBox.Show(ex.Message, "Erreur d'exécution", MessageBoxButton.OK, MessageBoxImage.Error);
                LnkFile.Visibility = Visibility.Visible;
            }
            finally
            {
                // Cacher la barre de chargement une fois l'opération terminée
                PrgLoading.Visibility = Visibility.Collapsed;

                // Réactiver les boutons
                if (BtnRunSingle != null) BtnRunSingle.IsEnabled = true;
                if (BtnRunBatch != null) BtnRunBatch.IsEnabled = true;
                if (BtnRunSingleAct != null) BtnRunSingleAct.IsEnabled = true;
                if (BtnRunBatchAct != null) BtnRunBatchAct.IsEnabled = true;
                if (BtnOpenComparisonFolder != null) BtnOpenComparisonFolder.IsEnabled = true;

                if (BtnCancelSingleAct != null) BtnCancelSingleAct.IsEnabled = false;
                if (BtnCancelBatchAct != null) BtnCancelBatchAct.IsEnabled = false;

                // Le bouton de comparaison ne se réactive que si les deux fichiers sont toujours bien renseignés
                if (BtnRunComparison != null)
                {
                    BtnRunComparison.IsEnabled = !string.IsNullOrWhiteSpace(TxtBaseFile?.Text) && !string.IsNullOrWhiteSpace(TxtTargetFile?.Text);
                }
            }
        }

        // --- GESTION DU LIEN CLIQUABLE (Hyperlink) ---
        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_lastGeneratedPath))
                {
                    // Ouvre simplement le dossier
                    Process.Start("explorer.exe", _lastGeneratedPath);
                }
                else if (File.Exists(_lastGeneratedPath))
                {
                    // Ouvre le dossier ET sélectionne le fichier spécifique
                    Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
                }
                else
                {
                    MessageBox.Show("Le fichier ou le dossier est introuvable. Il a peut-être été déplacé.", "Introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture du chemin : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}