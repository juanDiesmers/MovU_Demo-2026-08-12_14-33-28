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

    /// <summary>
    /// Carga el mejor tiempo guardado para la semilla dada.
    /// </summary>
    public static float LoadBestTime(int seed = 42)
    {
        return PlayerPrefs.GetFloat($"{BestTimeBaseKey}_{seed}", 9999f);
    }
}
