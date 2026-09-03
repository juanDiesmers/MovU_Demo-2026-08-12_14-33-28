using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// ============================================================================
// MeshiWalkableSetup.cs — Convierte el plano de Meshy en un entorno caminable
// ============================================================================
// Menú:  MovU > Preparar plano Meshy jugable
//
// Qué hace, en orden:
//   1. Normaliza el transform del modelo (rotación exacta de -90° en X para que
//      el relieve apunte hacia arriba, escala uniforme, apoyado en y = 0).
//   2. Le pone un MeshCollider para que el personaje no lo atraviese.
//   3. Localiza un punto de aparición VÁLIDO por trazado de rayos: detecta la
//      altura del piso, marca las celdas libres y elige la más despejada.
//   4. Instancia el personaje en primera persona (CharacterController +
//      PlayerController + MouseLook + CameraRig), con la jerarquía de cámara
//      ya lista para pasar a tercera persona con la tecla V.
//   5. Apaga cámaras sueltas de la escena y deja una sola iluminación.
//
// Es idempotente: se puede volver a ejecutar y reemplaza al personaje anterior.
// ============================================================================

public static class MeshiWalkableSetup
{
    // Los muros del plano miden 0.107 unidades. A 28x quedan en 3.0 m, la misma
    // altura que los muros del laberinto del demo, y la planta mide 19.7 x 53.3 m.
    private const float ScaleFactor = 28f;

    private const float PlayerHeightFallback = 1.8f;
    private const float PlayerRadiusFallback = 0.3f;
    private const float CameraHeightFallback = 1.65f;

    private const string MaterialFolder = "Assets/MovU/Materials";
    private const string MaterialPath = MaterialFolder + "/Mat_PlanoMeshy.mat";

    [MenuItem("MovU/Preparar plano Meshy jugable")]
    public static void Setup()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        Transform modelRoot = FindModelRoot(scene);
        if (modelRoot == null)
        {
            EditorUtility.DisplayDialog(
                "MovU",
                "No encontré el modelo de Meshy en la escena abierta.\n\n" +
                "Abre la escena que contiene el plano y vuelve a ejecutar este menú.",
                "Entendido");
            return;
        }

        Debug.Log($"[MeshiSetup] Modelo encontrado: '{modelRoot.name}'.");

        NormalizeTransform(modelRoot);
        EnsureCollider(modelRoot);
        AssignMaterial(modelRoot);

        Physics.SyncTransforms();

        Bounds bounds = ComputeBounds(modelRoot);
        Debug.Log($"[MeshiSetup] Planta escalada a {ScaleFactor}x → {bounds.size.x:F1} m x {bounds.size.z:F1} m, alto {bounds.size.y:F1} m.");

        float playerHeight = PlayerHeightFallback;
        float playerRadius = PlayerRadiusFallback;
        DemoConfig config = AssetDatabase.LoadAssetAtPath<DemoConfig>("Assets/MovU/DemoConfig.asset");
        if (config != null)
        {
            playerHeight = config.capsuleHeight;
            playerRadius = config.capsuleRadius;
        }

        if (!FindSpawnPoint(bounds, playerHeight, playerRadius, out Vector3 spawn, out float floorY))
        {
            EditorUtility.DisplayDialog(
                "MovU",
                "El modelo no tiene ninguna zona abierta suficientemente amplia para que " +
                "quepa el personaje.\n\nProbablemente haga falta subir la escala " +
                $"(ahora está en {ScaleFactor}x).",
                "Entendido");
            return;
        }

        Debug.Log($"[MeshiSetup] Piso detectado en y = {floorY:F2} m. Aparición en {spawn}.");

        BuildPlayer(spawn, config);
        PlaceReferenceCube(spawn, floorY);
        TidyCamerasAndLighting();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[MeshiSetup] Listo. Dale Play: WASD para moverte, mouse para mirar, " +
                  "V alterna primera/tercera persona, Esc libera el cursor.");
    }

    // -----------------------------------------------------------------------
    // Modelo
    // -----------------------------------------------------------------------

    private static Transform FindModelRoot(UnityEngine.SceneManagement.Scene scene)
    {
        Transform namedButEmpty = null;
        Transform biggest = null;
        int biggestVertexCount = 0;

        foreach (GameObject go in scene.GetRootGameObjects())
        {
            bool nameMatches = go.name.IndexOf("Meshy", System.StringComparison.OrdinalIgnoreCase) >= 0;

            int vertexCount = 0;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null) vertexCount += mf.sharedMesh.vertexCount;
            }

            if (vertexCount > 1000)
            {
                // El candidato ideal: se llama como el modelo Y tiene malla válida.
                if (nameMatches) return go.transform;

                if (vertexCount > biggestVertexCount)
                {
                    biggestVertexCount = vertexCount;
                    biggest = go.transform;
                }
            }
            else if (nameMatches && namedButEmpty == null)
            {
                namedButEmpty = go.transform;
            }
        }

        if (biggest != null) return biggest;

        if (namedButEmpty != null)
        {
            Debug.LogError(
                $"[MeshiSetup] '{namedButEmpty.name}' está en la escena pero no tiene ninguna malla. " +
                "El vínculo con el asset del modelo está roto (aparece en rojo en la Hierarchy). " +
                "Bórralo de la escena y vuelve a arrastrar el modelo desde Assets/MovU/Models.");
        }

        return null;
    }

    /// <summary>
    /// Deja el modelo plano, a escala y apoyado en y = 0. Se resetean los hijos
    /// para que la rotación manual previa no se acumule con la nueva.
    /// </summary>
    private static void NormalizeTransform(Transform modelRoot)
    {
        foreach (Transform child in modelRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child == modelRoot) continue;
            Undo.RecordObject(child, "Normalizar modelo");
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
        }

        Undo.RecordObject(modelRoot, "Normalizar modelo");
        modelRoot.position = Vector3.zero;

        // El OBJ viene de pie (el relieve sale en +Z). -90° en X lo acuesta con
        // los muros apuntando hacia arriba, sin la inclinación de 11° que tenía.
        modelRoot.rotation = Quaternion.Euler(-90f, 0f, 0f);
        modelRoot.localScale = Vector3.one * ScaleFactor;

        Bounds b = ComputeBounds(modelRoot);
        modelRoot.position -= new Vector3(b.center.x, b.min.y, b.center.z);
    }

    private static void EnsureCollider(Transform modelRoot)
    {
        foreach (MeshFilter mf in modelRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;

            MeshCollider mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = Undo.AddComponent<MeshCollider>(mf.gameObject);

            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }
    }

    private static void AssignMaterial(Transform modelRoot)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            mat = new Material(shader) { name = "Mat_PlanoMeshy" };
            mat.color = new Color(0.82f, 0.81f, 0.78f);

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/MovU", "Materials");
            }

            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MeshiSetup] Material creado en {MaterialPath}. El OBJ no trae UVs ni .mtl, así que va en color plano.");
        }

        foreach (Renderer r in modelRoot.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterial = mat;
        }
    }

    private static Bounds ComputeBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // -----------------------------------------------------------------------
    // Punto de aparición
    // -----------------------------------------------------------------------

    /// <summary>
    /// Traza rayos verticales sobre una rejilla, deduce la altura del piso por la
    /// mediana de los impactos, marca como libres las celdas donde el rayo llega
    /// al piso y elige la celda más alejada de cualquier muro.
    /// </summary>
    private static bool FindSpawnPoint(Bounds bounds, float playerHeight, float playerRadius,
                                       out Vector3 spawn, out float floorY)
    {
        spawn = Vector3.zero;
        floorY = 0f;

        const float CellSize = 0.25f;
        int nx = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x / CellSize), 8, 400);
        int nz = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z / CellSize), 8, 400);

        float rayStartY = bounds.max.y + 25f;
        float rayLength = bounds.size.y + 60f;

        float[,] hitHeight = new float[nx, nz];
        bool[,] didHit = new bool[nx, nz];
        List<float> heights = new List<float>(nx * nz);

        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                Vector3 origin = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, (i + 0.5f) / nx),
                    rayStartY,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, (j + 0.5f) / nz));

                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLength, ~0, QueryTriggerInteraction.Ignore))
                {
                    hitHeight[i, j] = hit.point.y;
                    didHit[i, j] = true;
                    heights.Add(hit.point.y);
                }
            }
        }

        if (heights.Count == 0)
        {
            Debug.LogError("[MeshiSetup] Ningún rayo impactó el modelo. ¿El MeshCollider quedó bien puesto?");
            return false;
        }

        // El piso es la superficie mayoritaria: la mediana de los impactos.
        heights.Sort();
        floorY = heights[heights.Count / 2];

        bool[,] open = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                open[i, j] = didHit[i, j] && Mathf.Abs(hitHeight[i, j] - floorY) < 0.5f;

        // Transformada de distancia: cuántas celdas hay hasta el muro más cercano.
        int[,] dist = new int[nx, nz];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                if (open[i, j])
                {
                    dist[i, j] = -1;
                }
                else
                {
                    dist[i, j] = 0;
                    queue.Enqueue(new Vector2Int(i, j));
                }
            }
        }

        // Los bordes de la rejilla cuentan como muro para no aparecer al filo.
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int ni = c.x + dx[d];
                int nj = c.y + dz[d];
                if (ni < 0 || ni >= nx || nj < 0 || nj >= nz) continue;
                if (dist[ni, nj] != -1) continue;

                dist[ni, nj] = dist[c.x, c.y] + 1;
                queue.Enqueue(new Vector2Int(ni, nj));
            }
        }

        // Candidatas ordenadas de más despejada a menos.
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                if (dist[i, j] > 0) candidates.Add(new Vector2Int(i, j));

        if (candidates.Count == 0)
        {
            Debug.LogError("[MeshiSetup] No quedó ninguna celda abierta en la planta.");
            return false;
        }

        candidates.Sort((a, b) => dist[b.x, b.y].CompareTo(dist[a.x, a.y]));

        int openCells = candidates.Count;
        int totalCells = nx * nz;
        Debug.Log($"[MeshiSetup] Rejilla {nx}x{nz}: {openCells} celdas transitables de {totalCells} ({openCells * 100f / totalCells:F0}% de superficie libre).");

        // Se valida con una cápsula real: la primera candidata donde el personaje quepa.
        foreach (Vector2Int c in candidates)
        {
            Vector3 point = new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, (c.x + 0.5f) / nx),
                floorY + 0.05f,
                Mathf.Lerp(bounds.min.z, bounds.max.z, (c.y + 0.5f) / nz));

            Vector3 bottom = point + Vector3.up * playerRadius;
            Vector3 top = point + Vector3.up * (playerHeight - playerRadius);

            if (!Physics.CheckCapsule(bottom, top, playerRadius * 1.1f, ~0, QueryTriggerInteraction.Ignore))
            {
                spawn = point;
                return true;
            }
        }

        Debug.LogError($"[MeshiSetup] Hay {openCells} celdas abiertas, pero en ninguna cabe una cápsula de {playerRadius * 2f:F2} m de diámetro. Sube la escala.");
        return false;
    }

    // -----------------------------------------------------------------------
    // Personaje
    // -----------------------------------------------------------------------

    private static void BuildPlayer(Vector3 spawn, DemoConfig config)
    {
        GameObject previous = GameObject.Find("Player");
        if (previous != null)
        {
            Object.DestroyImmediate(previous);
            Debug.Log("[MeshiSetup] Se reemplazó el personaje anterior.");
        }

        float height = config != null ? config.capsuleHeight : PlayerHeightFallback;
        float radius = config != null ? config.capsuleRadius : PlayerRadiusFallback;
        float eyeHeight = config != null ? config.cameraHeight : CameraHeightFallback;

        GameObject player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = spawn;

        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = height;
        cc.radius = radius;
        cc.center = new Vector3(0f, height * 0.5f, 0f);
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.35f;
        cc.skinWidth = 0.02f;

        PlayerController pc = player.AddComponent<PlayerController>();
        SerializedObject soPc = new SerializedObject(pc);
        SetFloat(soPc, "walkSpeed", config != null ? config.walkSpeed : 3.0f);
        SetFloat(soPc, "gravity", config != null ? config.gravity : -9.81f);
        SetFloat(soPc, "capsuleHeight", height);
        SetFloat(soPc, "capsuleRadius", radius);
        soPc.ApplyModifiedProperties();

        // Cuerpo visible, apagado en primera persona y listo para la tercera.
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        body.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);

        Renderer bodyRenderer = body.GetComponent<Renderer>();
        bodyRenderer.enabled = false;

        // Pivote de cámara: MouseLook aplica aquí el pitch, y CameraRig aleja la
        // cámara sobre este mismo pivote para la vista en tercera persona.
        GameObject pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(player.transform, false);
        pivot.transform.localPosition = new Vector3(0f, eyeHeight, 0f);

        GameObject camObj = new GameObject("MainCamera");
        camObj.tag = "MainCamera";
        camObj.transform.SetParent(pivot.transform, false);

        Camera cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = config != null ? config.fieldOfView : 70f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        camObj.AddComponent<AudioListener>();
        camObj.AddComponent<UniversalAdditionalCameraData>();

        MouseLook look = player.AddComponent<MouseLook>();
        SerializedObject soLook = new SerializedObject(look);
        SetFloat(soLook, "mouseSensitivity", config != null ? config.mouseSensitivity : 0.15f);
        SetFloat(soLook, "pitchLimit", config != null ? config.pitchLimit : 85f);
        SetRef(soLook, "playerCamera", pivot.transform);
        soLook.ApplyModifiedProperties();

        CameraRig rig = player.AddComponent<CameraRig>();
        SerializedObject soRig = new SerializedObject(rig);
        SetRef(soRig, "cameraPivot", pivot.transform);
        SetRef(soRig, "cameraTransform", camObj.transform);
        SetRef(soRig, "bodyRenderer", bodyRenderer);
        soRig.ApplyModifiedProperties();

        Selection.activeGameObject = player;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    private static void SetFloat(SerializedObject so, string property, float value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null) p.floatValue = value;
    }

    private static void SetRef(SerializedObject so, string property, Object value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null) p.objectReferenceValue = value;
    }

    // -----------------------------------------------------------------------
    // Escena
    // -----------------------------------------------------------------------

    private static void PlaceReferenceCube(Vector3 spawn, float floorY)
    {
        GameObject cube = GameObject.Find("Meter_Cube");
        if (cube == null) return;

        // Se deja a 3 m del personaje para poder comparar la escala de un vistazo.
        cube.transform.position = new Vector3(spawn.x + 3f, floorY + 0.5f, spawn.z);
        cube.transform.localScale = Vector3.one;
        Debug.Log("[MeshiSetup] Meter_Cube reubicado junto al personaje como referencia de escala (1 m).");
    }

    private static void TidyCamerasAndLighting()
    {
        GameObject player = GameObject.Find("Player");

        foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (player != null && cam.transform.IsChildOf(player.transform)) continue;

            cam.gameObject.SetActive(false);
            Debug.Log($"[MeshiSetup] Cámara suelta '{cam.name}' desactivada (dos AudioListener activos generan advertencias).");
        }

        if (Object.FindFirstObjectByType<Light>() == null)
        {
            GameObject lightObj = new GameObject("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
    }
}
