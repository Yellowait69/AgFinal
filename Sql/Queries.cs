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

            // Clé appelée par ExtractionService (doit garder le nom GET_ELIA_DEMAND_IDS)
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
            { "LV.SCNTT0", "SELECT * FROM LV.SCNTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.SWBGT0", "SELECT * FROM LV.SWBGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, C_PROP DESC" },
            { "LV.SAVTT0", "SELECT * FROM LV.SAVTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT ASC" },
            { "LV.XRSTT0", "SELECT * FROM LV.XRSTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },

            // ==========================================
            // DONNEES LISA (PLANS ET GARANTIES)
            // ==========================================

            { "FJ1.TB5LPPL", "SELECT * FROM [FJ1].[TB5LPPL] WITH(NOLOCK) WHERE IT5LPPLNCON = @InternalId ORDER BY IT5LPPLNAVEREF, IT5LPPLLGAR, IT5LPPLNCON" },
            { "FJ1.TB5LPPR", "SELECT * FROM [FJ1].[TB5LPPR] WITH(NOLOCK) WHERE IT5LPPRNCON = @InternalId ORDER BY IT5LPPRNIDNPLNPRI DESC" },
            { "FJ1.TB5LGDR", "SELECT * FROM FJ1.TB5LGDR WITH(NOLOCK) WHERE IT5LGDRNCON = @InternalId" },

            // ==========================================
            // DONNEES DEMANDE ELIA (Utilise @DemandIds avec STRING_SPLIT pour ExtractionService)
            // ==========================================

            { "FJ1.TB5HDMD", "SELECT * FROM FJ1.TB5HDMD WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HDGM", "SELECT * FROM FJ1.TB5HDGM WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HDGD", "SELECT * FROM FJ1.TB5HDGD WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HPRO", "SELECT * FROM FJ1.TB5HPRO WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },

            // ==========================================
            // DONNEES ELIA (CONTRAT)
            // ==========================================

            { "FJ1.TB5HELT", "SELECT * FROM FJ1.TB5HELT WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UCON", "SELECT * FROM FJ1.TB5UCON WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UGAR", "SELECT * FROM FJ1.TB5UGAR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UASU", "SELECT * FROM FJ1.TB5UASU WITH(NOLOCK) WHERE IT5UASUAIDN IN (SELECT IT5UASUAIDN FROM FJ1.TB5UGAR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)" },
            { "FJ1.TB5UCCR", "SELECT * FROM FJ1.TB5UCCR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UAVE", "SELECT * FROM FJ1.TB5UAVE WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UPNR", "SELECT * FROM FJ1.TB5UPNR WITH(NOLOCK) WHERE IT5UPNRAIDN IN (SELECT IT5UPNRAIDN FROM FJ1.TB5UAVE WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)" },
            { "FJ1.TB5UPRP", "SELECT * FROM FJ1.TB5UPRP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UPRS", "SELECT * FROM FJ1.TB5UPRS WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UPMP", "SELECT * FROM FJ1.TB5UPMP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            // ==========================================
            // DONNEES FINANCIERES (LISA)
            // ==========================================

            { "LV.PRIST0", "SELECT * FROM LV.PRIST0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, D_ECH" },
            { "LV.PECHT0", "SELECT * FROM LV.PECHT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, D_ECH, NO_ORD_QUITT" },
            { "LV.PFIET0", "SELECT * FROM LV.PFIET0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY D_CRT_INST, NO_ORD_INST" },
            { "LV.PMNTT0", "SELECT * FROM LV.PMNTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, NO_ORD_QUITT" },
            { "LV.PRCTT0", "SELECT * FROM LV.PRCTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY D_REF_PRM ASC" },
            { "LV.PSUMT0", "SELECT * FROM LV.PSUMT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.SELTT0", "SELECT * FROM LV.SELTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },

            // ==========================================
            // DONNEES FONDS (LISA & ELIA)
            // ==========================================

            { "FJ1.TB5LPPF", "SELECT * FROM [FJ1].[TB5LPPF] WITH(NOLOCK) WHERE IT5LPPFNCON = @InternalId ORDER BY IT5LPPFCFDS" },
            { "FJ1.TB5UPRF", "SELECT * FROM [FJ1].[TB5UPRF] WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UFML", "SELECT * FROM [FJ1].[TB5UFML] WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "LV.FMVGT0",   "SELECT * FROM [LV].[FMVGT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY TSTAMP_DMOD" },
            { "LV.FMVDT0",   "SELECT * FROM [LV].[FMVDT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY TSTAMP_DMOD" },
            { "LV.SFTS",     "SELECT * FROM [LV].[LV5S18TSFTST0] WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.PINCT0",   "SELECT * FROM [LV].[PINCT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId" },

            // ==========================================
            // DONNEES CLAUSES (LISA & ELIA)
            // ==========================================

            { "LV.SCLST0", "SELECT * FROM [LV].[SCLST0] WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, NO_ORD_CLS" },
            { "LV.SCLRT0", "SELECT * FROM [LV].[LV5S16TSCLRT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.SCLDT0", "SELECT * FROM [LV].[LV5S17TSCLDT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "FJ1.TB5UCRB", "SELECT * FROM [FJ1].[TB5UCRB] WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UDCR", "SELECT * FROM [FJ1].[TB5UDCR] WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UBEN", "SELECT * FROM [FJ1].[TB5UBEN] WITH(NOLOCK) WHERE IT5UBENAIDN IN (SELECT IT5UBENAIDN FROM [FJ1].[TB5UDCR] WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)" },

            // ==========================================
            // DONNEES RESERVES (LISA)
            // ==========================================

            { "LV.BSPDT0", "SELECT * FROM LV.BSPDT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.BSPGT0", "SELECT * FROM LV.BSPGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.BPBAT0", "SELECT * FROM LV.BPBAT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
            { "LV.BPPAT0", "SELECT * FROM LV.BPPAT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },

            // ==========================================
            // DONNEES MODIFICATION (LISA)
            // ==========================================

            { "LV.MWBGT0", "SELECT * FROM LV.MWBGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_PRJ, C_PROP" }
        });
    }
}