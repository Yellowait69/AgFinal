using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoActivator.Sql;

namespace AutoActivator.Services
{
    public class ActivationDataService
    {
        public string FormatContractForJcl(string rawContract)
        {
            string cleaned = rawContract.Replace("-", "").Replace(" ", "").Trim();
            if (cleaned.Length == 12)
            {
                return cleaned.Substring(1, 9);
            }
            return cleaned;
        }

        public async Task<string> GetContractFromDemandAsync(string demandId, string envSuffix)
        {
            try
            {
                var db = new DatabaseManager(envSuffix);
                string cleanDemandId = demandId.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

                // OPTIMISATION : Utilisation de GetDataAsync natif pour ne pas bloquer de thread
                var dt = await db.GetDataAsync(SqlQueries.Queries["GET_CONTRACT_BY_DEMAND"], new Dictionary<string, object> { { "@DemandId", cleanDemandId } });

                if (dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["IT5UCONLREFEXN"]?.ToString()?.Trim();
                }
            }
            catch { }
            return null;
        }

        public async Task<string> FetchPremiumAsync(string contract, string envSuffix)
        {
            try
            {
                var db = new DatabaseManager(envSuffix);
                string dbContract = contract.Replace("\u00A0", "").Replace("\uFEFF", "").Trim();

                // OPTIMISATION : Appels asynchrones natifs
                var dtElia = await db.GetDataAsync(SqlQueries.Queries["GET_ELIA_ID"], new Dictionary<string, object> { { "@ContractNumber", dbContract } });

                if (dtElia.Rows.Count > 0)
                {
                    string eliaUconId = dtElia.Rows[0]["IT5UCONAIDN"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(eliaUconId) && SqlQueries.Queries.ContainsKey("FJ1.TB5UPRP"))
                    {
                        var dtPremium = await db.GetDataAsync(SqlQueries.Queries["FJ1.TB5UPRP"], new Dictionary<string, object> { { "@EliaId", eliaUconId } });
                        if (dtPremium.Rows.Count > 0 && dtPremium.Columns.Contains("IT5UPRPUBRU"))
                        {
                            return dtPremium.Rows[0]["IT5UPRPUBRU"]?.ToString()?.Trim() ?? "0";
                        }
                    }
                }
            }
            catch { }
            return "0";
        }

        // NOUVEAUTÉ ICI : Ajout du paramètre "bool skipPrime"
        public async Task ExecuteActivationSequenceAsync(string contract, string amount, string envValue, string cus, string bucp, string cmdpmt, string channel, bool skipPrime, string username, string password, Action<string> onProgress, CancellationToken token)
        {
            string q2 = envValue == "D" ? "Q2T" : "Q2C";
            string fastCtrl = envValue == "D" ? "I0T.DB.CA.FIB.FASTCTRL" : "I10.DB.CA.FIB.FASTCTRL";
            string envImsValue = envValue == "D" ? "T" : "C";

            // CORRECTION : Formatage explicite avec un point et 2 décimales
            if (decimal.TryParse(amount.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal parsedAmount))
            {
                // Force le format avec 2 décimales et un point ("0.00")
                // Exemple : 2500 devient "2500.00", 2500,5 devient "2500.50"
                amount = parsedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                amount = "0.00";
            }

            if (amount == "0.00" || amount == "0")
            {
                // Si c'est C05 on pourrait théoriquement se passer du montant vu qu'on ignore ADDPRCT,
                // mais c'est toujours bon de garder la sécurité ici pour s'assurer que le contrat est valide en DB.
                throw new Exception("Montant (Premium) introuvable ou égal à 0€. L'activation a été annulée car elle ne produirait aucun effet. Vérifiez le contrat (12 chiffres/tirets nécessaires pour la DB).");
            }

            // Remplit avec des zéros à gauche pour atteindre 10 caractères
            // Exemple : "2500.00" devient "0002500.00"
            string paddedAmount = amount.PadLeft(10, '0');
            string paddedBucp = bucp.PadLeft(5, '0');

            var generalVariables = new Dictionary<string, string>
            {
                { "ENV", envValue }, { "ENVIMS", envImsValue }, { "CUS", cus },
                { "YYMMDD", DateTime.Now.ToString("yyMMdd") }, { "YYYY", DateTime.Now.ToString("yyyy") },
                { "MM", DateTime.Now.ToString("MM") }, { "DD", DateTime.Now.ToString("dd") },
                { "CLASS", "A" }, { "CNTBEG", contract }, { "CNTEND", contract },
                { "MMDD", DateTime.Now.ToString("MMdd") }, { "CYMD", DateTime.Now.ToString("yyyyMMdd") },
                { "STE", "A" }, { "Q2", q2 }, { "CM.", "     " },
                { "DRUN", DateTime.Now.ToString("yyyyMMdd") }, { "NREMB", "20" },
                { "CONTR-EX", "Y" }, { "CONTR-RE", "Y" }, { "CONTR-UN", "Y" },
                { "NJJART72", "5" }, { "FASTCTRL", fastCtrl }
            };

            var addprctVariables = new Dictionary<string, string>
            {
                { "CMDPMT", cmdpmt }, { "AMOUNT", paddedAmount },
                { "BUCP", paddedBucp }, { "USERNAME", username }
            };

            string jclFolder = @"\\Jafile02\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\LVCHAIN\JCL";

            if (!Directory.Exists(jclFolder))
                throw new Exception($"Dossier réseau des JCL inaccessible: {jclFolder}");

            var orchestrator = new ActivationOrchestrator(jclFolder);

            // NOUVEAUTÉ ICI : Ajout de la variable skipPrime à l'appel
            await orchestrator.RunActivationSequenceAsync(
                generalVariables, addprctVariables, username, password, channel, skipPrime,
                onProgress, token
            );
        }
    }
}