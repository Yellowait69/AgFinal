using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoActivator.Config;

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
            string cleanJobName = jobName.ToUpper().Replace(".JCL", "");
            string fileName = jobName.ToUpper().EndsWith(".JCL") ? jobName : jobName + ".JCL";
            string filePath = Path.Combine(_jclDirectory, fileName);

            if (!File.Exists(filePath)) filePath = Path.Combine(_jclDirectory, jobName);
            if (!File.Exists(filePath)) throw new FileNotFoundException($"Fichier JCL introuvable: {fileName}");

            string rawContent;
            using (StreamReader reader = new StreamReader(filePath))
            {
                rawContent = await reader.ReadToEndAsync();
            }

            // Exécution STRICTEMENT identique à l'ancien code avec les corrections
            string correctedContent = DoCorrections(rawContent);
            return ApplyVariables(cleanJobName, correctedContent, variables, count);
        }

        private string DoCorrections(string content)
        {
            StringBuilder output = new StringBuilder();

            // 1. Nettoyage initial et coupure à 72 caractères
            foreach (string line in content.Replace("\r", "").Split('\n'))
            {
                // CORRECTIF 1 : Ignorer totalement les lignes de commentaires JCL.
                // Cela empêche les commentaires ("//* CKPTID=LAST,") de casser les continuations.
                if (line.StartsWith("//*")) continue;

                string processedLine = line;

                // CORRECTIF 2 : Ajouter l'espace manquant pour les conditions IF (ex: "RC<5" devient "RC < 5")
                if (processedLine.Contains(" IF ") && processedLine.Contains("RC<"))
                {
                    processedLine = processedLine.Replace("RC<", "RC < ");
                }

                // Coupure des numéros de séquence (colonnes > 72)
                if (processedLine.Length > 72 && int.TryParse(processedLine.Substring(72).Trim(), out int _))
                    output.AppendLine(processedLine.Substring(0, 72));
                else
                    output.AppendLine(processedLine);
            }

            // 2. Suppression sécurisée de la JobCard existante
            StringBuilder output2 = new StringBuilder();
            List<string> lines = output.ToString().Replace("\r", "").Split('\n').ToList();

            bool existingJobcard = false;
            bool jobCardFound = false;

            foreach (string line in lines)
            {
                // On détecte la JobCard même si elle n'est pas sur la première ligne
                if (!jobCardFound && line.ToUpper().Contains(" JOB "))
                {
                    existingJobcard = true;
                    jobCardFound = true;
                }

                if (!existingJobcard)
                    output2.AppendLine(line);

                // Si la ligne ne se termine pas par une virgule, c'est la fin de la JobCard
                if (existingJobcard && !line.Trim().EndsWith(","))
                    existingJobcard = false;
            }

            // cut off newline(s) at the end
            return output2.ToString().TrimEnd(new char[] { '\n', '\r' });
        }

        private string ApplyVariables(string cleanJobName, string content, Dictionary<string, string> vars, int count)
        {
            List<string> jclLines = content.Replace("\r", "").Split('\n').ToList();

            // Logique de LvChainTool : Injection de la variable JOBNAM si elle n'existe pas
            Dictionary<string, string> localVars = new Dictionary<string, string>(vars);
            if (!localVars.ContainsKey("JOBNAM"))
            {
                localVars.Add("JOBNAM", cleanJobName);
            }

            StringBuilder contentBuilder = new StringBuilder();
            foreach (string jclLine in jclLines)
            {
                contentBuilder.AppendLine(jclLine);
            }

            string env = localVars.ContainsKey("ENVIMS") ? localVars["ENVIMS"] : "D";
            string jobClass = localVars.ContainsKey("CLASS") ? localVars["CLASS"] : "A";

            // On conserve Environment.UserName tel quel comme vous l'avez demandé
            string username = localVars.ContainsKey("USERNAME") ? localVars["USERNAME"] : Environment.UserName;

            string schenv = "IM7T";
            if (env == "D") schenv = "IM7T";
            else if (env == "Q") schenv = "IM7C";
            else if (env == "A") schenv = "IM7Q";
            else if (env == "P") schenv = "IM7P";

            string jobcard = "//";

            // Reproduction de la logique de nommage de job EXACTE de l'ancien outil
            if (localVars.ContainsKey("JOBNAM") && !string.IsNullOrEmpty(localVars["JOBNAM"]))
            {
                 jobcard += localVars["JOBNAM"].Trim().ToUpper();
            }
            else
            {
                 jobcard += username + count;
            }

            jobcard += " JOB CLASS=" + jobClass + ",SCHENV=" + schenv + ",NOTIFY=" + username + "\r\n";

            contentBuilder.Insert(0, jobcard);

            string content2 = contentBuilder.ToString().Replace("\r\n\r\n", "\r\n").TrimEnd(new char[] { '\n', '\r' });

            string tempContent = ApplyVarsCore(content2, localVars);
            while (content2 != tempContent)
            {
                content2 = tempContent;
                tempContent = ApplyVarsCore(content2, localVars);
            }

            // temporary fix for +'s
            StringBuilder content3 = new StringBuilder();
            foreach (string line in content2.Replace("\r", "").Split('\n'))
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
        // PARTIE 2 : UTILITAIRES D'ANALYSE (INSPIRÉ DE LVCHAINTOOL)
        // =========================================================================

        public List<string> FindDSNs(string jclContent)
        {
            // remove comments
            StringBuilder content = new StringBuilder();
            List<string> jclLines = jclContent.Replace("\r", "").Split('\n').ToList();
            foreach (string jclLine in jclLines)
            {
                if (jclLine.StartsWith("//*")) continue;
                content.AppendLine(jclLine);
            }
            string cleanContent = content.ToString();

            HashSet<string> dsnList = new HashSet<string>();

            // find DSNs based on regexes
            List<string> patterns = new List<string>()
            {
                @"DSN=([^,\r\n]+)([,% \n\r])",
            };

            foreach (string pattern in patterns)
            {
                var matches = Regex.Matches(cleanContent, pattern);

                for (int i = 0; i < matches.Count; i++)
                {
                    var value = matches[i].Groups[1].Value;
                    // exceptions
                    if (value.StartsWith("&&")) continue;
                    dsnList.Add((value ?? "").Trim());
                }
            }

            return dsnList.OrderBy(x => x).ToList();
        }

        public List<string> FindAllVars(string jclContent)
        {
            // remove comments
            StringBuilder content2 = new StringBuilder();
            List<string> jclLines = jclContent.Replace("\r", "").Split('\n').ToList();
            foreach (string jclLine in jclLines)
            {
                if (jclLine.StartsWith("//*")) continue;
                content2.AppendLine(jclLine);
            }

            // loop over search in
            List<string> patterns = new List<string>()
            {
                @"(%%[ABCDEFGHIJKLMNOPQRSTUVWXYZ]+)(?=%)",
                @"(%%[ABCDEFGHIJKLMNOPQRSTUVWXYZ]+)([, \n\r])",
                @"(%%[ABCDEFGHIJKLMNOPQRSTUVWXYZ]+)(\.)",
                @"(%%[ABCDEFGHIJKLMNOPQRSTUVWXYZ]+)(\')",
                @"(%%[ABCDEFGHIJKLMNOPQRSTUVWXYZ]+)(\))",
                @"(%%[ABCDEFGHIJKLMNOPQRSTUVWXYZ]+)$"
            };

            HashSet<string> vars2 = new HashSet<string>();

            foreach (string pattern in patterns)
            {
                var matches = Regex.Matches(content2.ToString(), pattern);
                for (int i = 0; i < matches.Count; i++)
                {
                    vars2.Add(matches[i].Groups[1].Value.TrimStart('%'));
                }
            }

            return vars2.OrderBy(x => x).ToList();
        }

        // =========================================================================
        // PARTIE 3 : GENERATION DES JCL MFFTP
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