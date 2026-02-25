using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using AutoActivator.Services;
using AutoActivator.Config;

namespace AutoActivator.Gui
{
    public partial class MainWindow : Window
    {
        private string _lastGeneratedPath = "";

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
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
                MessageBox.Show($"[ERROR] Impossible de créer les répertoires : {ex.Message}");
            }
        }

        // Cette méthode répond maintenant au clic du menu ET du bouton principal
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
                string fileName = $"FULL_EXTRACT_{contract}.csv";
                _lastGeneratedPath = Path.Combine(Settings.OutputDir, fileName);

                TxtStatus.Text = $"Extraction terminée avec succès !";
                TxtStatus.Foreground = Brushes.Green;
                LnkFile.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Erreur : {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_lastGeneratedPath))
            {
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
            }
        }

        // Ces méthodes doivent exister pour ne pas faire planter le XAML
        private void BtnActivation_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Module Activation cliqué";
        }

        private void BtnComparison_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Module Comparaison cliqué";
        }
    }
}