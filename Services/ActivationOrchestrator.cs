using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActivator.Services
{
    /// <summary>
    /// Orchestrates the submission and monitoring of a sequence of Mainframe JCL jobs
    /// for a SINGLE contract activation.
    /// It communicates with the MicroFocus server via API to execute the business process.
    /// </summary>
    public class ActivationOrchestrator
    {
        private readonly JclProcessorService _jclProcessor;
        private readonly MicroFocusApiService _apiService;

        public ActivationOrchestrator(string jclDirectory)
        {
            _jclProcessor = new JclProcessorService(jclDirectory);
            _apiService = new MicroFocusApiService();
        }

        /// <summary>
        /// Runs the entire activation sequence, submitting jobs one by one and waiting for their successful completion.
        /// </summary>
        public async Task RunActivationSequenceAsync(
            Dictionary<string, string> generalVariables,
            Dictionary<string, string> addprctSpecificVariables,
            string username,
            string password,
            string channel,
            bool skipPrime,
            Action<string> onProgress,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string currentEnv = generalVariables.ContainsKey("ENV") ? generalVariables["ENV"] : "D";

                onProgress("1. Connecting to MicroFocus Mainframe...");
                bool isLogged = await _apiService.LogonAsync(username, password, currentEnv, onProgress, cancellationToken).ConfigureAwait(false);

                if (!isLogged)
                    throw new Exception("Unable to connect to the MicroFocus server (Check your VPN connection and credentials).");

                // Préparation des variables spécifiques au premier Job
                var addprctVars = new Dictionary<string, string>(generalVariables);
                foreach (var kvp in addprctSpecificVariables)
                {
                    addprctVars[kvp.Key] = kvp.Value;
                }

                int jobCounter = 1;
                onProgress("2. Starting JCL Sequence execution...");

                if (channel != "C05" && !skipPrime)
                {
                    await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
                    await ProcessSubmitAndWaitAsync("LVPP06U", generalVariables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
                    await ProcessSubmitAndWaitAsync("LVPG22U", generalVariables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
                }

                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobCounter++, onProgress, cancellationToken).ConfigureAwait(false);

                onProgress("3. Single Activation Sequence completed successfully!");
            }
            catch (OperationCanceledException)
            {
                onProgress("[CANCELLED] The activation sequence was stopped by the user.");
                throw;
            }
            catch (Exception ex)
            {
                // Remontée silencieuse pour le statut "Déjà actif"
                if (ex.Message == "ALREADY_ACTIVE") throw;

                onProgress($"[CRITICAL ERROR] Sequence interrupted: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Prepares the JCL with correct variables, submits it to the Mainframe, and polls the API until the job completes.
        /// </summary>
        private async Task ProcessSubmitAndWaitAsync(string jobName, Dictionary<string, string> variables, int count, Action<string> onProgress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            variables["JOBNAM"] = jobName;

            if (jobName == "LVPP06U" || jobName == "LVPG22U")
            {
                variables["AP"] = "LV";
            }
            else if (jobName == "LI1J04D0" || jobName == "LI1J04D2")
            {
                variables["AP"] = "LI";
            }

            string readyContent = await _jclProcessor.GetPreparedJclAsync(jobName, variables, count).ConfigureAwait(false);

            // Logique de nettoyage spécifique aux JCLs AG Insurance
            if (jobName == "LVPG22U")
            {
                int icegenerIndex = readyContent.IndexOf("//ICEGENER IF");
                if (icegenerIndex != -1) readyContent = readyContent.Substring(0, icegenerIndex);
            }

            if (jobName == "LI1J04D0" || jobName == "LI1J04D2")
            {
                string subJobName = (jobName == "LI1J04D0") ? "LI1J04D1" : "LI1J04D3";

                if (readyContent.Contains("$JOBNAME"))
                    readyContent = readyContent.Replace("$JOBNAME", subJobName);

                string envLetter = variables.ContainsKey("ENVIMS") ? variables["ENVIMS"] : "T";
                string notifyUser = variables.ContainsKey("USERNAME") ? variables["USERNAME"] : "XA3894";

                string dataMarker = "DLM=##";
                int dataIndex = readyContent.IndexOf(dataMarker);

                if (dataIndex != -1)
                {
                    int endOfLineIndex = readyContent.IndexOf('\n', dataIndex);
                    if (endOfLineIndex != -1)
                    {
                        string jobCardToInject = $"//{subJobName} JOB CLASS=A,SCHENV=IM7{envLetter},NOTIFY={notifyUser}\r\n";
                        readyContent = readyContent.Insert(endOfLineIndex + 1, jobCardToInject);
                    }
                }
            }

            // Sauvegarde de debug locale du JCL généré
            try
            {
                string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 6);
                string debugFilePath = Path.Combine(Path.GetTempPath(), $"DEBUG_JCL_{jobName}_{uniqueId}.txt");

                using (StreamWriter writer = new StreamWriter(debugFilePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(readyContent).ConfigureAwait(false);
                }
            }
            catch { /* Silently ignore debug file write errors to not crash the business flow */ }

            // Soumission au Mainframe
            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(readyContent, cancellationToken).ConfigureAwait(false);
            if (!Success)
                throw new Exception($"Failed to submit job {jobName}. Error:\n{Error}");

            onProgress($"   -> [Running] {jobName} (ID: {JobNum}) submitted...");

            bool finished = false;
            int maxAttempts = 300; // Equivaut à 10 minutes de polling maximum par job (300 * 2s)

            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);

                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken).ConfigureAwait(false);

                if (Status == "JCLError" || Status == "Abend" || Status == "Failed")
                {
                    throw new Exception($"System crash for job {jobName}. Status: {Status} (Code: {ReturnCode}).");
                }

                if (Status == "Complete" || Status == "Output" || Status == "Done")
                {
                    string cleanRC = ReturnCode.TrimStart('0');
                    if (string.IsNullOrEmpty(cleanRC)) cleanRC = "0";

                    // RC = 0 ou 4 sont considérés comme des succès métier
                    if (cleanRC != "0" && cleanRC != "4")
                    {
                        if (jobName == "ADDPRCT" && cleanRC == "8")
                        {
                            throw new Exception("ALREADY_ACTIVE");
                        }

                        throw new Exception($"Business error on job {jobName} (Return code: {ReturnCode}). Sequence cancelled.");
                    }

                    onProgress($"      [OK] {jobName} completed with RC={ReturnCode}.");

                    // Téléchargement du Spool pour audit
                    if (jobName == "ADDPRCT" || jobName == "LVPG22U" || jobName == "LI1J04D0")
                    {
                        try
                        {
                            string reportContent = await _apiService.GetJobBusinessReportAsync(JobNum, cancellationToken).ConfigureAwait(false);
                            string reportPath = Path.Combine(Path.GetTempPath(), $"REPORT_{jobName}_{JobNum}.txt");

                            using (StreamWriter writer = new StreamWriter(reportPath, false, Encoding.UTF8))
                            {
                                await writer.WriteAsync(reportContent).ConfigureAwait(false);
                            }
                        }
                        catch { /* Ignore if spool fetching fails, job was still technically successful */ }
                    }

                    finished = true;
                    break;
                }
            }

            if (!finished)
                throw new Exception($"Job {JobNum} ({jobName}) exceeded the maximum wait time (Timeout).");
        }
    }
}