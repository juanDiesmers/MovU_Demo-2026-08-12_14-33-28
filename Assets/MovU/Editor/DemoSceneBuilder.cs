using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.AI.Navigation;
using TMPro;

// ============================================================================
// DemoSceneBuilder.cs — Constructor estático de la escena y Build
// ============================================================================
// Script de Editor ejecutable desde el menú:
//   - MovU > Construir escena demo
//   - MovU > Generar Build de Windows
// ============================================================================

public static class DemoSceneBuilder
{
    private const string ScenePath = "Assets/MovU/Scenes/DemoMaze.unity";
    private const string ConfigPath = "Assets/MovU/DemoConfig.asset";

    [MenuItem("MovU/Generar Build de Windows")]
    public static void BuildWindowsExecutable()
    {
        // 1. Reconstruir primero la escena demo para garantizar frescura
        BuildScene();

        // 2. Ejecutar Build Pipeline
        string buildPath = "Builds/Windows/MovU_Demo.exe";
        string[] scenes = new string[] { ScenePath };

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Debug.Log("[DemoSceneBuilder] Iniciando generación de ejecutable Standalone Windows x64...");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            string fullPath = System.IO.Path.GetFullPath(buildPath);
            Debug.Log($"[DemoSceneBuilder] ¡Build de Windows generado con éxito!\nUbicación: {fullPath}\nTamaño: {summary.totalSize / (1024 * 1024)} MB");
            EditorUtility.RevealInFinder(fullPath);
        }
        else if (summary.result == BuildResult.Failed)
        {
            Debug.LogError($"[DemoSceneBuilder] Error al generar el Build de Windows ({summary.totalErrors} errores).");
        }
    }

    [MenuItem("MovU/Construir escena demo")]
    public static void BuildScene()
    {
        // 1. Garantizar que existen los Tags requeridos
        EnsureTagExists("Player");
        EnsureTagExists("Goal");

        // 2. Cargar o crear la configuración DemoConfig
        DemoConfig config = AssetDatabase.LoadAssetAtPath<DemoConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<DemoConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DemoSceneBuilder] Creado nuevo DemoConfig en {ConfigPath}");
        }

        // 3. Generar el laberinto lógico
        MazeGrid grid = MazeGenerator.Generate(config.mazeWidth, config.mazeHeight, config.mazeSeed, config.braidPercent);

        if (!grid.IsSolvable())
        {
            Debug.LogError("[DemoSceneBuilder] ¡El laberinto generado NO es resoluble! Abortando construcción.");
            return;
        }

        Debug.Log($"[DemoSceneBuilder] Laberinto generado correctamente. Inicio: ({grid.StartX}, {grid.StartY}), Objetivo: ({grid.GoalX}, {grid.GoalY})");

        // 4. Crear una nueva escena limpia
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 5. Crear contenedor principal
        GameObject environmentRoot = new GameObject("Environment");

        // 6. Crear materiales URP
        Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLitShader == null)
        {
            Debug.LogWarning("[DemoSceneBuilder] No se encontró el shader 'Universal Render Pipeline/Lit'. Se intentará usar 'Standard'.");
            urpLitShader = Shader.Find("Standard");
        }

        Material floorMat = CreateMaterial(urpLitShader, "Mat_Floor", new Color(0.85f, 0.85f, 0.82f));
        Material wallMat = CreateMaterial(urpLitShader, "Mat_Wall", new Color(0.35f, 0.38f, 0.42f));

        // 7. Generar suelo
        float totalWidth = config.mazeWidth * config.cellSize;
        float totalHeight = config.mazeHeight * config.cellSize;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.parent = environmentRoot.transform;
        floor.transform.position = new Vector3(totalWidth * 0.5f, -0.1f, totalHeight * 0.5f);
        floor.transform.localScale = new Vector3(totalWidth + config.wallThickness * 2f, 0.2f, totalHeight + config.wallThickness * 2f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        // 8. Generar muros optimizados (combinación colineal)
        GameObject wallsGroup = new GameObject("Walls");
        wallsGroup.transform.parent = environmentRoot.transform;

        BuildMergedWalls(grid, config, wallsGroup.transform, wallMat);

        // 9. Generar Landmarks en intersecciones de >= 3 salidas
        GameObject landmarksGroup = new GameObject("Landmarks");
        landmarksGroup.transform.parent = environmentRoot.transform;

        BuildLandmarks(grid, config, landmarksGroup.transform, urpLitShader);

        // 10. Iluminación
        BuildLighting();

        // 11. Componente MazeData en runtime
        GameObject mazeDataObj = new GameObject("MazeData");
        MazeData mazeData = mazeDataObj.AddComponent<MazeData>();
        mazeData.seed = config.mazeSeed;
        mazeData.width = config.mazeWidth;
        mazeData.height = config.mazeHeight;
        mazeData.braidPercent = config.braidPercent;
        mazeData.cellSize = config.cellSize;
        mazeData.wallHeight = config.wallHeight;
        mazeData.wallThickness = config.wallThickness;
        mazeData.originPosition = Vector3.zero;
        mazeData.startX = grid.StartX;
        mazeData.startY = grid.StartY;
        mazeData.goalX = grid.GoalX;
        mazeData.goalY = grid.GoalY;
        mazeData.InitializeGrid();

        // 12. Generar NavMeshSurface
        GameObject navMeshObj = new GameObject("NavMeshSurface");
        navMeshObj.transform.parent = environmentRoot.transform;
        NavMeshSurface navSurface = navMeshObj.AddComponent<NavMeshSurface>();
        navSurface.layerMask = ~0; // Incluir todas las capas
        navSurface.BuildNavMesh();
        Debug.Log("[DemoSceneBuilder] NavMesh construido exitosamente.");

        // 13. Instanciar Jugador en la celda S
        BuildPlayer(grid, config, mazeData);

        // 14. Instanciar Objetivo en la celda G
        GameObject goalObj = BuildGoal(grid, config, mazeData);

        // 15. Crear GameManager y MissionManager
        BuildManagers(goalObj);

        // 16. Crear Canvas de UI con HUD en las 4 esquinas y Panel de Resultados
        BuildHUDCanvas();

        // 17. Guardar la escena
        if (!AssetDatabase.IsValidFolder("Assets/MovU/Scenes"))
        {
            AssetDatabase.CreateFolder("Assets/MovU", "Scenes");
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[DemoSceneBuilder] Escena guardada con éxito en {ScenePath}");

        // 18. Añadir a Build Settings como escena 0
        AddSceneToBuildSettings(ScenePath);
    }

    private static void BuildMergedWalls(MazeGrid grid, DemoConfig config, Transform parent, Material wallMat)
    {
        int W = config.mazeWidth;
        int H = config.mazeHeight;
        float cs = config.cellSize;
        float wt = config.wallThickness;
        float wh = config.wallHeight;

        // --- Muros Horizontales (Sur a Norte) ---
        for (int y = 0; y <= H; y++)
        {
            int xStart = -1;
            for (int x = 0; x < W; x++)
            {
                bool wallExists;
                if (y == 0)
                {
                    wallExists = true; // Muro perimetral Sur
                }
                else if (y == H)
                {
                    wallExists = true; // Muro perimetral Norte
                }
                else
                {
                    // Muro Norte de la celda (x, y-1) o Muro Sur de la celda (x, y)
                    wallExists = grid.HasWall(x, y - 1, WallFlags.North);
                }

                if (wallExists)
                {
                    if (xStart == -1) xStart = x;
                }
                else
                {
                    if (xStart != -1)
                    {
                        InstantiateHorizontalWallSegment(xStart, x - 1, y, config, parent, wallMat);
                        xStart = -1;
                    }
                }
            }
            if (xStart != -1)
            {
                InstantiateHorizontalWallSegment(xStart, W - 1, y, config, parent, wallMat);
            }
        }

        // --- Muros Verticales (Oeste a Este) ---
        for (int x = 0; x <= W; x++)
        {
            int yStart = -1;
            for (int y = 0; y < H; y++)
            {
                bool wallExists;
                if (x == 0)
                {
                    wallExists = true; // Muro perimetral Oeste
                }
                else if (x == W)
                {
                    wallExists = true; // Muro perimetral Este
                }
                else
                {
                    // Muro Este de la celda (x-1, y) o Muro Oeste de la celda (x, y)
                    wallExists = grid.HasWall(x - 1, y, WallFlags.East);
                }

                if (wallExists)
                {
                    if (yStart == -1) yStart = y;
                }
                else
                {
                    if (yStart != -1)
                    {
                        InstantiateVerticalWallSegment(x, yStart, y - 1, config, parent, wallMat);
                        yStart = -1;
                    }
                }
            }
            if (yStart != -1)
            {
                InstantiateVerticalWallSegment(x, yStart, H - 1, config, parent, wallMat);
            }
        }
    }

    private static void InstantiateHorizontalWallSegment(int xFrom, int xTo, int yLevel, DemoConfig config, Transform parent, Material wallMat)
    {
        float cs = config.cellSize;
        float wt = config.wallThickness;
        float wh = config.wallHeight;

        float length = (xTo - xFrom + 1) * cs + wt;
        float posX = (xFrom + xTo + 1) * 0.5f * cs;
        float posZ = yLevel * cs;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"Wall_H_y{yLevel}_x{xFrom}-{xTo}";
        wall.transform.parent = parent;
        wall.transform.position = new Vector3(posX, wh * 0.5f, posZ);
        wall.transform.localScale = new Vector3(length, wh, wt);
        wall.GetComponent<Renderer>().sharedMaterial = wallMat;
        GameObjectUtility.SetStaticEditorFlags(wall, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
    }

    private static void InstantiateVerticalWallSegment(int xLevel, int yFrom, int yTo, DemoConfig config, Transform parent, Material wallMat)
    {
        float cs = config.cellSize;
        float wt = config.wallThickness;
        float wh = config.wallHeight;

        float length = (yTo - yFrom + 1) * cs + wt;
        float posX = xLevel * cs;
        float posZ = (yFrom + yTo + 1) * 0.5f * cs;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"Wall_V_x{xLevel}_y{yFrom}-{yTo}";
        wall.transform.parent = parent;
        wall.transform.position = new Vector3(posX, wh * 0.5f, posZ);
        wall.transform.localScale = new Vector3(wt, wh, length);
        wall.GetComponent<Renderer>().sharedMaterial = wallMat;
        GameObjectUtility.SetStaticEditorFlags(wall, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
    }

    private static void BuildLandmarks(MazeGrid grid, DemoConfig config, Transform parent, Shader shader)
    {
        // Paleta determinista de 6 colores distintos para landmarks
        Color[] palette = new Color[]
        {
            new Color(0.85f, 0.25f, 0.25f), // Rojo suave
            new Color(0.25f, 0.65f, 0.85f), // Azul claro
            new Color(0.35f, 0.75f, 0.35f), // Verde cálido
            new Color(0.90f, 0.60f, 0.20f), // Naranja
            new Color(0.65f, 0.35f, 0.75f), // Púrpura
            new Color(0.85f, 0.80f, 0.25f)  // Amarillo
        };

        int landmarkCount = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                if (grid.CountExits(x, y) >= 3)
                {
                    Color color = palette[(x * 7 + y * 13) % palette.Length];
                    Material mat = CreateMaterial(shader, $"Mat_Landmark_{x}_{y}", color);

                    GameObject column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    column.name = $"Landmark_Intersection_{x}_{y}";
                    column.transform.parent = parent;

                    float posX = (x + 0.5f) * config.cellSize;
                    float posZ = (y + 0.5f) * config.cellSize;

                    column.transform.position = new Vector3(posX, config.wallHeight * 0.5f, posZ);
                    column.transform.localScale = new Vector3(0.4f, config.wallHeight * 0.5f, 0.4f);
                    column.GetComponent<Renderer>().sharedMaterial = mat;

                    GameObjectUtility.SetStaticEditorFlags(column, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    landmarkCount++;
                }
            }
        }

        Debug.Log($"[DemoSceneBuilder] Creados {landmarkCount} landmarks en intersecciones.");
    }

    private static void BuildLighting()
    {
        GameObject lightObj = new GameObject("Directional Light");
        Light lightComp = lightObj.AddComponent<Light>();
        lightComp.type = LightType.Directional;
        lightComp.color = new Color(1.0f, 0.95f, 0.88f); // Luz cálida
        lightComp.intensity = 1.1f;
        lightObj.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.52f);
    }

    private static Material CreateMaterial(Shader shader, string name, Color color)
    {
        Material mat = new Material(shader);
        mat.name = name;
        mat.color = color;
        return mat;
    }

    private static void BuildPlayer(MazeGrid grid, DemoConfig config, MazeData mazeData)
    {
        GameObject playerObj = new GameObject("Player");
        playerObj.tag = "Player";

        // Posición en mundo del centro de la celda S
        Vector3 startPos = mazeData.CellToWorldCenter(grid.StartX, grid.StartY, 0f);
        playerObj.transform.position = startPos;

        // Determinar orientación inicial hacia el primer pasillo abierto
        Vector3 lookDir = Vector3.forward;
        if (!grid.HasWall(grid.StartX, grid.StartY, WallFlags.North)) lookDir = Vector3.forward;
        else if (!grid.HasWall(grid.StartX, grid.StartY, WallFlags.East)) lookDir = Vector3.right;
        else if (!grid.HasWall(grid.StartX, grid.StartY, WallFlags.South)) lookDir = Vector3.back;
        else if (!grid.HasWall(grid.StartX, grid.StartY, WallFlags.West)) lookDir = Vector3.left;

        playerObj.transform.rotation = Quaternion.LookRotation(lookDir);

        // Componente CharacterController
        CharacterController cc = playerObj.AddComponent<CharacterController>();
        cc.height = config.capsuleHeight;
        cc.radius = config.capsuleRadius;
        cc.center = new Vector3(0f, config.capsuleHeight * 0.5f, 0f);

        // Script PlayerController
        PlayerController pc = playerObj.AddComponent<PlayerController>();

        // Cámara hija (ojos)
        GameObject camObj = new GameObject("MainCamera");
        camObj.transform.parent = playerObj.transform;
        camObj.transform.localPosition = new Vector3(0f, config.cameraHeight, 0f);
        camObj.transform.localRotation = Quaternion.identity;
        camObj.tag = "MainCamera";

        Camera cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = config.fieldOfView;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        camObj.AddComponent<AudioListener>();

        // Asegurar componente URP adicional
        camObj.AddComponent<UniversalAdditionalCameraData>();

        // Script MouseLook
        MouseLook ml = playerObj.AddComponent<MouseLook>();
        ml.SetSensitivity(config.mouseSensitivity);

        // Script GuidanceArrow
        GameObject arrowObj = new GameObject("GuidanceArrow");
        arrowObj.transform.parent = playerObj.transform;
        arrowObj.AddComponent<GuidanceArrow>();

        Debug.Log($"[DemoSceneBuilder] Jugador creado en celda S ({grid.StartX}, {grid.StartY}), mirando hacia {lookDir}.");
    }

    private static GameObject BuildGoal(MazeGrid grid, DemoConfig config, MazeData mazeData)
    {
        GameObject goalObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        goalObj.name = "Goal";
        goalObj.tag = "Goal";

        Vector3 goalPos = mazeData.CellToWorldCenter(grid.GoalX, grid.GoalY, 1.0f);
        goalObj.transform.position = goalPos;
        goalObj.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);

        // Material emisivo dorado/amarillo URP
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null) urpShader = Shader.Find("Standard");

        Material goalMat = new Material(urpShader);
        goalMat.name = "Mat_Goal";
        Color goldColor = new Color(1.0f, 0.8f, 0.1f);
        goalMat.color = goldColor;
        goalMat.EnableKeyword("_EMISSION");
        goalMat.SetColor("_EmissionColor", goldColor * 1.5f);
        goalObj.GetComponent<Renderer>().sharedMaterial = goalMat;

        // Reemplazar collider por un SphereCollider trigger más amplio
        Object.DestroyImmediate(goalObj.GetComponent<Collider>());
        SphereCollider trigger = goalObj.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.5f;

        // Componente ObjectiveTrigger
        goalObj.AddComponent<ObjectiveTrigger>();

        Debug.Log($"[DemoSceneBuilder] Objetivo creado en celda G ({grid.GoalX}, {grid.GoalY}).");
        return goalObj;
    }

    private static void BuildManagers(GameObject goalObj)
    {
        GameObject gmObj = new GameObject("GameManager");
        gmObj.AddComponent<GameManager>();

        GameObject mmObj = new GameObject("MissionManager");
        MissionManager mm = mmObj.AddComponent<MissionManager>();

        SerializedObject soMM = new SerializedObject(mm);
        SerializedProperty activeObjProp = soMM.FindProperty("activeObjective");
        if (activeObjProp != null)
        {
            activeObjProp.FindPropertyRelative("id").stringValue = "server_room";
            activeObjProp.FindPropertyRelative("displayName").stringValue = "Encuentra la sala de servidores";
            activeObjProp.FindPropertyRelative("target").objectReferenceValue = goalObj.transform;
            soMM.ApplyModifiedProperties();
        }
    }

    private static void BuildHUDCanvas()
    {
        // Canvas principal
        GameObject canvasObj = new GameObject("HUDCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasObj.AddComponent<GraphicRaycaster>();

        HUDController hud = canvasObj.AddComponent<HUDController>();

        // EventSystem si no existe
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Esquina Superior Izquierda — Nombre del Objetivo
        TextMeshProUGUI txtObj = CreateTMPText("Txt_Objective", canvasObj.transform, 28, Color.white, TextAlignmentOptions.TopLeft);
        txtObj.text = "Objetivo: Encuentra la sala de servidores";
        SetRectTransform(txtObj.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(600f, 60f));

        // Esquina Superior Derecha — Cronómetro
        TextMeshProUGUI txtTimer = CreateTMPText("Txt_Timer", canvasObj.transform, 36, new Color(1f, 0.9f, 0.3f), TextAlignmentOptions.TopRight);
        txtTimer.text = "00:00";
        SetRectTransform(txtTimer.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(300f, 60f));

        // Esquina Inferior Izquierda — Distancia
        TextMeshProUGUI txtDist = CreateTMPText("Txt_Distance", canvasObj.transform, 26, Color.white, TextAlignmentOptions.BottomLeft);
        txtDist.text = "Distancia: -- m";
        SetRectTransform(txtDist.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 30f), new Vector2(400f, 50f));

        // Esquina Inferior Derecha — Modo de Guía
        TextMeshProUGUI txtMode = CreateTMPText("Txt_GuidanceMode", canvasObj.transform, 26, new Color(0.4f, 0.8f, 1f), TextAlignmentOptions.BottomRight);
        txtMode.text = "Guía: Sin guía";
        SetRectTransform(txtMode.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(400f, 50f));

        // Panel de Resultados (Centro)
        GameObject panelObj = new GameObject("ResultsPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        panelObj.transform.SetParent(canvasObj.transform, false);

        UnityEngine.UI.Image panelImg = panelObj.GetComponent<UnityEngine.UI.Image>();
        panelImg.color = new Color(0.08f, 0.1f, 0.14f, 0.92f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        SetRectTransform(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(650f, 400f));

        TextMeshProUGUI txtTitle = CreateTMPText("Txt_Title", panelObj.transform, 38, new Color(0.3f, 1f, 0.4f), TextAlignmentOptions.Top);
        SetRectTransform(txtTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(600f, 60f));
        txtTitle.text = "¡OBJETIVO ALCANZADO!";

        TextMeshProUGUI txtResTime = CreateTMPText("Txt_ResTime", panelObj.transform, 28, Color.white, TextAlignmentOptions.Center);
        SetRectTransform(txtResTime.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(600f, 40f));

        TextMeshProUGUI txtResDist = CreateTMPText("Txt_ResDist", panelObj.transform, 28, Color.white, TextAlignmentOptions.Center);
        SetRectTransform(txtResDist.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(600f, 40f));

        TextMeshProUGUI txtResBest = CreateTMPText("Txt_ResBest", panelObj.transform, 28, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.Center);
        SetRectTransform(txtResBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(600f, 40f));

        TextMeshProUGUI txtRestart = CreateTMPText("Txt_Restart", panelObj.transform, 24, new Color(0.7f, 0.7f, 0.7f), TextAlignmentOptions.Bottom);
        SetRectTransform(txtRestart.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 25f), new Vector2(600f, 40f));
        txtRestart.text = "Presiona [R] para reiniciar el laberinto";

        // Asignar referencias en el HUDController usando SerializedObject
        SerializedObject soHud = new SerializedObject(hud);
        soHud.FindProperty("txtObjective").objectReferenceValue = txtObj;
        soHud.FindProperty("txtTimer").objectReferenceValue = txtTimer;
        soHud.FindProperty("txtDistance").objectReferenceValue = txtDist;
        soHud.FindProperty("txtGuidanceMode").objectReferenceValue = txtMode;
        soHud.FindProperty("resultsPanel").objectReferenceValue = panelObj;
        soHud.FindProperty("txtResultsTime").objectReferenceValue = txtResTime;
        soHud.FindProperty("txtResultsDistance").objectReferenceValue = txtResDist;
        soHud.FindProperty("txtResultsBestTime").objectReferenceValue = txtResBest;
        soHud.ApplyModifiedProperties();

        panelObj.SetActive(false);
    }

    private static TextMeshProUGUI CreateTMPText(string name, Transform parent, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;

        TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
        if (defaultFont == null)
        {
            defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
        if (defaultFont != null)
        {
            tmp.font = defaultFont;
        }

        return tmp;
    }

    private static void SetRectTransform(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;
    }

    // Tags que Unity ya trae de fábrica: volver a registrarlos produce la
    // advertencia "Default GameObject Tag: X already registered".
    private static readonly string[] BuiltInTags =
    {
        "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController"
    };

    private static void EnsureTagExists(string tagName)
    {
        foreach (string builtIn in BuiltInTags)
        {
            if (builtIn == tagName) return;
        }

        var tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAsset != null && tagManagerAsset.Length > 0)
        {
            SerializedObject serializedObject = new SerializedObject(tagManagerAsset[0]);
            SerializedProperty tagsProperty = serializedObject.FindProperty("tags");
            bool exists = false;
            for (int i = 0; i < tagsProperty.arraySize; i++)
            {
                if (tagsProperty.GetArrayElementAtIndex(i).stringValue == tagName)
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                tagsProperty.InsertArrayElementAtIndex(tagsProperty.arraySize);
                tagsProperty.GetArrayElementAtIndex(tagsProperty.arraySize - 1).stringValue = tagName;
                serializedObject.ApplyModifiedProperties();
                Debug.Log($"[DemoSceneBuilder] Tag '{tagName}' creado en TagManager.");
            }
        }
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes;
        bool alreadyAdded = false;
        foreach (var s in scenes)
        {
            if (s.path == scenePath)
            {
                alreadyAdded = true;
                break;
            }
        }

        if (!alreadyAdded)
        {
            var newScenes = new EditorBuildSettingsScene[scenes.Length + 1];
            newScenes[0] = new EditorBuildSettingsScene(scenePath, true);
            for (int i = 0; i < scenes.Length; i++)
            {
                newScenes[i + 1] = scenes[i];
            }
            EditorBuildSettings.scenes = newScenes;
            Debug.Log($"[DemoSceneBuilder] Escena '{scenePath}' añadida como escena 0 a Build Settings.");
        }
    }
}
