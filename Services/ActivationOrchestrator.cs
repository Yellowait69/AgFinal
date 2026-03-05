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

                // Soumission séquentielle des 5 Jobs critiques avec les NOUVEAUX NOMS
                await ProcessSubmitAndWaitAsync("ADDPRCT", addprctVars, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPP06U", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LVPG22U", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D0", generalVariables, jobCounter++, onProgress, cancellationToken);
                await ProcessSubmitAndWaitAsync("LI1J04D2", generalVariables, jobCounter++, onProgress, cancellationToken);

                onProgress("=== SÉQUENCE D'ACTIVATION TERMINÉE AVEC SUCCÈS ===");
            }
            catch (OperationCanceledException)
            {
                onProgress("[ANNULATION] La séquence a été arrêtée par l'utilisateur.");
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

            // 1. Ajout de la variable implicite JOBNAM requise par les scripts JCL
            variables["JOBNAM"] = jobName;

            // 2. Préparer le JCL via le service dédié
            string readyContent = await _jclProcessor.GetPreparedJclAsync(jobName, variables, count);

            // --- DÉBUT DU MOUCHARD 2 ---
            // On sauvegarde le fichier JCL complet dans votre dossier temporaire (ex: %TEMP%)
            try
            {
                string debugFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DEBUG_JCL_{jobName}.txt");
                System.IO.File.WriteAllText(debugFilePath, readyContent);
                onProgress($"[DEBUG] JCL complet généré et sauvegardé pour inspection dans : {debugFilePath}");
            }
            catch (Exception ex)
            {
                onProgress($"[DEBUG] Impossible de sauvegarder le fichier de trace JCL : {ex.Message}");
            }
            // --- FIN DU MOUCHARD 2 ---

            // 3. Soumettre via l'API Micro Focus
            var (Success, JobNum, Error) = await _apiService.SubmitJobAsync(readyContent, cancellationToken);
            if (!Success) throw new Exception($"Échec de soumission de {jobName}. Erreur:\n{Error}");

            onProgress($"Job {jobName} soumis (JOBNUM: {JobNum}). Attente des résultats...");

            // 4. Attente active (Polling) avec vérification des codes retours
            int[] sleepDelays = { 1, 2, 3, 5, 8, 10, 15, 30, 30, 30, 30, 30, 45, 60 };
            bool finished = false;

            for (int i = 0; i < sleepDelays.Length; i++)
            {
                await Task.Delay(sleepDelays[i] * 1000, cancellationToken);

                // Récupération du Status ET du ReturnCode
                var (Status, ReturnCode) = await _apiService.CheckJobStatusAsync(JobNum, cancellationToken);

                // Cas 1 : Crash système (JCL Error ou ABEND)
                if (Status == "JCLError" || Status == "Abend")
                {
                    throw new Exception($"Crash système du job {jobName}. Statut: {Status}. ARRÊT DE LA SÉQUENCE.");
                }

                // Cas 2 : Exécution terminée
                if (Status == "Complete")
                {
                    // Nettoyage des zéros initiaux ("0000" devient "0", "0008" devient "8")
                    string cleanRC = ReturnCode.TrimStart('0');
                    if (string.IsNullOrEmpty(cleanRC)) cleanRC = "0";

                    // Vérification du code retour métier (on accepte 0 ou 4)
                    if (cleanRC != "0" && cleanRC != "4")
                    {
                        throw new Exception($"Le job {jobName} s'est terminé avec une erreur métier (Code retour: {ReturnCode}). ARRÊT DE LA SÉQUENCE pour protéger l'intégrité des données.");
                    }

                    finished = true;
                    break;
                }
            }

            if (!finished) throw new Exception($"Le Job {JobNum} ({jobName}) a dépassé le délai d'attente. Vérifiez manuellement dans ESCWA.");

            onProgress($"✅ Job {jobName} terminé avec succès (RC: {JobNum}) !");
        }
    }
}