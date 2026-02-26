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
            _connectionString = Settings.DbConfig.ConnectionString
                ?? throw new InvalidOperationException("Connection string is null.");
        }

        /// <summary>
        /// Tests SQL Server connectivity.
        /// </summary>
        public bool TestConnection()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand("SELECT 1", connection)
                {
                    CommandTimeout = 5
                };

                connection.Open();
                var result = command.ExecuteScalar();

                return result != null && Convert.ToInt32(result) == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Executes a parameterized SELECT query safely.
        /// </summary>
        public DataTable GetData(string query, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query is null or empty.");

            var dataTable = new DataTable();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = 60
                };

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        var sqlParam = command.Parameters.Add(param.Key, SqlDbType.Variant);
                        sqlParam.Value = param.Value ?? DBNull.Value;
                    }
                }

                using var adapter = new SqlDataAdapter(command);
                adapter.Fill(dataTable);
            }
            catch (SqlException ex)
            {
                throw new Exception($"SQL Error: {ex.Message}", ex);
            }

            return dataTable;
        }

        /// <summary>
        /// Injects a test payment into LV.PRCTT0.
        /// </summary>
        public bool InjectPayment(long contractInternalId, decimal amount, DateTime? paymentDate = null)
        {
            DateTime now = DateTime.Now;
            DateTime referenceDate = paymentDate?.Date ?? now.Date;
            DateTime timestamp = paymentDate?.TimeOfDay == TimeSpan.Zero
                ? paymentDate.Value.Date.AddHours(12)
                : now;

            string idStr = contractInternalId.ToString();
            string fakeCommu = $"820{(idStr.Length > 9 ? idStr[..9] : idStr)}99";

            const string query = @"
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
                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = 30
                };

                command.Parameters.Add("@no_cnt", SqlDbType.BigInt).Value = contractInternalId;
                command.Parameters.Add("@d_ref", SqlDbType.Date).Value = referenceDate;
                command.Parameters.Add("@tstamp", SqlDbType.DateTime).Value = timestamp;
                command.Parameters.Add("@amount", SqlDbType.Decimal).Value = amount;
                command.Parameters["@amount"].Precision = 18;
                command.Parameters["@amount"].Scale = 2;
                command.Parameters.Add("@commu", SqlDbType.VarChar, 50).Value = fakeCommu;

                connection.Open();
                command.ExecuteNonQuery();

                return true;
            }
            catch (SqlException)
            {
                return false;
            }
        }
    }
}