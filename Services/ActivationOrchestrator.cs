using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActivator.Services
{
    public class ActivationOrchestrator
    {
        private readonly JclProcessorService _jclProcessor;
        private readonly MicroFocusApiService _apiService;

        public ActivationOrchestrator(string jclDirectory)
        {
            _jclProcessor = new JclProcessorService(jclDirectory);
            _apiService = new MicroFocusApiService();
        }

        public async Task RunActivationSequenceAsync(Dictionary<string, string> generalVariables, Dictionary<string, string> addprctSpecificVariables, string username, string password, Action<string> onProgress, CancellationToken cancellationToken = default)
        {
            try
            {
                onProgress("=== START OF ACTIVATION SEQUENCE ===");

                // CORRECTION ICI : On utilise "ENV" (qui vaut D, Q, A, ou P) pour le serveur API
                string currentEnv = generalVariables.ContainsKey("ENV") ? generalVariables["ENV"] : "D";

                onProgress($"Connecting to MicroFocus server ({currentEnv}000)...");
                bool isLogged = await _apiService.LogonAsync(username, password, currentEnv, onProgress, cancellationToken);

                if (!isLogged) throw new Exception("Unable to connect to MicroFocus. Please check your VPN.");

                onProgress($"Successfully connected to server: {_apiService.ActiveServer}");

                var addprctVars = new Dictionary<string, string>(generalVariables);
                foreach (var kvp in addprctSpecificVariables) addprctVars[kvp.Key] = kvp.Value;

                int jobCounter = 1;

                // Sequential submission of the 5 critical Jobs
                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPP06U", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPG22U", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobCounter++, onProgress, cancellationToken);

                onProgress("=== ACTIVATION SEQUENCE COMPLETED SUCCESSFULLY ===");
            }
            catch (OperationCanceledException)
            {
                onProgress("[CANCELLATION] The sequence was stopped by the user.");
                throw;
            }
            catch (Exception ex)
            {
                onProgress($"[CRITICAL ERROR] Chain interrupted: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessSubmitAndWaitAsync(string jobName, Dictionary<string, string> variables, int count, Action<string> onProgress, CancellationToken cancellationToken)
        {
            onProgress($"\nPreparing job {jobName}...");

            // 1. Addition of the implicit JOBNAM variable required by the JCL scripts
            variables["JOBNAM"] = jobName;

            // Injecting the AP variable based on the current job
            if (jobName == "LVPP06U" || jobName == "LVPG22U")
            {
                variables["AP"] = "LV";
            }
            else if (jobName == "LI1J04D0" || jobName == "LI1J04D2")
            {
                variables["AP"] = "LI";
            }

            // 2. Prepare the JCL via the dedicated service
            string readyContent = await _jclProcessor.GetPreparedJclAsync(jobName, variables, count);

            if (jobName == "LVPG22U")
            {
                int icegenerIndex = readyContent.IndexOf("//ICEGENER IF");
                if (icegenerIndex != -1)
                {
                    readyContent = readyContent.Substring(0, icegenerIndex);
                }
            }

            if (jobName == "LI1J04D0" || jobName == "LI1J04D2")
            {
                string subJobName = (jobName == "LI1J04D0") ? "LI1J04D1" : "LI1J04D3";

                if (readyContent.Contains("$JOBNAME"))
                {
                    readyContent = readyContent.Replace("$JOBNAME", subJobName);
                }

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

            try
            {
                string debugFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DEBUG_JCL_{jobName}.txt");
                System.IO.File.WriteAllText(debugFilePath, readyContent);
                onProgress($"[DEBUG] Full JCL generated and saved for inspection at: {debugFilePath}");
            }
            catch (Exception ex)
            {
                onProgress($"[DEBUG] Unable to save the JCL trace file: {ex.Message}");
            }

            // 3. Submit via the Micro Focus API
            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(readyContent, cancellationToken);
            if (!Success) throw new Exception($"Failed to submit {jobName}. Error:\n{Error}");

            onProgress($"Job {jobName} submitted (JOBNUM: {JobNum}). Waiting for results...");

            // 4. Active waiting (Polling) with return code verification
            int[] sleepDelays = { 1, 2, 3, 5, 8, 10, 15, 30, 30, 30, 30, 30, 45, 60 };
            bool finished = false;

            for (int i = 0; i < sleepDelays.Length; i++)
            {
                await Task.Delay(sleepDelays[i] * 1000, cancellationToken);

                // Retrieval of Status AND ReturnCode
                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken);

                // Case 1: System crash (JCL Error or ABEND)
                if (Status == "JCLError" || Status == "Abend")
                {
                    throw new Exception($"System crash for job {jobName}. Status: {Status} (Code: {ReturnCode}). SEQUENCE STOPPED.");
                }

                // Case 2: Execution completed
                if (Status == "Complete")
                {
                    // Cleaning leading zeros ("0000" becomes "0", "0008" becomes "8")
                    string cleanRC = ReturnCode.TrimStart('0');
                    if (string.IsNullOrEmpty(cleanRC)) cleanRC = "0";

                    // Verification of the business return code (accepting 0 or 4)
                    if (cleanRC != "0" && cleanRC != "4")
                    {
                        throw new Exception($"Job {jobName} ended with a business error (Return code: {ReturnCode}). SEQUENCE STOPPED to protect data integrity.");
                    }

                    if (jobName == "ADDPRCT" || jobName == "LVPG22U" || jobName == "LI1J04D0")
                    {
                        onProgress($"[INFO] Downloading business report for {jobName}...");
                        string reportContent = await _apiService.GetJobBusinessReportAsync(JobNum, cancellationToken);

                        try
                        {
                            string reportPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"REPORT_{jobName}.txt");
                            System.IO.File.WriteAllText(reportPath, reportContent);

                            // Ouverture du bloc-note (mouchard) retirée ici
                            onProgress($" Report for {jobName} downloaded successfully !");
                        }
                        catch { }
                    }

                    finished = true;
                    break;
                }
            }

            if (!finished) throw new Exception($"Job {JobNum} ({jobName}) exceeded the timeout. Please check manually in ESCWA.");

            onProgress($" Job {jobName} completed successfully (RC: {JobNum}) !");
        }
    }
}