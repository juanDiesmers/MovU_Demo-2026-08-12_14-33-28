using UnityEngine;

// ============================================================================
// SaveSystem.cs — Persistencia del mejor tiempo con PlayerPrefs
// ============================================================================

public static class SaveSystem
{
    private const string BestTimeBaseKey = "MovU_BestTime";

    /// <summary>
    /// Intenta guardar el tiempo actual como nuevo récord para la semilla específica (Hallazgo #22).
    /// </summary>
    public static bool TrySaveBestTime(int seed, float timeSec)
    {
        float currentBest = LoadBestTime(seed);

        if (timeSec < currentBest)
        {
            PlayerPrefs.SetFloat($"{BestTimeBaseKey}_{seed}", timeSec);
            PlayerPrefs.Save();
            Debug.Log($"[SaveSystem] ¡Nuevo récord para semilla {seed}!: {timeSec:F2}s (Anterior: {currentBest:F2}s)");
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Récord por misión (escena Edificio). La clave es el id de la misión.
    // ------------------------------------------------------------------
    public static bool TrySaveBestTime(string missionId, float timeSec)
    {
        if (string.IsNullOrEmpty(missionId)) return false;

        float currentBest = LoadBestTime(missionId);
        if (timeSec < currentBest)
        {
            PlayerPrefs.SetFloat($"{BestTimeBaseKey}_M_{missionId}", timeSec);
            PlayerPrefs.Save();
            return true;
        }
        return false;
    }

    public static float LoadBestTime(string missionId)
    {
        if (string.IsNullOrEmpty(missionId)) return 9999f;
        return PlayerPrefs.GetFloat($"{BestTimeBaseKey}_M_{missionId}", 9999f);
    }

    /// <summary>
    /// Carga el mejor tiempo guardado para la semilla dada.
    /// </summary>
    public static float LoadBestTime(int seed = 42)
    {
        return PlayerPrefs.GetFloat($"{BestTimeBaseKey}_{seed}", 9999f);
    }
}
