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
using System.Windows.Media;
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
                MessageBox.Show($"[ERROR] Répertoires : {ex.Message}");
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtContract.Text.Trim();

            if (string.IsNullOrEmpty(input))
            {
                TxtStatus.Text = "Veuillez entrer un numéro de contrat.";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            PrgLoading.Visibility = Visibility.Visible;
            LnkFile.Visibility = Visibility.Collapsed;
            BtnRun.IsEnabled = false;
            TxtStatus.Text = "Extraction en cours depuis LISA et ELIA...";
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                string resultInfo = await Task.Run(() => PerformExtractionLogic(input));

                TxtStatus.Text = $"Terminé ! {resultInfo}";
                TxtStatus.Foreground = Brushes.Green;
                LnkFile.Visibility = Visibility.Visible;

                ExtractionHistory.Insert(0, new ExtractionItem
                {
                    ContractId = input,
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    FilePath = _lastGeneratedPath
                });
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Erreur : {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
            finally
            {
                PrgLoading.Visibility = Visibility.Collapsed;
                BtnRun.IsEnabled = true;
            }
        }

        private string PerformExtractionLogic(string targetContract)
        {
            targetContract = targetContract.Replace("\u00A0", "");
            var db = new DatabaseManager();

            if (!db.TestConnection())
                throw new Exception("Connexion SQL impossible.");

            var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };

            // Récupération des IDs initiaux
            var dtLisa = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
            var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

            if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                throw new Exception($"Contrat {targetContract} introuvable.");

            StringBuilder sb = new StringBuilder();
            _lastGeneratedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

            // Variables pour l'affichage des IDs ELIA demandés
            string eliaUconId = "Non trouvé";
            string eliaDemandId = "Non trouvé";
            string lisaId = "Non trouvé";

            // --- SECTION LISA ---
            if (dtLisa.Rows.Count > 0)
            {
                long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                lisaId = internalId.ToString();

                var lisaTables = new[] {
                    "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0",
                    "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0",
                    "LV.MWBGT0", "LV.PRIST0", "LV.FMVGT0", "LV.ELIAT0"
                };

                foreach (var table in lisaTables)
                {
                    var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", internalId } });
                    AddTableToBuffer(sb, table, dt);
                }
            }

            // --- SECTION ELIA ---
            if (dtElia.Rows.Count > 0)
            {
                eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"].ToString();

                // 1. Récupération du Demand ID pour les tables liées via la table de lien HEPT ou HELT
                var dtDemand = db.GetData(SqlQueries.Queries["GET_ELIA_DEMAND_ID"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });

                if (dtDemand.Rows.Count > 0)
                {
                    eliaDemandId = dtDemand.Rows[0]["IT5HDMDAIDN"].ToString();
                }

                // 2. Extraction des tables basées sur EliaId
                var eliaTables = new[] {
                    "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UPRP",
                    "FJ1.TB5UAVE", "FJ1.TB5UDCR", "FJ1.TB5UBEN", "FJ1.TB5UPRS",
                    "FJ1.TB5URPP", "FJ1.TB5HELT"
                };

                foreach (var table in eliaTables)
                {
                    var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                    AddTableToBuffer(sb, table, dt);
                }

                // 3. Extraction des tables basées sur DemandId (si trouvé)
                if (eliaDemandId != "Non trouvé")
                {
                    var demandTables = new[] { "FJ1.TB5HDMD", "FJ1.TB5HPRO", "FJ1.TB5HDIC", "FJ1.TB5HEPT" };
                    foreach (var table in demandTables)
                    {
                        var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@DemandId", eliaDemandId } });
                        AddTableToBuffer(sb, table, dt);
                    }
                }
            }

            File.WriteAllText(_lastGeneratedPath, sb.ToString(), Encoding.UTF8);

            // Retourne la chaîne formatée avec les IDs LISA, UCONAIDN et HDMDAIDN
            return $"LISA ID: {lisaId} | UCONAIDN: {eliaUconId} | HDMDAIDN: {eliaDemandId}";
        }

        private void AddTableToBuffer(StringBuilder sb, string tableName, DataTable dt)
        {
            sb.AppendLine("################################################################################");
            sb.AppendLine($"### TABLE : {tableName} | Lignes : {dt.Rows.Count}");
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
            else { sb.AppendLine("AUCUNE DONNEE TROUVEE"); }
            sb.AppendLine();
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_lastGeneratedPath))
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Module Activation sélectionné."; }
        private void BtnComparison_Click(object sender, RoutedEventArgs e) { TxtStatus.Text = "Module Comparaison sélectionné."; }
    }
}