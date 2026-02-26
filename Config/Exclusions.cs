using System.Collections.Generic;

public static class Exclusions
{
    // LISTE GLOBALE
    public static readonly IReadOnlyList<string> IgnoreColumns = new List<string>
    {
        // Identifiants techniques majeurs
        "NO_CNT", "NO_CNT_EXTENDED", "NO_AVT", "C_STE",

        // Dates techniques de creation et modification
        "D_CRT", "D_CRT_CNT", "TSTAMP_DMOD", "D_MOD", "D_JOB_DMOD", "D_GEST_DMOD",

        // Auteurs et Processus
        "NM_AUTEUR_CRT", "NM_AUTEUR_DMOD", "NM_AUTEUR", "NM_JOB_DMOD",
        "C_ID_GEST_DMOD", "C_ID_GEST", "TY_DMOD",

        // Fillers et champs vides
        "T_FILLER_11", "T_FILLER_20", "T_FILLER_30", "T_FILLER_31",
        "T_FILLER_36", "T_FILLER_84", "T_FILLER_85"
    };

    // EXCLUSIONS SPECIFIQUES
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SpecificExclusions = new Dictionary<string, IReadOnlyList<string>>
    {
        // Table Contrat
        { "LV.SCNTT0", new List<string> { "NO_POLICE_PAPIER", "NO_BUR_INTRO", "NO_BUR_INT_GES" } },

        // Table Beneficiaires / Clauses
        { "LV.SCLST0", new List<string> { "NO_ORD_CLS" } },
        { "LV.SCLRT0", new List<string> { "NO_ORD_RNG", "NO_ORD_CLS" } },

        // Tables Financieres
        { "LV.BSPDT0", new List<string> { "NO_ORD_TRF_EPA", "NO_ORD_MVT_EPA", "NO_ORD_QUITT", "NO_ORD_MVT_ANNUL", "D_REF_MVT_EPA", "D_STA_IMPR", "C_STA_IMPR" } },
        { "LV.BSPGT0", new List<string> { "NO_ORD_TRF_EPA" } },

        // Table Avenant
        { "LV.SAVTT0", new List<string> { "NO_AVT_REF", "NO_AVT_CLS", "NO_AVT_T_LBR", "NO_AVT_ELT", "NO_AVT_PB", "NO_AVT_DCL" } }
    };


    /// Methode utilitaire pour recuperer toutes les colonnes a exclure pour une table donnee

    public static HashSet<string> GetExclusionsForTable(string tableName)
    {
        // Utilisation d'un HashSet pour optimiser la recherche de colonnes (O(1) au lieu de O(N))
        var exclusions = new HashSet<string>(IgnoreColumns);

        if (SpecificExclusions.TryGetValue(tableName, out var specificCols))
        {
            foreach (var col in specificCols)
            {
                exclusions.Add(col);
            }
        }

        return exclusions;
    }
}