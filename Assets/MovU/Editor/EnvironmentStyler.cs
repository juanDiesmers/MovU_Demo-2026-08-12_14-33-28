using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// ============================================================================
// EnvironmentStyler.cs — Le da acabado visual al plano de Meshy
// ============================================================================
// Menú:  MovU > Estilizar entorno > (Limpio | Institucional | Maqueta)
//        MovU > Estilizar entorno > Añadir suelo exterior
//
// El OBJ no trae UVs ni material, así que no sirve ninguna textura clásica.
// Este script monta el material con el shader MovU/StylizedEnvironment, que
// proyecta baldosas, zócalo y sombreado desde el espacio de mundo, y de paso
// deja la iluminación de la escena en un estado presentable.
//
// Qué hace, en orden:
//   1. Encuentra el modelo en la escena abierta.
//   2. Detecta a qué altura está el piso por trazado de rayos (mediana de los
//      impactos), porque el zócalo y el AO de contacto se miden desde ahí.
//   3. Crea o actualiza Assets/MovU/Materials/Mat_EntornoEstilizado.mat con el
//      preset elegido y se lo asigna a todos los renderers del modelo.
//   4. Endurece las normales del importador (ángulo de suavizado bajo) para que
//      las esquinas se lean nítidas en vez de redondeadas.
//   5. Ajusta luz direccional, luz ambiental y niebla acordes al preset.
//
// Es idempotente: se puede correr las veces que haga falta.
// ============================================================================

public static class EnvironmentStyler
{
    private const string ShaderName    = "MovU/StylizedEnvironment";
    private const string MaterialFolder = "Assets/MovU/Materials";
    private const string MaterialPath   = MaterialFolder + "/Mat_EntornoEstilizado.mat";
    private const string GroundName     = "SueloExterior";

    // Un modelo "grande" para distinguirlo del cubo de referencia o del jugador.
    private const int MinVertexCount = 5000;

    // ------------------------------------------------------------------
    // Presets
    // ------------------------------------------------------------------
    private struct Preset
    {
        public string name;

        public Color floorA, floorB, grout;
        public float tileSize, groutWidth;

        public Color wallLow, wallHigh, baseboard;
        public float wallGradient, baseboardHeight, panelWidth, panelStrength;

        public Color ceiling;

        public float sharpness, aoStrength, aoHeight, smoothness;

        public Color sunColor;      public float sunIntensity;
        public Vector3 sunAngles;   public float shadowStrength;
        public Color ambSky, ambEquator, ambGround;
        public Color fogColor;      public float fogStart, fogEnd;
        public bool  fogEnabled;
    }

    // Estilizado / low-poly limpio: colores planos, contraste suave, lectura clara.
    private static Preset Limpio() => new Preset
    {
        name = "Limpio",
        floorA = C(0.66f, 0.68f, 0.71f), floorB = C(0.60f, 0.62f, 0.66f), grout = C(0.44f, 0.46f, 0.50f),
        tileSize = 1.2f, groutWidth = 0.035f,
        wallLow = C(0.87f, 0.87f, 0.84f), wallHigh = C(0.96f, 0.96f, 0.94f), baseboard = C(0.28f, 0.31f, 0.36f),
        wallGradient = 3.0f, baseboardHeight = 0.16f, panelWidth = 2.4f, panelStrength = 0.5f,
        ceiling = C(0.93f, 0.93f, 0.95f),
        sharpness = 8f, aoStrength = 0.45f, aoHeight = 0.55f, smoothness = 0.15f,
        sunColor = C(1.00f, 0.97f, 0.91f), sunIntensity = 1.15f,
        sunAngles = new Vector3(48f, 145f, 0f), shadowStrength = 0.72f,
        ambSky = C(0.55f, 0.60f, 0.68f), ambEquator = C(0.44f, 0.45f, 0.47f), ambGround = C(0.24f, 0.24f, 0.26f),
        fogColor = C(0.72f, 0.76f, 0.81f), fogStart = 25f, fogEnd = 95f, fogEnabled = true,
    };

    // Institucional: concreto y baldosa de pasillo universitario, tonos cálidos.
    private static Preset Institucional() => new Preset
    {
        name = "Institucional",
        floorA = C(0.72f, 0.69f, 0.63f), floorB = C(0.67f, 0.64f, 0.58f), grout = C(0.50f, 0.47f, 0.43f),
        tileSize = 0.9f, groutWidth = 0.028f,
        wallLow = C(0.90f, 0.87f, 0.80f), wallHigh = C(0.97f, 0.95f, 0.90f), baseboard = C(0.38f, 0.30f, 0.24f),
        wallGradient = 3.0f, baseboardHeight = 0.18f, panelWidth = 3.0f, panelStrength = 0.35f,
        ceiling = C(0.95f, 0.94f, 0.92f),
        sharpness = 8f, aoStrength = 0.50f, aoHeight = 0.60f, smoothness = 0.22f,
        sunColor = C(1.00f, 0.95f, 0.85f), sunIntensity = 1.25f,
        sunAngles = new Vector3(42f, 160f, 0f), shadowStrength = 0.78f,
        ambSky = C(0.58f, 0.58f, 0.60f), ambEquator = C(0.46f, 0.44f, 0.41f), ambGround = C(0.26f, 0.24f, 0.22f),
        fogColor = C(0.80f, 0.78f, 0.74f), fogStart = 30f, fogEnd = 110f, fogEnabled = true,
    };

    // Maqueta arquitectónica: blanco y gris, líneas marcadas, sin niebla.
    private static Preset Maqueta() => new Preset
    {
        name = "Maqueta",
        floorA = C(0.92f, 0.92f, 0.93f), floorB = C(0.88f, 0.88f, 0.90f), grout = C(0.62f, 0.64f, 0.68f),
        tileSize = 2.0f, groutWidth = 0.05f,
        wallLow = C(0.97f, 0.97f, 0.97f), wallHigh = C(1.00f, 1.00f, 1.00f), baseboard = C(0.20f, 0.42f, 0.62f),
        wallGradient = 3.0f, baseboardHeight = 0.10f, panelWidth = 4.0f, panelStrength = 0.25f,
        ceiling = C(0.98f, 0.98f, 1.00f),
        sharpness = 12f, aoStrength = 0.35f, aoHeight = 0.45f, smoothness = 0.05f,
        sunColor = C(1.00f, 1.00f, 1.00f), sunIntensity = 1.05f,
        sunAngles = new Vector3(55f, 130f, 0f), shadowStrength = 0.55f,
        ambSky = C(0.72f, 0.74f, 0.78f), ambEquator = C(0.64f, 0.65f, 0.68f), ambGround = C(0.48f, 0.48f, 0.50f),
        fogColor = C(0.90f, 0.92f, 0.95f), fogStart = 60f, fogEnd = 200f, fogEnabled = false,
    };

    private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

    // ------------------------------------------------------------------
    // Menús
    // ------------------------------------------------------------------
    [MenuItem("MovU/Estilizar entorno/Limpio (low-poly)", priority = 20)]
    public static void ApplyLimpio() => Apply(Limpio());

    [MenuItem("MovU/Estilizar entorno/Institucional", priority = 21)]
    public static void ApplyInstitucional() => Apply(Institucional());

    [MenuItem("MovU/Estilizar entorno/Maqueta arquitectonica", priority = 22)]
    public static void ApplyMaqueta() => Apply(Maqueta());

    // ------------------------------------------------------------------
    // Flujo principal
    // ------------------------------------------------------------------
    private static void Apply(Preset p)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "MovU",
                "No encuentro el shader '" + ShaderName + "'.\n\n" +
                "Debería estar en Assets/MovU/Shaders/StylizedEnvironment.shader. " +
                "Si el archivo está pero Unity no lo ve, revisa la consola: probablemente " +
                "tenga un error de compilación.",
                "Entendido");
            return;
        }

        Transform modelRoot = FindModelRoot();
        if (modelRoot == null)
        {
            EditorUtility.DisplayDialog(
                "MovU",
                "No encontré el modelo del entorno en la escena abierta.\n\n" +
                "Abre la escena con el plano (o corre primero " +
                "'MovU > Preparar plano Meshy jugable') y vuelve a intentarlo.",
                "Entendido");
            return;
        }

        float floorY = DetectFloorLevel(modelRoot);

        Material mat = BuildMaterial(shader, p, floorY);
        AssignMaterial(modelRoot, mat);
        HardenNormals(modelRoot);
        SetupLighting(p);

        EditorSceneManager.MarkSceneDirty(modelRoot.gameObject.scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[EnvStyler] Preset '{p.name}' aplicado sobre '{modelRoot.name}'. " +
                  $"Nivel de piso detectado en y = {floorY:F2} m. " +
                  $"Material: {MaterialPath}");
    }

    // ------------------------------------------------------------------
    // Localizar el modelo: el renderer con más vértices de la escena.
    // ------------------------------------------------------------------
    private static Transform FindModelRoot()
    {
        Transform best = null;
        int bestCount = MinVertexCount;

        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.sharedMesh.vertexCount <= bestCount) continue;

            bestCount = mf.sharedMesh.vertexCount;
            best = mf.transform;
        }

        if (best == null) return null;

        // Subimos al ancestro más alto que siga siendo parte del modelo importado,
        // para que el material se aplique a todas las submallas si las hubiera.
        Transform root = best;
        while (root.parent != null && root.parent.GetComponentsInChildren<MeshFilter>(true).Length ==
                                       root.GetComponentsInChildren<MeshFilter>(true).Length)
        {
            root = root.parent;
        }
        return root;
    }

    // ------------------------------------------------------------------
    // Altura del piso: mediana de los impactos de una rejilla de rayos.
    // Es lo mismo que hace MeshiWalkableSetup para encontrar la aparición,
    // y garantiza que el zócalo quede pegado al suelo real y no a y = 0.
    // ------------------------------------------------------------------
    private static float DetectFloorLevel(Transform modelRoot)
    {
        Bounds b = ComputeBounds(modelRoot);
        Physics.SyncTransforms();

        const int steps = 32;
        var hits = new List<float>(steps * steps);

        float top = b.max.y + 5f;
        float rayLength = b.size.y + 10f;

        for (int ix = 0; ix < steps; ix++)
        {
            float tx = (ix + 0.5f) / steps;
            float x = Mathf.Lerp(b.min.x, b.max.x, tx);

            for (int iz = 0; iz < steps; iz++)
            {
                float tz = (iz + 0.5f) / steps;
                float z = Mathf.Lerp(b.min.z, b.max.z, tz);

                var origin = new Vector3(x, top, z);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLength))
                {
                    hits.Add(hit.point.y);
                }
            }
        }

        if (hits.Count == 0)
        {
            // Sin collider todavía: caemos a la base del bounding box.
            Debug.LogWarning("[EnvStyler] Ningún rayo tocó el modelo (¿falta el MeshCollider?). " +
                             "Uso la base del bounding box como nivel de piso.");
            return b.min.y;
        }

        hits.Sort();
        return hits[hits.Count / 2];
    }

    private static Bounds ComputeBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // ------------------------------------------------------------------
    // Material
    // ------------------------------------------------------------------
    private static Material BuildMaterial(Shader shader, Preset p, float floorY)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets/MovU", "Materials");
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "Mat_EntornoEstilizado" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }

        mat.SetColor("_FloorColor",     p.floorA);
        mat.SetColor("_FloorColorAlt",  p.floorB);
        mat.SetColor("_GroutColor",     p.grout);
        mat.SetFloat("_TileSize",       p.tileSize);
        mat.SetFloat("_GroutWidth",     p.groutWidth);

        mat.SetColor("_WallColor",      p.wallLow);
        mat.SetColor("_WallTopColor",   p.wallHigh);
        mat.SetColor("_BaseboardColor", p.baseboard);
        mat.SetFloat("_WallGradientHeight", p.wallGradient);
        mat.SetFloat("_BaseboardHeight",    p.baseboardHeight);
        mat.SetFloat("_PanelWidth",         p.panelWidth);
        mat.SetFloat("_PanelStrength",      p.panelStrength);

        mat.SetColor("_CeilingColor",   p.ceiling);

        mat.SetFloat("_FloorLevel",     floorY);
        mat.SetFloat("_Sharpness",      p.sharpness);
        mat.SetFloat("_AOStrength",     p.aoStrength);
        mat.SetFloat("_AOHeight",       p.aoHeight);
        mat.SetFloat("_Smoothness",     p.smoothness);
        mat.SetFloat("_Metallic",       0f);

        // El OBJ viene de una generación por IA: hay caras con el winding invertido.
        // Sin culling no se ven huecos negros al mirar un muro desde el lado "malo".
        mat.SetFloat("_Cull", (float)CullMode.Off);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void AssignMaterial(Transform modelRoot, Material mat)
    {
        foreach (Renderer r in modelRoot.GetComponentsInChildren<Renderer>(true))
        {
            Undo.RecordObject(r, "Estilizar entorno");

            int slots = Mathf.Max(1, r.sharedMaterials.Length);
            var mats = new Material[slots];
            for (int i = 0; i < slots; i++) mats[i] = mat;
            r.sharedMaterials = mats;

            // Sin lightmap UVs no tiene sentido marcarlo como estático de iluminación.
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }
    }

    // ------------------------------------------------------------------
    // Normales: con ángulo de suavizado alto, una malla decimada se ve
    // "derretida". Bajarlo a 35° devuelve esquinas nítidas, que es justo
    // lo que pide un acabado low-poly limpio.
    // ------------------------------------------------------------------
    private static void HardenNormals(Transform modelRoot)
    {
        var reimported = new HashSet<string>();

        foreach (MeshFilter mf in modelRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;

            string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (string.IsNullOrEmpty(path) || !reimported.Add(path)) continue;

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            bool changed = false;

            if (importer.importNormals != ModelImporterNormals.Calculate)
            {
                importer.importNormals = ModelImporterNormals.Calculate;
                changed = true;
            }
            if (!Mathf.Approximately(importer.normalSmoothingAngle, 35f))
            {
                importer.normalSmoothingAngle = 35f;
                changed = true;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                // El OBJ reconstruido trae un .mtl con colores planos. Si Unity
                // importa esos materiales, se quedan pegados a las ranuras y el
                // material estilizado no se ve: el modelo sale blanco y liso.
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }
            if (importer.addCollider)
            {
                // El MeshCollider lo pone MeshiWalkableSetup a mano; que lo genere
                // también el importador duplica dos millones de triángulos de colisión.
                importer.addCollider = false;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                Debug.Log($"[EnvStyler] Normales endurecidas (35°) en {path}.");
            }
        }
    }

    // ------------------------------------------------------------------
    // Iluminación de escena
    // ------------------------------------------------------------------
    private static void SetupLighting(Preset p)
    {
        Light sun = null;
        foreach (Light l in Object.FindObjectsByType<Light>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            if (sun == null || l.intensity > sun.intensity) sun = l;
        }

        if (sun == null)
        {
            var go = new GameObject("Sol");
            Undo.RegisterCreatedObjectUndo(go, "Estilizar entorno");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        Undo.RecordObject(sun, "Estilizar entorno");
        Undo.RecordObject(sun.transform, "Estilizar entorno");

        sun.color            = p.sunColor;
        sun.intensity        = p.sunIntensity;
        sun.shadows          = LightShadows.Soft;
        sun.shadowStrength   = p.shadowStrength;
        sun.transform.rotation = Quaternion.Euler(p.sunAngles);

        RenderSettings.ambientMode          = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor      = p.ambSky;
        RenderSettings.ambientEquatorColor  = p.ambEquator;
        RenderSettings.ambientGroundColor   = p.ambGround;
        RenderSettings.ambientIntensity     = 1f;

        RenderSettings.fog        = p.fogEnabled;
        RenderSettings.fogMode    = FogMode.Linear;
        RenderSettings.fogColor   = p.fogColor;
        RenderSettings.fogStartDistance = p.fogStart;
        RenderSettings.fogEndDistance   = p.fogEnd;
    }

    // ------------------------------------------------------------------
    // Suelo exterior: un plano grande, sin collider, para que el horizonte
    // no sea el vacío del skybox cuando se mira por encima de los muros.
    // Sin collider a propósito: así no interfiere con el trazado de rayos
    // que usa MeshiWalkableSetup para escoger el punto de aparición.
    // ------------------------------------------------------------------
    [MenuItem("MovU/Estilizar entorno/Anadir suelo exterior", priority = 40)]
    public static void AddExteriorGround()
    {
        Transform modelRoot = FindModelRoot();
        if (modelRoot == null)
        {
            EditorUtility.DisplayDialog("MovU", "No encontré el modelo en la escena abierta.", "Entendido");
            return;
        }

        Bounds b = ComputeBounds(modelRoot);
        float floorY = DetectFloorLevel(modelRoot);

        GameObject ground = GameObject.Find(GroundName);
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = GroundName;
            Undo.RegisterCreatedObjectUndo(ground, "Anadir suelo exterior");
        }

        // Sin collider a proposito (ver comentario del metodo).
        Collider groundCollider = ground.GetComponent<Collider>();
        if (groundCollider != null) Object.DestroyImmediate(groundCollider);

        // El plano primitivo mide 10x10, de ahí el /10 y el margen de 6x la huella.
        float side = Mathf.Max(b.size.x, b.size.z) * 6f;
        ground.transform.position   = new Vector3(b.center.x, floorY - 0.15f, b.center.z);
        ground.transform.localScale = new Vector3(side / 10f, 1f, side / 10f);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            name = "Mat_SueloExterior"
        };
        mat.SetColor("_BaseColor", new Color(0.34f, 0.36f, 0.33f));
        mat.SetFloat("_Smoothness", 0.05f);

        string matPath = MaterialFolder + "/Mat_SueloExterior.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mat, matPath);
            existing = mat;
        }
        else
        {
            existing.CopyPropertiesFromMaterial(mat);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mat);
        }

        ground.GetComponent<Renderer>().sharedMaterial = existing;
        ground.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        EditorSceneManager.MarkSceneDirty(ground.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[EnvStyler] Suelo exterior de {side:F0} x {side:F0} m en y = {floorY - 0.15f:F2}. Sin collider.");
    }

    // ------------------------------------------------------------------
    // Cambiar al modelo reconstruido
    // ------------------------------------------------------------------
    // La malla de Meshy tiene la planta bien pero la extrusion rota: la altura
    // de la corona va de 1,2 a 3,4 m, y esa silueta dentada es el defecto que
    // se ve en el juego. Tools/reconstruir_planta.py levanta la misma planta
    // con muros planos, a escuadra y todos a la misma altura.
    // ------------------------------------------------------------------
    private const string ModeloReconstruido = "Assets/MovU/Models/PlantaMovU_Reconstruida.obj";

    [MenuItem("MovU/Estilizar entorno/Cambiar al modelo reconstruido", priority = 60)]
    public static void SwapToRebuilt()
    {
        var nuevo = AssetDatabase.LoadAssetAtPath<GameObject>(ModeloReconstruido);
        if (nuevo == null)
        {
            EditorUtility.DisplayDialog(
                "MovU",
                "No encuentro " + ModeloReconstruido + ".\n\n" +
                "Generalo primero con:\n    python3 Tools/reconstruir_planta.py",
                "Entendido");
            return;
        }

        Transform viejo = FindModelRoot();
        string nombreViejo = viejo != null ? viejo.name : "(ninguno)";

        bool ok = EditorUtility.DisplayDialog(
            "MovU",
            "Voy a reemplazar el modelo del entorno.\n\n" +
            "Sale:  " + nombreViejo + "\n" +
            "Entra: PlantaMovU_Reconstruida\n\n" +
            "Despues hay que volver a correr 'Preparar plano Meshy jugable' y " +
            "'Estilizar entorno', porque cambia la geometria y el punto de aparicion.",
            "Cambiar", "Cancelar");
        if (!ok) return;

        var instancia = (GameObject)PrefabUtility.InstantiatePrefab(nuevo);
        Undo.RegisterCreatedObjectUndo(instancia, "Cambiar al modelo reconstruido");
        instancia.name = "PlantaMovU_Reconstruida";
        instancia.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instancia.transform.localScale = Vector3.one;

        if (viejo != null)
        {
            Undo.DestroyObjectImmediate(viejo.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(instancia.scene);

        // Encadenamos el resto: colision + punto de aparicion, material
        // estilizado, y culling en Back (el modelo reconstruido si trae las
        // normales bien). Dejarlo en manos del usuario era justo el paso que
        // se olvidaba, y sin el, el entorno se ve blanco y liso.
        MeshiWalkableSetup.Setup();
        ApplyLimpio();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null && mat.HasProperty("_Cull"))
        {
            mat.SetFloat("_Cull", (float)CullMode.Back);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }

        Debug.Log("[EnvStyler] Modelo reconstruido montado, con colision, "
                  + "material estilizado y culling en Back. Guarda la escena.");
    }

    // ------------------------------------------------------------------
    // Alternar el culling de caras
    // ------------------------------------------------------------------
    // El modelo de Meshy necesita culling apagado porque tiene caras con el
    // giro invertido. El reconstruido trae las normales bien, asi que puede ir
    // en Back: es mas rapido y evita ver el interior de los muros.
    // ------------------------------------------------------------------
    [MenuItem("MovU/Estilizar entorno/Alternar culling de caras", priority = 61)]
    public static void ToggleCulling()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null || !mat.HasProperty("_Cull"))
        {
            EditorUtility.DisplayDialog(
                "MovU",
                "No encuentro " + MaterialPath + " con el shader del entorno.\n\n" +
                "Corre primero 'MovU > Estilizar entorno > Limpio (low-poly)'.",
                "Entendido");
            return;
        }

        bool estabaApagado = Mathf.Approximately(mat.GetFloat("_Cull"), (float)CullMode.Off);
        var nuevoModo = estabaApagado ? CullMode.Back : CullMode.Off;
        mat.SetFloat("_Cull", (float)nuevoModo);

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log($"[EnvStyler] Culling de caras: {nuevoModo}.");
    }
}
