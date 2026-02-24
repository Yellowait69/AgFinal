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

        // Ajout de SnapshotDir pour corriger l'erreur de compilation dans Program.cs
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
            public const string TrustedConnection = "yes";
            public const string Uid = "XA3894";

            // Récupération sécurisée du mot de passe via variable d'environnement
            public static string Pwd => Environment.GetEnvironmentVariable("DB_PWD") ?? "Maxpanpan02!Amandine";

            public static string ConnectionString =>
                TrustedConnection.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? $"Server={Server};Database={Database};Integrated Security=True;TrustServerCertificate=True;"
                    : $"Server={Server};Database={Database};User Id={Uid};Password={Pwd};TrustServerCertificate=True;";
        }

        // Configuration additionnelle (ex: Oracle ou autre source) pour éviter les erreurs de contexte
        public static class OtherDbConfig
        {
            public const string Server = "NOM_SERVEUR";
            public const string Uid = "UTILISATEUR";
            public static string Pwd => Environment.GetEnvironmentVariable("OTHER_DB_PWD") ?? "PASSWORD";

            // Correction : Cette propriété utilise maintenant les variables définies dans sa propre classe
            public static string ConnectionString => $"Data Source={Server};User Id={Uid};Password={Pwd};";
        }
    }
}