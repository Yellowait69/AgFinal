using System.Collections.Generic;

namespace AutoActivator.Models
{
    // ==========================================
    // EXTRACTION MODELS
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

        // NEW: Property to carry the premium extracted from ELIA
        public string Premium { get; set; }

        // NEW: Property to carry the real contract number (Contract Extended) resolved from Demand ID
        public string ContractReference { get; set; }
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
    // GRAPHICAL USER INTERFACE (GUI) MODELS
    // ==========================================

    public class ExtractionItem
    {
        public string ContractId { get; set; }
        public string InternalId { get; set; }
        public string Product { get; set; }
        public string Premium { get; set; }
        public string Ucon { get; set; }
        public string Hdmd { get; set; }
        public string Time { get; set; }
        public string Test { get; set; }
        public string FilePath { get; set; }
    }

    // ==========================================
    // COMPARISON MODELS
    // ==========================================

    public class ComparisonReport
    {
        /// <summary>
        /// Overall success percentage (Correct rows / Total analyzed rows).
        /// </summary>
        public double GlobalSuccessPercentage { get; set; }

        /// <summary>
        /// Total number of rows read and compared across all files.
        /// </summary>
        public int TotalRowsCompared { get; set; }

        /// <summary>
        /// Total number of rows containing at least one comparison error.
        /// </summary>
        public int TotalDifferencesFound { get; set; }

        /// <summary>
        /// Detailed list of results file by file, table by table.
        /// </summary>
        public List<FileComparisonResult> FileResults { get; set; } = new List<FileComparisonResult>();
    }

    public class FileComparisonResult
    {
        /// <summary>
        /// Indicates whether it is an "ELIA" or "LISA" comparison.
        /// </summary>
        public string FileType { get; set; }

        /// <summary>
        /// Name of the first file compared (the reference/base file).
        /// </summary>
        public string BaseFileName { get; set; }

        /// <summary>
        /// Name of the second file compared (the target file).
        /// </summary>
        public string TargetFileName { get; set; }

        /// <summary>
        /// Name of the SQL/CSV table currently being compared.
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// Status returned by the Comparator ("OK", "OK_EMPTY", "KO", "KO_ROW_COUNT", etc.).
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// The raw detailed report returned by the Comparator (Row X, Attribute Y: Source vs Target).
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Boolean shortcut to determine if the comparison of this specific file is a complete success.
        /// </summary>
        public bool IsMatch => Status == "OK" || Status == "OK_EMPTY";
    }
}