using System;
using System.IO;
using System.Text;
using AutoActivator.Config;
using AutoActivator.Models;

namespace AutoActivator.Services
{
    public class BatchExtractionService
    {
        private readonly ExtractionService _extractionService;

        public BatchExtractionService(ExtractionService extractionService)
        {
            _extractionService = extractionService;
        }

        public void PerformBatchExtraction(string filePath, Action<BatchProgressInfo> onProgressUpdate)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Le fichier CSV spécifié est introuvable.", filePath);

            string rawText = File.ReadAllText(filePath).Replace("\uFEFF", "");
            string[] lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1)
                throw new Exception("Le fichier CSV est vide ou ne contient que l'en-tête.");

            StringBuilder globalLisa = new StringBuilder();
            StringBuilder globalElia = new StringBuilder();

            string[] headers = lines[0].Split(new[] { ';', ',' });
            int contractIndex = 0, premiumIndex = -1, productIndex = -1;

            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim().Trim('"').ToLower();
                if (h.Contains("contract") || h.Contains("contrat") || h.Contains("lisa")) contractIndex = i;
                if (h.Contains("premium") || h.Contains("prime")) premiumIndex = i;
                if (h.Contains("product") || h.Contains("produit")) productIndex = i;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(new[] { ';', ',' });

                if (columns.Length > contractIndex)
                {
                    string contractNumber = columns[contractIndex].Replace("=", "").Replace("\"", "").Trim();
                    string premiumAmount = (premiumIndex != -1 && columns.Length > premiumIndex) ? columns[premiumIndex].Replace("=", "").Replace("\"", "").Trim() : "0";
                    string productValue = (productIndex != -1 && columns.Length > productIndex) ? columns[productIndex].Replace("=", "").Replace("\"", "").Trim() : "N/A";

                    if (!string.IsNullOrEmpty(contractNumber))
                    {
                        try
                        {
                            ExtractionResult result = _extractionService.PerformExtraction(contractNumber, false);

                            if (!string.IsNullOrWhiteSpace(result.LisaContent))
                            {
                                globalLisa.AppendLine(new string('-', 60));
                                globalLisa.AppendLine($"### CONTRACT: {contractNumber} | PRODUCT: {productValue}");
                                globalLisa.AppendLine(new string('-', 60));
                                globalLisa.Append(result.LisaContent).AppendLine();
                            }

                            if (!string.IsNullOrWhiteSpace(result.EliaContent))
                            {
                                globalElia.AppendLine(new string('-', 60));
                                globalElia.AppendLine($"### CONTRACT: {contractNumber} | UCON: {result.UconId}");
                                globalElia.AppendLine(new string('-', 60));
                                globalElia.Append(result.EliaContent).AppendLine();
                            }

                            onProgressUpdate?.Invoke(new BatchProgressInfo
                            {
                                ContractId = contractNumber, InternalId = result.InternalId, Product = productValue,
                                Premium = premiumAmount, UconId = result.UconId, DemandId = result.DemandId, Status = "OK"
                            });
                        }
                        catch (Exception ex)
                        {
                            onProgressUpdate?.Invoke(new BatchProgressInfo
                            {
                                ContractId = $"{contractNumber} (FAILED)", InternalId = "Error", Product = productValue,
                                Premium = premiumAmount, UconId = "Error", DemandId = "Error",
                                Status = ex.Message.ToLower().Contains("introuvable") ? "Non trouvé en BDD" : "Erreur SQL"
                            });
                        }
                    }
                }
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            Directory.CreateDirectory(Settings.OutputDir);
            File.WriteAllText(Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_LISA_{timestamp}.csv"), globalLisa.Length > 0 ? globalLisa.ToString() : "AUCUN CONTRAT LISA TROUVE.", Encoding.UTF8);
            File.WriteAllText(Path.Combine(Settings.OutputDir, $"BATCH_GLOBAL_ELIA_{timestamp}.csv"), globalElia.Length > 0 ? globalElia.ToString() : "AUCUN CONTRAT ELIA TROUVE.", Encoding.UTF8);
        }
    }
}