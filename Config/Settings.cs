using System;
using System.IO;

namespace AutoActivator.Config
{
    public static class Settings
    {
        // -----------------------------------------------------------------------------
        // 1. DIRECTORY AND FILE PATHS
        // -----------------------------------------------------------------------------
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

        public static readonly string InputDir = Path.Combine(BaseDir, "data", "input");
        public static readonly string OutputDir = Path.Combine(BaseDir, "data", "output");

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
            public const string Server = "SQLMFDBD01";

            // SET TO "no" TO FORCE THE USE OF UID AND PWD BELOW
            public const string TrustedConnection = "yes";

            // Hardcoded credentials for testing purposes
            // (Note: Consider moving to environment variables or appsettings.json for production security)
            public const string Uid = "XA3894";
            public static string Pwd => "Maxpanpan02!Amandine";

            /// <summary>
            /// Generates the connection string dynamically based on the target environment.
            /// </summary>
            /// <param name="envSuffix">Environment suffix (e.g., "D000" or "Q000")</param>
            /// <returns>The formatted SQL connection string</returns>
            public static string GetConnectionString(string envSuffix)
            {
                // Concatène le préfixe de la base avec l'environnement demandé (D000 ou Q000)
                string database = $"FJ0AGDB_{envSuffix}";

                return TrustedConnection.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? $"Server={Server};Database={database};Integrated Security=True;TrustServerCertificate=True;"
                    : $"Server={Server};Database={database};User Id={Uid};Password={Pwd};TrustServerCertificate=True;";
            }
        }
    }
}