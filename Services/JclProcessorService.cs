using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoActivator.Config; // Ajout nécessaire pour accéder à Settings.DbConfig.Uid et Pwd

namespace AutoActivator.Services
{
    // Énumérations nécessaires pour définir le sens et le mode de transfert FTP
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

        // Paramètres réseau pour le transfert FTP avec le Mainframe
        private static readonly string TempShare = "FILES.FIBE.FORTIS";
        private static readonly string WriteTempFolder = @"\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\FTP-Write\";
        private static readonly string ReadTempFolder = @"\elia\11 - Technical Architecture\11 - IS Tooling\01 - Tools\FTP-Read\";

        public JclProcessorService(string jclDirectory)
        {
            _jclDirectory = jclDirectory;
        }

        // =========================================================================
        // PARTIE 1 : TRAITEMENT DES FICHIERS JCL CLASSIQUES (CLONE LVCHAINTOOL)
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

            // Étape 1 : Nettoyage d'origine basé strictement sur l'algorithme LvChainTool
            string correctedContent = DoCorrections(rawContent);

            // Étape 2 : Application des variables et formatage final comme l'application d'origine
            return ApplyVariables(correctedContent, variables, count);
        }

        private string DoCorrections(string content)
        {
            StringBuilder output = new StringBuilder();

            // 1. Coupure à 72 caractères uniquement s'il s'agit de numéros de séquence (colonnes 73-80)
            foreach (String line in content.Replace("\r", "").Split('\n'))
            {
                if (line.Length > 72 && int.TryParse(line.Substring(72).Trim(), out int _))
                    output.AppendLine(line.Substring(0, 72));
                else
                    output.AppendLine(line);
            }

            // 2. Suppression de la JobCard existante
            StringBuilder output2 = new StringBuilder();
            List<String> lines = new List<string>(output.ToString().Replace("\r", "").Split('\n'));
            bool existingJobcard = false;

            foreach (String line in lines)
            {
                // Utilisation de la même logique stricte d'origine pour cibler la jobcard
                if (lines.Count > 0 && lines[0] == line && !line.StartsWith("//*") && line.ToUpper().Contains(" JOB "))
                    existingJobcard = true;

                if (!existingJobcard)
                    output2.AppendLine(line);

                // Pas de virgule à la fin = fin de la définition de la job card
                if (!line.Trim().EndsWith(",") && !line.StartsWith("//*"))
                    existingJobcard = false;
            }

            return output2.ToString().TrimEnd(new char[] { '\n', '\r' });
        }

        private string ApplyVariables(string content, Dictionary<string, string> vars, int count)
        {
            List<String> jclLines = new List<string>(content.Replace("\r", "").Split('\n'));

            StringBuilder contentBuilder = new StringBuilder();
            foreach (String jclLine in jclLines)
                contentBuilder.AppendLine(jclLine);

            string env = vars.ContainsKey("ENVIMS") ? vars["ENVIMS"] : "D";
            string jobClass = vars.ContainsKey("CLASS") ? vars["CLASS"] : "A";
            string username = vars.ContainsKey("USERNAME") ? vars["USERNAME"] : Environment.UserName;

            // Définition de l'environnement (SCHENV)
            string schenv = "IM7T";
            if (env == "D") schenv = "IM7T";
            else if (env == "Q") schenv = "IM7C";
            else if (env == "A") schenv = "IM7Q";
            else if (env == "P") schenv = "IM7P";

            // Récupération du JOBNAM comme dans LvChainTool
            string jobNameStr = vars.ContainsKey("JOBNAM") ? vars["JOBNAM"] : (username + count);

            // Construction de la JOBCARD
            String jobcard = "//" + jobNameStr.Trim().ToUpper() + " JOB CLASS=" + jobClass + ",SCHENV=" + schenv + ",NOTIFY=" + username + "\r\n";

            contentBuilder.Insert(0, jobcard);

            // Le fix indispensable : supprime les lignes vides multiples qui causent le parsing error !
            String content2 = contentBuilder.ToString().Replace("\r\n\r\n", "\r\n").TrimEnd(new char[] { '\n', '\r' });

            // Boucle d'application pour gérer les variables imbriquées (nested variables)
            String tempContent = ApplyVars(content2, vars);
            while (content2 != tempContent)
            {
                content2 = tempContent;
                tempContent = ApplyVars(content2, vars);
            }

            // Gestion du symbole '+' en début de ligne (ex: les SYSIN et SYSPARM)
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

            return content3.ToString().TrimEnd(new char[] { '\n', '\r' });
        }

        /// <summary>
        /// Moteur de Regex copié trait pour trait depuis LvChainTool pour garantir un matching parfait
        /// </summary>
        private string ApplyVars(string content, Dictionary<string, string> vars)
        {
            String temp = content;
            foreach (var kvp in vars)
            {
                String varName = kvp.Key.Trim().ToUpper();
                String value = kvp.Value ?? "";

                if (value == "''") value = "";
                else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 3)
                {
                    value = value.Substring(1, value.Length - 2);
                    // Unescaping single quotes
                    if (value.Contains("''")) value = value.Replace("''", "'");
                }
                value = value.Replace("$", "$$");

                try
                {
                    string pattern = @"(%%" + varName + @")(?=%)";
                    string pattern2 = @"(%%" + varName + @")([, \n\r])";
                    string pattern3 = @"(%%" + varName + @")(\.)";
                    string pattern4 = @"(%%" + varName + @")(\')";
                    string pattern5 = @"(%%" + varName + @")(\))";
                    string pattern6 = @"(%%" + varName + @")$";

                    temp = Regex.Replace(temp, pattern, value);
                    temp = Regex.Replace(temp, pattern2, value + "$2");
                    temp = Regex.Replace(temp, pattern3, value);
                    temp = Regex.Replace(temp, pattern4, value + "$2");
                    temp = Regex.Replace(temp, pattern5, value + "$2");
                    temp = Regex.Replace(temp, pattern6, value);
                }
                catch { /* Ignorer les erreurs de RegEx mal formées */ }
            }
            return temp;
        }

        // =========================================================================
        // PARTIE 2 : GENERATION DES JCL MFFTP (NOUVEAU)
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
                throw new InvalidOperationException("Les identifiants (Uid/Pwd) ne sont pas définis dans Settings.DbConfig. Veuillez vous connecter d'abord.");
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
                ? "LOCSITE ENCODING=SBCS\r\nLOCSITE SBDATACONN=TABSTD\r\n"
                : "";

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