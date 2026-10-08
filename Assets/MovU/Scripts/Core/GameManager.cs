using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// ============================================================================
// GameManager.cs — Coordinador del flujo del juego y persistencia de métricas
// ============================================================================
// Escucha al MissionManager y escribe una fila de métricas por corrida:
//   - Demo (legado): una fila al completar el objetivo, o al abortar con R.
//   - Edificio (catálogo): una fila POR MISIÓN, con su id, el participante y
//     los cambios de piso. R reinicia la misión en curso sin recargar la escena.
// ============================================================================

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Playing, Completed }
    public GameState CurrentState { get; private set; } = GameState.Playing;

    private float totalFrameTime = 0f;
    private int frameCount = 0;

    // Peor segundo de la corrida: el SRS pide "nunca menos de 30 FPS" (RD-1),
    // y un promedio no lo deja ver.
    private float tiempoDeVentana = 0f;
    private int framesDeVentana = 0;
    private float fpsMinimo = float.MaxValue;

    private MissionManager misiones;
    private GuidanceArrow flecha;
    private MazeData laberinto;
    private bool laberintoBuscado;

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
        misiones = MissionManager.Instance;
        if (misiones != null)
        {
            misiones.OnObjectiveCompleted += HandleObjectiveCompleted;
            misiones.OnMissionStarted += HandleMissionStarted;
            misiones.OnMissionCompleted += HandleMissionCompleted;
        }
    }

    private void OnDestroy()
    {
        if (misiones != null)
        {
            misiones.OnObjectiveCompleted -= HandleObjectiveCompleted;
            misiones.OnMissionStarted -= HandleMissionStarted;
            misiones.OnMissionCompleted -= HandleMissionCompleted;
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
        float dt = Time.unscaledDeltaTime;
        totalFrameTime += dt;
        frameCount++;

        tiempoDeVentana += dt;
        framesDeVentana++;
        if (tiempoDeVentana >= 1f)
        {
            float fps = framesDeVentana / tiempoDeVentana;
            if (fps < fpsMinimo) fpsMinimo = fps;
            tiempoDeVentana = 0f;
            framesDeVentana = 0;
        }

        // Hallazgo #9: Tecla R para reiniciar
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            if (TestSession.EsperandoInicio) return;   // todavía no empezó la sesión

            if (misiones != null && misiones.ModoCatalogo)
            {
                ReiniciarEnCatalogo();
                return;
            }

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

    private void ReiniciarMedidaDeFps()
    {
        totalFrameTime = 0f;
        frameCount = 0;
        tiempoDeVentana = 0f;
        framesDeVentana = 0;
        fpsMinimo = float.MaxValue;
    }

    // ------------------------------------------------------------------
    // Modo catálogo (Edificio)
    // ------------------------------------------------------------------
    private void ReiniciarEnCatalogo()
    {
        switch (misiones.Estado)
        {
            case MissionManager.EstadoDeMisiones.EnCurso:
                LogRunData(aborted: true);          // la que se abandona queda registrada
                misiones.ReiniciarMision();
                break;

            case MissionManager.EstadoDeMisiones.EntreMisiones:
                misiones.ReiniciarMision();         // repetir la que se acaba de completar
                break;

            case MissionManager.EstadoDeMisiones.Terminado:
                RestartGame();                      // sesión nueva (otro participante)
                break;
        }
    }

    private void HandleMissionStarted(MisionEnCurso mision)
    {
        CurrentState = GameState.Playing;
        ReiniciarMedidaDeFps();
    }

    private void HandleMissionCompleted(MisionEnCurso mision)
    {
        CurrentState = GameState.Completed;
        LogRunData(aborted: false);
        // El jugador conserva el control: la siguiente misión empieza donde esté.
    }

    // ------------------------------------------------------------------
    // Modo legado (DemoMaze)
    // ------------------------------------------------------------------
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
        MissionManager mm = MissionManager.Instance;

        float finalTime = mm != null ? mm.ElapsedTime : 0f;
        float totalDist = HUDController.Instance != null ? HUDController.Instance.TotalDistanceTraveled : 0f;
        float optimalDist = mm != null ? mm.OptimalPathLength : 1f;
        bool isNavMeshValid = mm != null && mm.IsNavMeshValid;

        if (flecha == null) flecha = FindFirstObjectByType<GuidanceArrow>();
        GuidanceArrow arrow = flecha;
        string finalMode = arrow != null ? arrow.CurrentMode.ToString() : "Off";
        int modeChanges = arrow != null ? arrow.ModeChangeCount : 0;

        float timeOff = arrow != null ? arrow.TimeInOff : 0f;
        float timeDirect = arrow != null ? arrow.TimeInDirect : 0f;
        float timeNavMesh = arrow != null ? arrow.TimeInNavMesh : 0f;

        float distOff = arrow != null ? arrow.DistInOff : 0f;
        float distDirect = arrow != null ? arrow.DistInDirect : 0f;
        float distNavMesh = arrow != null ? arrow.DistInNavMesh : 0f;

        bool catalogo = mm != null && mm.ModoCatalogo;
        MisionEnCurso mision = catalogo ? mm.MisionActual : null;

        int uniqueCells = HUDController.Instance != null ? HUDController.Instance.UniqueCellsVisitedCount : 0;

        if (!laberintoBuscado)
        {
            laberinto = FindFirstObjectByType<MazeData>();
            laberintoBuscado = true;
        }
        MazeData mazeData = laberinto;

        // En el edificio no hay laberinto: las columnas del demo van en cero en
        // vez de arrastrar los valores por defecto (semilla 42, 13 x 13), que en
        // el CSV parecerían datos reales.
        int seed = mazeData != null ? mazeData.seed : (catalogo ? 0 : 42);
        int w = mazeData != null ? mazeData.width : (catalogo ? 0 : 13);
        int h = mazeData != null ? mazeData.height : (catalogo ? 0 : 13);
        float braid = mazeData != null ? mazeData.braidPercent : (catalogo ? 0f : 0.15f);

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

        runMetrics.scene = SceneManager.GetActiveScene().name;
        runMetrics.participantId = TestSession.ParticipantId;
        runMetrics.guidanceLocked = TestSession.GuiaBloqueada;
        runMetrics.minFps = fpsMinimo < float.MaxValue ? fpsMinimo : GetAverageFPS();

        if (mision != null)
        {
            runMetrics.missionId = mision.def != null ? mision.def.id : "";
            runMetrics.missionIndex = mision.indice + 1;
            runMetrics.poiId = mision.destino != null ? mision.destino.Id : "";
            runMetrics.startFloor = mision.pisoDeInicio + 1;
            runMetrics.targetFloor = mision.pisoDeDestino + 1;
            runMetrics.floorChanges = mision.cambiosDePiso;
            runMetrics.wrongFloorVisits = mision.pisosEquivocados;
            runMetrics.captureRadius = mision.radioDeLlegada;
            runMetrics.npcConsults = mision.consultasANpc;
            runMetrics.routeUsesElevator = mision.rutaConAscensor;
        }

        MetricsLogger.LogRun(runMetrics);

        if (!aborted)
        {
            if (mision != null && mision.def != null)
            {
                SaveSystem.TrySaveBestTime(mision.def.id, finalTime);
            }
            else
            {
                // Hallazgo #22: Clave de récord parametrizada con la semilla
                SaveSystem.TrySaveBestTime(seed, finalTime);
            }
        }
    }

    public void RestartGame()
    {
        Scene escena = SceneManager.GetActiveScene();
        if (escena.buildIndex < 0)
        {
            Debug.LogWarning("[GameManager] La escena '" + escena.name + "' no está en Build " +
                             "Settings, así que no se puede recargar. Añádela con " +
                             "'MovU > Proyecto > Guardar escena como Edificio.unity'.");
            return;
        }

        Debug.Log("[GameManager] Reiniciando escena...");
        SceneManager.LoadScene(escena.buildIndex);
    }
}
