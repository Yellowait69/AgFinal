using System;
using System.IO;

namespace AutoActivator.Config
{
    public static class Settings
    {
        // -----------------------------------------------------------------------------
        // 1. CONFIGURATION DES CHEMINS (FILESYSTEM)
        // -----------------------------------------------------------------------------

        // En .NET, BaseDirectory correspond à la racine de l'exécutable (bin/Debug/netX.0/)
        // Si vous voulez remonter à la racine du projet en dev, vous pouvez ajuster ce chemin.
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Dossiers d'entrée/sortie
        public static readonly string InputDir = Path.Combine(BaseDir, "data", "input");
        public static readonly string OutputDir = Path.Combine(BaseDir, "data", "output");

        // --- DEFINITION DES FICHIERS CLES ---

        // A. Fichier source (Optionnel : liste des ID à dupliquer pour le script d'activation)
        public static readonly string SourceFile = Path.Combine(InputDir, "contrats_sources.xlsx");

        // B. Fichier pivot (Sortie de l'Activation -> Entrée du Comparateur)
        public static readonly string ActivationOutputFile = Path.Combine(InputDir, "contrats_en_attente_activation.xlsx");

        // C. Variable utilisée par run_comparison.cs (doit pointer sur le fichier pivot)
        public static readonly string InputFile = ActivationOutputFile;


        // -----------------------------------------------------------------------------
        // 2. CONFIGURATION BASES DE DONNÉES
        // -----------------------------------------------------------------------------

        // A. Configuration LISA (SQL Server)
        public static class DbConfig
        {
            public const string Driver = "SQL Server"; // Ou "ODBC Driver 17 for SQL Server"
            public const string Server = "SQLMFDBD01";
            public const string Database = "FJ0AGDB_D000";
            public const string TrustedConnection = "yes"; // 'yes' utilise l'auth Windows, sinon utilise UID/PWD
            public const string Uid = "XA3894";

            // Lecture sécurisée depuis les variables d'environnement (comme os.getenv en Python)
            public static string Pwd => Environment.GetEnvironmentVariable("DB_PWD") ?? "*****************";

            // Ajout pratique pour .NET : Génération automatique de la chaîne de connexion SQL Server
            public static string ConnectionString =>
                TrustedConnection.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? $"Server={Server};Database={Database};Integrated Security=True;TrustServerCertificate=True;"
                    : $"Server={Server};Database={Database};User Id={Uid};Password={Pwd};TrustServerCertificate=True;";
        }

        // B. Configuration ELIA (Pour l'injection/duplication - Oracle)
        public static class DbConfigElia
        {
            public const string Driver = "Oracle in OraClient19Home1";
            public const string Server = "ELIA_PROD_DB";
            public const string Database = "ELIA_SCHEMA";
            public const string Uid = "USER_ELIA";
            public const string Pwd = "PASSWORD_ELIA";

            // Ajout pratique pour .NET : Génération automatique de la chaîne de connexion Oracle
            public static string ConnectionString => $"Data Source={Server};User Id={Uid};Password={Pwd};";
        }
    }
}