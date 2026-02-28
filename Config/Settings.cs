using System;
using System.IO;

namespace AutoActivator.Config
{
    public static class Settings
    {
        // -----------------------------------------------------------------------------
        // 1. CHEMINS DES RÉPERTOIRES ET FICHIERS
        // -----------------------------------------------------------------------------
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

        public static readonly string InputDir = Path.Combine(BaseDir, "data", "input");
        public static readonly string OutputDir = Path.Combine(BaseDir, "data", "output");

        public static readonly string SnapshotDir = Path.Combine(OutputDir, "snapshots");

        public static readonly string SourceFile = Path.Combine(InputDir, "contrats_sources.xlsx");
        public static readonly string ActivationOutputFile = Path.Combine(InputDir, "contrats_en_attente_activation.xlsx");
        public static readonly string InputFile = ActivationOutputFile;

        // -----------------------------------------------------------------------------
        // 2. CONFIGURATION BASES DE DONNEES
        // -----------------------------------------------------------------------------

        // Configuration LISA (SQL Server)
        public static class DbConfig
        {
            public const string Driver = "SQL Server";
            public const string Server = "SQLMFDBD01";
            public const string Database = "FJ0AGDB_D000";

            // METTRE SUR "no" POUR FORCER L'UTILISATION DE UID ET PWD CI-DESSOUS
            public const string TrustedConnection = "yes";

            // Identifiants en dur pour faciliter les tests
            public const string Uid = "XA3894";
            public static string Pwd => "Maxpanpan02!Amandine";

            public static string ConnectionString =>
                TrustedConnection.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? $"Server={Server};Database={Database};Integrated Security=True;TrustServerCertificate=True;"
                    : $"Server={Server};Database={Database};User Id={Uid};Password={Pwd};TrustServerCertificate=True;";
        }

        // Configuration additionnelle (ex: Oracle ou autre source)
        public static class OtherDbConfig
        {
            public const string Server = "NOM_SERVEUR";
            public const string Uid = "UTILISATEUR";

            // Identifiants en dur pour les tests
            public static string Pwd => "PASSWORD";

            public static string ConnectionString => $"Data Source={Server};User Id={Uid};Password={Pwd};";
        }
    }
}