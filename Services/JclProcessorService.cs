using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoActivator.Config;

namespace AutoActivator.Services
{
    public enum DsnDirection
    {
        Read,
        Write
    }

    public enum TransferMode
    {
        Text,
        Binary
    }

    public class JclProcessorService
    {
        private readonly string _jclDirectory;

        private static readonly string TempShare = "FILES.FIBE.FORTIS";
        private static readonly string WriteTempFolder = @"\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\FTP-Write\";
        private static readonly string ReadTempFolder = @"\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\FTP-Read\";

        public JclProcessorService(string jclDirectory)
        {
            _jclDirectory = jclDirectory;
        }

        // =========================================================================
        // PARTIE 1 : TRAITEMENT DES FICHIERS JCL CLASSIQUES
        // =========================================================================

        public async Task<string> GetPreparedJclAsync(string jobName, Dictionary<string, string> variables, int count)
        {
            string fileName = jobName.EndsWith(".JCL", StringComparison.OrdinalIgnoreCase) ? jobName : jobName + ".JCL";
            string filePath = Path.Combine(_jclDirectory, fileName);

            if (!File.Exists(filePath)) filePath = Path.Combine(_jclDirectory, jobName);
            if (!File.Exists(filePath)) throw new FileNotFoundException($"Fichier JCL introuvable: {fileName}");

            string rawContent;
            using (StreamReader reader = new StreamReader(filePath))
            {
                rawContent = await reader.ReadToEndAsync();
            }

            // Étape 1 : Nettoyage d'origine basé sur l'app source (LvChainTool)
            string correctedContent = DoCorrections(rawContent);

            // Étape 2 : Application récursive des variables et suppression des sauts de ligne fantômes
            return ApplyVariables(correctedContent, variables, count);
        }

        /// <summary>
        /// Nettoyage calqué sur le comportement d'origine de l'application source
        /// </summary>
        private string DoCorrections(string content)
        {
            StringBuilder output = new StringBuilder();

            // 1. Coupure stricte à 72 caractères UNIQUEMENT s'il s'agit de numéros de séquence (colonnes 73-80)
            foreach (String line in content.Replace("\r", "").Split('\n'))
            {
                if (line.Length > 72 && int.TryParse(line.Substring(72).Trim(), out int _))
                {
                    output.AppendLine(line.Substring(0, 72));
                }
                else
                {
                    output.AppendLine(line);
                }
            }

            // 2. Suppression de la JobCard existante pour éviter les doublons avec celle que l'on va générer
            StringBuilder output2 = new StringBuilder();
            List<String> lines = new List<string>(output.ToString().Replace("\r", "").Split('\n'));
            bool existingJobcard = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                // Détection du début de la JobCard d'origine
                if (i == 0 && !line.StartsWith("//*") && line.ToUpper().Contains(" JOB "))
                {
                    existingJobcard = true;
                }

                if (!existingJobcard)
                {
                    output2.AppendLine(line);
                }

                // Pas de virgule à la fin = fin de la définition de la job card (sur plusieurs lignes)
                if (!line.TrimEnd().EndsWith(",") && !line.StartsWith("//*"))
                {
                    existingJobcard = false;
                }
            }

            // cut off newline(s) at the end comme dans le code d'origine
            return output2.ToString().TrimEnd('\n', '\r');
        }

        /// <summary>
        /// Application des variables avec le même algorithme que l'app d'origine
        /// </summary>
        private string ApplyVariables(string content, Dictionary<string, string> vars, int count)
        {
            string env = vars.ContainsKey("ENVIMS") ? vars["ENVIMS"] : "D";
            string jobClass = vars.ContainsKey("CLASS") ? vars["CLASS"] : "A";
            string username = vars.ContainsKey("USERNAME") ? vars["USERNAME"] : Environment.UserName;

            string schenv;
            switch (env)
            {
                case "Q": schenv = "IM7C"; break;
                case "A": schenv = "IM7Q"; break;
                case "P": schenv = "IM7P"; break;
                default:  schenv = "IM7T"; break;
            }

            // Sécurisation stricte du nom du job et notify (limite imposée de 8 caractères max en JCL)
            string safeJobName = vars.ContainsKey("JOBNAM") ? vars["JOBNAM"] :
                                 (username.Length > 7 ? username.Substring(0, 7) : username) + count;
            safeJobName = safeJobName.Replace(".", "").ToUpper();

            string safeNotify = username.Length > 8 ? username.Substring(0, 8) : username;
            safeNotify = safeNotify.Replace(".", "").ToUpper();

            // Création et injection de la nouvelle JobCard
            string jobcard = $"//{safeJobName} JOB CLASS={jobClass},SCHENV={schenv},NOTIFY={safeNotify}\r\n";
            string contentWithJobcard = jobcard + content;

            // SÉCURITÉ CRITIQUE : Supprime les lignes vides (\r\n\r\n) qui provoquent le "JES000050E Parsing Error"
            string content2 = contentWithJobcard.Replace("\r\n\r\n", "\r\n").TrimEnd('\n', '\r');

            // Boucle d'application des variables (Gère les variables imbriquées)
            string tempContent = ApplyVarsCore(content2, vars);
            while (content2 != tempContent)
            {
                content2 = tempContent;
                tempContent = ApplyVarsCore(content2, vars);
            }

            // Étape finale d'origine : Retire les "+" en début de ligne (pour les SYSIN et SYSPARM)
            StringBuilder content3 = new StringBuilder();
            foreach (String line in content2.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("+"))
                {
                    if (line.Length > 1) content3.AppendLine(line.Substring(1));
                    else content3.AppendLine("");
                }
                else
                {
                    content3.AppendLine(line);
                }
            }

            return content3.ToString().TrimEnd('\n', '\r');
        }

        /// <summary>
        /// Moteur de Regex d'origine pour remplacer les variables
        /// </summary>
        private string ApplyVarsCore(string content, Dictionary<string, string> vars)
        {
            string temp = content;
            foreach (var kvp in vars)
            {
                string varName = kvp.Key.Trim().ToUpper();
                string value = kvp.Value ?? "";

                if (value == "''") value = "";
                else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 3)
                {
                    value = value.Substring(1, value.Length - 2);
                    // unescaping single quotes
                    if (value.Contains("''")) value = value.Replace("''", "'");
                }
                value = value.Replace("$", "$$");

                try
                {
                    temp = Regex.Replace(temp, @"(%%" + varName + @")(?=%)", value);
                    temp = Regex.Replace(temp, @"(%%" + varName + @")([, \n\r])", value + "$2");
                    temp = Regex.Replace(temp, @"(%%" + varName + @")(\.)", value);
                    temp = Regex.Replace(temp, @"(%%" + varName + @")(\')", value + "$2");
                    temp = Regex.Replace(temp, @"(%%" + varName + @")(\))", value + "$2");
                    temp = Regex.Replace(temp, @"(%%" + varName + @")$", value);
                }
                catch { /* Ignorer les erreurs de RegEx mal formées */ }
            }
            return temp;
        }

        // =========================================================================
        // PARTIE 2 : GENERATION DES JCL MFFTP
        // =========================================================================

        public string GenerateFtpJcl(DsnDirection direction, string dsn, string tempFileName, TransferMode transferMode)
        {
            return GenerateFtpJcl(direction, new List<Tuple<string, string>> { new Tuple<string, string>(dsn, tempFileName) }, transferMode);
        }

        public string GenerateFtpJcl(DsnDirection direction, List<Tuple<string, string>> dsnAndFile, TransferMode transferMode)
        {
            string uid = Settings.DbConfig.Uid ?? "";
            string pwd = Settings.DbConfig.Pwd ?? "";

            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(pwd))
            {
                throw new InvalidOperationException("Les identifiants ne sont pas définis.");
            }

            string jclTemplate =
@"//##USER##T JOB CLASS=I
//TRANSFER EXEC PGM=MFFTP,
//            PARM='##SERVER##'
//SYSPRINT DD SYSOUT=*
//OUTPUT   DD SYSOUT=*
//INPUT    DD *
AG\##USER## ##PASS##
CD '##PATH##'
##OPTIONS##LOCSTAT
##INSTRUCTION##
QUIT
/*
//ENVVAR   DD *
MFFTP_TRANSLATE_SAFETY=OFF
MFFTP_SENDEOL=CRLF
MFFTP_PROCESS_TRAILS_ONGET=FALSE
/*";

            string tempFolder;
            string instructionTemplate;

            switch (direction)
            {
                case DsnDirection.Read:
                    tempFolder = ReadTempFolder;
                    instructionTemplate = "PUT ##DSN## +\r\n##FILE##";
                    break;
                case DsnDirection.Write:
                    tempFolder = WriteTempFolder;
                    instructionTemplate = "GET ##FILE## +\r\n'##DSN##' (REP";
                    break;
                default:
                    throw new ArgumentException("Unknown DsnDirection");
            }

            StringBuilder instructions = new StringBuilder();
            foreach (var tuple in dsnAndFile)
            {
                string instruction = instructionTemplate
                    .Replace("##DSN##", tuple.Item1.ToUpper().Trim().Trim('\''))
                    .Replace("##FILE##", tuple.Item2.Trim().Trim('\''));
                instructions.AppendLine(instruction);
            }

            string options = transferMode == TransferMode.Text
                ? "LOCSITE ENCODING=SBCS\r\nLOCSITE SBDATACONN=TABSTD\r\n" : "";

            string jcl = jclTemplate
                .Replace("##USER##", uid)
                .Replace("##PASS##", pwd)
                .Replace("##SERVER##", TempShare)
                .Replace("##PATH##", tempFolder)
                .Replace("##OPTIONS##", options)
                .Replace("##INSTRUCTION##", instructions.ToString().TrimEnd('\n', '\r'));

            return jcl;
        }

        public string ComposeTempFileName(DsnDirection direction)
        {
            string uid = Settings.DbConfig.Uid ?? "UNKNOWN";
            string d = direction == DsnDirection.Read ? "R" : "S";
            return $"{uid}_{d}_{Guid.NewGuid()}.TXT";
        }

        public string GetReadTempFolderPath()
        {
            return $@"\\{TempShare}\{ReadTempFolder.Trim('\\')}";
        }

        public string GetWriteTempFolderPath()
        {
            return $@"\\{TempShare}\{WriteTempFolder.Trim('\\')}";
        }
    }
}