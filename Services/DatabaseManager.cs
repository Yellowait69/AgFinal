using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using AutoActivator.Config;

namespace AutoActivator.Services
{
    public class DatabaseManager
    {
        private readonly string _connectionString;

        public DatabaseManager()
        {
            // Récupération sécurisée de la chaîne de connexion depuis les réglages
            _connectionString = Settings.DbConfig.ConnectionString;
        }

        /// <summary>
        /// Vérifie la validité de la connexion au serveur SQL.
        /// </summary>
        public bool TestConnection()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    using (SqlCommand command = new SqlCommand("SELECT 1", connection))
                    {
                        var result = command.ExecuteScalar();
                        if (result != null && result.ToString() == "1")
                        {
                            Console.WriteLine($"[INFO] Connexion réussie à la base de données : {Settings.DbConfig.Database}");
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Échec de la connexion : {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Exécute une requête SELECT avec des paramètres pour prévenir les injections SQL.
        /// </summary>
        public DataTable GetData(string query, Dictionary<string, object> parameters = null)
        {
            DataTable dataTable = new DataTable();

            // SUPPRESSION DU BLOC DE REMPLACEMENT "IN @DemandIds" :
            // Ce bloc était dangereux car il risquait de corrompre les requêtes SQL complexes
            // utilisant STRING_SPLIT ou des clauses IN légitimes.

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                // Gestion propre des valeurs nulles
                                command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                            }
                        }

                        using (SqlDataAdapter adapter = new SqlDataAdapter(command))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Erreur SQL sur la requête : {query}\nDétails : {e.Message}");
                // On remonte l'erreur pour que l'interface utilisateur soit informée
                throw new Exception($"Erreur SQL : {e.Message}");
            }

            return dataTable;
        }

        /// <summary>
        /// Injecte un paiement de test dans la table LV.PRCTT0.
        /// </summary>
        public bool InjectPayment(string contractInternalId, decimal amount, DateTime? paymentDate = null)
        {
            // CORRECTION : On utilise 'string' pour contractInternalId pour préserver les zéros initiaux (ex: 0822...)
            // comme recommandé pour résoudre les problèmes d'extraction LISA.

            DateTime now = DateTime.Now;
            DateTime referenceDate = paymentDate ?? now;
            DateTime timestamp = (paymentDate.HasValue && paymentDate.Value.TimeOfDay == TimeSpan.Zero)
                ? paymentDate.Value.AddHours(12)
                : now;

            // Génération d'une communication structurée fictive
            string idStr = contractInternalId?.Trim() ?? "";
            string fakeCommu = $"820{(idStr.Length > 9 ? idStr.Substring(0, 9) : idStr)}99";

            string query = @"
                INSERT INTO LV.PRCTT0 (
                    C_STE, NO_CNT, C_MD_PMT, D_REF_PRM, NO_ORD_RCP, TSTAMP_CRT_RCT,
                    C_TY_RCT, D_BISM_DVA, D_BISM_DCOR, M_PAY, NM_CP,
                    T_ADR_1_CP, T_ADR_2_CP, C_ETAT_RCP, T_COMMU, NO_BUR_SERV,
                    NO_AVT, PC_COM, PC_FR_GEST, NO_IBAN_CP, C_BIC_CP,
                    NM_AUTEUR_CRT, D_CRT, TY_DMOD, D_ORGN_DEV, C_ORGN_DEV
                ) VALUES (
                    'A', @no_cnt, '6', @d_ref, '1', @tstamp,
                    '1', @d_ref, @d_ref, @amount, 'TEST AUTOMATION',
                    'RUE DU TEST 1', '1000 BRUXELLES', 'B', @commu, '12831',
                    '0', 0.0245, 0.0105, 'BE47001304609580', 'GEBABEBB',
                    'AUTO_TEST', @d_ref, 'O', @d_ref, 'EUR'
                )";

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@no_cnt", contractInternalId);
                        command.Parameters.AddWithValue("@d_ref", referenceDate.Date);
                        command.Parameters.AddWithValue("@tstamp", timestamp);
                        command.Parameters.AddWithValue("@amount", amount);
                        command.Parameters.AddWithValue("@commu", fakeCommu);

                        connection.Open();
                        command.ExecuteNonQuery();

                        Console.WriteLine($"[INFO] Paiement de {amount} EUR injecté (Contrat : {contractInternalId})");
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] ÉCHEC de l'injection du paiement : {e.Message}");
                return false;
            }
        }
    }
}