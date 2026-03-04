using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoActivator.Services
{
    public class MicroFocusApiService
    {
        // STATIC : Permet de conserver la session et le meilleur serveur entre les instances
        private static readonly CookieContainer _cookieContainer = new CookieContainer();
        private static readonly Dictionary<string, string> _lastWorkingServers = new Dictionary<string, string>();

        private readonly Dictionary<string, List<string>> _activeServers = new Dictionary<string, List<string>>()
        {
            { "D", new List<string> { "sdmfas01", "sdmfas03", "sdmfas05" } },
            { "Q", new List<string> { "sqmfas06", "sqmfas08", "sqmfas10", "sqmfas02", "sqmfas04" } },
            { "A", new List<string> { "samfas04", "samfas06", "samfas02" } },
            { "P", new List<string> { "spmfas07", "spmfas09", "spmfas01", "spmfas03", "spmfas05", "spmfas11", "spmfas13" } }
        };

        public string ActiveServer { get; private set; }
        private string _nodeUrl;

        public MicroFocusApiService()
        {
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        }

        public async Task<bool> LogonAsync(string username, string password, string env, Action<string> onProgress, CancellationToken cancellationToken)
        {
            string baseUrl = $"https://escwa{env.ToLower()}.aginsurance.intranet:10086";
            if (!_activeServers.ContainsKey(env)) throw new ArgumentException($"Environnement inconnu: {env}");

            string logonUrl = $"{baseUrl}/logon";

            // 1. Tenter de réutiliser la session existante
            if (_lastWorkingServers.TryGetValue(env, out string lastServer))
            {
                _nodeUrl = $"{baseUrl}/native/v1/regions/{lastServer}/86/BATCH{env}/";
                Cookie esAdminCookie = null;

                foreach (Cookie c in _cookieContainer.GetCookies(new Uri(logonUrl)))
                {
                    if (c.Name.Equals("esadmin-cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        esAdminCookie = c;
                        break;
                    }
                }

                if (esAdminCookie != null && (esAdminCookie.Expires == DateTime.MinValue || esAdminCookie.Expires > DateTime.Now.AddMinutes(1)))
                {
                    if (await TestConnectionAsync(_nodeUrl, cancellationToken))
                    {
                        ActiveServer = lastServer;
                        onProgress($"Session existante réutilisée avec succès sur {ActiveServer}.");
                        return true;
                    }
                }
            }

            // 2. Connexion classique avec Failover
            string lastErrorMessage = "";

            foreach (var server in _activeServers[env])
            {
                cancellationToken.ThrowIfCancellationRequested();

                ActiveServer = server;
                _nodeUrl = $"{baseUrl}/native/v1/regions/{ActiveServer}/86/BATCH{env}/";
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
                        if (response.StatusCode == HttpStatusCode.OK && await TestConnectionAsync(_nodeUrl, cancellationToken))
                        {
                            _lastWorkingServers[env] = ActiveServer;
                            return true;
                        }
                    }
                }
                catch (WebException ex)
                {
                    if (ex.Response is HttpWebResponse errorResponse)
                    {
                        if (errorResponse.StatusCode == HttpStatusCode.Unauthorized || errorResponse.StatusCode == HttpStatusCode.Forbidden)
                            throw new UnauthorizedAccessException("Mot de passe incorrect. Arrêt immédiat (Anti-Ban).");

                        using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                        {
                            lastErrorMessage = await reader.ReadToEndAsync();
                            onProgress($"Serveur {ActiveServer} ignoré (HTTP {(int)errorResponse.StatusCode}).");
                        }
                    }
                    else lastErrorMessage = ex.Message;
                }
                catch (UnauthorizedAccessException) { throw; }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { lastErrorMessage = ex.Message; }
            }

            throw new Exception($"Aucun serveur n'a répondu. Dernière erreur : {lastErrorMessage}");
        }

        public async Task<(bool Success, string JobNum, string Error)> SubmitJobAsync(string jclContent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_nodeUrl)) throw new InvalidOperationException("Non connecté au serveur. Appelez LogonAsync d'abord.");

            string submitUrl = $"{_nodeUrl}jescontrol/";
            var request = CreateRequest(submitUrl, "POST", submitUrl);
            var payload = new { subJes = "2", ctlSubmit = "Submit", JCLIn = jclContent };
            string jsonPayload = JsonConvert.SerializeObject(payload);

            try
            {
                using (cancellationToken.Register(() => request.Abort()))
                using (var streamWriter = new StreamWriter(await request.GetRequestStreamAsync()))
                    await streamWriter.WriteAsync(jsonPayload);

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
                            var match = Regex.Match(line, @"JOBNUM=(\d+)");
                            if (match.Success) jobNum = "J" + match.Groups[1].Value;
                            if (line.Contains("Job ready for execution")) isReady = true;
                            if (line.Contains("JCL parsing error")) errorMsg.AppendLine(line);
                        }
                        if (isReady) return (true, jobNum, null);
                        return (false, null, errorMsg.ToString());
                    }
                    return (false, null, "Format JSON inattendu.");
                }
            }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        // MODIFICATION : Retourne désormais un Tuple avec le Status ET le ReturnCode
        public async Task<(string Status, string ReturnCode)> CheckJobStatusAsync(string jobNum, CancellationToken cancellationToken)
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

                    string status = doc["JobStatus"]?.ToString().Trim() ?? "Unknown";

                    // Selon la version d'ESCWA, le code retour peut avoir un nom différent.
                    // On tente de récupérer "JobRetCode", sinon "MaxCC", sinon "RetCode".
                    string returnCode = doc["JobRetCode"]?.ToString().Trim()
                                     ?? doc["MaxCC"]?.ToString().Trim()
                                     ?? doc["RetCode"]?.ToString().Trim()
                                     ?? "Unknown";

                    return (status, returnCode);
                }
            }
            catch
            {
                return ("Unknown", "Unknown");
            }
        }

        private async Task<bool> TestConnectionAsync(string nodeUrl, CancellationToken cancellationToken)
        {
            try
            {
                string testUrl = $"{nodeUrl}region-functionality";
                var request = CreateRequest(testUrl, "GET", testUrl);
                using (cancellationToken.Register(() => request.Abort()))
                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch { return false; }
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
            request.Timeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            request.Headers.Add("accept-encoding", "gzip, deflate, br");
            request.Headers.Add("sec-fetch-dest", "empty");
            request.Headers.Add("sec-fetch-mode", "cors");
            request.Headers.Add("sec-fetch-site", "same-origin");
            request.Headers.Add("accept-language", "en-BE");
            request.Headers.Add("x-requested-with", "XMLHttpRequest");

            return request;
        }
    }
}