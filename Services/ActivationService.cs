using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoActivator.Services
{
    public class ActivationService
    {
        private readonly string _jclDirectory;
        private readonly CookieContainer _cookieContainer;
        private string _activeServer;
        private string _currentEnv;
        private string _baseUrl;
        private string _nodeUrl;

        // Dictionnaire des serveurs actifs par environnement
        private readonly Dictionary<string, List<string>> _activeServers = new Dictionary<string, List<string>>()
        {
            { "D", new List<string> { "sdmfas01", "sdmfas03", "sdmfas05" } },
            { "Q", new List<string> { "sqmfas06", "sqmfas08", "sqmfas10", "sqmfas02", "sqmfas04" } },
            { "A", new List<string> { "samfas04", "samfas06", "samfas02" } },
            { "P", new List<string> { "spmfas07", "spmfas09", "spmfas01", "spmfas03", "spmfas05", "spmfas11", "spmfas13" } }
        };

        public ActivationService(string jclDirectory)
        {
            _jclDirectory = jclDirectory;
            _cookieContainer = new CookieContainer();

            // Ignore les erreurs de certificat SSL (Auto-signé en Intranet)
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

            // Force l'utilisation des protocoles de sécurité modernes et anciens pour la compatibilité
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        }

        public async Task RunActivationSequenceAsync(Dictionary<string, string> generalVariables, Dictionary<string, string> addprctSpecificVariables, string username, string password, Action<string> onProgress, CancellationToken cancellationToken = default)
        {
            try
            {
                onProgress("=== DÉBUT DE LA SÉQUENCE D'ACTIVATION ===");

                _currentEnv = generalVariables.ContainsKey("ENVIMS") ? generalVariables["ENVIMS"] : "D";

                // 1. Authentification MicroFocus
                onProgress($"Connexion au serveur MicroFocus ({_currentEnv}000)...");
                bool isLogged = await LogonAsync(username, password, _currentEnv, onProgress, cancellationToken);

                if (!isLogged) throw new Exception("Impossible de se connecter à MicroFocus. Vérifiez l'état du VPN ou des serveurs.");

                onProgress($"Connecté avec succès au serveur : {_activeServer}");

                // 2. Préparation des variables
                var addprctVars = new Dictionary<string, string>(generalVariables);
                foreach (var kvp in addprctSpecificVariables) addprctVars[kvp.Key] = kvp.Value;

                int jobCounter = 1;

                // 3. Soumission et Attente des 5 Jobs dans l'ordre
                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVD4PP06", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVD4PG22", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobCounter++, onProgress, cancellationToken);

                onProgress("=== SÉQUENCE D'ACTIVATION TERMINÉE ===");
            }
            catch (OperationCanceledException)
            {
                onProgress("[ANNULATION] La séquence d'activation a été annulée par l'utilisateur.");
                throw;
            }
            catch (Exception ex)
            {
                onProgress($"[ERREUR CRITIQUE] La chaîne d'activation a été interrompue : {ex.Message}");
                throw;
            }
        }

        private async Task<bool> LogonAsync(string username, string password, string env, Action<string> onProgress, CancellationToken cancellationToken)
        {
            _baseUrl = $"https://escwa{env.ToLower()}.aginsurance.intranet:10086";
            if (!_activeServers.ContainsKey(env)) throw new Exception($"Environnement inconnu: {env}");

            string lastErrorMessage = "";

            foreach (var server in _activeServers[env])
            {
                cancellationToken.ThrowIfCancellationRequested();

                _activeServer = server;
                _nodeUrl = $"{_baseUrl}/native/v1/regions/{_activeServer}/86/BATCH{env}/";
                string logonUrl = $"{_baseUrl}/logon";

                var request = CreateRequest(logonUrl, "POST", logonUrl);

                string jsonPayload = $"{{\"mfUser\":\"{username}\",\"mfNewPassword\":\"\",\"mfPassword\":\"{password}\"}}";

                try
                {
                    using (cancellationToken.Register(() => request.Abort()))
                    using (var streamWriter = new StreamWriter(await request.GetRequestStreamAsync()))
                    {
                        await streamWriter.WriteAsync(jsonPayload);
                    }

                    using (var response = (HttpWebResponse)await request.GetResponseAsync())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            // Test de fonctionnalité
                            var testRequest = CreateRequest($"{_nodeUrl}region-functionality", "GET", $"{_nodeUrl}region-functionality");
                            using (cancellationToken.Register(() => testRequest.Abort()))
                            using (var testResponse = (HttpWebResponse)await testRequest.GetResponseAsync())
                            {
                                if (testResponse.StatusCode == HttpStatusCode.OK) return true;
                            }
                        }
                    }
                }
                catch (WebException ex)
                {
                    if (ex.Response is HttpWebResponse errorResponse)
                    {
                        if (errorResponse.StatusCode == HttpStatusCode.Unauthorized || errorResponse.StatusCode == HttpStatusCode.Forbidden)
                        {
                            // PREVENT BAN : Si 401 ou 403, mauvais mot de passe
                            throw new UnauthorizedAccessException("Mot de passe incorrect ou compte verrouillé. Arrêt immédiat pour éviter le bannissement.");
                        }

                        using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                        {
                            string errorBody = await reader.ReadToEndAsync();
                            lastErrorMessage = $"Logon refusé (HTTP {(int)errorResponse.StatusCode}). Détails : {errorBody}";
                            onProgress($"Serveur {_activeServer} ignoré : {lastErrorMessage}");
                        }
                    }
                    else
                    {
                        lastErrorMessage = ex.Message;
                        onProgress($"Erreur connexion {_activeServer} : {ex.Message}");
                    }
                    // Continue vers le serveur suivant
                }
                catch (UnauthorizedAccessException)
                {
                    throw; // Remonte l'erreur critique
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastErrorMessage = ex.Message;
                    onProgress($"Erreur inattendue {_activeServer} : {ex.Message}");
                }
            }

            throw new Exception($"Aucun serveur n'a répondu. Dernière erreur : {lastErrorMessage}");
        }

        private async Task ProcessSubmitAndWaitAsync(string jobName, Dictionary<string, string> variables, int count, Action<string> onProgress, CancellationToken cancellationToken)
        {
            string fileName = jobName.EndsWith(".JCL") ? jobName : jobName + ".JCL";
            string filePath = Path.Combine(_jclDirectory, fileName);
            if (!File.Exists(filePath)) filePath = Path.Combine(_jclDirectory, jobName);
            if (!File.Exists(filePath)) throw new FileNotFoundException($"Fichier JCL introuvable: {fileName}");

            onProgress($"\nPréparation du job {jobName}...");

            string rawContent;
            using (StreamReader reader = new StreamReader(filePath))
            {
                rawContent = await reader.ReadToEndAsync();
            }

            string correctedContent = DoCorrections(rawContent);
            string readyContent = ApplyVariables(correctedContent, variables, count);

            var (Success, JobNum, Error) = await SubmitJobAsync(readyContent, cancellationToken);
            if (!Success) throw new Exception($"Échec de soumission de {jobName}. Erreur: {Error}");

            onProgress($"Job {jobName} soumis (JOBNUM: {JobNum}). Attente de la fin de l'exécution...");

            int[] sleepDelays = { 1, 2, 3, 5, 8, 10, 15, 30, 30, 30, 30, 30, 45, 60 };
            bool finished = false;

            for (int i = 0; i < sleepDelays.Length; i++)
            {
                await Task.Delay(sleepDelays[i] * 1000, cancellationToken);
                string status = await CheckJobStatusAsync(JobNum, cancellationToken);

                if (status == "Complete")
                {
                    finished = true;
                    break;
                }
            }

            if (!finished) throw new Exception($"Le Job {JobNum} ({jobName}) prend trop de temps. L'outil arrête de l'attendre. Vérifiez manuellement dans ESCWA.");

            onProgress($"✅ Job {jobName} terminé avec succès !");
        }

        private async Task<(bool Success, string JobNum, string Error)> SubmitJobAsync(string jclContent, CancellationToken cancellationToken)
        {
            string submitUrl = $"{_nodeUrl}jescontrol/";
            var request = CreateRequest(submitUrl, "POST", submitUrl);

            var payload = new { subJes = "2", ctlSubmit = "Submit", JCLIn = jclContent };
            string jsonPayload = JsonConvert.SerializeObject(payload);

            try
            {
                using (cancellationToken.Register(() => request.Abort()))
                using (var streamWriter = new StreamWriter(await request.GetRequestStreamAsync()))
                {
                    await streamWriter.WriteAsync(jsonPayload);
                }

                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseBody = await reader.ReadToEndAsync();
                    JObject doc = JObject.Parse(responseBody);

                    if (doc.TryGetValue("JobMsg", out JToken jobMsgToken))
                    {
                        string jobNum = null;
                        bool isReady = false;
                        var errorMsg = new StringBuilder();

                        foreach (var lineToken in jobMsgToken)
                        {
                            string line = lineToken.ToString();
                            if (line.Contains("JOBNUM="))
                            {
                                var match = Regex.Match(line, @"JOBNUM=(\d+)");
                                if (match.Success) jobNum = "J" + match.Groups[1].Value;
                            }
                            if (line.Contains("Job ready for execution")) isReady = true;
                            if (line.Contains("JCL parsing error")) errorMsg.AppendLine(line);
                        }
                        if (isReady) return (true, jobNum, null);
                        return (false, null, errorMsg.ToString());
                    }
                    return (false, null, "Format de réponse JSON inattendu.");
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse errResp)
                {
                    using (var reader = new StreamReader(errResp.GetResponseStream()))
                    {
                        string err = await reader.ReadToEndAsync();
                        return (false, null, $"HTTP Error {errResp.StatusCode} : {err}");
                    }
                }
                return (false, null, ex.Message);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private async Task<string> CheckJobStatusAsync(string jobNum, CancellationToken cancellationToken)
        {
            string url = $"{_nodeUrl}jobview/{jobNum}";
            var request = CreateRequest(url, "GET", url);

            try
            {
                using (cancellationToken.Register(() => request.Abort()))
                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseBody = await reader.ReadToEndAsync();
                    JObject doc = JObject.Parse(responseBody);
                    return doc["JobStatus"]?.ToString().Trim() ?? "Unknown";
                }
            }
            catch
            {
                return "Unknown";
            }
        }

        private HttpWebRequest CreateRequest(string url, string method, string referer)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.ContentType = "application/json";
            request.Accept = "application/json, text/plain, */*";
            request.KeepAlive = false;
            request.Referer = referer;
            request.CookieContainer = _cookieContainer;

            // Timeout fixé à 30 secondes
            request.Timeout = 30000;

            // Décompression GZip
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            // Ajout des Headers exactement comme dans l'ancien code
            request.Headers.Add("accept-encoding", "gzip, deflate, br");
            request.Headers.Add("sec-fetch-dest", "empty");
            request.Headers.Add("sec-fetch-mode", "cors");
            request.Headers.Add("sec-fetch-site", "same-origin");
            request.Headers.Add("accept-language", "en-BE");
            request.Headers.Add("x-requested-with", "XMLHttpRequest");

            return request;
        }

        // =========================================================================================
        // Méthodes de parsing et de nettoyage des JCL
        // =========================================================================================

        private string DoCorrections(string content)
        {
            var output = new StringBuilder();
            using (StringReader reader = new StringReader(content))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length > 72 && int.TryParse(line.Substring(72).Trim(), out _))
                    {
                        output.AppendLine(line.Substring(0, 72));
                    }
                    else
                    {
                        output.AppendLine(line);
                    }
                }
            }

            var output2 = new StringBuilder();
            bool isFirstLine = true;
            bool existingJobcard = false;

            using (StringReader reader = new StringReader(output.ToString()))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (isFirstLine && !line.StartsWith("//*") && line.ToUpper().Contains(" JOB "))
                        existingJobcard = true;

                    if (!existingJobcard) output2.AppendLine(line);

                    if (!line.TrimEnd().EndsWith(",") && !line.StartsWith("//*"))
                        existingJobcard = false;

                    isFirstLine = false;
                }
            }

            return output2.ToString().TrimEnd('\n', '\r');
        }

        private string ApplyVariables(string content, Dictionary<string, string> vars, int count)
        {
            string env = vars.ContainsKey("ENVIMS") ? vars["ENVIMS"] : "D";
            string jobClass = vars.ContainsKey("CLASS") ? vars["CLASS"] : "A";
            string username = vars.ContainsKey("USERNAME") ? vars["USERNAME"] : Environment.UserName;

            string schenv;
            switch (env)
            {
                case "Q": schenv = "IM7C"; break;
                case "A": schenv = "IM7Q"; break;
                case "P": schenv = "IM7P"; break;
                case "D":
                default: schenv = "IM7T"; break;
            }

            string jobcard = $"//{username}{count} JOB CLASS={jobClass},SCHENV={schenv},NOTIFY={username}\r\n";
            string tempContent = jobcard + content;

            foreach (var kvp in vars)
            {
                string varName = kvp.Key.Trim().ToUpper();
                string value = kvp.Value ?? "";

                if (value == "''") value = "";
                else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 3)
                {
                    value = value.Substring(1, value.Length - 2);
                    if (value.Contains("''")) value = value.Replace("''", "'");
                }
                value = value.Replace("$", "$$");

                try
                {
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(?=%)", value);
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")([, \n\r])", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\.)", value);
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\')", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\))", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")$", value);
                }
                catch { }
            }

            var finalContent = new StringBuilder();
            using (StringReader reader = new StringReader(tempContent))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("+"))
                    {
                        if (line.Length > 1) finalContent.AppendLine(line.Substring(1));
                        else finalContent.AppendLine("");
                    }
                    else finalContent.AppendLine(line);
                }
            }

            return finalContent.ToString().TrimEnd('\n', '\r');
        }
    }
}