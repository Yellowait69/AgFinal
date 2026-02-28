using System.Collections.Generic;

namespace AutoActivator.Models
{
    // ==========================================
    // MODÈLES POUR L'EXTRACTION
    // ==========================================

    public class ExtractionResult
    {
        public string FilePath { get; set; }
        public string StatusMessage { get; set; }
        public string InternalId { get; set; }
        public string UconId { get; set; }
        public string DemandId { get; set; }
        public string LisaContent { get; set; }
        public string EliaContent { get; set; }
    }

    public class BatchProgressInfo
    {
        public string ContractId { get; set; }
        public string InternalId { get; set; }
        public string Product { get; set; }
        public string Premium { get; set; }
        public string UconId { get; set; }
        public string DemandId { get; set; }
        public string Status { get; set; }
    }

    // ==========================================
    // MODÈLES POUR LA COMPARAISON (Nouveaux)
    // ==========================================

    public class ComparisonReport
    {
        /// <summary>
        /// Pourcentage global de réussite (Lignes correctes / Total des lignes analysées)
        /// </summary>
        public double GlobalSuccessPercentage { get; set; }

        /// <summary>
        /// Nombre total de lignes lues et comparées tous fichiers confondus
        /// </summary>
        public int TotalRowsCompared { get; set; }

        /// <summary>
        /// Nombre total de lignes contenant au moins une erreur de comparaison
        /// </summary>
        public int TotalDifferencesFound { get; set; }

        /// <summary>
        /// Liste détaillée des résultats fichier par fichier, table par table
        /// </summary>
        public List<FileComparisonResult> FileResults { get; set; } = new List<FileComparisonResult>();
    }

    public class FileComparisonResult
    {
        /// <summary>
        /// Indique si c'est une comparaison "ELIA" ou "LISA"
        /// </summary>
        public string FileType { get; set; }

        /// <summary>
        /// Nom du premier fichier comparé (le fichier de référence/base)
        /// </summary>
        public string BaseFileName { get; set; }

        /// <summary>
        /// Nom du second fichier comparé (le fichier cible)
        /// </summary>
        public string TargetFileName { get; set; }

        /// <summary>
        /// Nom de la table SQL/CSV actuellement comparée
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// Statut renvoyé par le Comparator ("OK", "OK_EMPTY", "KO", "KO_ROW_COUNT", etc.)
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Le rapport détaillé brut renvoyé par le Comparator (Ligne X, Attribut Y : Source vs Target)
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Raccourci booléen pour savoir si la comparaison de ce fichier précis est un succès total
        /// </summary>
        public bool IsMatch => Status == "OK" || Status == "OK_EMPTY";
    }
}