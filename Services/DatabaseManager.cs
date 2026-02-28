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
        public string EnvironmentName { get; }

        public DatabaseManager(string envSuffix)
        {
            EnvironmentName = envSuffix;
            // Secure retrieval of the connection string dynamically based on the environment
            _connectionString = Settings.DbConfig.GetConnectionString(envSuffix);
        }

        /// <summary>
        /// Checks the validity of the connection to the SQL server.
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
                            Console.WriteLine($"[INFO] Successful connection to the database (Environment: {EnvironmentName})");
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"[ERROR] SQL connection failure (Code: {ex.Number}) : {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Connection failure (General error) : {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Executes a SELECT query with parameters to prevent SQL injections.
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
                                // Clean handling of null values
                                command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                            }
                        }

                        // Optimization: SqlDataReader is faster than SqlDataAdapter for a simple SELECT
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
                Console.WriteLine($"[ERROR] SQL Error (Code {ex.Number}) on query: {query}\nDetails: {ex.Message}");
                // AJOUT DE 'ex' POUR PRESERVER LA STACK TRACE
                throw new Exception($"DB Error ({ex.Number}) : {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] System Error on query: {query}\nDetails: {ex.Message}");
                // AJOUT DE 'ex' POUR PRESERVER LA STACK TRACE
                throw new Exception($"System Error : {ex.Message}", ex);
            }

            return dataTable;
        }

        /// <summary>
        /// Injects a test payment into the LV.PRCTT0 table safely and dynamically.
        /// Optional parameters replace hardcoded values for more flexibility.
        /// </summary>
        public bool InjectPayment(
            string contractInternalId,
            decimal amount,
            DateTime? paymentDate = null,
            string simulatedName = "TEST AUTOMATION",
            string simulatedAddress1 = "TEST STREET 1",
            string simulatedAddress2 = "1000 BRUSSELS",
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

            // Generation of a fictional structured communication
            string idStr = contractInternalId?.Trim() ?? "";
            string fakeCommu = $"820{(idStr.Length > 9 ? idStr.Substring(0, 9) : idStr)}99";

            // The SQL query now uses dynamic parameters instead of hardcoded strings
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
                        // Assignment of business parameters
                        command.Parameters.AddWithValue("@no_cnt", contractInternalId);
                        command.Parameters.AddWithValue("@d_ref", referenceDate.Date);
                        command.Parameters.AddWithValue("@tstamp", timestamp);
                        command.Parameters.AddWithValue("@amount", amount);
                        command.Parameters.AddWithValue("@commu", fakeCommu);

                        // Assignment of simulation parameters (formerly hardcoded)
                        command.Parameters.AddWithValue("@nom_cp", simulatedName);
                        command.Parameters.AddWithValue("@adr_1", simulatedAddress1);
                        command.Parameters.AddWithValue("@adr_2", simulatedAddress2);
                        command.Parameters.AddWithValue("@no_bur", bureauNumber);
                        command.Parameters.AddWithValue("@iban", simulatedIban);
                        command.Parameters.AddWithValue("@bic", simulatedBic);
                        command.Parameters.AddWithValue("@auteur", authorId);

                        connection.Open();
                        command.ExecuteNonQuery();

                        Console.WriteLine($"[INFO] Payment of {amount} EUR successfully injected (Contract: {contractInternalId} | Env: {EnvironmentName})");
                        return true;
                    }
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"[ERROR] SQL FAILURE during payment injection (Code: {ex.Number}) : {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] System FAILURE during payment injection : {ex.Message}");
                return false;
            }
        }
    }
}