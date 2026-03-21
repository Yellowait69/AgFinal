using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient; // Remplacez par Microsoft.Data.SqlClient si vous êtes sur du .NET récent
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AutoActivator.Config;

namespace AutoActivator.Services
{
    // 1. MISE À JOUR DE L'INTERFACE (Ajout des CancellationToken)
    public interface IDatabaseManager
    {
        string EnvironmentName { get; }

        Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

        Task<DataTable> GetDataAsync(string query, Dictionary<string, object> parameters = null, CancellationToken cancellationToken = default);

        Task<bool> InjectPaymentAsync(
            string contractInternalId, decimal amount, DateTime? paymentDate = null,
            string simulatedName = "TEST AUTOMATION", string simulatedAddress1 = "TEST STREET 1",
            string simulatedAddress2 = "1000 BRUSSELS", string simulatedIban = "BE47001304609580",
            string simulatedBic = "GEBABEBB", string bureauNumber = "12831", string authorId = "AUTO_TEST",
            CancellationToken cancellationToken = default);
    }

    public class DatabaseManager : IDatabaseManager
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseManager> _logger;
        public string EnvironmentName { get; }

        // 2. INJECTION DU LOGGER (Optionnel par défaut pour faciliter la transition)
        public DatabaseManager(string envSuffix, ILogger<DatabaseManager> logger = null)
        {
            if (string.IsNullOrWhiteSpace(envSuffix))
                throw new ArgumentException("L'environnement (envSuffix) ne peut pas être vide.", nameof(envSuffix));

            EnvironmentName = envSuffix;
            _connectionString = Settings.DbConfig.GetConnectionString(envSuffix);
            _logger = logger;
        }

        #region MÉTHODES ASYNCHRONES MODERNISÉES

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                // Prise en charge de l'annulation
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using var command = new SqlCommand("SELECT 1", connection);
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                if (result != null && result.ToString() == "1")
                {
                    _logger?.LogInformation("Successful connection to the database (Environment: {EnvironmentName})", EnvironmentName);
                    return true;
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Test de connexion annulé par l'utilisateur.");
                return false;
            }
            catch (SqlException ex)
            {
                _logger?.LogError(ex, "SQL connection failure (Code: {ErrorCode})", ex.Number);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Connection failure (General error)");
                return false;
            }
        }

        public async Task<DataTable> GetDataAsync(string query, Dictionary<string, object> parameters = null, CancellationToken cancellationToken = default)
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

                // Ouverture de la connexion avec jeton d'annulation
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Exécution de la requête en asynchrone avec jeton d'annulation
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                dataTable.Load(reader);

                return dataTable;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("L'exécution de la requête a été annulée : {Query}", query);
                throw; // On relance l'exception pour que le parent sache que c'est une annulation
            }
            catch (SqlException ex)
            {
                _logger?.LogError(ex, "SQL Error (Code {ErrorCode}) on query: {Query}", ex.Number, query);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "System Error on query: {Query}", query);
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
            string authorId = "AUTO_TEST",
            CancellationToken cancellationToken = default)
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

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation("Payment of {Amount} EUR successfully injected (Contract: {Contract} | Env: {Env})", amount, contractInternalId, EnvironmentName);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("L'injection du paiement a été annulée pour le contrat {Contract}", contractInternalId);
                return false;
            }
            catch (SqlException ex)
            {
                _logger?.LogError(ex, "SQL FAILURE during payment injection (Code: {ErrorCode})", ex.Number);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "System FAILURE during payment injection");
                return false;
            }
        }

        #endregion
    }
}