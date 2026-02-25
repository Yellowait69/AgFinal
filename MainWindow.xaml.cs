using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using AutoActivator.Services;
using AutoActivator.Config;
using AutoActivator.Sql; // Ajout nécessaire pour accéder aux requêtes SQL

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

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtContract.Text.Trim();

            if (string.IsNullOrEmpty(input))
            {
                TxtStatus.Text = "Veuillez entrer un numéro de contrat.";
                TxtStatus.Foreground = Brushes.Orange;
                return;
            }

            string targetContract = input.Replace("\u00A0", "");
            TxtStatus.Text = "Connexion à la base de données...";
            TxtStatus.Foreground = Brushes.Blue;

            try
            {
                var db = new DatabaseManager();
                if (!db.TestConnection())
                {
                    TxtStatus.Text = "Échec de la connexion à la base de données.";
                    TxtStatus.Foreground = Brushes.Red;
                    return;
                }

                // 1. Récupération des IDs (Logique reprise de Program.cs)
                var parameters = new Dictionary<string, object> { { "@ContractNumber", targetContract } };
                var dtLisa = db.GetData(SqlQueries.Queries["GET_INTERNAL_ID"], parameters);
                var dtElia = db.GetData(SqlQueries.Queries["GET_ELIA_ID"], parameters);

                if (dtLisa.Rows.Count == 0 && dtElia.Rows.Count == 0)
                {
                    TxtStatus.Text = $"Contrat {targetContract} introuvable.";
                    TxtStatus.Foreground = Brushes.Red;
                    return;
                }

                // 2. Préparation du contenu du fichier unique
                StringBuilder sbFullContent = new StringBuilder();
                _lastGeneratedPath = Path.Combine(Settings.OutputDir, $"FULL_EXTRACT_{targetContract}.csv");

                // Fonction pour ajouter les tables au buffer
                void AddTableToBuffer(string tableName, DataTable dt)
                {
                    sbFullContent.AppendLine("################################################################################");
                    sbFullContent.AppendLine($"### TABLE : {tableName} | Lignes : {dt.Rows.Count}");
                    sbFullContent.AppendLine("################################################################################");

                    if (dt.Rows.Count > 0)
                    {
                        var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                        sbFullContent.AppendLine(string.Join(";", columns));

                        foreach (DataRow row in dt.Rows)
                        {
                            var fields = row.ItemArray.Select(f => f?.ToString().Replace(";", " ").Replace("\n", " ").Trim());
                            sbFullContent.AppendLine(string.Join(";", fields));
                        }
                    }
                    else
                    {
                        sbFullContent.AppendLine("AUCUNE DONNEE TROUVEE");
                    }
                    sbFullContent.AppendLine();
                }

                // 3. Extraction LISA
                if (dtLisa.Rows.Count > 0)
                {
                    long internalId = Convert.ToInt64(dtLisa.Rows[0]["NO_CNT"]);
                    var lisaTables = new[] { "LV.SCNTT0", "LV.SAVTT0", "LV.PRCTT0", "LV.SWBGT0", "LV.SCLST0", "LV.SCLRT0", "LV.BSPDT0", "LV.BSPGT0", "LV.MWBGT0", "LV.PRIST0", "LV.FMVGT0" };
                    foreach (var table in lisaTables)
                    {
                        var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@InternalId", internalId } });
                        AddTableToBuffer(table, dt);
                    }
                }

                // 4. Extraction ELIA
                if (dtElia.Rows.Count > 0)
                {
                    string eliaId = dtElia.Rows[0]["IT5UCONAIDN"].ToString();
                    var eliaTables = new[] { "FJ1.TB5UCON", "FJ1.TB5UGAR", "FJ1.TB5UASU", "FJ1.TB5UPRP", "FJ1.TB5UAVE", "FJ1.TB5UDCR", "FJ1.TB5UBEN" };
                    foreach (var table in eliaTables)
                    {
                        var dt = db.GetData(SqlQueries.Queries[table], new Dictionary<string, object> { { "@EliaId", eliaId } });
                        AddTableToBuffer(table, dt);
                    }
                }

                // 5. Écriture réelle du fichier sur le disque
                File.WriteAllText(_lastGeneratedPath, sbFullContent.ToString(), Encoding.UTF8);

                TxtStatus.Text = $"Extraction terminée ! Fichier généré avec succès.";
                TxtStatus.Foreground = Brushes.Green;
                LnkFile.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Erreur lors de l'extraction : {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_lastGeneratedPath))
            {
                // Lance l'explorateur et sélectionne le fichier créé
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
            }
            else
            {
                MessageBox.Show("Le fichier n'a pas pu être trouvé.");
            }
        }

        private void BtnActivation_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Module Activation : Prêt pour le développement.";
            LnkFile.Visibility = Visibility.Collapsed;
        }

        private void BtnComparison_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Module Comparaison : Prêt pour le développement.";
            LnkFile.Visibility = Visibility.Collapsed;
        }
    }
}