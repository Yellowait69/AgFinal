using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient; // Remplacez par Microsoft.Data.SqlClient si vous êtes sur du .NET récent
using System.Threading.Tasks;
using AutoActivator.Config;

namespace AutoActivator.Services
{
    // 1. EXTRACTION DE L'INTERFACE (Pour permettre l'injection de dépendances et les tests)
    public interface IDatabaseManager
    {
        string EnvironmentName { get; }
        Task<bool> TestConnectionAsync();
        Task<DataTable> GetDataAsync(string query, Dictionary<string, object> parameters = null);
        Task<bool> InjectPaymentAsync(
            string contractInternalId, decimal amount, DateTime? paymentDate = null,
            string simulatedName = "TEST AUTOMATION", string simulatedAddress1 = "TEST STREET 1",
            string simulatedAddress2 = "1000 BRUSSELS", string simulatedIban = "BE47001304609580",
            string simulatedBic = "GEBABEBB", string bureauNumber = "12831", string authorId = "AUTO_TEST");
    }

    public class DatabaseManager : IDatabaseManager
    {
        private readonly string _connectionString;
        public string EnvironmentName { get; }

        public DatabaseManager(string envSuffix)
        {
            if (string.IsNullOrWhiteSpace(envSuffix))
                throw new ArgumentException("L'environnement (envSuffix) ne peut pas être vide.", nameof(envSuffix));

            EnvironmentName = envSuffix;
            _connectionString = Settings.DbConfig.GetConnectionString(envSuffix);
        }

        #region MÉTHODES ASYNCHRONES MODERNISÉES

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // 2. UTILISATION DE 'using var' (C# 8.0+) pour un code plus propre et sûr
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync().ConfigureAwait(false);

                using var command = new SqlCommand("SELECT 1", connection);
                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);

                if (result != null && result.ToString() == "1")
                {
                    System.Diagnostics.Debug.WriteLine($"[INFO] Successful connection to the database (Environment: {EnvironmentName})");
                    return true;
                }

                return false;
            }
            catch (SqlException ex)
            {
                // Remplacement de Console.WriteLine par Debug.WriteLine (plus adapté pour WPF)
                System.Diagnostics.Debug.WriteLine($"[ERROR] SQL connection failure (Code: {ex.Number}) : {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Connection failure (General error) : {ex.Message}");
                return false;
            }
        }

        public async Task<DataTable> GetDataAsync(string query, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("La requête SQL ne peut pas être vide.", nameof(query));

            DataTable dataTable = new DataTable();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        // 3. GESTION SÉCURISÉE DES NULLS : Protège contre les crashs si param.Value est null
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                // Ouverture de la connexion sans bloquer le thread principal de l'UI
                await connection.OpenAsync().ConfigureAwait(false);

                // Exécution de la requête en asynchrone
                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                dataTable.Load(reader);

                return dataTable;
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] SQL Error (Code {ex.Number}) on query: {query}\nDetails: {ex.Message}");
                // 4. CORRECTION DU THROW : On utilise `throw;` pour conserver la StackTrace complète et le type SqlException
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] System Error on query: {query}\nDetails: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> InjectPaymentAsync(
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
                    '1', @d_ref, @d_ref, @amount, @nom_cp,
                    @adr_1, @adr_2, 'B', @commu, @no_bur,
                    '0', 0.0245, 0.0105, @iban, @bic,
                    @auteur, @d_ref, 'O', @d_ref, 'EUR'
                )";

            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                command.Parameters.AddWithValue("@no_cnt", contractInternalId);
                command.Parameters.AddWithValue("@d_ref", referenceDate.Date);
                command.Parameters.AddWithValue("@tstamp", timestamp);
                command.Parameters.AddWithValue("@amount", amount);
                command.Parameters.AddWithValue("@commu", fakeCommu);

                command.Parameters.AddWithValue("@nom_cp", simulatedName);
                command.Parameters.AddWithValue("@adr_1", simulatedAddress1);
                command.Parameters.AddWithValue("@adr_2", simulatedAddress2);
                command.Parameters.AddWithValue("@no_bur", bureauNumber);
                command.Parameters.AddWithValue("@iban", simulatedIban);
                command.Parameters.AddWithValue("@bic", simulatedBic);
                command.Parameters.AddWithValue("@auteur", authorId);

                await connection.OpenAsync().ConfigureAwait(false);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                System.Diagnostics.Debug.WriteLine($"[INFO] Payment of {amount} EUR successfully injected (Contract: {contractInternalId} | Env: {EnvironmentName})");
                return true;
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] SQL FAILURE during payment injection (Code: {ex.Number}) : {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] System FAILURE during payment injection : {ex.Message}");
                return false;
            }
        }

        #endregion

        // 5. SUPPRESSION DÉFINITIVE DE LA RÉGION "MÉTHODES SYNCHRONES"
        // Le code mort (TestConnection, GetData, InjectPayment synchrones) a été entièrement retiré
        // pour alléger la classe, éviter la confusion et forcer les appelants à utiliser de l'asynchrone.
    }
}