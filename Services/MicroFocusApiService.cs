using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoActivator.Services
{
    /// <summary>
    /// Service responsible for communicating with the MicroFocus Mainframe REST API.
    /// It handles authentication, JCL job submission, status polling, and spool retrieval.
    /// </summary>
    public class MicroFocusApiService
    {
        private static readonly CookieContainer _cookieContainer = new CookieContainer();

        private static readonly ConcurrentDictionary<string, string> _lastWorkingServers = new ConcurrentDictionary<string, string>();

        private static readonly HttpClient _httpClient;

        private readonly Dictionary<string, List<string>> _activeServers = new Dictionary<string, List<string>>()
        {
            { "D", new List<string> { "sdmfas01", "sdmfas03", "sdmfas05" } },
            { "Q", new List<string> { "sqmfas06", "sqmfas08", "sqmfas10", "sqmfas02", "sqmfas04" } },
            { "A", new List<string> { "samfas04", "samfas06", "samfas02" } },
            { "P", new List<string> { "spmfas07", "spmfas09", "spmfas01", "spmfas03", "spmfas05", "spmfas11", "spmfas13" } }
        };

        // CORRECTION MAJEURE : Ces propriétés sont passées en 'static'.
        // Elles partagent ainsi l'URL de connexion et le nom du serveur avec toutes les instances
        // du service lors d'un traitement par lot (Batch), évitant l'erreur "Not connected".
        public static string ActiveServer { get; private set; }
        private static string _nodeUrl;

        static MicroFocusApiService()
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                MaxConnectionsPerServer = 500
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.Add("accept-encoding", "gzip, deflate, br");
            _httpClient.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
            _httpClient.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
            _httpClient.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
            _httpClient.DefaultRequestHeaders.Add("accept-language", "en-BE");
            _httpClient.DefaultRequestHeaders.Add("x-requested-with", "XMLHttpRequest");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        }

        public MicroFocusApiService()
        {
            // The instance constructor is empty because configuration is handled globally.
        }

        /// <summary>
        /// Authenticates with the Mainframe network. Includes a failover mechanism that automatically
        /// switches to the next available server node if the primary one is down.
        /// </summary>
        public async Task<bool> LogonAsync(string username, string password, string env, Action<string> onProgress, CancellationToken cancellationToken)
        {
            string baseUrl = $"https://escwa{env.ToLower()}.aginsurance.intranet:10086";
            if (!_activeServers.ContainsKey(env)) throw new ArgumentException($"Unknown environment: {env}");

            string logonUrl = $"{baseUrl}/logon";

            if (_lastWorkingServers.TryGetValue(env, out string lastServer))
            {
                _nodeUrl = $"{baseUrl}/native/v1/regions/{lastServer}/86/BATCH{env}/";

                var cookies = _cookieContainer.GetCookies(new Uri(logonUrl)).Cast<Cookie>();
                var esAdminCookie = cookies.FirstOrDefault(c => c.Name.Equals("esadmin-cookie", StringComparison.OrdinalIgnoreCase));

                if (esAdminCookie != null && (esAdminCookie.Expires == DateTime.MinValue || esAdminCookie.Expires > DateTime.Now.AddMinutes(1)))
                {
                    if (await TestConnectionAsync(_nodeUrl, cancellationToken).ConfigureAwait(false))
                    {
                        ActiveServer = lastServer;
                        onProgress($"Existing session successfully reused on {ActiveServer}.");
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
                string jsonPayload = $"{{\"mfUser\":\"{username}\",\"mfNewPassword\":\"\",\"mfPassword\":\"{password}\"}}";

                try
                {
                    using (var request = CreateHttpRequest(HttpMethod.Post, logonUrl, logonUrl))
                    {
                        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                        {
                            if (response.IsSuccessStatusCode && await TestConnectionAsync(_nodeUrl, cancellationToken).ConfigureAwait(false))
                            {
                                _lastWorkingServers[env] = ActiveServer;
                                return true;
                            }
                            else if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                            {
                                throw new UnauthorizedAccessException("Incorrect password. Immediate stop to prevent account ban.");
                            }
                            else
                            {
                                lastErrorMessage = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                onProgress($"Server {ActiveServer} ignored (HTTP {(int)response.StatusCode}).");
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastErrorMessage = ex.Message;
                }
                catch (UnauthorizedAccessException) { throw; }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { lastErrorMessage = ex.Message; }
            }

            throw new Exception($"No server responded. Last error: {lastErrorMessage}");
        }

        public async Task<(bool Success, string JobNum, string Error)> SubmitJobAsync(string jclContent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_nodeUrl)) throw new InvalidOperationException("Not connected to the server. Call LogonAsync first.");

            string submitUrl = $"{_nodeUrl}jescontrol/";
            var payload = new { subJes = "2", ctlSubmit = "Submit", JCLIn = jclContent };
            string jsonPayload = JsonConvert.SerializeObject(payload);

            try
            {
                using (var request = CreateHttpRequest(HttpMethod.Post, submitUrl, submitUrl))
                {
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            return (false, null, $"HTTP Error {(int)response.StatusCode}: {responseBody}");
                        }

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

                            if (string.IsNullOrEmpty(jobNum) && isReady)
                            {
                                var fallback = Regex.Match(errorMsg.ToString(), @"[Jj]\d{5,7}");
                                if (fallback.Success) jobNum = fallback.Value.ToUpper();
                            }

                            if (isReady && !string.IsNullOrEmpty(jobNum)) return (true, jobNum, null);
                            return (false, null, string.IsNullOrEmpty(jobNum) ? "JobNum not found. \n" + errorMsg : errorMsg.ToString());
                        }
                        return (false, null, "Unexpected JSON format.");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public async Task<(string Status, string ReturnCode)> CheckJobStatusAsync(string jobNum, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(jobNum)) return ("Unknown", "Invalid JobNum");

            string url = $"{_nodeUrl}jobview/{jobNum}";

            try
            {
                using (var request = CreateHttpRequest(HttpMethod.Get, url, url))
                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    try
                    {
                        string debugFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DEBUG_API_{jobNum}.txt");
                        System.IO.File.AppendAllText(debugFilePath, "\n--- NEW VERIFICATION ---\n" + responseBody);
                    }
                    catch { /* Silently ignore if writing fails to prevent application crash */ }

                    if (!response.IsSuccessStatusCode)
                    {
                        return ("Unknown", $"HTTP Error: {(int)response.StatusCode}");
                    }

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
            catch (Exception ex)
            {
                return ("Unknown", $"Request error: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieves the business report (Spool) of a specific job to log exactly what the Mainframe did.
        /// </summary>
        public async Task<string> GetJobBusinessReportAsync(string jobNum, CancellationToken cancellationToken)
        {
            string url = $"{_nodeUrl}jobview/{jobNum}";
            string ddEntityName = null;
            string targetDdName = "";

            try
            {
                using (var request = CreateHttpRequest(HttpMethod.Get, url, url))
                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return $"HTTP Error when requesting report: {(int)response.StatusCode}";

                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    JObject doc = JObject.Parse(responseBody);

                    if (doc.TryGetValue("JobDDs", out JToken ddsToken))
                    {
                        var dd = ddsToken.FirstOrDefault(d => d["DDName"]?.ToString() == "BERPCTLO")
                              ?? ddsToken.FirstOrDefault(d => d["DDName"]?.ToString() == "SYSOUT");

                        if (dd != null)
                        {
                            ddEntityName = dd["DDEntityName"]?.ToString();
                            targetDdName = dd["DDName"]?.ToString();
                        }
                    }
                }

                if (string.IsNullOrEmpty(ddEntityName)) return "No business report file found (BERPCTLO or SYSOUT).";

                string spoolUrl = $"{_nodeUrl}spool/{ddEntityName}";
                try
                {
                    using (var spoolReq = CreateHttpRequest(HttpMethod.Get, spoolUrl, spoolUrl))
                    using (var spoolResp = await _httpClient.SendAsync(spoolReq, cancellationToken).ConfigureAwait(false))
                    {
                        spoolResp.EnsureSuccessStatusCode();
                        string spoolContent = await spoolResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return $"--- BUSINESS REPORT ({targetDdName}) OF JOB {jobNum} ---\n\n{spoolContent}";
                    }
                }
                catch (HttpRequestException)
                {
                    string ddviewUrl = $"{_nodeUrl}ddview/{ddEntityName}";
                    try
                    {
                        using (var ddviewReq = CreateHttpRequest(HttpMethod.Get, ddviewUrl, ddviewUrl))
                        using (var ddviewResp = await _httpClient.SendAsync(ddviewReq, cancellationToken).ConfigureAwait(false))
                        {
                            ddviewResp.EnsureSuccessStatusCode();
                            string ddviewContent = await ddviewResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return $"--- BUSINESS REPORT ({targetDdName}) OF JOB {jobNum} ---\n\n{ddviewContent}";
                        }
                    }
                    catch (Exception innerEx)
                    {
                        return $"Unable to download the report (Spool and DDView failed). Error: {innerEx.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Internal error during report extraction: {ex.Message}";
            }
        }

        private async Task<bool> TestConnectionAsync(string nodeUrl, CancellationToken cancellationToken)
        {
            try
            {
                string testUrl = $"{nodeUrl}region-functionality";
                using (var request = CreateHttpRequest(HttpMethod.Get, testUrl, testUrl))
                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private HttpRequestMessage CreateHttpRequest(HttpMethod method, string url, string referer)
        {
            var request = new HttpRequestMessage(method, url);

            if (!string.IsNullOrEmpty(referer))
            {
                request.Headers.Referrer = new Uri(referer);
            }

            return request;
        }
    }
}