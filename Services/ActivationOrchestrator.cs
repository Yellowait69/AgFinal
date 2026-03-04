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
                onProgress("=== DÉBUT DE LA SÉQUENCE D'ACTIVATION ===");
                string currentEnv = generalVariables.ContainsKey("ENVIMS") ? generalVariables["ENVIMS"] : "D";

                onProgress($"Connexion au serveur MicroFocus ({currentEnv}000)...");
                bool isLogged = await _apiService.LogonAsync(username, password, currentEnv, onProgress, cancellationToken);

                if (!isLogged) throw new Exception("Impossible de se connecter à MicroFocus. Vérifiez le VPN.");

                onProgress($"Connecté avec succès au serveur : {_apiService.ActiveServer}");

                var addprctVars = new Dictionary<string, string>(generalVariables);
                foreach (var kvp in addprctSpecificVariables) addprctVars[kvp.Key] = kvp.Value;

                int jobCounter = 1;

                // Soumission des 5 Jobs
                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVD4PP06", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVD4PG22", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobCounter++, onProgress, cancellationToken);

                onProgress("=== SÉQUENCE D'ACTIVATION TERMINÉE ===");
            }
            catch (OperationCanceledException)
            {
                onProgress("[ANNULATION] La séquence a été annulée par l'utilisateur.");
                throw;
            }
            catch (Exception ex)
            {
                onProgress($"[ERREUR CRITIQUE] Chaîne interrompue : {ex.Message}");
                throw;
            }
        }

        private async Task ProcessSubmitAndWaitAsync(string jobName, Dictionary<string, string> variables, int count, Action<string> onProgress, CancellationToken cancellationToken)
        {
            onProgress($"\nPréparation du job {jobName}...");

            // 1. Ajout de la variable implicite JOBNAM requise par certains scripts
            variables["JOBNAM"] = jobName;

            // 2. Préparer le JCL via le service dédié
            string readyContent = await _jclProcessor.GetPreparedJclAsync(jobName, variables, count);

            // 3. Soumettre via l'API
            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(readyContent, cancellationToken);
            if (!Success) throw new Exception($"Échec de soumission de {jobName}. Erreur: {Error}");

            onProgress($"Job {jobName} soumis (JOBNUM: {JobNum}). Attente de la fin...");

            // 4. Attente (Polling)
            int[] sleepDelays = { 1, 2, 3, 5, 8, 10, 15, 30, 30, 30, 30, 30, 45, 60 };
            bool finished = false;

            for (int i = 0; i < sleepDelays.Length; i++)
            {
                await Task.Delay(sleepDelays[i] * 1000, cancellationToken);

                // Récupération du Status ET du ReturnCode
                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken);

                // Si le job a planté au niveau système
                if (Status == "JCLError" || Status == "Abend")
                {
                    throw new Exception($"Crash système du job {jobName}. Statut: {Status}. ARRÊT DE LA SÉQUENCE.");
                }

                // Si le job a fini de s'exécuter
                if (Status == "Complete")
                {
                    // Vérification CRITIQUE du code retour (souvent "0000" ou "0004" max)
                    if (ReturnCode != "0000" && ReturnCode != "0004")
                    {
                        throw new Exception($"Le job {jobName} s'est terminé avec une erreur métier (Code retour: {ReturnCode}). ARRÊT DE LA SÉQUENCE pour protéger les données.");
                    }

                    finished = true;
                    break;
                }
            }

            if (!finished) throw new Exception($"Le Job {JobNum} ({jobName}) prend trop de temps. Vérifiez dans ESCWA.");

            onProgress($"✅ Job {jobName} terminé avec succès !");
        }
    }
}