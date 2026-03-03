using System;
using System.IO;

namespace AutoActivator.Config
{
    /// <summary>
    /// This file contains the global configuration settings for the AutoActivator application.
    /// It centrally manages directory paths for input, output, and snapshot files.
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

            // Les identifiants sont maintenant stockés en mémoire pour la session en cours
            // Uid a une valeur par défaut, Pwd est vide au démarrage.
            public static string Uid { get; set; } = "XA3894";
            public static string Pwd { get; set; } = string.Empty;

            /// <summary>
            /// Generates the connection string dynamically based on the target environment.
            /// </summary>
            /// <param name="envSuffix">Environment suffix (e.g., "D000" or "Q000")</param>
            /// <returns>The formatted SQL connection string</returns>
            public static string GetConnectionString(string envSuffix)
            {
                // Récupère la lettre de l'environnement (D ou Q) en fonction du suffixe
                string envLetter = string.IsNullOrEmpty(envSuffix) ? "D" : envSuffix.Substring(0, 1).ToUpper();

                // Détermine dynamiquement le nom du serveur
                string server = $"SQLMFDB{envLetter}01";

                // Concatène le préfixe de la base avec l'environnement demandé (D000 ou Q000)
                string database = $"FJ0AGDB_{envSuffix}";

                return TrustedConnection.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? $"Server={server};Database={database};Integrated Security=True;TrustServerCertificate=True;"
                    : $"Server={server};Database={database};User Id={Uid};Password={Pwd};TrustServerCertificate=True;";
            }
        }
    }
}