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

                // Utilisation de "ENV" (qui vaut D, Q, A, ou P) pour le serveur API
                string currentEnv = generalVariables.ContainsKey("ENV") ? generalVariables["ENV"] : "D";

                onProgress($"Connecting to MicroFocus server ({currentEnv}000)...");
                bool isLogged = await _apiService.LogonAsync(username, password, currentEnv, onProgress, cancellationToken);

                if (!isLogged) throw new Exception("Unable to connect to MicroFocus. Please check your VPN.");

                onProgress($"Successfully connected to server: {_apiService.ActiveServer}");

                var addprctVars = new Dictionary<string, string>(generalVariables);
                foreach (var kvp in addprctSpecificVariables) addprctVars[kvp.Key] = kvp.Value;

                int jobCounter = 1;

                // Soumission séquentielle des 5 Jobs critiques
                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPP06U", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPG22U", generalVariables, jobCounter++, onProgress, cancellationToken);

                // Exécution normale (sans désencapsulation) pour laisser le Mainframe gérer l'Internal Reader
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

            // 1. Ajout de la variable implicite JOBNAM requise par les scripts JCL
            variables["JOBNAM"] = jobName;

            // Injection de la variable AP basée sur le job en cours
            if (jobName == "LVPP06U" || jobName == "LVPG22U")
            {
                variables["AP"] = "LV";
            }
            else if (jobName == "LI1J04D0" || jobName == "LI1J04D2")
            {
                variables["AP"] = "LI";
            }

            // 2. Préparation du JCL via le service dédié
            string readyContent = await _jclProcessor.GetPreparedJclAsync(jobName, variables, count);

            if (jobName == "LVPG22U")
            {
                int icegenerIndex = readyContent.IndexOf("//ICEGENER IF");
                if (icegenerIndex != -1)
                {
                    readyContent = readyContent.Substring(0, icegenerIndex);
                }
            }

            // --- LE BLOC DE DÉSENCAPSULATION POUR LI1J04D0 A ÉTÉ SUPPRIMÉ D'ICI ---
            // Le JCL LI1J04D0 va maintenant tourner en entier avec toutes ses allocations.

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

            // 3. Soumission via l'API Micro Focus
            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(readyContent, cancellationToken);
            if (!Success) throw new Exception($"Failed to submit {jobName}. Error:\n{Error}");

            onProgress($"Job {jobName} submitted (JOBNUM: {JobNum}). Waiting for results...");

            // 4. Attente active (Polling) avec vérification du code retour
            int[] sleepDelays = { 1, 2, 3, 5, 8, 10, 15, 30, 30, 30, 30, 30, 45, 60 };
            bool finished = false;
            string finalReturnCode = "";

            for (int i = 0; i < sleepDelays.Length; i++)
            {
                await Task.Delay(sleepDelays[i] * 1000, cancellationToken);

                // Récupération du statut ET du ReturnCode
                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken);

                // Cas 1 : Crash système (JCL Error ou ABEND)
                if (Status == "JCLError" || Status == "Abend")
                {
                    throw new Exception($"System crash for job {jobName}. Status: {Status} (Code: {ReturnCode}). SEQUENCE STOPPED.");
                }

                // Cas 2 : Exécution terminée
                if (Status == "Complete")
                {
                    // Nettoyage des zéros inutiles
                    string cleanRC = ReturnCode.TrimStart('0');
                    if (string.IsNullOrEmpty(cleanRC)) cleanRC = "0";
                    finalReturnCode = cleanRC;

                    // Vérification du code retour métier (accepte 0 ou 4)
                    if (cleanRC != "0" && cleanRC != "4")
                    {
                        throw new Exception($"Job {jobName} ended with a business error (Return code: {ReturnCode}). SEQUENCE STOPPED to protect data integrity.");
                    }

                    // On écoute le rapport de LI1J04D0 (et plus LI1J04D1)
                    if (jobName == "ADDPRCT" || jobName == "LVPG22U" || jobName == "LI1J04D0")
                    {
                        onProgress($"[INFO] Downloading business report for {jobName}...");
                        string reportContent = await _apiService.GetJobBusinessReportAsync(JobNum, cancellationToken);

                        try
                        {
                            string reportPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"REPORT_{jobName}.txt");
                            System.IO.File.WriteAllText(reportPath, reportContent);
                            System.Diagnostics.Process.Start("notepad.exe", reportPath);
                            onProgress($" Report for {jobName} opened !");
                        }
                        catch { }
                    }

                    finished = true;
                    break;
                }
            }

            if (!finished) throw new Exception($"Job {JobNum} ({jobName}) exceeded the timeout. Please check manually in ESCWA.");

            onProgress($" Job {jobName} completed successfully (RC: {finalReturnCode}) !");
        }
    }
}