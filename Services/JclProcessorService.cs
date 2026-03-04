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
        // PARTIE 1 : TRAITEMENT DES FICHIERS JCL CLASSIQUES (VOTRE CODE EXISTANT)
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

            string correctedContent = DoCorrections(rawContent);
            return ApplyVariables(correctedContent, variables, count);
        }

        private string DoCorrections(string content)
        {
            var output = new StringBuilder();
            using (StringReader reader = new StringReader(content))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length > 72 && int.TryParse(line.Substring(72).Trim(), out _))
                        output.AppendLine(line.Substring(0, 72));
                    else
                        output.AppendLine(line);
                }
            }

            var output2 = new StringBuilder();
            bool isFirstLine = true;
            bool existingJobcard = false;

            using (StringReader reader = new StringReader(output.ToString()))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (isFirstLine && !line.StartsWith("//*") && line.ToUpper().Contains(" JOB "))
                        existingJobcard = true;

                    if (!existingJobcard) output2.AppendLine(line);

                    if (!line.TrimEnd().EndsWith(",") && !line.StartsWith("//*"))
                        existingJobcard = false;

                    isFirstLine = false;
                }
            }

            return output2.ToString().TrimEnd('\n', '\r');
        }

        private string ApplyVariables(string content, Dictionary<string, string> vars, int count)
        {
            string env = vars.ContainsKey("ENVIMS") ? vars["ENVIMS"] : "D";
            string jobClass = vars.ContainsKey("CLASS") ? vars["CLASS"] : "A";
            string username = vars.ContainsKey("USERNAME") ? vars["USERNAME"] : Environment.UserName;

            // Remplacement de la syntaxe C# 8.0 par la syntaxe classique compatible C# 7.3
            string schenv;
            switch (env)
            {
                case "Q": schenv = "IM7C"; break;
                case "A": schenv = "IM7Q"; break;
                case "P": schenv = "IM7P"; break;
                default:  schenv = "IM7T"; break;
            }

            string jobcard = $"//{username}{count} JOB CLASS={jobClass},SCHENV={schenv},NOTIFY={username}\r\n";
            string tempContent = jobcard + content;

            foreach (var kvp in vars)
            {
                string varName = kvp.Key.Trim().ToUpper();
                string value = kvp.Value ?? "";

                if (value == "''") value = "";
                else if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 3)
                {
                    value = value.Substring(1, value.Length - 2);
                    if (value.Contains("''")) value = value.Replace("''", "'");
                }
                value = value.Replace("$", "$$");

                try
                {
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(?=%)", value);
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")([, \n\r])", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\.)", value);
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\')", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")(\))", value + "$2");
                    tempContent = Regex.Replace(tempContent, @"(%%" + varName + @")$", value);
                }
                catch { /* Ignorer les erreurs de RegEx mal formées */ }
            }

            var finalContent = new StringBuilder();
            using (StringReader reader = new StringReader(tempContent))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("+"))
                    {
                        if (line.Length > 1) finalContent.AppendLine(line.Substring(1));
                        else finalContent.AppendLine("");
                    }
                    else finalContent.AppendLine(line);
                }
            }

            return finalContent.ToString().TrimEnd('\n', '\r');
        }

        // =========================================================================
        // PARTIE 2 : GENERATION DES JCL MFFTP (NOUVEAU - INSPIRÉ DE DSN.CS)
        // =========================================================================

        /// <summary>
        /// Génère le JCL nécessaire pour transférer un Data Set (DSN) via FTP vers/depuis un dossier partagé
        /// </summary>
        public string GenerateFtpJcl(DsnDirection direction, string dsn, string tempFileName, TransferMode transferMode)
        {
            return GenerateFtpJcl(direction, new List<Tuple<string, string>> { new Tuple<string, string>(dsn, tempFileName) }, transferMode);
        }

        /// <summary>
        /// Génère le JCL nécessaire pour transférer plusieurs Data Sets (DSN) en une seule soumission
        /// </summary>
        public string GenerateFtpJcl(DsnDirection direction, List<Tuple<string, string>> dsnAndFile, TransferMode transferMode)
        {
            // Récupération sécurisée du mot de passe en mémoire (saisi via LoginWindow)
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
                .Replace("##PASS##", pwd) // Injection du mot de passe pour le FTP
                .Replace("##SERVER##", TempShare)
                .Replace("##PATH##", tempFolder)
                .Replace("##OPTIONS##", options)
                .Replace("##INSTRUCTION##", instructions.ToString().TrimEnd('\n', '\r'));

            return jcl;
        }

        /// <summary>
        /// Génère un nom de fichier temporaire unique pour le transfert
        /// </summary>
        public string ComposeTempFileName(DsnDirection direction)
        {
            string uid = Settings.DbConfig.Uid ?? "UNKNOWN";
            string d = direction == DsnDirection.Read ? "R" : "S";
            return $"{uid}_{d}_{Guid.NewGuid()}.TXT";
        }

        /// <summary>
        /// Retourne le chemin UNC complet du dossier partagé de lecture
        /// </summary>
        public string GetReadTempFolderPath()
        {
            return $@"\\{TempShare}\{ReadTempFolder.Trim('\\')}";
        }

        /// <summary>
        /// Retourne le chemin UNC complet du dossier partagé d'écriture
        /// </summary>
        public string GetWriteTempFolderPath()
        {
            return $@"\\{TempShare}\{WriteTempFolder.Trim('\\')}";
        }
    }
}