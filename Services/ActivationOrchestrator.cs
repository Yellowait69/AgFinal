using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActivator.Services
{
    public class ActivationOrchestrator
    {
        private readonly JclProcessorService _jclProcessor;
        private readonly MicroFocusApiService _apiService;

        private const int POLLING_DELAY_MS = 2000;
        private const int MAX_POLL_ATTEMPTS = 90;

        public ActivationOrchestrator(string jclDirectory)
        {
            _jclProcessor = new JclProcessorService(jclDirectory);
            _apiService = new MicroFocusApiService();
        }

        /// <summary>
        /// Lance la séquence complète d’activation (5 jobs critiques)
        /// </summary>
        public async Task RunActivationSequenceAsync(
            Dictionary<string, string> generalVariables,
            Dictionary<string, string> addprctSpecificVariables,
            string username,
            string password,
            Action<string> onProgress,
            CancellationToken cancellationToken = default,
            bool skipLogon = false)
        {
            try
            {
                string env = GetVariable(generalVariables, "ENV", "D");

                if (!skipLogon)
                {
                    onProgress($"Connexion au serveur MicroFocus ({env}000)...");

                    bool logged = await _apiService.LogonAsync(
                        username,
                        password,
                        env,
                        msg => { },
                        cancellationToken);

                    if (!logged)
                        throw new Exception("Impossible de se connecter au serveur MicroFocus.");
                }

                var addprctVars = MergeVariables(generalVariables, addprctSpecificVariables);

                int jobIndex = 1;

                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobIndex++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPP06U", generalVariables, jobIndex++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPG22U", generalVariables, jobIndex++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobIndex++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobIndex++, onProgress, cancellationToken);

                onProgress("Séquence d’activation terminée avec succès.");
            }
            catch (OperationCanceledException)
            {
                onProgress("[ANNULATION] La séquence a été stoppée.");
                throw;
            }
            catch (Exception ex)
            {
                onProgress($"[ERREUR CRITIQUE] {ex.Message}");
                throw;
            }
        }

        private async Task ProcessSubmitAndWaitAsync(
            string jobName,
            Dictionary<string, string> variables,
            int jobIndex,
            Action<string> onProgress,
            CancellationToken cancellationToken)
        {
            onProgress($"Préparation du job {jobName}...");

            PrepareJobVariables(jobName, variables);

            string readyJcl = await _jclProcessor.GetPreparedJclAsync(jobName, variables, jobIndex);

            readyJcl = ApplyJobSpecificAdjustments(jobName, readyJcl, variables);

            await WriteDebugFile(jobName, readyJcl);

            onProgress($"Soumission du job {jobName}...");

            var (success, jobNumber, error) = await _apiService.SubmitJobAsync(readyJcl, cancellationToken);

            if (!success)
                throw new Exception($"Échec soumission {jobName} : {error}");

            onProgress($"Job {jobName} soumis ({jobNumber})");

            await WaitForJobCompletion(jobName, jobNumber, onProgress, cancellationToken);
        }

        private void PrepareJobVariables(string jobName, Dictionary<string, string> vars)
        {
            vars["JOBNAM"] = jobName;

            if (jobName is "LVPP06U" or "LVPG22U")
                vars["AP"] = "LV";

            if (jobName is "LI1J04D0" or "LI1J04D2")
                vars["AP"] = "LI";
        }

        private string ApplyJobSpecificAdjustments(
            string jobName,
            string content,
            Dictionary<string, string> variables)
        {
            if (jobName == "LVPG22U")
            {
                int index = content.IndexOf("//ICEGENER IF");
                if (index != -1)
                    content = content.Substring(0, index);
            }

            if (jobName == "LI1J04D0" || jobName == "LI1J04D2")
            {
                string subJob = jobName == "LI1J04D0" ? "LI1J04D1" : "LI1J04D3";

                content = content.Replace("$JOBNAME", subJob);

                string envLetter = GetVariable(variables, "ENVIMS", "T");
                string notifyUser = GetVariable(variables, "USERNAME", "UNKNOWN");

                int dataIndex = content.IndexOf("DLM=##");

                if (dataIndex != -1)
                {
                    int lineEnd = content.IndexOf('\n', dataIndex);

                    if (lineEnd != -1)
                    {
                        string jobCard =
                            $"//{subJob} JOB CLASS=A,SCHENV=IM7{envLetter},NOTIFY={notifyUser}\r\n";

                        content = content.Insert(lineEnd + 1, jobCard);
                    }
                }
            }

            return content;
        }

        private async Task WaitForJobCompletion(
            string jobName,
            string jobNumber,
            Action<string> onProgress,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MAX_POLL_ATTEMPTS; attempt++)
            {
                await Task.Delay(POLLING_DELAY_MS, cancellationToken);

                var (status, rc) = await _apiService.CheckJobStatusAsync(jobNumber, cancellationToken);

                if (status == "JCLError" || status == "Abend")
                    throw new Exception($"Crash système {jobName} ({status}) RC={rc}");

                if (status == "Complete")
                {
                    string cleanRC = rc.TrimStart('0');
                    if (string.IsNullOrEmpty(cleanRC))
                        cleanRC = "0";

                    if (cleanRC != "0" && cleanRC != "4")
                        throw new Exception($"Erreur métier {jobName} RC={rc}");

                    onProgress($"Job {jobName} terminé RC={cleanRC}");

                    await SaveBusinessReportIfNeeded(jobName, jobNumber, cancellationToken);

                    return;
                }
            }

            throw new Exception($"Timeout pour le Job {jobName} ({jobNumber})");
        }

        private async Task SaveBusinessReportIfNeeded(
            string jobName,
            string jobNumber,
            CancellationToken cancellationToken)
        {
            if (jobName is not ("ADDPRCT" or "LVPG22U" or "LI1J04D0"))
                return;

            try
            {
                string report = await _apiService.GetJobBusinessReportAsync(jobNumber, cancellationToken);

                string path = Path.Combine(
                    Path.GetTempPath(),
                    $"REPORT_{jobName}_{jobNumber}.txt");

                await File.WriteAllTextAsync(path, report, Encoding.UTF8, cancellationToken);
            }
            catch
            {
                // volontairement silencieux
            }
        }

        private async Task WriteDebugFile(string jobName, string content)
        {
            try
            {
                string id = Guid.NewGuid().ToString("N")[..6];

                string path = Path.Combine(
                    Path.GetTempPath(),
                    $"DEBUG_JCL_{jobName}_{id}.txt");

                await File.WriteAllTextAsync(path, content, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private Dictionary<string, string> MergeVariables(
            Dictionary<string, string> baseVars,
            Dictionary<string, string> extraVars)
        {
            var result = new Dictionary<string, string>(baseVars);

            foreach (var kv in extraVars)
                result[kv.Key] = kv.Value;

            return result;
        }

        private string GetVariable(Dictionary<string, string> vars, string key, string defaultValue)
        {
            return vars.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }
}