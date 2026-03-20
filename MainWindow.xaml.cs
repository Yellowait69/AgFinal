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
        // Shared variable to know which folder/file to open when clicking the hyperlink
        public string LastGeneratedPath { get; set; } = "";

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();

            // Set "Activation" as the default tab on startup
            BtnMenu_Click(BtnActivation, null);
        }

        private void InitializeDirectories()
        {
            try
            {
                if (!Directory.Exists(Settings.OutputDir)) Directory.CreateDirectory(Settings.OutputDir);
                if (!Directory.Exists(Settings.SnapshotDir)) Directory.CreateDirectory(Settings.SnapshotDir);
                if (!Directory.Exists(Settings.InputDir)) Directory.CreateDirectory(Settings.InputDir);

                // NEW: Create Baseline directory for the Smart Matcher feature
                if (!Directory.Exists(Settings.BaselineDir)) Directory.CreateDirectory(Settings.BaselineDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[ERROR] Failed to create directories: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- NEW: Method to route user to the exact Help tab they need ---
        public void OpenHelpTargetingTab(int tabIndex)
        {
            // 1. Simulate a click on the sidebar "Help" button to switch the UI view
            BtnMenu_Click(BtnHelp, null);

            // 2. Instruct the HelpView to switch to the correct internal tab
            ViewHelp.SelectTab(tabIndex);
        }

        // --- SIDEBAR MENU NAVIGATION ---
        private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            // 1. Hide all views
            if (ViewActivation != null) ViewActivation.Visibility = Visibility.Collapsed;
            if (ViewExtraction != null) ViewExtraction.Visibility = Visibility.Collapsed;
            if (ViewComparison != null) ViewComparison.Visibility = Visibility.Collapsed;
            if (ViewHelp != null) ViewHelp.Visibility = Visibility.Collapsed;

            // 2. Reset colors for all menu buttons to the default dark blue
            var inactiveColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#34495E");
            if (BtnActivation != null) BtnActivation.Background = inactiveColor;
            if (BtnExtraction != null) BtnExtraction.Background = inactiveColor;
            if (BtnComparison != null) BtnComparison.Background = inactiveColor;
            if (BtnHelp != null) BtnHelp.Background = inactiveColor;

            // 3. Set the active theme colors
            var activeColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#1ABC9C"); // Green/Teal
            var helpColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#8E44AD");   // Purple

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

        // --- GLOBAL ASYNC ENGINE (Manages progress bar for all views) ---
        // This method is public so the specific Views (UserControls) can call it.
        public async Task RunProcessAsync(Func<Task> action)
        {
            // Show loading bar and hide link
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                // Execute the requested async task
                await action();

                // On success
                LnkFile.Visibility = Visibility.Visible;
                TxtStatus.Foreground = Brushes.Green;
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Operation canceled by user.";
                TxtStatus.Foreground = Brushes.DarkOrange;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Critical Error: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
                MessageBox.Show(ex.Message, "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LnkFile.Visibility = Visibility.Visible;
            }
            finally
            {
                // Hide loading bar when done
                PrgLoading.Visibility = Visibility.Collapsed;
            }
        }

        // --- GLOBAL HYPERLINK CLICK HANDLER ---
        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(LastGeneratedPath))
                {
                    // Open folder
                    Process.Start("explorer.exe", LastGeneratedPath);
                }
                else if (File.Exists(LastGeneratedPath))
                {
                    // Open folder AND select the specific generated file
                    Process.Start("explorer.exe", $"/select,\"{LastGeneratedPath}\"");
                }
                else
                {
                    MessageBox.Show("File or folder not found. It might have been moved or deleted.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}