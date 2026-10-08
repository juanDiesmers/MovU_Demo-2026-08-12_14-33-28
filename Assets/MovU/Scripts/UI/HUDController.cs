using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ============================================================================
// HUDController.cs — Controlador de la interfaz gráfica (HUD)
// ============================================================================
// Sirve a las dos escenas:
//   - DemoMaze: las referencias vienen serializadas en la escena.
//   - Edificio: el lienzo lo arma HudBuilder en tiempo de ejecución y entrega
//     las referencias con Configure(...). Los campos nuevos (contador de
//     misión, piso, aviso, subtítulo) son opcionales: en el demo quedan en null.
//
// Rendimiento: los textos solo se reescriben cuando CAMBIA lo que muestran.
// Antes el cronómetro y la distancia creaban una cadena nueva en cada frame
// (unas 120 por segundo), y en la escena del edificio se buscaba un MazeData
// inexistente por toda la escena, también en cada frame.
// ============================================================================

public class HUDController : MonoBehaviour
{
    public static HUDController Instance { get; private set; }

    [Header("Elementos de Pantalla (TextMeshPro)")]
    [SerializeField] private TextMeshProUGUI txtObjective;
    [SerializeField] private TextMeshProUGUI txtTimer;
    [SerializeField] private TextMeshProUGUI txtDistance;
    [SerializeField] private TextMeshProUGUI txtGuidanceMode;

    [Header("Panel de Resultados")]
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TextMeshProUGUI txtResultsTime;
    [SerializeField] private TextMeshProUGUI txtResultsDistance;
    [SerializeField] private TextMeshProUGUI txtResultsBestTime;

    [Header("Edificio (opcionales)")]
    [SerializeField] private TextMeshProUGUI txtResultsTitle;
    [SerializeField] private TextMeshProUGUI txtResultsHint;
    [SerializeField] private TextMeshProUGUI txtMission;
    [SerializeField] private TextMeshProUGUI txtFloor;
    [SerializeField] private GameObject promptBox;
    [SerializeField] private TextMeshProUGUI txtPrompt;
    [SerializeField] private GameObject subtitleBox;
    [SerializeField] private TextMeshProUGUI txtSubtitle;

    private Transform playerTransform;
    private Transform goalTransform;
    private float totalDistanceTraveled = 0f;
    private Vector3 lastPlayerPosition;
    private MazeData mazeData;
    private bool mazeBuscado;
    private float bestTimeBeforeRun = 9999f;

    private HashSet<(int x, int y)> visitedCells = new HashSet<(int, int)>();

    // Lo último que se escribió en pantalla, para no reescribirlo si no cambió.
    private int segundosMostrados = -1;
    private int decimetrosMostrados = -1;
    private float ocultarSubtituloEn = -1f;
    private float siguienteBusquedaDeJugador;

    private MissionManager misiones;
    private FloorManager pisos;
    private NpcManager npcs;

    public float TotalDistanceTraveled => totalDistanceTraveled;
    public int UniqueCellsVisitedCount => visitedCells.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Entrega las referencias cuando el HUD se construye en tiempo de
    /// ejecución (escena Edificio). Hay que llamarlo antes de Start.
    /// </summary>
    public void Configure(TextMeshProUGUI objective, TextMeshProUGUI timer,
                          TextMeshProUGUI distance, TextMeshProUGUI guidanceMode,
                          GameObject results, TextMeshProUGUI resultsTitle,
                          TextMeshProUGUI resultsTime, TextMeshProUGUI resultsDistance,
                          TextMeshProUGUI resultsBest, TextMeshProUGUI resultsHint,
                          TextMeshProUGUI mission, TextMeshProUGUI floor,
                          GameObject prompt, TextMeshProUGUI promptText,
                          GameObject subtitle, TextMeshProUGUI subtitleText)
    {
        txtObjective = objective;
        txtTimer = timer;
        txtDistance = distance;
        txtGuidanceMode = guidanceMode;
        resultsPanel = results;
        txtResultsTitle = resultsTitle;
        txtResultsTime = resultsTime;
        txtResultsDistance = resultsDistance;
        txtResultsBestTime = resultsBest;
        txtResultsHint = resultsHint;
        txtMission = mission;
        txtFloor = floor;
        promptBox = prompt;
        txtPrompt = promptText;
        subtitleBox = subtitle;
        txtSubtitle = subtitleText;
    }

    private void Start()
    {
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (promptBox != null) promptBox.SetActive(false);
        if (subtitleBox != null) subtitleBox.SetActive(false);

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            lastPlayerPosition = playerTransform.position;
        }

        mazeData = FindFirstObjectByType<MazeData>();
        mazeBuscado = true;

        // Se cachea el récord ANTES de que empiece la corrida. Si se leyera al
        // completar el objetivo, el valor dependería de si GameManager ya guardó
        // el nuevo récord (ambos escuchan el mismo evento y el orden de Start()
        // no está garantizado), y el panel podía mostrar la corrida actual como
        // si fuera la mejor marca anterior.
        bestTimeBeforeRun = SaveSystem.LoadBestTime(mazeData != null ? mazeData.seed : 42);

        misiones = MissionManager.Instance;
        if (misiones != null)
        {
            misiones.OnObjectiveCompleted += ShowResultsPanel;
            misiones.OnMissionStarted += AlEmpezarMision;
            misiones.OnMissionCompleted += AlCompletarMision;

            if (misiones.ActiveObjective != null)
            {
                goalTransform = misiones.ActiveObjective.target;
                if (txtObjective != null)
                {
                    txtObjective.text = $"Objetivo: {misiones.ActiveObjective.displayName}";
                }
            }
            else if (misiones.ModoCatalogo && txtObjective != null)
            {
                txtObjective.text = "";
            }

            if (txtMission != null) txtMission.text = "";
        }

        pisos = FloorManager.Instance;
        if (pisos != null)
        {
            pisos.OnPisoCambiado += AlCambiarDePiso;
            MostrarPiso(playerTransform != null
                ? pisos.PisoSegunAltura(playerTransform.position.y)
                : pisos.PisoActual);
        }
        else if (txtFloor != null)
        {
            txtFloor.text = "";
        }

        npcs = NpcManager.Instance;
        if (npcs != null)
        {
            npcs.OnAvisoCambiado += MostrarAviso;
            npcs.OnNpcHabla += AlHablarNpc;
        }

        // Hallazgo #14: Consultar el modo actual de la flecha en lugar de incondicional "Sin guía"
        GuidanceArrow arrow = FindFirstObjectByType<GuidanceArrow>();
        if (arrow != null)
        {
            string modeName = "Sin guía";
            switch (arrow.CurrentMode)
            {
                case GuidanceMode.Direct: modeName = "Directa"; break;
                case GuidanceMode.NavMesh: modeName = "Ruta (NavMesh)"; break;
            }
            SetGuidanceModeText(modeName);
        }
        else
        {
            SetGuidanceModeText("Sin guía");
        }
    }

    private void OnDestroy()
    {
        if (misiones != null)
        {
            misiones.OnObjectiveCompleted -= ShowResultsPanel;
            misiones.OnMissionStarted -= AlEmpezarMision;
            misiones.OnMissionCompleted -= AlCompletarMision;
        }
        if (pisos != null) pisos.OnPisoCambiado -= AlCambiarDePiso;
        if (npcs != null)
        {
            npcs.OnAvisoCambiado -= MostrarAviso;
            npcs.OnNpcHabla -= AlHablarNpc;
        }

        // Hallazgo #16: Limpiar Instance al destruirse
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        UpdateTimer();
        UpdateDistanceAndTraveled();
        TrackVisitedCells();

        if (ocultarSubtituloEn > 0f && Time.unscaledTime >= ocultarSubtituloEn)
        {
            ocultarSubtituloEn = -1f;
            if (subtitleBox != null) subtitleBox.SetActive(false);
        }
    }

    private void UpdateTimer()
    {
        if (misiones == null || txtTimer == null) return;

        int total = Mathf.FloorToInt(misiones.ElapsedTime);
        if (total == segundosMostrados) return;       // el texto ya dice eso
        segundosMostrados = total;

        txtTimer.text = $"{total / 60:00}:{total % 60:00}";
    }

    private void UpdateDistanceAndTraveled()
    {
        if (playerTransform == null)
        {
            // Dos veces por segundo basta: buscar por tag en cada frame es caro.
            if (Time.unscaledTime < siguienteBusquedaDeJugador) return;
            siguienteBusquedaDeJugador = Time.unscaledTime + 0.5f;

            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                lastPlayerPosition = playerTransform.position;
            }
            return;
        }

        // Hallazgo #10: Calcular distancia en plano horizontal (ignorar ruido Y de la gravedad)
        if (misiones != null && misiones.IsTimerRunning)
        {
            Vector3 posA = playerTransform.position; posA.y = 0f;
            Vector3 posB = lastPlayerPosition;       posB.y = 0f;

            float frameDist = Vector3.Distance(posA, posB);
            if (frameDist < 5.0f)
            {
                totalDistanceTraveled += frameDist;
            }
            lastPlayerPosition = playerTransform.position;
        }

        if (goalTransform == null && misiones != null && misiones.ActiveObjective != null)
        {
            goalTransform = misiones.ActiveObjective.target;
        }

        if (goalTransform != null && txtDistance != null)
        {
            float distToGoal = Vector3.Distance(playerTransform.position, goalTransform.position);
            int decimetros = Mathf.RoundToInt(distToGoal * 10f);
            if (decimetros != decimetrosMostrados)
            {
                decimetrosMostrados = decimetros;
                txtDistance.text = $"Distancia: {distToGoal:F1} m";
            }
        }
    }

    private void TrackVisitedCells()
    {
        // Sin laberinto (escena Edificio) no hay celdas que contar. La búsqueda
        // se hizo una vez en Start; no se repite.
        if (mazeData == null)
        {
            if (mazeBuscado) return;
            mazeData = FindFirstObjectByType<MazeData>();
            mazeBuscado = true;
            if (mazeData == null) return;
        }

        if (playerTransform == null || misiones == null || !misiones.IsTimerRunning)
            return;

        if (mazeData.WorldToCell(playerTransform.position, out int cx, out int cy))
        {
            visitedCells.Add((cx, cy));
        }
    }

    public void SetGuidanceModeText(string modeName)
    {
        if (txtGuidanceMode != null)
        {
            txtGuidanceMode.text = $"Guía: {modeName}";
        }
    }

    // ------------------------------------------------------------------
    // Edificio: misiones encadenadas
    // ------------------------------------------------------------------
    private void AlEmpezarMision(MisionEnCurso mision)
    {
        if (resultsPanel != null) resultsPanel.SetActive(false);

        totalDistanceTraveled = 0f;
        visitedCells.Clear();
        if (playerTransform != null) lastPlayerPosition = playerTransform.position;

        segundosMostrados = -1;
        decimetrosMostrados = -1;

        goalTransform = mision.destino != null ? mision.destino.transform : null;
        bestTimeBeforeRun = SaveSystem.LoadBestTime(mision.def != null ? mision.def.id : "");

        if (txtObjective != null)
        {
            txtObjective.text = mision.def != null ? mision.def.enunciado : "";
        }
        if (txtMission != null)
        {
            txtMission.text = $"Misión {mision.indice + 1} de {mision.total}";
        }
    }

    private void AlCompletarMision(MisionEnCurso mision)
    {
        if (resultsPanel == null) return;
        resultsPanel.SetActive(true);

        bool esLaUltima = misiones == null ||
                          misiones.Estado == MissionManager.EstadoDeMisiones.Terminado;

        if (txtResultsTitle != null)
        {
            txtResultsTitle.text = mision.destino != null
                ? $"¡Llegaste a {mision.destino.Nombre}!"
                : "¡Misión completada!";
        }

        EscribirResultados(mision.tiempo, mision.rutaOptima, false);

        if (txtResultsHint != null)
        {
            txtResultsHint.text = esLaUltima
                ? "Completaste todas las misiones.  R: empezar de nuevo"
                : "Enter: siguiente misión     R: repetir esta";
        }
    }

    private void AlCambiarDePiso(int anterior, int nuevo)
    {
        MostrarPiso(nuevo);
    }

    private void MostrarPiso(int indice)
    {
        if (txtFloor != null) txtFloor.text = $"Piso {FloorManager.NumeroVisible(indice)}";
    }

    /// <summary>Aviso corto de interacción ("E — Preguntar a..."). Vacío = ocultarlo.</summary>
    public void MostrarAviso(string texto)
    {
        if (promptBox == null || txtPrompt == null) return;

        bool visible = !string.IsNullOrEmpty(texto);
        if (visible) txtPrompt.text = texto;
        if (promptBox.activeSelf != visible) promptBox.SetActive(visible);
    }

    private void AlHablarNpc(string quien, string frase)
    {
        MostrarSubtitulo(string.IsNullOrEmpty(quien) ? frase : $"<b>{quien}:</b>  {frase}", 6f);
    }

    public void MostrarSubtitulo(string texto, float segundos)
    {
        if (subtitleBox == null || txtSubtitle == null) return;
        txtSubtitle.text = texto;
        subtitleBox.SetActive(true);
        ocultarSubtituloEn = Time.unscaledTime + Mathf.Max(1f, segundos);
    }

    // ------------------------------------------------------------------
    // Resultados
    // ------------------------------------------------------------------
    public void ShowResultsPanel(Objective objective)
    {
        if (resultsPanel == null) return;

        resultsPanel.SetActive(true);

        float finalTime = misiones != null ? misiones.ElapsedTime : 0f;
        float optimalDist = misiones != null ? misiones.OptimalPathLength : 1f;
        EscribirResultados(finalTime, optimalDist, true);
    }

    private void EscribirResultados(float finalTime, float optimalDist, bool conCeldas)
    {
        int minutes = Mathf.FloorToInt(finalTime / 60f);
        int seconds = Mathf.FloorToInt(finalTime % 60f);

        float detour = totalDistanceTraveled / Mathf.Max(0.001f, optimalDist);

        if (txtResultsTime != null)
        {
            txtResultsTime.text = $"Tiempo: {minutes:00}:{seconds:00} (Desvío: {detour:F2}x)";
        }

        if (txtResultsDistance != null)
        {
            txtResultsDistance.text = conCeldas
                ? $"Distancia: {totalDistanceTraveled:F1}m (Óptima: {optimalDist:F1}m | Celdas: {visitedCells.Count})"
                : $"Distancia: {totalDistanceTraveled:F1} m (Óptima: {optimalDist:F1} m)";
        }

        if (txtResultsBestTime != null)
        {
            bool isNewRecord = finalTime < bestTimeBeforeRun;

            if (bestTimeBeforeRun >= 9999f)
            {
                txtResultsBestTime.text = "Mejor tiempo: --:-- (primera corrida)";
            }
            else
            {
                int bMin = Mathf.FloorToInt(bestTimeBeforeRun / 60f);
                int bSec = Mathf.FloorToInt(bestTimeBeforeRun % 60f);
                string prefix = isNewRecord ? "¡NUEVO RÉCORD! Anterior" : "Mejor tiempo";
                txtResultsBestTime.text = $"{prefix}: {bMin:00}:{bSec:00}";
            }
        }
    }
}
