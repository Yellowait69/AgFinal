using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AutoActivator.Sql
{
    public static class SqlQueries
    {
        public static readonly ReadOnlyDictionary<string, string> Queries = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            // ==========================================
            // RECUPERATION DES CLES (LISA & ELIA)
            // ==========================================

            // Recherche LISA : récupère le numéro technique interne (NO_CNT)
            // Correction : Utilisation de LIKE pour gérer le padding CHAR(17) de NO_CNT_EXTENDED
            { "GET_INTERNAL_ID", @"
                SELECT TOP 1 NO_CNT
                FROM LV.SCNTT0 WITH(NOLOCK)
                WHERE NO_CNT_EXTENDED LIKE @ContractNumber + '%'"
            },

            // Recherche ELIA : récupère l'ID de contrat (IT5UCONAIDN) via la référence externe LISA
            { "GET_ELIA_ID", @"
                SELECT TOP 1 IT5UCONAIDN
                FROM FJ1.TB5UCON WITH(NOLOCK)
                WHERE IT5UCONLREFEXN LIKE @ContractNumber + '%'"
            },

            // Recherche ELIA : récupère l'ID de demande (DemandId) lié au contrat ELIA
            { "GET_ELIA_DEMAND_ID", @"
                SELECT DISTINCT IT5HDMDAIDN
                FROM FJ1.TB5HELT WITH(NOLOCK)
                WHERE IT5UCONAIDN = @EliaId"
            },

            // ==========================================
            // DONNEES LISA (Tables LV) - Utilise @InternalId
            // ==========================================

            { "LV.SCNTT0", "SELECT * FROM LV.SCNTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.SAVTT0", "SELECT * FROM LV.SAVTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT ASC" },
            { "LV.SWBGT0", "SELECT * FROM LV.SWBGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, C_PROP DESC" },
            { "LV.PRCTT0", "SELECT * FROM LV.PRCTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY D_REF_PRM ASC" },
            { "LV.SCLST0", "SELECT * FROM LV.SCLST0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, NO_ORD_CLS" },
            { "LV.SCLRT0", "SELECT * FROM LV.LV5S16TSCLRT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.BSPDT0", "SELECT * FROM LV.BSPDT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.BSPGT0", "SELECT * FROM LV.BSPGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.MWBGT0", "SELECT * FROM LV.MWBGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_PRJ, C_PROP" },
            { "LV.PRIST0", "SELECT * FROM LV.PRIST0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, D_ECH" },
            { "LV.FMVGT0", "SELECT * FROM LV.FMVGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY TSTAMP_DMOD" },
            { "LV.ELIAT0", "SELECT * FROM LV.ELIAT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.ELIHT0", "SELECT * FROM LV.ELIHT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.PCONT0", "SELECT * FROM [LV].[LV5P02TPCONT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.XRSTT0", "SELECT * FROM LV.XRSTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },

            // ==========================================
            // DONNEES ELIA (Tables FJ1) - Utilise @EliaId
            // ==========================================

            { "FJ1.TB5UCON", "SELECT * FROM FJ1.TB5UCON WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UGAR", "SELECT * FROM FJ1.TB5UGAR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UASU", @"
                SELECT * FROM FJ1.TB5UASU WITH(NOLOCK)
                WHERE IT5UASUAIDN IN (SELECT IT5UASUAIDN FROM FJ1.TB5UGAR WHERE IT5UCONAIDN = @EliaId)"
            },
            { "FJ1.TB5UPRP", "SELECT * FROM FJ1.TB5UPRP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UAVE", "SELECT * FROM FJ1.TB5UAVE WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UDCR", "SELECT * FROM FJ1.TB5UDCR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UBEN", @"
                SELECT * FROM FJ1.TB5UBEN WITH(NOLOCK)
                WHERE IT5UBENAIDN IN (SELECT IT5UBENAIDN FROM FJ1.TB5UDCR WHERE IT5UCONAIDN = @EliaId)"
            },
            { "FJ1.TB5UPRS", "SELECT * FROM FJ1.TB5UPRS WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5URPP", "SELECT * FROM FJ1.TB5URPP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5HELT", "SELECT * FROM FJ1.TB5HELT WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UCCR", "SELECT * FROM FJ1.TB5UCCR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UPNR", "SELECT * FROM FJ1.TB5UPNR WITH(NOLOCK) WHERE IT5UPNRAIDN IN (SELECT IT5UPNRAIDN FROM FJ1.TB5UAVE WHERE IT5UCONAIDN = @EliaId)" },

            // ==========================================
            // DONNEES DEMANDE ELIA - Utilise @DemandId
            // ==========================================

            { "FJ1.TB5HDMD", "SELECT * FROM FJ1.TB5HDMD WITH(NOLOCK) WHERE IT5HDMDAIDN = @DemandId" },
            { "FJ1.TB5HPRO", "SELECT * FROM FJ1.TB5HPRO WITH(NOLOCK) WHERE IT5HDMDAIDN = @DemandId" },
            { "FJ1.TB5HDIC", "SELECT * FROM FJ1.TB5HDIC WITH(NOLOCK) WHERE IT5HDMDAIDN = @DemandId" },
            { "FJ1.TB5HEPT", "SELECT * FROM FJ1.TB5HEPT WITH(NOLOCK) WHERE IT5HDMDAIDN = @DemandId" },
            { "FJ1.TB5HDGM", "SELECT * FROM FJ1.TB5HDGM WITH(NOLOCK) WHERE IT5HDMDAIDN = @DemandId" },
            { "FJ1.TB5HDGD", "SELECT * FROM FJ1.TB5HDGD WITH(NOLOCK) WHERE IT5HDMDAIDN = @DemandId" }
        });
    }
}