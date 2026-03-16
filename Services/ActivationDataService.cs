using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoActivator.Sql;

namespace AutoActivator.Services
{
    public class ActivationDataService
    {
        private const string JCL_FOLDER =
            @"\\Jafile02\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\LVCHAIN\JCL";

        public string FormatContractForJcl(string rawContract)
        {
            if (string.IsNullOrWhiteSpace(rawContract))
                return string.Empty;

            string cleaned = CleanString(rawContract)
                .Replace("-", "")
                .Replace(" ", "");

            if (cleaned.Length == 12)
                return cleaned.Substring(1, 9);

            return cleaned;
        }

        public async Task<string> GetContractFromDemandAsync(string demandId, string envSuffix)
        {
            try
            {
                var db = new DatabaseManager(envSuffix);

                string cleanDemandId = CleanString(demandId);

                var dt = await db.GetDataAsync(
                    SqlQueries.Queries["GET_CONTRACT_BY_DEMAND"],
                    new Dictionary<string, object>
                    {
                        { "@DemandId", cleanDemandId }
                    });

                if (dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["IT5UCONLREFEXN"]?
                        .ToString()?
                        .Trim();
                }
            }
            catch
            {
                // volontairement silencieux pour traitement batch
            }

            return null;
        }

        public async Task<string> FetchPremiumAsync(string contract, string envSuffix)
        {
            try
            {
                var db = new DatabaseManager(envSuffix);

                string dbContract = CleanString(contract);

                var dtElia = await db.GetDataAsync(
                    SqlQueries.Queries["GET_ELIA_ID"],
                    new Dictionary<string, object>
                    {
                        { "@ContractNumber", dbContract }
                    });

                if (dtElia.Rows.Count == 0)
                    return "0";

                string eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?
                    .ToString()?
                    .Trim();

                if (string.IsNullOrEmpty(eliaUconId))
                    return "0";

                if (!SqlQueries.Queries.ContainsKey("FJ1.TB5UPRP"))
                    return "0";

                var dtPremium = await db.GetDataAsync(
                    SqlQueries.Queries["FJ1.TB5UPRP"],
                    new Dictionary<string, object>
                    {
                        { "@EliaId", eliaUconId }
                    });

                if (dtPremium.Rows.Count > 0 &&
                    dtPremium.Columns.Contains("IT5UPRPUBRU"))
                {
                    return dtPremium.Rows[0]["IT5UPRPUBRU"]?
                        .ToString()?
                        .Trim() ?? "0";
                }
            }
            catch
            {
            }

            return "0";
        }

        public async Task ExecuteActivationSequenceAsync(
            string contract,
            string amount,
            string envValue,
            string cus,
            string bucp,
            string cmdpmt,
            string username,
            string password,
            Action<string> onProgress,
            CancellationToken token,
            bool skipLogon = false)
        {
            EnsureJclFolderExists();

            string q2 = envValue == "D" ? "Q2T" : "Q2C";
            string fastCtrl = envValue == "D"
                ? "I0T.DB.CA.FIB.FASTCTRL"
                : "I10.DB.CA.FIB.FASTCTRL";

            string envImsValue = envValue == "D" ? "T" : "C";

            amount = NormalizeAmount(amount);

            if (amount == "0.00")
                throw new Exception(
                    "Montant (Premium) introuvable ou égal à 0€. Activation annulée.");

            string paddedAmount = amount.PadLeft(10, '0');
            string paddedBucp = bucp.PadLeft(5, '0');

            var generalVariables = BuildGeneralVariables(
                contract,
                envValue,
                envImsValue,
                cus,
                q2,
                fastCtrl);

            var addprctVariables = new Dictionary<string, string>
            {
                { "CMDPMT", cmdpmt },
                { "AMOUNT", paddedAmount },
                { "BUCP", paddedBucp },
                { "USERNAME", username }
            };

            var orchestrator = new ActivationOrchestrator(JCL_FOLDER);

            await orchestrator.RunActivationSequenceAsync(
                generalVariables,
                addprctVariables,
                username,
                password,
                onProgress,
                token,
                skipLogon);
        }

        private Dictionary<string, string> BuildGeneralVariables(
            string contract,
            string envValue,
            string envImsValue,
            string cus,
            string q2,
            string fastCtrl)
        {
            var now = DateTime.Now;

            return new Dictionary<string, string>
            {
                { "ENV", envValue },
                { "ENVIMS", envImsValue },
                { "CUS", cus },
                { "YYMMDD", now.ToString("yyMMdd") },
                { "YYYY", now.ToString("yyyy") },
                { "MM", now.ToString("MM") },
                { "DD", now.ToString("dd") },
                { "MMDD", now.ToString("MMdd") },
                { "CYMD", now.ToString("yyyyMMdd") },
                { "DRUN", now.ToString("yyyyMMdd") },

                { "CLASS", "A" },
                { "STE", "A" },

                { "CNTBEG", contract },
                { "CNTEND", contract },

                { "Q2", q2 },
                { "CM.", "     " },

                { "NREMB", "20" },
                { "CONTR-EX", "Y" },
                { "CONTR-RE", "Y" },
                { "CONTR-UN", "Y" },

                { "NJJART72", "5" },
                { "FASTCTRL", fastCtrl }
            };
        }

        private string NormalizeAmount(string amount)
        {
            if (string.IsNullOrWhiteSpace(amount))
                return "0.00";

            if (decimal.TryParse(
                    amount.Replace(",", "."),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal parsed))
            {
                return parsed.ToString("0.00", CultureInfo.InvariantCulture);
            }

            return "0.00";
        }

        private void EnsureJclFolderExists()
        {
            if (!Directory.Exists(JCL_FOLDER))
            {
                throw new Exception(
                    $"Dossier réseau des JCL inaccessible: {JCL_FOLDER}");
            }
        }

        private string CleanString(string value)
        {
            return value?
                .Replace("\u00A0", "")
                .Replace("\uFEFF", "")
                .Trim();
        }
    }
}