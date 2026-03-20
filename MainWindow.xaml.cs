using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoActivator.Config;

namespace AutoActivator.Gui
{
    public partial class MainWindow : Window
    {
        // Variable partagée pour savoir quel dossier ouvrir quand on clique sur le lien
        public string LastGeneratedPath { get; set; } = "";

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();

            // Définir "Activation" comme l'onglet par défaut au démarrage
            BtnMenu_Click(BtnActivation, null);
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

        // --- GESTION DU MENU ET DE LA NAVIGATION ---
        private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            // 1. Cacher toutes les vues
            if (ViewActivation != null) ViewActivation.Visibility = Visibility.Collapsed;
            if (ViewExtraction != null) ViewExtraction.Visibility = Visibility.Collapsed;
            if (ViewComparison != null) ViewComparison.Visibility = Visibility.Collapsed;
            if (ViewHelp != null) ViewHelp.Visibility = Visibility.Collapsed;

            // 2. Réinitialiser les couleurs de tous les boutons du menu
            var inactiveColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#34495E");
            if (BtnActivation != null) BtnActivation.Background = inactiveColor;
            if (BtnExtraction != null) BtnExtraction.Background = inactiveColor;
            if (BtnComparison != null) BtnComparison.Background = inactiveColor;
            if (BtnHelp != null) BtnHelp.Background = inactiveColor;

            // 3. Afficher la vue demandée et colorer le bouton
            var activeColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#1ABC9C");
            var helpColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#8E44AD");

            var clickedButton = sender as System.Windows.Controls.Button;

            if (clickedButton == BtnActivation)
            {
                ViewActivation.Visibility = Visibility.Visible;
                BtnActivation.Background = activeColor;
            }
            else if (clickedButton == BtnExtraction)
            {
                ViewExtraction.Visibility = Visibility.Visible;
                BtnExtraction.Background = activeColor;
            }
            else if (clickedButton == BtnComparison)
            {
                ViewComparison.Visibility = Visibility.Visible;
                BtnComparison.Background = activeColor;
            }
            else if (clickedButton == BtnHelp)
            {
                ViewHelp.Visibility = Visibility.Visible;
                BtnHelp.Background = helpColor;
            }
        }

        // --- MOTEUR ASYNCHRONE GLOBAL (Gère la barre de chargement pour toutes les vues) ---
        // Cette méthode est "public" pour que les Vues (UserControls) puissent l'appeler.
        public async Task RunProcessAsync(Func<Task> action)
        {
            // Afficher la barre de chargement et cacher le lien
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                // Exécuter l'action asynchrone (demandée par la Vue)
                await action();

                // Si réussi
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
            }
        }

        // --- GESTION DU LIEN CLIQUABLE EN BAS DE LA FENETRE ---
        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(LastGeneratedPath))
                {
                    // Ouvre le dossier
                    Process.Start("explorer.exe", LastGeneratedPath);
                }
                else if (File.Exists(LastGeneratedPath))
                {
                    // Ouvre le dossier ET sélectionne le fichier spécifique
                    Process.Start("explorer.exe", $"/select,\"{LastGeneratedPath}\"");
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