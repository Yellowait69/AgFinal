using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AutoActivator.Sql
{
    public static class SqlQueries
    {
        public static readonly ReadOnlyDictionary<string, string> Queries =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            // ==========================================
            // RECUPERATION DES CLES (LISA & ELIA)
            // ==========================================

            { "GET_INTERNAL_ID", @"
                SELECT TOP 1 NO_CNT
                FROM LV.SCNTT0 WITH(NOLOCK)
                WHERE NO_CNT_EXTENDED = @ContractNumber"
            },

            { "GET_ELIA_ID", @"
                SELECT TOP 1 IT5UCONAIDN
                FROM FJ1.TB5UCON WITH(NOLOCK)
                WHERE IT5UCONLREFEXN = @ContractNumber"
            },

            { "GET_ELIA_DEMAND_IDS", @"
                SELECT DISTINCT IT5HDMDAIDN
                FROM FJ1.TB5HELT WITH(NOLOCK)
                WHERE IT5UCONAIDN = @EliaId"
            },

            // ==========================================
            // DONNEES LISA (CONTRAT ET AVENANTS)
            // ==========================================

            { "LV.PCONT0", "SELECT * FROM [LV].[LV5P02TPCONT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.ELIAT0", "SELECT * FROM LV.ELIAT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.ELIHT0", "SELECT * FROM LV.ELIHT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },

            { "LV.SCNTT0", @"
                SELECT * FROM LV.SCNTT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId"
            },

            { "LV.SWBGT0", @"
                SELECT NO_CNT, NO_AVT, C_PROP, C_TRF, C_SEX_ASS, C_PER_PRM, DU_GAR, D_TRM_GAR,
                       D_TRM_GAR_CLI, B_TAX_PRM_ETAL, C_VALID_INTRT_DRG, PC_ANN_COM,
                       PC_FR_GEST_ANN, C_APPL_GAR, * FROM LV.SWBGT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY NO_AVT, C_PROP DESC"
            },

            { "LV.SAVTT0", @"
                SELECT * FROM [LV].[SAVTT0] WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY NO_AVT, NO_CNT"
            },

            // ==========================================
            // DONNEES LISA (PLANS ET GARANTIES)
            // ==========================================

            { "FJ1.TB5LPPL", @"
                SELECT * FROM [FJ1].[TB5LPPL] WITH(NOLOCK)
                WHERE IT5LPPLNCON = @InternalId
                ORDER BY IT5LPPLNAVEREF, IT5LPPLLGAR, IT5LPPLNCON"
            },

            { "FJ1.TB5LPPR", @"
                SELECT * FROM [FJ1].[TB5LPPR] WITH(NOLOCK)
                WHERE IT5LPPRNCON = @InternalId
                ORDER BY IT5LPPRNIDNPLNPRI DESC"
            },

            { "FJ1.TB5LGDR", "SELECT * FROM FJ1.TB5LGDR WITH(NOLOCK) WHERE IT5LGDRNCON = @InternalId" },

            // ==========================================
            // DONNEES DEMANDE ELIA
            // ==========================================

            { "FJ1.TB5HDMD", @"
                SELECT IT2RELANRELGST, IT5HDMDLREFEXN AS HDMDLREFEXN,
                       IT5HDMDLREFAGTCUA AS HDMDLREFAGTCUA, * FROM FJ1.TB5HDMD WITH(NOLOCK)
                WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))"
            },

            { "FJ1.TB5HDGM", "SELECT * FROM FJ1.TB5HDGM WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HDGD", "SELECT * FROM FJ1.TB5HDGD WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HPRO", "SELECT * FROM FJ1.TB5HPRO WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },

            // ==========================================
            // DONNEES ELIA (CONTRAT)
            // ==========================================

            { "FJ1.TB5HELT", "SELECT * FROM FJ1.TB5HELT WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UCON", "SELECT * FROM FJ1.TB5UCON WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UGAR", "SELECT * FROM FJ1.TB5UGAR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            { "FJ1.TB5UASU", @"
                SELECT * FROM FJ1.TB5UASU WITH(NOLOCK)
                WHERE IT5UASUAIDN IN (SELECT IT5UASUAIDN FROM FJ1.TB5UGAR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)"
            },

            { "FJ1.TB5UAVE", "SELECT * FROM FJ1.TB5UAVE WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            { "FJ1.TB5UPNR", @"
                SELECT * FROM FJ1.TB5UPNR WITH(NOLOCK)
                WHERE IT5UPNRAIDN IN (SELECT IT5UPNRAIDN FROM FJ1.TB5UAVE WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)"
            },

            { "FJ1.TB5UPRP", "SELECT * FROM FJ1.TB5UPRP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UPRS", "SELECT * FROM FJ1.TB5UPRS WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UPMP", "SELECT * FROM FJ1.TB5UPMP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            // ==========================================
            // DONNEES FINANCIERES & FONDS
            // ==========================================

            { "LV.PRIST0", "SELECT * FROM LV.PRIST0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, D_ECH, NO_ORD_QUITT" },
            { "LV.PECHT0", "SELECT * FROM LV.PECHT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, D_ECH, NO_ORD_QUITT" },
            { "LV.PFIET0", "SELECT * FROM LV.PFIET0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY D_CRT_INST, NO_ORD_INST" },
            { "FJ1.TB5LPPF", "SELECT * FROM [FJ1].[TB5LPPF] WITH(NOLOCK) WHERE IT5LPPFNCON = @InternalId ORDER BY IT5LPPFCFDS" },
            { "LV.FMVGT0", "SELECT * FROM [LV].[FMVGT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY TSTAMP_DMOD" },

            // ==========================================
            // DONNEES CLAUSES & RESERVES
            // ==========================================

            { "LV.SCLST0", "SELECT * FROM [LV].[SCLST0] WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, NO_ORD_CLS" },
            { "FJ1.TB5UBEN", @"
                SELECT * FROM [FJ1].[TB5UBEN] WITH(NOLOCK)
                WHERE IT5UBENAIDN IN (SELECT IT5UBENAIDN FROM [FJ1].[TB5UDCR] WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)"
            },
            { "LV.BSPDT0", "SELECT * FROM LV.BSPDT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.MWBGT0", "SELECT * FROM LV.MWBGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_PRJ, C_PROP" }
        });
    }
}