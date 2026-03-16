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
    public class MicroFocusApiService
    {
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
                    if (await TestConnectionAsync(_nodeUrl, cancellationToken).ConfigureAwait(false))
                    {
                        ActiveServer = lastServer;
                        onProgress($"Session existante réutilisée avec succès sur {ActiveServer}.");
                        return true;
                    }
                }
            }

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
                    using (var streamWriter = new StreamWriter(await request.GetRequestStreamAsync().ConfigureAwait(false)))
                    {
                        await streamWriter.WriteAsync(jsonPayload).ConfigureAwait(false);
                    }

                    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.OK && await TestConnectionAsync(_nodeUrl, cancellationToken).ConfigureAwait(false))
                        {
                            _lastWorkingServers[env] = ActiveServer;
                            return true;
                        }
                    }
                }
                catch (WebException ex)
                {
                    // CORRECTION : Fermeture de la réponse pour libérer le port HTTP
                    if (ex.Response is HttpWebResponse errorResponse)
                    {
                        using (errorResponse)
                        {
                            if (errorResponse.StatusCode == HttpStatusCode.Unauthorized || errorResponse.StatusCode == HttpStatusCode.Forbidden)
                                throw new UnauthorizedAccessException("Mot de passe incorrect. Arrêt immédiat (Anti-Ban).");

                            using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                            {
                                lastErrorMessage = await reader.ReadToEndAsync().ConfigureAwait(false);
                                onProgress($"Serveur {ActiveServer} ignoré (HTTP {(int)errorResponse.StatusCode}).");
                            }
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
                using (var streamWriter = new StreamWriter(await request.GetRequestStreamAsync().ConfigureAwait(false)))
                    await streamWriter.WriteAsync(jsonPayload).ConfigureAwait(false);

                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseBody = await reader.ReadToEndAsync().ConfigureAwait(false);
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

                            errorMsg.AppendLine(line);
                        }

                        // Sécurité supplémentaire : Si le format Regex habituel échoue
                        if (string.IsNullOrEmpty(jobNum) && isReady)
                        {
                            var fallback = Regex.Match(errorMsg.ToString(), @"[Jj]\d{5,7}");
                            if (fallback.Success) jobNum = fallback.Value.ToUpper();
                        }

                        if (isReady && !string.IsNullOrEmpty(jobNum)) return (true, jobNum, null);
                        return (false, null, string.IsNullOrEmpty(jobNum) ? "JobNum introuvable. \n" + errorMsg : errorMsg.ToString());
                    }
                    return (false, null, "Format JSON inattendu.");
                }
            }
            catch (WebException ex) // CORRECTION : libération du port HTTP
            {
                ex.Response?.Close();
                return (false, null, ex.Message);
            }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        public async Task<(string Status, string ReturnCode)> CheckJobStatusAsync(string jobNum, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(jobNum)) return ("Unknown", "JobNum invalide");

            string url = $"{_nodeUrl}jobview/{jobNum}";
            var request = CreateRequest(url, "GET", url);

            try
            {
                using (cancellationToken.Register(() => request.Abort()))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseBody = await reader.ReadToEndAsync().ConfigureAwait(false);

                    // DÉBUT DU MOUCHARD API
                    try
                    {
                        string debugFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DEBUG_API_{jobNum}.txt");
                        System.IO.File.AppendAllText(debugFilePath, "\n--- NOUVELLE VERIFICATION ---\n" + responseBody);
                    }
                    catch { /* On ignore si l'écriture échoue pour ne pas bloquer l'appli */ }
                    // FIN DU MOUCHARD API

                    JObject doc = JObject.Parse(responseBody);

                    string GetValueCI(string key) =>
                        doc.Properties().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value?.ToString().Trim();

                    string status = GetValueCI("JobStatus") ?? "Unknown";

                    string returnCode = GetValueCI("JobCOND")
                                     ?? GetValueCI("JobRetCode")
                                     ?? GetValueCI("MaxCC")
                                     ?? GetValueCI("ReturnCode")
                                     ?? GetValueCI("RetCode")
                                     ?? "Unknown";

                    return (status, returnCode);
                }
            }
            catch (WebException ex) // CORRECTION : libération du port HTTP
            {
                ex.Response?.Close();
                return ("Unknown", $"Erreur HTTP: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ("Unknown", $"Erreur de lecture: {ex.Message}");
            }
        }

        // TÉLÉCHARGEMENT DU RAPPORT MÉTIER
        public async Task<string> GetJobBusinessReportAsync(string jobNum, CancellationToken cancellationToken)
        {
            string url = $"{_nodeUrl}jobview/{jobNum}";
            var request = CreateRequest(url, "GET", url);

            try
            {
                string ddEntityName = null;
                string targetDdName = "";

                using (cancellationToken.Register(() => request.Abort()))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string responseBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                    JObject doc = JObject.Parse(responseBody);

                    if (doc.TryGetValue("JobDDs", out JToken ddsToken))
                    {
                        // On cherche le journal de log métier COBOL (BERPCTLO) en priorité
                        var dd = ddsToken.FirstOrDefault(d => d["DDName"]?.ToString() == "BERPCTLO")
                              ?? ddsToken.FirstOrDefault(d => d["DDName"]?.ToString() == "SYSOUT");

                        if (dd != null)
                        {
                            ddEntityName = dd["DDEntityName"]?.ToString();
                            targetDdName = dd["DDName"]?.ToString();
                        }
                    }
                } // FIN DU USING : La réponse HTTP principale est bien libérée ici.

                if (string.IsNullOrEmpty(ddEntityName)) return "Aucun fichier de rapport métier trouvé (BERPCTLO ou SYSOUT).";

                // On interroge l'endpoint du Mainframe pour lire le fichier Spool (log)
                string spoolUrl = $"{_nodeUrl}spool/{ddEntityName}";
                var spoolReq = CreateRequest(spoolUrl, "GET", spoolUrl);

                try
                {
                    using (var spoolResp = (HttpWebResponse)await spoolReq.GetResponseAsync().ConfigureAwait(false))
                    using (var spoolReader = new StreamReader(spoolResp.GetResponseStream()))
                    {
                        return $"--- RAPPORT METIER ({targetDdName}) DU JOB {jobNum} ---\n\n" + await spoolReader.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
                catch (WebException ex) // CORRECTION : libération du port HTTP avant de tenter DDView
                {
                    ex.Response?.Close();

                    string ddviewUrl = $"{_nodeUrl}ddview/{ddEntityName}";
                    var ddviewReq = CreateRequest(ddviewUrl, "GET", ddviewUrl);

                    try
                    {
                        using (var ddviewResp = (HttpWebResponse)await ddviewReq.GetResponseAsync().ConfigureAwait(false))
                        using (var ddviewReader = new StreamReader(ddviewResp.GetResponseStream()))
                        {
                            return $"--- RAPPORT METIER ({targetDdName}) DU JOB {jobNum} ---\n\n" + await ddviewReader.ReadToEndAsync().ConfigureAwait(false);
                        }
                    }
                    catch (WebException innerEx)
                    {
                        innerEx.Response?.Close();
                        return "Impossible de télécharger le rapport (Spool et DDView échoués).";
                    }
                }
            }
            catch (WebException ex) // CORRECTION : libération globale du port HTTP
            {
                ex.Response?.Close();
                return $"Erreur HTTP lors de la demande de rapport: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Erreur interne lors du rapport : {ex.Message}";
            }
        }

        private async Task<bool> TestConnectionAsync(string nodeUrl, CancellationToken cancellationToken)
        {
            try
            {
                string testUrl = $"{nodeUrl}region-functionality";
                var request = CreateRequest(testUrl, "GET", testUrl);
                using (cancellationToken.Register(() => request.Abort()))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch (WebException ex) // CORRECTION : libération du port HTTP
            {
                ex.Response?.Close();
                return false;
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