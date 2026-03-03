using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoActivator.Services
{
    public class ActivationService
    {
        private readonly string _jclDirectory;
        private HttpClient _httpClient;
        private CookieContainer _cookieContainer;
        private string _activeServer;
        private string _currentEnv;
        private string _baseUrl;
        private string _nodeUrl;

        // Dictionnaire des serveurs actifs par environnement (récupéré de ton outil interne AGConnector)
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
        }

        /// <summary>
        /// Orchestre la séquence complète d'activation.
        /// </summary>
        public async Task RunActivationSequenceAsync(Dictionary<string, string> generalVariables, Dictionary<string, string> addprctSpecificVariables, string username, string password, Action<string> onProgress)
        {
            try
            {
                onProgress("=== DÉBUT DE LA SÉQUENCE D'ACTIVATION ===");

                _currentEnv = generalVariables.ContainsKey("ENVIMS") ? generalVariables["ENVIMS"] : "D";

                // 1. Authentification MicroFocus (Logon)
                onProgress($"Connexion au serveur MicroFocus ({_currentEnv}000)...");
                bool isLogged = await LogonAsync(username, password, _currentEnv);
                if (!isLogged) throw new Exception("Impossible de se connecter au serveur MicroFocus. Vérifiez vos identifiants ou l'état des serveurs.");

                onProgress($"Connecté avec succès au serveur : {_activeServer}");

                // 2. Préparation des variables
                var addprctVars = new Dictionary<string, string>(generalVariables);
                foreach (var kvp in addprctSpecificVariables) addprctVars[kvp.Key] = kvp.Value;

                int jobCounter = 1;

                // 3. Soumission et Attente des 5 Jobs dans l'ordre
                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress);
                await ProcessSubmitAndWaitAsync("LVD4PP06", generalVariables, jobCounter++, onProgress);
                await ProcessSubmitAndWaitAsync("LVD4PG22", generalVariables, jobCounter++, onProgress);
                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobCounter++, onProgress);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobCounter++, onProgress);

                onProgress("=== SÉQUENCE D'ACTIVATION TERMINÉE ===");
            }
            catch (Exception)
            {
                onProgress("[ERREUR CRITIQUE] La chaîne d'activation a été interrompue.");
                throw;
            }
        }

        private async Task<bool> LogonAsync(string username, string password, string env)
        {
            _baseUrl = $"https://escwa{env.ToLower()}.aginsurance.intranet:10086";

            if (!_activeServers.ContainsKey(env)) throw new Exception($"Environnement inconnu: {env}");

            // Teste les serveurs de l'environnement un par un jusqu'à en trouver un actif
            foreach (var server in _activeServers[env])
            {
                _activeServer = server;
                _nodeUrl = $"{_baseUrl}/native/v1/regions/{_activeServer}/86/BATCH{env}/";

                _cookieContainer = new CookieContainer();
                var handler = new HttpClientHandler
                {
                    CookieContainer = _cookieContainer,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true // Ignore les erreurs SSL internes si besoin
                };

                _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

                var payload = new { mfUser = username, mfNewPassword = "", mfPassword = password };

                // Utilisation de Newtonsoft.Json
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                AddCommonHeaders(content.Headers);

                try
                {
                    var response = await _httpClient.PostAsync($"{_baseUrl}/logon", content);
                    if (response.IsSuccessStatusCode)
                    {
                        // Test de la région pour confirmer que ce serveur fonctionne bien
                        var testRequest = new HttpRequestMessage(HttpMethod.Get, $"{_nodeUrl}region-functionality");
                        AddCommonHeaders(testRequest.Headers);
                        var testResponse = await _httpClient.SendAsync(testRequest);

                        if (testResponse.IsSuccessStatusCode) return true; // Ce serveur est le bon !
                    }
                }
                catch
                {
                    // Si ce serveur plante, la boucle continue et tente le serveur suivant
                    continue;
                }
            }
            return false; // Tous les serveurs sont KO
        }

        private async Task ProcessSubmitAndWaitAsync(string jobName, Dictionary<string, string> variables, int count, Action<string> onProgress)
        {
            // 1. Lecture et corrections du JCL
            string fileName = jobName.EndsWith(".JCL") ? jobName : jobName + ".JCL";
            string filePath = Path.Combine(_jclDirectory, fileName);
            if (!File.Exists(filePath)) filePath = Path.Combine(_jclDirectory, jobName);
            if (!File.Exists(filePath)) throw new FileNotFoundException($"Fichier JCL introuvable: {fileName}");

            onProgress($"\nPréparation du job {jobName}...");
            string rawContent = await File.ReadAllTextAsync(filePath);
            string correctedContent = DoCorrections(rawContent);
            string readyContent = ApplyVariables(correctedContent, variables, count);

            // 2. Soumission
            var (Success, JobNum, Error) = await SubmitJobAsync(readyContent);
            if (!Success) throw new Exception($"Échec de soumission de {jobName}. Erreur: {Error}");

            onProgress($"Job {jobName} soumis (JOBNUM: {JobNum}). Attente de la fin de l'exécution...");

            // 3. Polling (Attente de la fin du Job)
            int[] sleepDelays = { 1, 2, 3, 5, 8, 10, 15, 30, 30, 30, 30, 30, 45, 60 };
            bool finished = false;

            for (int i = 0; i < sleepDelays.Length; i++)
            {
                await Task.Delay(sleepDelays[i] * 1000); // Pause
                string status = await CheckJobStatusAsync(JobNum);

                if (status == "Complete")
                {
                    finished = true;
                    break;
                }
            }

            if (!finished) throw new Exception($"Le Job {JobNum} ({jobName}) prend trop de temps. L'outil arrête de l'attendre. Vérifiez manuellement dans ESCWA.");

            onProgress($"✅ Job {jobName} terminé avec succès !");
        }

        private async Task<(bool Success, string JobNum, string Error)> SubmitJobAsync(string jclContent)
        {
            var payload = new { subJes = "2", ctlSubmit = "Submit", JCLIn = jclContent };
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_nodeUrl}jescontrol/");

            // Utilisation de Newtonsoft.Json
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            AddCommonHeaders(request.Content.Headers);
            _httpClient.DefaultRequestHeaders.Referrer = new Uri($"{_nodeUrl}jescontrol/");

            try
            {
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (false, null, $"HTTP Error {response.StatusCode}");

                string responseBody = await response.Content.ReadAsStringAsync();

                // Utilisation de JObject (Newtonsoft)
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
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private async Task<string> CheckJobStatusAsync(string jobNum)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_nodeUrl}jobview/{jobNum}");
            AddCommonHeaders(request.Headers);
            _httpClient.DefaultRequestHeaders.Referrer = new Uri($"{_nodeUrl}jobview/{jobNum}");

            try
            {
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return "Unknown";

                string responseBody = await response.Content.ReadAsStringAsync();

                // Utilisation de JObject (Newtonsoft)
                JObject doc = JObject.Parse(responseBody);
                return doc["JobStatus"]?.ToString().Trim() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private void AddCommonHeaders(System.Net.Http.Headers.HttpHeaders headers)
        {
            headers.Add("accept-encoding", "gzip, deflate, br");
            headers.Add("sec-fetch-dest", "empty");
            headers.Add("sec-fetch-mode", "cors");
            headers.Add("sec-fetch-site", "same-origin");
            headers.Add("accept-language", "en-BE");
            headers.Add("x-requested-with", "XMLHttpRequest");
        }

        // =========================================================================================
        // Méthodes de parsing et de nettoyage des JCL (Issues du code historique)
        // =========================================================================================

        private string DoCorrections(string content)
        {
            var output = new StringBuilder();

            // Coupure à 72 caractères si la suite est un numéro de séquence
            foreach (string line in content.Replace("\r", "").Split('\n'))
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

            // Suppression de l'ancienne JobCard
            var output2 = new StringBuilder();
            var lines = output.ToString().Replace("\r", "").Split('\n').ToList();
            bool existingJobcard = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i == 0 && !line.StartsWith("//*") && line.ToUpper().Contains(" JOB "))
                    existingJobcard = true;

                if (!existingJobcard) output2.AppendLine(line);

                // Fin de la JobCard quand il n'y a plus de virgule à la fin
                if (!line.Trim().EndsWith(",") && !line.StartsWith("//*"))
                    existingJobcard = false;
            }

            return output2.ToString().TrimEnd('\n', '\r');
        }

        private string ApplyVariables(string content, Dictionary<string, string> vars, int count)
        {
            var contentBuilder = new StringBuilder();
            foreach (string line in content.Replace("\r", "").Split('\n')) contentBuilder.AppendLine(line);

            // Récupération des paramètres pour la JobCard
            string env = vars.ContainsKey("ENVIMS") ? vars["ENVIMS"] : "D";
            string jobClass = vars.ContainsKey("CLASS") ? vars["CLASS"] : "A";
            string username = vars.ContainsKey("USERNAME") ? vars["USERNAME"] : Environment.UserName;

            // Environnement cible
            string schenv = env switch
            {
                "D" => "IM7T",
                "Q" => "IM7C",
                "A" => "IM7Q",
                "P" => "IM7P",
                _ => "IM7T"
            };

            // Insertion de la nouvelle JobCard
            string jobcard = $"//{username}{count} JOB CLASS={jobClass},SCHENV={schenv},NOTIFY={username}\r\n";
            contentBuilder.Insert(0, jobcard);

            string tempContent = contentBuilder.ToString().Replace("\r\n\r\n", "\r\n").TrimEnd('\n', '\r');

            // Remplacement des variables
            foreach (var kvp in vars)
            {
                string varName = kvp.Key.Trim().ToUpper();
                string value = kvp.Value ?? "";

                // Échappement des quotes exactement comme l'original
                if (value == "''") value = "";
                else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 3)
                {
                    value = value.Substring(1, value.Length - 2);
                    if (value.Contains("''")) value = value.Replace("''", "'");
                }
                value = value.Replace("$", "$$");

                try
                {
                    // Les 6 Regex originales pour gérer les ponctuations adjacentes
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(?=%)", value);
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")([, \n\r])", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\.)", value);
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\')", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\))", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")$", value);
                }
                catch { /* Ignore regex errors silently */ }
            }

            // Fix temporaire pour les "+" en début de ligne
            var finalContent = new StringBuilder();
            foreach (string line in tempContent.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("+"))
                {
                    if (line.Length > 1) finalContent.AppendLine(line.Substring(1));
                    else finalContent.AppendLine("");
                }
                else finalContent.AppendLine(line);
            }

            return finalContent.ToString().TrimEnd('\n', '\r');
        }
    }
}