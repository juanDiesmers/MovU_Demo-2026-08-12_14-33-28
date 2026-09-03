using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// ============================================================================
// GuidanceArrow.cs — Flecha 3D de guía espacial (La pieza central)
// ============================================================================
// Registra tiempo y distancia acumulados bajo cada modo (Hallazgo #8).
// Utiliza un material estático cacheado (Hallazgo #17).
// Posicionamiento ajustado a 0.8m para evitar recortes con paredes (Hallazgo #13).
// ============================================================================

public class GuidanceArrow : MonoBehaviour
{
    [Header("Configuración de Guía")]
    [SerializeField] private GuidanceMode currentMode = GuidanceMode.Off;
    [SerializeField] private float repathInterval = 0.25f;
    [SerializeField] private float rotationSpeed = 8.0f;
    [SerializeField] private float forwardOffset = 0.8f;   // Hallazgo #13: 0.8m frente a la cámara
    [SerializeField] private float verticalOffset = -0.25f;

    [Header("Depuración")]
    [SerializeField] private bool drawDebugPath = true;

    private Transform playerCamera;
    private Transform playerTransform;
    private Transform objectiveTarget;
    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;
    private Mesh arrowMesh;

    private Vector3 targetDirection = Vector3.forward;
    private NavMeshPath navMeshPath;
    private float lastRepathTime = 0f;
    private Vector3 lastPlayerPosition;

    // Cache estático de material (Hallazgo #17)
    private static Material cachedArrowMaterial;

    // Métricas por modo (Hallazgo #8)
    public float TimeInOff { get; private set; } = 0f;
    public float TimeInDirect { get; private set; } = 0f;
    public float TimeInNavMesh { get; private set; } = 0f;

    public float DistInOff { get; private set; } = 0f;
    public float DistInDirect { get; private set; } = 0f;
    public float DistInNavMesh { get; private set; } = 0f;

    public GuidanceMode CurrentMode => currentMode;
    public int ModeChangeCount { get; private set; } = 0;

    private void Awake()
    {
        navMeshPath = new NavMeshPath();
        BuildProceduralArrowMesh();
    }

    private void OnDestroy()
    {
        // Hallazgo #17b: el Mesh procedural es un objeto de Unity que no lo recoge
        // el GC de C#. Sin esto se filtra una malla por cada recarga de escena ([R]).
        if (arrowMesh != null)
        {
            if (Application.isPlaying) Destroy(arrowMesh);
            else DestroyImmediate(arrowMesh);
            arrowMesh = null;
        }
    }

    private void Start()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            playerCamera = mainCam.transform;
        }
        else
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerCamera = player.GetComponentInChildren<Camera>()?.transform;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            lastPlayerPosition = playerTransform.position;
            lastPlayerPosition.y = 0f;
        }

        if (MissionManager.Instance != null && MissionManager.Instance.ActiveObjective != null)
        {
            objectiveTarget = MissionManager.Instance.ActiveObjective.target;
        }

        UpdateModeState();
    }

    private void Update()
    {
        HandleInput();
        TrackMetricsPerMode();

        if (currentMode == GuidanceMode.Off) return;

        if (objectiveTarget == null && MissionManager.Instance != null && MissionManager.Instance.ActiveObjective != null)
        {
            objectiveTarget = MissionManager.Instance.ActiveObjective.target;
        }

        if (objectiveTarget == null || playerCamera == null) return;

        // Posicionar la flecha flotando frente a la cámara
        Vector3 desiredPosition = playerCamera.position + playerCamera.forward * forwardOffset + playerCamera.up * verticalOffset;
        transform.position = desiredPosition;

        CalculateTargetDirection();

        if (targetDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    private void HandleInput()
    {
        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            CycleMode();
        }
    }

    private void TrackMetricsPerMode()
    {
        if (MissionManager.Instance == null || !MissionManager.Instance.IsTimerRunning) return;

        float dt = Time.deltaTime;

        // Hallazgo #8 (corregido): se mide el desplazamiento HORIZONTAL DEL JUGADOR,
        // la misma fuente que HUDController.totalDistanceTraveled. Antes se medía la
        // posición de la propia flecha, que salta ~0.8 m al cambiar de modo y se mueve
        // en 3D, así que DistOff+DistDirect+DistNavMesh nunca sumaba TotalDistance.
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                lastPlayerPosition = playerTransform.position;
                lastPlayerPosition.y = 0f;
            }
        }

        float frameDist = 0f;
        if (playerTransform != null)
        {
            Vector3 currentPos = playerTransform.position;
            currentPos.y = 0f;

            frameDist = Vector3.Distance(currentPos, lastPlayerPosition);
            if (frameDist >= 5f) frameDist = 0f; // Descartar saltos bruscos (recarga/teleport)
            lastPlayerPosition = currentPos;
        }

        switch (currentMode)
        {
            case GuidanceMode.Off:
                TimeInOff += dt;
                DistInOff += frameDist;
                break;
            case GuidanceMode.Direct:
                TimeInDirect += dt;
                DistInDirect += frameDist;
                break;
            case GuidanceMode.NavMesh:
                TimeInNavMesh += dt;
                DistInNavMesh += frameDist;
                break;
        }
    }

    public void CycleMode()
    {
        switch (currentMode)
        {
            case GuidanceMode.Off: currentMode = GuidanceMode.Direct; break;
            case GuidanceMode.Direct: currentMode = GuidanceMode.NavMesh; break;
            case GuidanceMode.NavMesh: currentMode = GuidanceMode.Off; break;
        }

        ModeChangeCount++;
        UpdateModeState();
    }

    private void UpdateModeState()
    {
        if (meshRenderer != null)
        {
            meshRenderer.enabled = (currentMode != GuidanceMode.Off);
        }

        string modeName = "Sin guía";
        switch (currentMode)
        {
            case GuidanceMode.Direct: modeName = "Directa"; break;
            case GuidanceMode.NavMesh: modeName = "Ruta (NavMesh)"; break;
        }

        if (HUDController.Instance != null)
        {
            HUDController.Instance.SetGuidanceModeText(modeName);
        }
    }

    private void CalculateTargetDirection()
    {
        if (objectiveTarget == null) return;

        if (currentMode == GuidanceMode.Direct)
        {
            Vector3 dir = (objectiveTarget.position - transform.position);
            dir.y = 0;
            targetDirection = dir.normalized;
        }
        else if (currentMode == GuidanceMode.NavMesh)
        {
            if (Time.time - lastRepathTime >= repathInterval)
            {
                lastRepathTime = Time.time;
                UpdateNavMeshPath();
            }
        }
    }

    private void UpdateNavMeshPath()
    {
        if (objectiveTarget == null || playerCamera == null) return;

        Vector3 startPos = playerCamera.position;
        Vector3 goalPos = objectiveTarget.position;

        if (NavMesh.SamplePosition(startPos, out NavMeshHit startHit, 5.0f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(goalPos, out NavMeshHit goalHit, 5.0f, NavMesh.AllAreas))
        {
            if (NavMesh.CalculatePath(startHit.position, goalHit.position, NavMesh.AllAreas, navMeshPath) &&
                navMeshPath.status == NavMeshPathStatus.PathComplete && navMeshPath.corners.Length > 1)
            {
                int targetCornerIndex = 1;
                if (navMeshPath.corners.Length > 2)
                {
                    float distToNextCorner = Vector3.Distance(transform.position, navMeshPath.corners[1]);
                    if (distToNextCorner < 1.5f)
                    {
                        targetCornerIndex = 2; // Look-ahead smoothing
                    }
                }

                Vector3 targetCorner = navMeshPath.corners[targetCornerIndex];
                Vector3 dir = (targetCorner - transform.position);
                dir.y = 0;
                targetDirection = dir.normalized;

                if (drawDebugPath)
                {
                    for (int i = 0; i < navMeshPath.corners.Length - 1; i++)
                    {
                        Debug.DrawLine(navMeshPath.corners[i], navMeshPath.corners[i + 1], Color.cyan, repathInterval);
                    }
                }
            }
            else
            {
                Vector3 dir = (goalPos - transform.position);
                dir.y = 0;
                targetDirection = dir.normalized;
            }
        }
    }

    private void BuildProceduralArrowMesh()
    {
        // No re-añadir componentes si el prefab/escena ya los trae
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();
        mesh.name = "ProceduralArrowMesh";

        float h = 0.025f;

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(0f,      h,  0.35f),
            new Vector3(0.16f,   h,  0.08f),
            new Vector3(0.06f,   h,  0.08f),
            new Vector3(0.06f,   h, -0.25f),
            new Vector3(-0.06f,  h, -0.25f),
            new Vector3(-0.06f,  h,  0.08f),
            new Vector3(-0.16f,  h,  0.08f),

            new Vector3(0f,     -h,  0.35f),
            new Vector3(0.16f,  -h,  0.08f),
            new Vector3(0.06f,  -h,  0.08f),
            new Vector3(0.06f,  -h, -0.25f),
            new Vector3(-0.06f, -h, -0.25f),
            new Vector3(-0.06f, -h,  0.08f),
            new Vector3(-0.16f, -h,  0.08f)
        };

        int[] triangles = new int[]
        {
            0, 1, 2,  0, 2, 5,  0, 5, 6,  2, 3, 4,  2, 4, 5,
            7, 9, 8,  7, 12, 9, 7, 13, 12, 9, 11, 10, 9, 12, 11,
            0, 7, 8,   0, 8, 1,
            1, 8, 9,   1, 9, 2,
            2, 9, 10,  2, 10, 3,
            3, 10, 11, 3, 11, 4,
            4, 11, 12, 4, 12, 5,
            5, 12, 13, 5, 13, 6,
            6, 13, 7,  6, 7, 0
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        arrowMesh = mesh;
        meshFilter.sharedMesh = mesh;

        // Cache estático de material para evitar pérdidas de memoria (Hallazgo #17)
        if (cachedArrowMaterial == null)
        {
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Standard");

            cachedArrowMaterial = new Material(urpShader);
            cachedArrowMaterial.name = "Mat_GuidanceArrow_Shared";
            Color cyanColor = new Color(0.0f, 0.85f, 1.0f);
            cachedArrowMaterial.color = cyanColor;
            cachedArrowMaterial.EnableKeyword("_EMISSION");
            cachedArrowMaterial.SetColor("_EmissionColor", cyanColor * 2.0f);
        }

        meshRenderer.sharedMaterial = cachedArrowMaterial;
    }
}
