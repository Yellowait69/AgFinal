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
                FROM (
                    SELECT NO_CNT FROM LV.SCNTT0 WITH(NOLOCK) WHERE NO_CNT_EXTENDED LIKE '%' + @ContractNumber + '%'
                    UNION ALL
                    SELECT NO_CNT FROM LV.SCNTT1 WITH(NOLOCK) WHERE NO_CNT_EXTENDED LIKE '%' + @ContractNumber + '%'
                ) AS ResultTbl"
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

            { "LV.SCNTT0", @"
                SELECT C_STE, NO_CNT, C_PROP_PRINC, D_CRT_CNT, D_PDC_CNT, D_EFF_CNT, C_ETAT_CNT,
                       DU_ASSNC_ORGN, D_TRM_CNT, M_CAP_DC_ORGN, M_CAP_VIE_ORGN, M_RTE_ORGN, C_PROP_PRINC_ORGN,
                       D_ETAT_CNT, C_TRAIT_CNT, D_TRAIT_CNT, C_EVOL_TRAIT, D_EVOL_TRAIT, C_ETAT_CNT_PIE,
                       D_ETAT_CNT_PIE, D_RCP_POL_SIGN, M_CAP_DC, M_CAP_VIE, M_RTE, D_PDC_AVT, TY_BENEF,
                       NO_BUR_INT_GES, NO_CNT_EXTENDED, NO_DERN_AVT, NO_DERN_PRJ, C_ORGN_CNT, NO_BUR_INTRO,
                       TSTAMP_DMOD, NM_AUTEUR_CRT, D_CRT, TY_DMOD, C_ID_GEST_DMOD, D_GEST_DMOD, NM_JOB_DMOD,
                       D_JOB_DMOD, C_SIN_APR_LQD, C_BUR_INT, NO_PERS_DEMR, D_LIM_CLI_TSFR_RES,
                       D_DEB_CLI_TSFR_RES, C_STA_SOC_ASS, B_CNT_PROL, * FROM LV.SCNTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },

            { "LV.SWBGT0", @"
                SELECT NO_CNT, NO_AVT, C_PROP, C_TRF, C_SEX_ASS, C_PER_PRM, DU_GAR, D_TRM_GAR, D_TRM_GAR_CLI,
                       B_TAX_PRM_ETAL, C_VALID_INTRT_DRG, PC_ANN_COM, PC_FR_GEST_ANN, C_APPL_GAR, 'All DATA' as All_Data, * FROM LV.SWBGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, C_PROP DESC" },

            { "LV.SAVTT0", @"
                SELECT NO_AVT_GAR, NO_AVT_DRG, N_COMPL, N_RS, TY_MDF, C_OP_MDF, C_PROC_ELIA, C_STE, NO_CNT,
                       NO_AVT, NO_BUR_SERV, C_ETAT_AVT, D_ETAT_AVT, D_PDC_AVT, D_PDC_CLI, D_EFF_AVT, C_ID_GEST,
                       C_CAT_MDF, C_PROP_PRINC, NO_AVT_REF, NO_AVT_CLS, NO_AVT_T_LBR, NO_AVT_ELT, NO_AVT_PB,
                       TSTAMP_DMOD, NM_AUTEUR_CRT, D_CRT, TY_DMOD, NO_AVT_DCL, * FROM [LV].[SAVTT0] WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, NO_CNT" },

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

            { "FJ1.TB5HDMD", @"
                SELECT IT2RELANRELGST, IT5HDMDLREFEXN as HDMDLREFEXN, IT5HDMDLREFAGTCUA as HDMDLREFAGTCUA, * FROM FJ1.TB5HDMD WITH(NOLOCK)
                WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HDGM", "SELECT * FROM FJ1.TB5HDGM WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HDGD", "SELECT * FROM FJ1.TB5HDGD WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },
            { "FJ1.TB5HPRO", "SELECT * FROM FJ1.TB5HPRO WITH(NOLOCK) WHERE IT5HDMDAIDN IN (SELECT value FROM STRING_SPLIT(@DemandIds, ','))" },

            // ==========================================
            // DONNEES ELIA (CONTRAT)
            // ==========================================

            { "FJ1.TB5HELT", "SELECT * FROM FJ1.TB5HELT WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UCON", "SELECT * FROM FJ1.TB5UCON WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            { "FJ1.TB5UGAR", @"
                SELECT IT5UGARCSEGTAR, IT5UGARBTARNONFUM, IT5UGARPDRGPRI, IT5UGARPSPRPFS, IT5UGARPSPRSPO,
                       IT5UGARPSPRAME, IT5UGARPSPRTPEAME, IT5UGARQDRESPRTPE, * FROM FJ1.TB5UGAR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            { "FJ1.TB5UASU", @"
                SELECT IT5UASUBECLCLUPEN, IT5UASULCRAREFEXN, IT5UASUPSPRBMI, IT5UASUPSPRFUM, * FROM FJ1.TB5UASU WITH(NOLOCK)
                WHERE IT5UASUAIDN IN (SELECT IT5UASUAIDN FROM FJ1.TB5UGAR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)" },

            { "FJ1.TB5UCCR", "SELECT * FROM FJ1.TB5UCCR WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            { "FJ1.TB5UAVE", @"
                SELECT IT5UAVELREFAGTCUA as UAVELREFAGTCUA, * FROM FJ1.TB5UAVE WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            { "FJ1.TB5UPNR", @"
                SELECT IT5UPNRCACTPFS, * FROM FJ1.TB5UPNR WITH(NOLOCK)
                WHERE IT5UPNRAIDN IN (SELECT IT5UPNRAIDN FROM FJ1.TB5UAVE WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId)" },

            { "FJ1.TB5UPRP", @"
                SELECT IT5UPRPPSPRPMT, IT5UPRPPSPRPMTCMS, IT5UPRPPSPRPMTILM, IT5UPRPPSPRBMI, IT5UPRPPSPRFUM, * FROM FJ1.TB5UPRP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            { "FJ1.TB5UPRS", "SELECT * FROM FJ1.TB5UPRS WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },
            { "FJ1.TB5UPMP", "SELECT * FROM FJ1.TB5UPMP WITH(NOLOCK) WHERE IT5UCONAIDN = @EliaId" },

            // ==========================================
            // DONNEES FINANCIERES (LISA)
            // ==========================================

            { "LV.PRIST0", "SELECT * FROM LV.PRIST0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, D_ECH" },
            { "LV.PECHT0", "SELECT * FROM LV.PECHT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, D_ECH, NO_ORD_QUITT" },
            { "LV.PFIET0", "SELECT * FROM LV.PFIET0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY D_CRT_INST, NO_ORD_INST" },
            { "LV.PMNTT0", "SELECT * FROM LV.PMNTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_AVT, NO_ORD_QUITT" },
            { "LV.PRCTT0", "SELECT * FROM LV.PRCTT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId" },
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

            { "LV.MWBGT0", @"
                SELECT NM_BENEF, PNM_BENEF, D_NAIS_BENEF, C_SEX_BENEF, NO_PERS_BENEF, * FROM LV.MWBGT0 WITH(NOLOCK) WHERE NO_CNT = @InternalId ORDER BY NO_PRJ, C_PROP" }
        });
    }
}