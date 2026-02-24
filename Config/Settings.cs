using System;
using System.IO;

namespace AutoActivator.Config
{
    public static class Settings
    {

        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;


        public static readonly string InputDir = Path.Combine(BaseDir, "data", "input");
        public static readonly string OutputDir = Path.Combine(BaseDir, "data", "output");

        public static readonly string SourceFile = Path.Combine(InputDir, "contrats_sources.xlsx");

        public static readonly string ActivationOutputFile = Path.Combine(InputDir, "contrats_en_attente_activation.xlsx");

        public static readonly string InputFile = ActivationOutputFile;


        // -----------------------------------------------------------------------------
        // 2. CONFIGURATION BASES DE DONNEES
        // -----------------------------------------------------------------------------

        // A. Configuration LISA (SQL Server)
        public static class DbConfig
        {
            public const string Driver = "SQL Server";
            public const string Server = "SQLMFDBD01";
            public const string Database = "FJ0AGDB_D000";
            public const string TrustedConnection = "yes";
            public const string Uid = "XA3894";
            public static string Pwd => Environment.GetEnvironmentVariable("DB_PWD") ?? "*****************";

            public static string ConnectionString =>
                TrustedConnection.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? $"Server={Server};Database={Database};Integrated Security=True;TrustServerCertificate=True;"
                    : $"Server={Server};Database={Database};User Id={Uid};Password={Pwd};TrustServerCertificate=True;";
        }


            // Ajout pratique pour .NET : Generation automatique de la chaine de connexion Oracle
            public static string ConnectionString => $"Data Source={Server};User Id={Uid};Password={Pwd};";
        }
    }
}