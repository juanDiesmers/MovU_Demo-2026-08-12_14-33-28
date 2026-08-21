using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ============================================================================
// HUDController.cs — Controlador de la interfaz gráfica (HUD)
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

    private Transform playerTransform;
    private Transform goalTransform;
    private float totalDistanceTraveled = 0f;
    private Vector3 lastPlayerPosition;
    private MazeData mazeData;

    private HashSet<(int x, int y)> visitedCells = new HashSet<(int, int)>();

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

    private void Start()
    {
        if (resultsPanel != null)
        {
            resultsPanel.SetActive(false);
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            lastPlayerPosition = playerTransform.position;
        }

        mazeData = FindFirstObjectByType<MazeData>();

        if (MissionManager.Instance != null)
        {
            MissionManager.Instance.OnObjectiveCompleted += ShowResultsPanel;

            if (MissionManager.Instance.ActiveObjective != null)
            {
                goalTransform = MissionManager.Instance.ActiveObjective.target;
                if (txtObjective != null)
                {
                    txtObjective.text = $"Objetivo: {MissionManager.Instance.ActiveObjective.displayName}";
                }
            }
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
        if (MissionManager.Instance != null)
        {
            MissionManager.Instance.OnObjectiveCompleted -= ShowResultsPanel;
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
    }

    private void UpdateTimer()
    {
        if (MissionManager.Instance == null || txtTimer == null) return;

        float time = MissionManager.Instance.ElapsedTime;
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        txtTimer.text = $"{minutes:00}:{seconds:00}";
    }

    private void UpdateDistanceAndTraveled()
    {
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                lastPlayerPosition = playerTransform.position;
            }
            return;
        }

        // Hallazgo #10: Calcular distancia en plano horizontal (ignorar ruido Y de la gravedad)
        if (MissionManager.Instance != null && MissionManager.Instance.IsTimerRunning)
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

        if (goalTransform == null && MissionManager.Instance != null && MissionManager.Instance.ActiveObjective != null)
        {
            goalTransform = MissionManager.Instance.ActiveObjective.target;
        }

        if (goalTransform != null && txtDistance != null)
        {
            float distToGoal = Vector3.Distance(playerTransform.position, goalTransform.position);
            txtDistance.text = $"Distancia: {distToGoal:F1} m";
        }
    }

    private void TrackVisitedCells()
    {
        if (playerTransform == null || MissionManager.Instance == null || !MissionManager.Instance.IsTimerRunning)
            return;

        if (mazeData == null)
        {
            mazeData = FindFirstObjectByType<MazeData>();
        }

        if (mazeData != null)
        {
            if (mazeData.WorldToCell(playerTransform.position, out int cx, out int cy))
            {
                visitedCells.Add((cx, cy));
            }
        }
    }

    public void SetGuidanceModeText(string modeName)
    {
        if (txtGuidanceMode != null)
        {
            txtGuidanceMode.text = $"Guía: {modeName}";
        }
    }

    public void ShowResultsPanel(Objective objective)
    {
        if (resultsPanel == null) return;

        resultsPanel.SetActive(true);

        float finalTime = MissionManager.Instance != null ? MissionManager.Instance.ElapsedTime : 0f;
        int minutes = Mathf.FloorToInt(finalTime / 60f);
        int seconds = Mathf.FloorToInt(finalTime % 60f);

        float optimalDist = MissionManager.Instance != null ? MissionManager.Instance.OptimalPathLength : 1f;
        float detour = totalDistanceTraveled / Mathf.Max(0.001f, optimalDist);

        if (txtResultsTime != null)
        {
            txtResultsTime.text = $"Tiempo: {minutes:00}:{seconds:00} (Desvío: {detour:F2}x)";
        }

        if (txtResultsDistance != null)
        {
            txtResultsDistance.text = $"Distancia: {totalDistanceTraveled:F1}m (Óptima: {optimalDist:F1}m | Celdas: {visitedCells.Count})";
        }

        if (txtResultsBestTime != null)
        {
            int seed = mazeData != null ? mazeData.seed : 42;
            float bestTime = SaveSystem.LoadBestTime(seed);
            if (bestTime < 9999f)
            {
                int bMin = Mathf.FloorToInt(bestTime / 60f);
                int bSec = Mathf.FloorToInt(bestTime % 60f);
                txtResultsBestTime.text = $"Mejor tiempo: {bMin:00}:{bSec:00}";
            }
            else
            {
                txtResultsBestTime.text = "Mejor tiempo: --:--";
            }
        }
    }
}
