using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AutoActivator.Sql
{
    public static class SqlQueries
    {
        public static readonly ReadOnlyDictionary<string, string> Queries = new(new Dictionary<string, string>
        {
            // ==========================================
            // RECUPERATION ID
            // ==========================================


            { "GET_INTERNAL_ID", """
                SELECT TOP 1 NO_CNT
                FROM LV.SCNTT0 WITH(NOLOCK)
                WHERE NO_CNT_EXTENDED = @ContractNumber
                """ },


            // ==========================================
            // DONNEES CONTRAT & AVENANTS
            // ==========================================

            { "LV.SCNTT0", """
                SELECT * FROM LV.SCNTT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                """ },

            { "LV.SAVTT0", """
                SELECT * FROM LV.SAVTT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY NO_AVT ASC
                """ },


            // ==========================================
            // DONNEES PAIEMENTS
            // ==========================================

            // Indispensable pour verifier que l'activation (le paiement) a bien ete prise en compte.
            // On trie par date de reference et timestamp pour comparer l'historique comptable.
            { "LV.PRCTT0", """
                SELECT * FROM LV.PRCTT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY D_REF_PRM ASC, TSTAMP_CRT_RCT ASC
                """ },


            // ==========================================
            // DONNEES PRODUITS / GARANTIES
            // ==========================================

            { "LV.SWBGT0", """
                SELECT * FROM LV.SWBGT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY NO_AVT ASC, C_PROP ASC
                """ },


            // ==========================================
            // DONNEES BENEFICIAIRES & CLAUSES
            // ==========================================

            { "LV.SCLST0", """
                SELECT * FROM LV.SCLST0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY NO_AVT ASC, NO_ORD_CLS ASC
                """ },

            { "LV.SCLRT0", """
                SELECT * FROM LV.SCLRT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY NO_AVT ASC, NO_ORD_CLS ASC, NO_ORD_RNG ASC
                """ },


            // ==========================================
            // DONNEES FINANCIERES
            // ==========================================

            // Tri par date d'abord, puis par sequence.
            // Cela stabilise la comparaison si les sequences techniques changent mais pas la chronologie metier.
            { "LV.BSPDT0", """
                SELECT * FROM LV.BSPDT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY D_REF_MVT_EPA ASC, NO_ORD_TRF_EPA ASC, NO_ORD_MVT_EPA ASC
                """ },

            { "LV.BSPGT0", """
                SELECT * FROM LV.BSPGT0 WITH(NOLOCK)
                WHERE NO_CNT = @InternalId
                ORDER BY D_REF_MVT_EPA ASC, NO_ORD_TRF_EPA ASC
                """ }
        });
    }
}