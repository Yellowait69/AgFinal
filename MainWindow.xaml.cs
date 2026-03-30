using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoActivator.Config;

namespace AutoActivator.Gui
{
    public partial class MainWindow : Window
    {
        public string LastGeneratedPath { get; set; } = "";

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();

            BtnMenu_Click(BtnActivation, null);
        }

        /// <summary>
        /// Ensures all required local directories exist before the application starts writing files.
        /// </summary>
        private void InitializeDirectories()
        {
            try
            {
                if (!Directory.Exists(Settings.OutputDir)) Directory.CreateDirectory(Settings.OutputDir);
                if (!Directory.Exists(Settings.SnapshotDir)) Directory.CreateDirectory(Settings.SnapshotDir);
                if (!Directory.Exists(Settings.InputDir)) Directory.CreateDirectory(Settings.InputDir);

                if (!Directory.Exists(Settings.BaselineDir)) Directory.CreateDirectory(Settings.BaselineDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[ERROR] Failed to create directories: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Routes the user to the Help view and opens a specific internal tab.
        /// Called by the "?" buttons located in the individual views.
        /// </summary>
        public void OpenHelpTargetingTab(int tabIndex)
        {
            BtnMenu_Click(BtnHelp, null);

            ViewHelp.SelectTab(tabIndex);
        }

        private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            if (ViewActivation != null) ViewActivation.Visibility = Visibility.Collapsed;
            if (ViewExtraction != null) ViewExtraction.Visibility = Visibility.Collapsed;
            if (ViewComparison != null) ViewComparison.Visibility = Visibility.Collapsed;
            if (ViewHelp != null) ViewHelp.Visibility = Visibility.Collapsed;

            var inactiveColor = (SolidColorBrush)new BrushConverter().ConvertFrom("#34495E");
            if (BtnActivation != null) BtnActivation.Background = inactiveColor;
            if (BtnExtraction != null) BtnExtraction.Background = inactiveColor;
            if (BtnComparison != null) BtnComparison.Background = inactiveColor;
            if (BtnHelp != null) BtnHelp.Background = inactiveColor;

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

        /// <summary>
        /// Centralized wrapper for executing long-running asynchronous tasks from the sub-views.
        /// It manages the global loading bar, the status text colors, and catches unhandled exceptions safely.
        /// </summary>
        public async Task RunProcessAsync(Func<Task> action)
        {
            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                await action();

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
                PrgLoading.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Converts an Excel file to a CSV file using OLEDB.
        /// This is an alternative to Interop that runs faster but requires the Microsoft ACE OLEDB drivers installed on the machine.
        /// </summary>
        public string PrepareCsvFromExcel(string excelFilePath, string environmentSuffix)
        {
            string outputCsvPath = Path.Combine(Settings.InputDir,
                $"ConvertedBatch_{environmentSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            string connString;
            if (excelFilePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                connString = $"Provider=Microsoft.Jet.OLEDB.4.0;Data Source=\"{excelFilePath}\";Extended Properties=\"Excel 8.0;HDR=YES;IMEX=1\";";
            else
                connString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=\"{excelFilePath}\";Extended Properties=\"Excel 12.0 Xml;HDR=YES;IMEX=1\";";

            try
            {
                using (var conn = new System.Data.OleDb.OleDbConnection(connString))
                {
                    conn.Open();

                    var schemaTable = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Tables, null);
                    if (schemaTable == null || schemaTable.Rows.Count == 0)
                        throw new Exception("No sheets found in the Excel file.");

                    string sheetName = schemaTable.Rows[0]["TABLE_NAME"].ToString();

                    using (var cmd = new System.Data.OleDb.OleDbCommand($"SELECT * FROM [{sheetName}]", conn))
                    using (var reader = cmd.ExecuteReader())
                    using (var writer = new StreamWriter(outputCsvPath, false, Encoding.UTF8))
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            writer.Write(reader.GetName(i));
                            if (i < reader.FieldCount - 1) writer.Write(";");
                        }
                        writer.WriteLine();

                        while (reader.Read())
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string val = reader[i]?.ToString() ?? "";

                                if (val.Contains(";") || val.Contains("\"") || val.Contains("\n") || val.Contains("\r"))
                                {
                                    val = $"\"{val.Replace("\"", "\"\"")}\"";
                                }

                                writer.Write(val);
                                if (i < reader.FieldCount - 1) writer.Write(";");
                            }
                            writer.WriteLine();
                        }
                    }
                }
                return outputCsvPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to convert Excel to CSV. Technical error: {ex.Message}");
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(LastGeneratedPath))
                {
                    Process.Start("explorer.exe", LastGeneratedPath);
                }
                else if (File.Exists(LastGeneratedPath))
                {
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