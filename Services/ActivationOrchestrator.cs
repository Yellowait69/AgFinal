using System;
using System.Collections.Generic;
using System.IO;
using System.Text; // Ajouté pour l'encodage (Encoding.UTF8)
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
                string currentEnv = generalVariables.ContainsKey("ENV") ? generalVariables["ENV"] : "D";

                // On réduit la verbosité pour ne pas spammer l'UI en mode batch
                bool isLogged = await _apiService.LogonAsync(username, password, currentEnv, msg => {}, cancellationToken);

                if (!isLogged) throw new Exception("Impossible de se connecter au serveur MicroFocus (Vérifiez le VPN).");

                var addprctVars = new Dictionary<string, string>(generalVariables);
                foreach (var kvp in addprctSpecificVariables) addprctVars[kvp.Key] = kvp.Value;

                int jobCounter = 1;

                // Soumission Séquentielle (Obligatoire pour la cohérence des bases de données Mainframe)
                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPP06U", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPG22U", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobCounter++, onProgress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                onProgress("[ANNULATION] La séquence a été stoppée par l'utilisateur.");
                throw;
            }
            catch (Exception ex)
            {
                onProgress($"[ERREUR CRITIQUE] Chaîne interrompue: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessSubmitAndWaitAsync(string jobName, Dictionary<string, string> variables, int count, Action<string> onProgress, CancellationToken cancellationToken)
        {
            // 1. Ajout de la variable JOBNAM
            variables["JOBNAM"] = jobName;

            // Injection AP
            if (jobName == "LVPP06U" || jobName == "LVPG22U")
            {
                variables["AP"] = "LV";
            }
            else if (jobName == "LI1J04D0" || jobName == "LI1J04D2")
            {
                variables["AP"] = "LI";
            }

            // 2. Préparation du JCL
            string readyContent = await _jclProcessor.GetPreparedJclAsync(jobName, variables, count);

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

            // OPTIMISATION 1 : Fichiers "Thread-Safe" (identifiant unique GUID) + Async compatible .NET Framework
            try
            {
                string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 6);
                string debugFilePath = Path.Combine(Path.GetTempPath(), $"DEBUG_JCL_{jobName}_{uniqueId}.txt");

                // CORRECTION CS0117 ICI : Utilisation de StreamWriter asynchrone
                using (StreamWriter writer = new StreamWriter(debugFilePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(readyContent);
                }
            }
            catch { /* On ignore silencieusement les erreurs d'écriture de debug */ }

            // 3. Soumission API Micro Focus
            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(readyContent, cancellationToken);
            if (!Success) throw new Exception($"Échec de la soumission de {jobName}. Erreur:\n{Error}");

            // OPTIMISATION 2 : Polling Agressif (toutes les 2 secondes pour maximiser la vitesse)
            bool finished = false;
            int maxAttempts = 90; // 3 minutes maximum par Job (90 * 2s)

            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(2000, cancellationToken); // Attente courte constante

                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken);

                // Cas 1: Crash système JCL
                if (Status == "JCLError" || Status == "Abend")
                {
                    throw new Exception($"Crash système pour {jobName}. Statut: {Status} (Code: {ReturnCode}).");
                }

                // Cas 2: Exécution terminée
                if (Status == "Complete")
                {
                    string cleanRC = ReturnCode.TrimStart('0');
                    if (string.IsNullOrEmpty(cleanRC)) cleanRC = "0";

                    // Code d'erreur métier
                    if (cleanRC != "0" && cleanRC != "4")
                    {
                        throw new Exception($"Erreur métier sur le job {jobName} (Return code: {ReturnCode}). Séquence annulée.");
                    }

                    // Téléchargement des rapports
                    if (jobName == "ADDPRCT" || jobName == "LVPG22U" || jobName == "LI1J04D0")
                    {
                        try
                        {
                            string reportContent = await _apiService.GetJobBusinessReportAsync(JobNum, cancellationToken);

                            // OPTIMISATION 3 : ID de Job dans le nom de fichier pour éviter les conflits en Batch
                            string reportPath = Path.Combine(Path.GetTempPath(), $"REPORT_{jobName}_{JobNum}.txt");

                            // OPTIMISATION 4 : Async et Suppression de "Process.Start(notepad.exe)" pour éviter le crash PC
                            // CORRECTION CS0117 ICI : Utilisation de StreamWriter asynchrone
                            using (StreamWriter writer = new StreamWriter(reportPath, false, Encoding.UTF8))
                            {
                                await writer.WriteAsync(reportContent);
                            }
                        }
                        catch { }
                    }

                    finished = true;
                    break; // On sort de la boucle d'attente instantanément
                }
            }

            if (!finished) throw new Exception($"Le Job {JobNum} ({jobName}) a dépassé le temps d'attente maximum (Timeout).");
        }
    }
}