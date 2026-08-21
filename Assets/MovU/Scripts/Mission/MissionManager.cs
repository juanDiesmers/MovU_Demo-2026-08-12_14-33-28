using System;
using UnityEngine;
using UnityEngine.AI;

// ============================================================================
// MissionManager.cs — Gestor de misiones y cronómetro
// ============================================================================

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("Objetivo")]
    [SerializeField] private Objective activeObjective;

    public event Action<Objective> OnObjectiveCompleted;

    private float elapsedTime = 0f;
    private bool isTimerRunning = false;
    private float optimalPathLength = 0f;
    private bool isNavMeshValid = false;

    public float ElapsedTime => elapsedTime;
    public float OptimalPathLength => optimalPathLength;
    public Objective ActiveObjective => activeObjective;
    public bool IsTimerRunning => isTimerRunning;
    public bool IsNavMeshValid => isNavMeshValid;

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
        if (activeObjective == null || activeObjective.target == null)
        {
            GameObject goalObj = GameObject.FindWithTag("Goal");
            if (goalObj == null) goalObj = GameObject.Find("Goal");

            if (goalObj != null)
            {
                activeObjective = new Objective("goal_01", "Encuentra la sala de servidores", goalObj.transform);
            }
        }

        CalculateOptimalPathLength();
        StartTimer();
    }

    private void OnDestroy()
    {
        // Hallazgo #16: Limpiar Instance al destruirse
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (isTimerRunning)
        {
            elapsedTime += Time.deltaTime;
        }
    }

    private void CalculateOptimalPathLength()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null || activeObjective == null || activeObjective.target == null)
        {
            Debug.LogWarning("[MissionManager] No se pudo calcular la ruta óptima: falta Jugador u Objetivo.");
            return;
        }

        Vector3 startPos = playerObj.transform.position;
        Vector3 targetPos = activeObjective.target.position;

        // Hallazgo #1: Verificación dura del NavMesh
        if (!NavMesh.SamplePosition(startPos, out NavMeshHit startHit, 5.0f, NavMesh.AllAreas))
        {
            Debug.LogError("[MissionManager] ¡CRÍTICO! NavMesh NO disponible en la posición inicial. Métricas de eficiencia inválidas.");
            isNavMeshValid = false;
            optimalPathLength = Vector3.Distance(startPos, targetPos);
            return;
        }

        if (NavMesh.SamplePosition(targetPos, out NavMeshHit targetHit, 5.0f, NavMesh.AllAreas))
        {
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(startHit.position, targetHit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                isNavMeshValid = true;
                float rawLength = 0f;
                for (int i = 0; i < path.corners.Length - 1; i++)
                {
                    rawLength += Vector3.Distance(path.corners[i], path.corners[i + 1]);
                }

                // Hallazgo #6: Descontar el radio del trigger (0.6m) para eliminar sesgo de distancia
                optimalPathLength = Mathf.Max(0.1f, rawLength - 0.6f);
                Debug.Log($"[MissionManager] Ruta óptima NavMesh calculada: {optimalPathLength:F2} m (Ajustada con radio de trigger).");
            }
            else
            {
                Debug.LogWarning("[MissionManager] NavMesh.CalculatePath no pudo encontrar una ruta completa.");
                isNavMeshValid = false;
                optimalPathLength = Vector3.Distance(startPos, targetPos);
            }
        }
        else
        {
            Debug.LogWarning("[MissionManager] NavMesh.SamplePosition falló en el objetivo.");
            isNavMeshValid = false;
            optimalPathLength = Vector3.Distance(startPos, targetPos);
        }
    }

    public void SetActiveObjective(Objective objective)
    {
        activeObjective = objective;
    }

    public void StartTimer()
    {
        isTimerRunning = true;
    }

    public void StopTimer()
    {
        isTimerRunning = false;
    }

    public void CompleteObjective()
    {
        if (!isTimerRunning) return;

        StopTimer();
        Debug.Log($"[MissionManager] ¡Objetivo '{activeObjective?.displayName}' completado en {elapsedTime:F2}s!");
        OnObjectiveCompleted?.Invoke(activeObjective);
    }
}
