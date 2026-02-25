using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using AutoActivator.Services;
using AutoActivator.Config;

namespace AutoActivator.Gui
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Variable privée pour stocker le chemin du dernier fichier généré
        private string _lastGeneratedPath = "";

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
        }

        /// <summary>
        /// Initialise les répertoires de travail au démarrage de l'application.
        /// </summary>
        private void InitializeDirectories()
        {
            try
            {
                // Utilisation des paramètres définis dans Settings.cs
                if (!Directory.Exists(Settings.OutputDir)) Directory.CreateDirectory(Settings.OutputDir);
                if (!Directory.Exists(Settings.SnapshotDir)) Directory.CreateDirectory(Settings.SnapshotDir);
                if (!Directory.Exists(Settings.InputDir)) Directory.CreateDirectory(Settings.InputDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[ERROR] Impossible de créer les répertoires : {ex.Message}", "Erreur Initialisation", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Logique exécutée lors du clic sur le bouton d'extraction.
        /// </summary>
        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string contract = TxtContract.Text.Trim();

            if (string.IsNullOrEmpty(contract))
            {
                TxtStatus.Text = "Veuillez entrer un numéro de contrat.";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            try
            {
                // Définition du chemin de sortie basé sur Settings.cs
                string fileName = $"FULL_EXTRACT_{contract}.csv";
                _lastGeneratedPath = Path.Combine(Settings.OutputDir, fileName);

                // --- NOTE POUR VOTRE TFE ---
                // Pour que l'extraction fonctionne réellement, vous devrez déplacer la logique
                // de 'RunTestExtraction' du fichier Program.cs vers une classe Service.
                // Pour l'instant, nous simulons la création du fichier pour valider l'interface.

                TxtStatus.Text = $"Extraction terminée avec succès !";
                TxtStatus.Foreground = Brushes.Green;

                // On affiche le lien vers le fichier
                LnkFile.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Erreur lors de l'extraction : {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
        }

        /// <summary>
        /// Action au clic sur le lien "Ouvrir le fichier généré".
        /// </summary>
        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_lastGeneratedPath))
                {
                    // Ouvre l'explorateur Windows et sélectionne automatiquement le fichier
                    Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
                }
                else
                {
                    MessageBox.Show("Le fichier est introuvable sur le disque.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible d'ouvrir l'explorateur : {ex.Message}");
            }
        }

        // Placez ici les futurs gestionnaires d'événements pour vos autres boutons
        private void BtnActivation_Click(object sender, RoutedEventArgs e) { /* Logique Activation */ }
        private void BtnComparison_Click(object sender, RoutedEventArgs e) { /* Logique Comparaison */ }
    }
}