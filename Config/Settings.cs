using System;
using System.IO;

namespace AutoActivator.Config
{
    /// <summary>
    /// This file contains the global configuration settings for the AutoActivator application.
    /// It centrally manages directory paths for input, output, baseline, and snapshot files.
    /// It also handles database connection parameters, including the logic to dynamically
    /// generate the correct connection string based on the target environment (e.g., D000 for Dev, Q000 for Test).
    /// </summary>
    public static class Settings
    {
        // -----------------------------------------------------------------------------
        // 1. DIRECTORY AND FILE PATHS
        // -----------------------------------------------------------------------------
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

        public static readonly string InputDir = Path.Combine(BaseDir, "data", "input");
        public static readonly string OutputDir = Path.Combine(BaseDir, "data", "output");

        // NOUVEAU : Dossier Baseline pour les fichiers de référence (Smart Matcher)
        public static readonly string BaselineDir = Path.Combine(BaseDir, "data", "baseline");

        public static readonly string SnapshotDir = Path.Combine(OutputDir, "snapshots");

        public static readonly string SourceFile = Path.Combine(InputDir, "contrats_sources.xlsx");
        public static readonly string ActivationOutputFile = Path.Combine(InputDir, "contrats_en_attente_activation.xlsx");
        public static readonly string InputFile = ActivationOutputFile;

        // -----------------------------------------------------------------------------
        // 2. DATABASE CONFIGURATION
        // -----------------------------------------------------------------------------

        // LISA Configuration (SQL Server)
        public static class DbConfig
        {
            public const string Driver = "SQL Server";

            // SET TO "no" TO FORCE THE USE OF UID AND PWD BELOW
            public const string TrustedConnection = "yes";

            // Credentials are now stored in memory for the current session.
            // Uid has a default value, Pwd is empty at startup.
            public static string Uid { get; set; } = "XA3894";
            public static string Pwd { get; set; } = string.Empty;

            /// <summary>
            /// Generates the connection string dynamically based on the target environment.
            /// </summary>
            /// <param name="envSuffix">Environment suffix (e.g., "D000" or "Q000")</param>
            /// <returns>The formatted SQL connection string</returns>
            public static string GetConnectionString(string envSuffix)
            {
                // Retrieves the environment letter (D or Q) based on the suffix
                string envLetter = string.IsNullOrEmpty(envSuffix) ? "D" : envSuffix.Substring(0, 1).ToUpper();

                // Dynamically determines the server name
                string server = $"SQLMFDB{envLetter}01";

                // Concatenates the database prefix with the requested environment (D000 or Q000)
                string database = $"FJ0AGDB_{envSuffix}";

                return TrustedConnection.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? $"Server={server};Database={database};Integrated Security=True;TrustServerCertificate=True;"
                    : $"Server={server};Database={database};User Id={Uid};Password={Pwd};TrustServerCertificate=True;";
            }
        }
    }
}