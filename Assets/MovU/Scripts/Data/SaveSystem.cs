using System.Collections.Generic;
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

    // ------------------------------------------------------------------
    // Progreso de la jornada (RF-11): misiones completadas por participante.
    // Se guarda en PlayerPrefs como "M01;M02;..." con el id del participante
    // ya limpio (MetricsLogger.Limpiar), para poder continuar una sesión.
    // ------------------------------------------------------------------
    private const string ProgresoBaseKey = "MovU_Progreso_";

    public static List<string> CargarCompletadas(string participante)
    {
        var lista = new List<string>();
        if (string.IsNullOrEmpty(participante)) return lista;
        string crudo = PlayerPrefs.GetString(ProgresoBaseKey + participante, "");
        if (string.IsNullOrEmpty(crudo)) return lista;
        foreach (string id in crudo.Split(';'))
        {
            if (!string.IsNullOrEmpty(id) && !lista.Contains(id)) lista.Add(id);
        }
        return lista;
    }

    public static void GuardarMisionCompletada(string participante, string missionId)
    {
        if (string.IsNullOrEmpty(participante) || string.IsNullOrEmpty(missionId)) return;
        List<string> lista = CargarCompletadas(participante);
        if (lista.Contains(missionId)) return;
        lista.Add(missionId);
        PlayerPrefs.SetString(ProgresoBaseKey + participante, string.Join(";", lista));
        PlayerPrefs.Save();
    }

    public static void BorrarProgreso(string participante)
    {
        if (string.IsNullOrEmpty(participante)) return;
        PlayerPrefs.DeleteKey(ProgresoBaseKey + participante);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------
    // Opciones del jugador (RF-13): sensibilidad del ratón y volumen.
    // ------------------------------------------------------------------
    public const float SensibilidadMin = 0.05f, SensibilidadMax = 0.40f, SensibilidadPorDefecto = 0.15f;
    private const string SensibilidadKey = "MovU_Opt_Sensibilidad";
    private const string VolumenKey = "MovU_Opt_Volumen";

    public static float Sensibilidad
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(SensibilidadKey, SensibilidadPorDefecto), SensibilidadMin, SensibilidadMax);
        set { PlayerPrefs.SetFloat(SensibilidadKey, Mathf.Clamp(value, SensibilidadMin, SensibilidadMax)); }
    }

    public static float Volumen
    {
        // 1 = el volumen que ya fija el JSON (AudioManager); esto lo escala por encima.
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumenKey, 1f));
        set { PlayerPrefs.SetFloat(VolumenKey, Mathf.Clamp01(value)); }
    }

    public static void GuardarOpciones() { PlayerPrefs.Save(); }

    /// <summary>
    /// Carga el mejor tiempo guardado para la semilla dada.
    /// </summary>
    public static float LoadBestTime(int seed = 42)
    {
        return PlayerPrefs.GetFloat($"{BestTimeBaseKey}_{seed}", 9999f);
    }
}
