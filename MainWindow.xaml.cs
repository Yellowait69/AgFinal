using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32; // Ajouté pour OpenFileDialog
using AutoActivator.Services;
using AutoActivator.Config;
using AutoActivator.Sql;

namespace AutoActivator.Gui
{
    public class ExtractionItem
    {
        public string ContractId { get; set; }
        public string Time { get; set; }
        public string FilePath { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string _lastGeneratedPath = "";
        public ObservableCollection<ExtractionItem> ExtractionHistory { get; set; } = new ObservableCollection<ExtractionItem>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirectories();
            ListHistory.ItemsSource = ExtractionHistory;
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
                MessageBox.Show($"[ERROR] Directories: {ex.Message}");
            }
        }

        // Événement pour vider le champ fichier si on tape un contrat manuellement
        private void TxtContract_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtFilePath != null && !string.IsNullOrEmpty(TxtContract.Text))
            {
                TxtFilePath.Text = string.Empty;
            }
        }

        // Événement du bouton Parcourir pour sélectionner le fichier CSV
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Fichiers CSV|*.csv",
                Title = "Sélectionnez un fichier CSV contenant les contrats"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtFilePath.Text = openFileDialog.FileName;
                TxtContract.Text = string.Empty; // On vide le champ unique si un fichier est choisi
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string singleInput = TxtContract.Text.Trim();
            string fileInput = TxtFilePath?.Text.Trim();

            if (string.IsNullOrEmpty(singleInput) && string.IsNullOrEmpty(fileInput))
            {
                TxtStatus.Text = "Please enter a contract number or select a CSV file.";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;
            BtnRun.IsEnabled = false;
            if (BtnBrowse != null) BtnBrowse.IsEnabled = false;

            TxtStatus.Text = string.IsNullOrEmpty(fileInput) ? "Extraction in progress from LISA and ELIA..." : "Batch extraction in progress...";
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                if (!string.IsNullOrEmpty(fileInput))
                {
                    // Lancement du mode Batch (fichier CSV)
                    await Task.Run(() => PerformBatchExtraction(fileInput));
                    TxtStatus.Text = "Batch extraction completed!";
                    TxtStatus.Foreground = Brushes.Green;
                }
                else
                {
                    // Lancement du mode Contrat Unique (logique d'origine)
                    string resultInfo = await Task.Run(() => PerformExtractionLogic(singleInput));

                    TxtStatus.Text = $"Completed! {resultInfo}";
                    TxtStatus.Foreground = Brushes.Green;
                    LnkFile.Visibility = Visibility.Visible;

                    ExtractionHistory.Insert(0, new ExtractionItem
                    {
                        ContractId = singleInput,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        FilePath = _lastGeneratedPath
                    });
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
            finally
            {
                PrgLoading.Visibility = Visibility.Collapsed;
                BtnRun.IsEnabled = true;
                if (BtnBrowse != null) BtnBrowse.IsEnabled = true;
            }
        }

        // Nouvelle méthode d'extraction par lots via fichier CSV
        private void PerformBatchExtraction(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);

            // On commence à i = 1 pour ignorer la ligne d'en-tête (s'il y en a une)
            // Si votre fichier CSV n'a pas d'en-tête, changez "int i = 1" en "int i = 0"
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // On sépare par point-virgule ou virgule
                string[] columns = line.Split(new[] { ';', ',' });

                if (columns.Length >= 1)
                {
                    string contractNumber = columns[0].Trim();
                    // string premiumAmount = columns.Length > 1 ? columns[1].Trim() : "0"; // Utile plus tard pour les primes

                    if (!string.IsNullOrEmpty(contractNumber))
                    {
                        try
                        {
                            PerformExtractionLogic(contractNumber);

                            // Mise à jour de l'historique sur le thread UI principal
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ExtractionHistory.Insert(0, new ExtractionItem
                                {
                                    ContractId = contractNumber,
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
                                    FilePath = _lastGeneratedPath
                                });
                            });
                        }
                        catch (Exception)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ExtractionHistory.Insert(0, new ExtractionItem
                                {
                                    ContractId = $"{contractNumber} (FAILED)",
                                    Time = DateTime.Now.ToString("HH:mm:ss"),
                                    FilePath = string.Empty
                                });
                            });
                        }
                    }
                }
            }
        }

        private string PerformExtractionLogic(string targetContract)
        {
            targetContract = targetContract.Replace("\u00A0", "").Trim();
            var db = new DatabaseManager();

            if (!db.TestConnection())
                throw new Exception("Unable to establish SQL connection.");

            var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };

            // Initial IDs retrieval via optimized queries in SqlQueries.cs
            var dtLisa = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contract {targetContract} not found.");

            StringBuilder sb = new StringBuilder();
            _lastGeneratedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

            string eliaUconId = "Not found";
            string eliaDemandId = "Not found";
            string lisaId = "Not found";

            // --- LISA SECTION ---
            if (dtLisa.Rows.Count > 0)
            {
                // Assigning NO_CNT (LISA ID) for final display
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                lisaId = internalId.ToString();

                // Enriched list with LISA tables identified in the SQL script (PCONT0, ELIHT0, etc.)
                var lisaTables = new[] {
                    "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0",
                    "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0",
                    "LV.MWBGT0", "LV.PRIST0", "LV.FMVGT0", "LV.ELIAT0",
                    "LV.ELIHT0", "LV.PCONT0", "LV.XRSTT0"
                };

                foreach (var table in lisaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", internalId } });
                        AddTableToBuffer(sb, table, dt);
                    }
                }
            }

            // --- ELIA SECTION ---
            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"].ToString().Trim();

                // Retrieving Demand ID (HDMDAIDN) to link secondary FJ1 tables
                var dtDemand = db.GetData(SqlQueries.Queries["GET_ELIA_DEMAND_ID"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });

                if (dtDemand.Rows.Count > 0)
                {
                    eliaDemandId = dtDemand.Rows[0]["IT5HDMDAIDN"].ToString().Trim();
                }

                // ELIA tables based on contract ID (UCONAIDN)
                var eliaTables = new[] {
                    "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UPRP",
                    "FJ1.TB5UAVE", "FJ1.TB5UDCR", "FJ1.TB5UBEN", "FJ1.TB5UPRS",
                    "FJ1.TB5URPP", "FJ1.TB5HELT", "FJ1.TB5UCCR", "FJ1.TB5UPNR"
                };

                foreach (var table in eliaTables)
                {
                    if (SqlQueries.Queries.ContainsKey(table))
                    {
                        var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                        AddTableToBuffer(sb, table, dt);
                    }
                }

                // ELIA tables based on demand ID (DemandId)
                if (eliaDemandId != "Not found")
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HPRO", "FJ1.TB5HDIC", "FJ1.TB5HEPT", "FJ1.TB5HDGM", "FJ1.TB5HDGD" };
                    foreach (var table in demandTables)
                    {
                        if (SqlQueries.Queries.ContainsKey(table))
                        {
                            var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@DemandId", eliaDemandId } });
                            AddTableToBuffer(sb, table, dt);
                        }
                    }
                }
            }

            File.WriteAllText(_lastGeneratedPath, sb.ToString(), Encoding.UTF8);

            // Returns the complete status with the technical identifiers found
            return $"UCONAIDN: {eliaUconId} | HDMDAIDN: {eliaDemandId}";
        }

        private void AddTableToBuffer(StringBuilder sb, string tableName, DataTable dt)
        {
            sb.AppendLine("################################################################################");
            sb.AppendLine($"### TABLE : {tableName} | Rows : {dt.Rows.Count}");
            sb.AppendLine("################################################################################");

            if (dt.Rows.Count > 0)
            {
                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                sb.AppendLine(string.Join(";", columns));

                foreach (DataRow row in dt.Rows)
                {
                    var fields = row.ItemArray.Select(f => f?.ToString().Replace(";", " ").Replace("\n", " ").Trim());
                    sb.AppendLine(string.Join(";", fields));
                }
            }
            else { sb.AppendLine("NO DATA FOUND"); }
            sb.AppendLine();
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_lastGeneratedPath))
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Activation Module selected."; }
        private void BtnComparison_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Comparison Module selected."; }
    }
}