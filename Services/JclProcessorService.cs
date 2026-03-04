using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoActivator.Services
{
    public class JclProcessorService
    {
        private readonly string _jclDirectory;

        public JclProcessorService(string jclDirectory)
        {
            _jclDirectory = jclDirectory;
        }

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

            string schenv = env switch
            {
                "Q" => "IM7C",
                "A" => "IM7Q",
                "P" => "IM7P",
                _ => "IM7T"
            };

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
    }
}