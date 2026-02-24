using System;
using System.Data;
using Microsoft.Data.SqlClient;
using AutoActivator.Config;

namespace AutoActivator.Services
{
    public class DatabaseManager
    {
        private readonly string _connectionString;

        public DatabaseManager()
        {
            // Recuperation de la chaine de connexion definie dans le fichier Settings.cs
            _connectionString = Settings.DbConfig.ConnectionString;
        }


        /// Methode utilitaire pour verifier si la connexion fonctionne.

        public bool TestConnection()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                using var command = new SqlCommand("SELECT 1", connection);
                var result = command.ExecuteScalar();

                if (result != null && result.ToString() == "1")
                {
                    Console.WriteLine($"[INFO] Connexion r�ussie � la base : {Settings.DbConfig.Database}");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] �chec de la connexion : {e.Message}");
                return false;
            }
        }


        /// Execute une requete SQL SELECT et retourne un DataTable

        /// <param name="query">La requete SQL a executer</param>
        /// <param name="parameters">Parametres optionnels pour securiser la requete</param>
        /// <returns>Un DataTable contenant les resultats</returns>
        public DataTable GetData(string query, SqlParameter[] parameters = null)
        {
            var dataTable = new DataTable();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                if (parameters != null)
                {
                    command.Parameters.AddRange(parameters);
                }

                using var adapter = new SqlDataAdapter(command);
                // Fill va ouvrir la connexion, lire les donnees, les charger dans le DataTable et fermer la connexion
                adapter.Fill(dataTable);
            }
            catch (SqlException e)
            {
                Console.WriteLine($"[ERROR] Erreur SQL lors de l'ex�cution de la requ�te : {e.Message}");
                // On retourne un DataTable vide en cas d'erreur pour ne pas faire planter le script de comparaison
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Erreur inattendue : {e.Message}");
                throw;
            }

            return dataTable;
        }


        /// Insere un paiement dans LV.PRCTT0 pour activer le contrat.
        /// Se base sur la structure de donnees fournie

        public bool InjectPayment(long contractInternalId, decimal amount, DateTime? paymentDate = null)
        {
            // Si pas de date fournie, on prend maintenant
            DateTime now = DateTime.Now;
            DateTime referenceDate = paymentDate ?? now;

            // Si une date specifique est fournie sans heure, on lui donne arbitrairement 12:00:00
            DateTime timestamp = paymentDate.HasValue && paymentDate.Value.TimeOfDay == TimeSpan.Zero
                ? paymentDate.Value.AddHours(12)
                : now;

            // Generation d'une communication structuree fictive basee sur l'ID contrat
            // Format : 820 + 9 chiffres ID + 99 (juste pour l'unicite)
            string idStr = contractInternalId.ToString();
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
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                // Ajout des parametres securises
                command.Parameters.AddWithValue("@no_cnt", contractInternalId);
                command.Parameters.AddWithValue("@d_ref", referenceDate.Date);
                command.Parameters.AddWithValue("@tstamp", timestamp);
                command.Parameters.AddWithValue("@amount", amount);
                command.Parameters.AddWithValue("@commu", fakeCommu);

                connection.Open();

                // Execution de l'insertion (la transaction est geree implicitement, ou vous pouvez ajouter SqlTransaction si besoin)
                command.ExecuteNonQuery();

                Console.WriteLine($"[INFO] SUCCES: Paiement de {amount} EUR injecte pour le contrat {contractInternalId} (Date: {referenceDate:yyyy-MM-dd})");
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] ECHEC: Erreur lors de l'injection du paiement pour {contractInternalId} : {e.Message}");
                return false;
            }
        }
    }
}