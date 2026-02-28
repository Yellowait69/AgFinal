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
            catch (SqlException ex)
            {
                Console.WriteLine($"[ERROR] Échec de la connexion SQL (Code: {ex.Number}) : {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Échec de la connexion (Erreur générale) : {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Exécute une requête SELECT avec des paramètres pour prévenir les injections SQL.
        /// </summary>
        public DataTable GetData(string query, Dictionary<string, object> parameters = null)
        {
            DataTable dataTable = new DataTable();

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

                        // Optimisation : SqlDataReader est plus performant que SqlDataAdapter pour un simple SELECT
                        connection.Open();
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            dataTable.Load(reader);
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"[ERROR] Erreur SQL (Code {ex.Number}) sur la requête : {query}\nDétails : {ex.Message}");
                throw new Exception($"Erreur BDD ({ex.Number}) : {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Erreur système sur la requête : {query}\nDétails : {ex.Message}");
                throw new Exception($"Erreur système : {ex.Message}");
            }

            return dataTable;
        }

        /// <summary>
        /// Injecte un paiement de test dans la table LV.PRCTT0 de manière sécurisée et dynamique.
        /// Les paramètres optionnels remplacent les valeurs anciennement "en dur" pour plus de flexibilité.
        /// </summary>
        public bool InjectPayment(
            string contractInternalId,
            decimal amount,
            DateTime? paymentDate = null,
            string simulatedName = "TEST AUTOMATION",
            string simulatedAddress1 = "RUE DU TEST 1",
            string simulatedAddress2 = "1000 BRUXELLES",
            string simulatedIban = "BE47001304609580",
            string simulatedBic = "GEBABEBB",
            string bureauNumber = "12831",
            string authorId = "AUTO_TEST")
        {
            DateTime now = DateTime.Now;
            DateTime referenceDate = paymentDate ?? now;
            DateTime timestamp = (paymentDate.HasValue && paymentDate.Value.TimeOfDay == TimeSpan.Zero)
                ? paymentDate.Value.AddHours(12)
                : now;

            // Génération d'une communication structurée fictive
            string idStr = contractInternalId?.Trim() ?? "";
            string fakeCommu = $"820{(idStr.Length > 9 ? idStr.Substring(0, 9) : idStr)}99";

            // La requête SQL utilise maintenant les paramètres dynamiques au lieu des chaînes en dur
            string query = @"
                INSERT INTO LV.PRCTT0 (
                    C_STE, NO_CNT, C_MD_PMT, D_REF_PRM, NO_ORD_RCP, TSTAMP_CRT_RCT,
                    C_TY_RCT, D_BISM_DVA, D_BISM_DCOR, M_PAY, NM_CP,
                    T_ADR_1_CP, T_ADR_2_CP, C_ETAT_RCP, T_COMMU, NO_BUR_SERV,
                    NO_AVT, PC_COM, PC_FR_GEST, NO_IBAN_CP, C_BIC_CP,
                    NM_AUTEUR_CRT, D_CRT, TY_DMOD, D_ORGN_DEV, C_ORGN_DEV
                ) VALUES (
                    'A', @no_cnt, '6', @d_ref, '1', @tstamp,
                    '1', @d_ref, @d_ref, @amount, @nom_cp,
                    @adr_1, @adr_2, 'B', @commu, @no_bur,
                    '0', 0.0245, 0.0105, @iban, @bic,
                    @auteur, @d_ref, 'O', @d_ref, 'EUR'
                )";

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        // Assignation des paramètres métiers
                        command.Parameters.AddWithValue("@no_cnt", contractInternalId);
                        command.Parameters.AddWithValue("@d_ref", referenceDate.Date);
                        command.Parameters.AddWithValue("@tstamp", timestamp);
                        command.Parameters.AddWithValue("@amount", amount);
                        command.Parameters.AddWithValue("@commu", fakeCommu);

                        // Assignation des paramètres de simulation (anciennement en dur)
                        command.Parameters.AddWithValue("@nom_cp", simulatedName);
                        command.Parameters.AddWithValue("@adr_1", simulatedAddress1);
                        command.Parameters.AddWithValue("@adr_2", simulatedAddress2);
                        command.Parameters.AddWithValue("@no_bur", bureauNumber);
                        command.Parameters.AddWithValue("@iban", simulatedIban);
                        command.Parameters.AddWithValue("@bic", simulatedBic);
                        command.Parameters.AddWithValue("@auteur", authorId);

                        connection.Open();
                        command.ExecuteNonQuery();

                        Console.WriteLine($"[INFO] Paiement de {amount} EUR injecté avec succès (Contrat : {contractInternalId})");
                        return true;
                    }
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"[ERROR] ÉCHEC SQL lors de l'injection du paiement (Code: {ex.Number}) : {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] ÉCHEC système lors de l'injection du paiement : {ex.Message}");
                return false;
            }
        }
    }
}