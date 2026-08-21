using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// ============================================================================
// GameManager.cs — Coordinador del flujo del juego y persistencia de métricas
// ============================================================================

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Playing, Completed }
    public GameState CurrentState { get; private set; } = GameState.Playing;

    private float totalFrameTime = 0f;
    private int frameCount = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (MissionManager.Instance != null)
        {
            MissionManager.Instance.OnObjectiveCompleted += HandleObjectiveCompleted;
        }
    }

    private void OnDestroy()
    {
        if (MissionManager.Instance != null)
        {
            MissionManager.Instance.OnObjectiveCompleted -= HandleObjectiveCompleted;
        }

        // Hallazgo #16: Limpiar Instance al destruirse
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Acumular FPS para métricas
        totalFrameTime += Time.unscaledDeltaTime;
        frameCount++;

        // Hallazgo #9: Tecla R para reiniciar
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            if (CurrentState == GameState.Playing)
            {
                // Si reinicia a mitad de camino, registrar corrida abortada (Hallazgo #9)
                LogRunData(aborted: true);
            }
            RestartGame();
        }
    }

    public float GetAverageFPS()
    {
        if (totalFrameTime <= 0f || frameCount <= 0) return 60f;
        return frameCount / totalFrameTime;
    }

    private void HandleObjectiveCompleted(Objective objective)
    {
        CurrentState = GameState.Completed;

        // Desactivar control del jugador
        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.SetInputEnabled(false);

        MouseLook mouseLook = FindFirstObjectByType<MouseLook>();
        if (mouseLook != null) mouseLook.SetLookEnabled(false);

        // Hallazgo #3: Registrar métricas desde GameManager, desvinculado de la UI
        LogRunData(aborted: false);
    }

    private void LogRunData(bool aborted)
    {
        float finalTime = MissionManager.Instance != null ? MissionManager.Instance.ElapsedTime : 0f;
        float totalDist = HUDController.Instance != null ? HUDController.Instance.TotalDistanceTraveled : 0f;
        float optimalDist = MissionManager.Instance != null ? MissionManager.Instance.OptimalPathLength : 1f;
        bool isNavMeshValid = MissionManager.Instance != null && MissionManager.Instance.IsNavMeshValid;

        GuidanceArrow arrow = FindFirstObjectByType<GuidanceArrow>();
        string finalMode = arrow != null ? arrow.CurrentMode.ToString() : "Off";
        int modeChanges = arrow != null ? arrow.ModeChangeCount : 0;

        float timeOff = arrow != null ? arrow.TimeInOff : 0f;
        float timeDirect = arrow != null ? arrow.TimeInDirect : 0f;
        float timeNavMesh = arrow != null ? arrow.TimeInNavMesh : 0f;

        float distOff = arrow != null ? arrow.DistInOff : 0f;
        float distDirect = arrow != null ? arrow.DistInDirect : 0f;
        float distNavMesh = arrow != null ? arrow.DistInNavMesh : 0f;

        int uniqueCells = HUDController.Instance != null ? HUDController.Instance.UniqueCellsVisitedCount : 0;
        MazeData mazeData = FindFirstObjectByType<MazeData>();

        int seed = mazeData != null ? mazeData.seed : 42;
        int w = mazeData != null ? mazeData.width : 13;
        int h = mazeData != null ? mazeData.height : 13;
        float braid = mazeData != null ? mazeData.braidPercent : 0.15f;

        RunMetrics runMetrics = new RunMetrics(
            totalTime: finalTime,
            totalDistance: totalDist,
            optimalDistance: optimalDist,
            finalGuidanceMode: finalMode,
            modeChangeCount: modeChanges,
            uniqueCellsVisited: uniqueCells,
            seed: seed,
            mazeWidth: w,
            mazeHeight: h,
            braidPercent: braid,
            isNavMeshValid: isNavMeshValid,
            aborted: aborted,
            avgFps: GetAverageFPS(),
            timeOff: timeOff,
            timeDirect: timeDirect,
            timeNavMesh: timeNavMesh,
            distOff: distOff,
            distDirect: distDirect,
            distNavMesh: distNavMesh
        );

        MetricsLogger.LogRun(runMetrics);

        if (!aborted)
        {
            // Hallazgo #22: Clave de récord parametrizada con la semilla
            SaveSystem.TrySaveBestTime(seed, finalTime);
        }
    }

    public void RestartGame()
    {
        Debug.Log("[GameManager] Reiniciando escena...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
