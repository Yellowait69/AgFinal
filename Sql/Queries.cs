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

            // Recherche avec LIKE pour gérer le padding des colonnes CHAR(17) et les espaces de fin
            { "GET_INTERNAL_ID", @"
                SELECT TOP 1 NO_CNT
                FROM LV.SCNTT0 WITH(NOLOCK)
                WHERE NO_CNT_EXTENDED LIKE @ContractNumber + '%'
                OR NO_CNT_EXTENDED = REPLACE(@ContractNumber, '-', '')"
            },

            { "GET_ELIA_ID", @"
                SELECT TOP 1 IT5UCONAIDN
                FROM FJ1.TB5UCON WITH(NOLOCK)
                WHERE IT5UCONLREFEXN LIKE @ContractNumber + '%'"
            },

            // ==========================================
            // DONNEES LISA (Tables LV)
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
            }
        });
    }
}