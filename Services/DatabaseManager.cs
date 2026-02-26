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
            // Secure retrieval of the connection string
            _connectionString = Settings.DbConfig.ConnectionString;
        }

        /// <summary>
        /// Checks the validity of the connection to SQL Server.
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
                            Console.WriteLine($"[INFO] Successful connection to the database: {Settings.DbConfig.Database}");
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] Connection failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Executes a SELECT query with parameters to prevent SQL injection.
        /// </summary>
        public DataTable GetData(string query, Dictionary<string, object> parameters = null)
        {
            DataTable dataTable = new DataTable();

            // CORRECTION MINEURE : Correction automatique de la syntaxe IN si on a passé un paramètre simple (pour éviter l'erreur SQL)
            if (query.Contains("IN @DemandIds"))
            {
                query = query.Replace("IN @DemandIds", "= @DemandIds");
            }

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
                                // Proper handling of null values
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
            catch (Exception e) // CORRECTION MAJEURE : On catch tout et on throw pour que l'UI affiche "Erreur SQL"
            {
                Console.WriteLine($"[ERROR] SQL Error on query : {query}\nDetails: {e.Message}");
                // On remonte l'erreur pour que l'interface graphique (ExtractionService -> MainWindow) soit au courant
                throw new Exception($"Erreur SQL: {e.Message}");
            }

            return dataTable;
        }

        /// <summary>
        /// Injects a test payment into the LV.PRCTT0 table.
        /// </summary>
        public bool InjectPayment(long contractInternalId, decimal amount, DateTime? paymentDate = null)
        {
            DateTime now = DateTime.Now;
            DateTime referenceDate = paymentDate ?? now;
            DateTime timestamp = (paymentDate.HasValue && paymentDate.Value.TimeOfDay == TimeSpan.Zero)
                ? paymentDate.Value.AddHours(12)
                : now;

            // Generation of a structured communication
            string idStr = contractInternalId.ToString();
            string fakeCommu = $"820{(idStr.Length > 9 ? idStr.Substring(0, 9) : idStr)}99";

            // Native insert query
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

                        Console.WriteLine($"[INFO] Payment of {amount} EUR injected (Contract: {contractInternalId})");
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR] FAILED payment injection: {e.Message}");
                return false;
            }
        }
    }
}