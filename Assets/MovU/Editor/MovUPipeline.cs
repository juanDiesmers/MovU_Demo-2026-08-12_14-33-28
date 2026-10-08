using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// ============================================================================
// MovUPipeline.cs — Un solo menu para dejar el juego listo
// ============================================================================
// Menu:  MovU > Preparar todo (recomendado)
//
// Los tres menus que hay que correr en orden son faciles de correr en el orden
// equivocado, y el orden equivocado no falla: deja la escena rara. Los casos
// reales:
//
//   - Estilizar DESPUES de construir el edificio pisaba _FloorLevel y
//     _WallGradientHeight con los valores de un solo piso, y el zocalo quedaba
//     bien en el piso 1 y a media pared en el 2 y el 3.
//   - 'Preparar plano Meshy jugable' con el edificio ya construido encontraba
//     el edificio entero como si fuera la planta suelta y le aplicaba la
//     rotacion y la escala de una planta.
//
// Las dos cosas estan tapadas en su propio menu, pero lo que de verdad sobra es
// tener que acordarse. Este menu los encadena en el orden correcto y deja la
// escena guardada como Assets/MovU/Scenes/Edificio.unity, que a partir de ahi
// es la fuente de verdad: se versiona, se abre y se le anade contenido como a
// cualquier escena de Unity. Los menus quedan para REGENERAR la geometria
// cuando cambie el modelo o la escala, no para correrlos cada vez.
// ============================================================================

public static class MovUPipeline
{
    private const string RutaEscena = "Assets/MovU/Scenes/Edificio.unity";
    private const string CarpetaEscenas = "Assets/MovU/Scenes";

    // ------------------------------------------------------------------
    [MenuItem("MovU/Preparar todo (recomendado)", priority = 0)]
    public static void PrepararTodo()
    {
        Scene escena = SceneManager.GetActiveScene();

        bool seguir = EditorUtility.DisplayDialog(
            "MovU",
            "Voy a dejar el juego listo en cuatro pasos:\n\n" +
            "  1. Preparar la planta de Meshy (colliders, escala, personaje)\n" +
            "  2. Estilizar el entorno (preset Limpio)\n" +
            "  3. Construir el edificio de 3 pisos con ascensor\n" +
            "  4. Hornear el NavMesh de los tres pisos\n\n" +
            "Trabajo sobre " + RutaEscena + ", no sobre la escena del demo.\n\n" +
            "Lo que este en la raiz 'Contenido' no se toca.",
            "Adelante",
            "Cancelar");
        if (!seguir) return;

        if (!AsegurarEscenaDelEdificio(escena)) return;

        if (!Paso("1/4 Preparando la planta", () => MeshiWalkableSetup.SetupInterno(false))) return;
        if (!Paso("2/4 Estilizando el entorno", () => EnvironmentStyler.AplicarLimpio(false))) return;
        if (!Paso("3/4 Construyendo el edificio", () => BuildingSetup.ConstruirInterno(false))) return;

        // El NavMesh no corta la cadena si falla: el edificio ya quedó bien y
        // el juego lo hornea al vuelo dentro del editor. Se avisa al final.
        EditorUtility.DisplayProgressBar("MovU", "4/4 Horneando el NavMesh...", 0.9f);
        bool navMeshListo;
        try
        {
            navMeshListo = MovUJuegoMenu.HornearNavMeshInterno(false);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Scene actual = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(actual);
        EditorSceneManager.SaveScene(actual);
        RegistrarEnBuildSettings(actual.path);

        Debug.Log("[MovU] Escena lista y guardada en " + actual.path + ".");

        EditorUtility.DisplayDialog(
            "MovU",
            "Listo. La escena quedo guardada en:\n" + actual.path + "\n\n" +
            (navMeshListo
                ? "El NavMesh quedo horneado y guardado junto a la escena.\n\n"
                : "OJO: el NavMesh NO se pudo hornear (el motivo esta en la consola). " +
                  "Prueba 'MovU > Juego > Hornear NavMesh de todos los pisos'.\n\n") +
            "Dale Play: los POIs, las misiones y los NPC salen de\n" +
            "Assets/MovU/Resources/MovU/contenido_piso9.json y se montan solos.\n\n" +
            "Opcional, para rendimiento (menu MovU > Rendimiento):\n" +
            "  - Quitar sombras del sol (interior)\n" +
            "  - Hornear Occlusion Culling\n\n" +
            "'MovU > Proyecto > Revisar estado de la escena' te dice en cualquier " +
            "momento que falta.",
            "Entendido");
    }

    /// <summary>Un paso de la cadena, con barra de progreso y corte si falla.</summary>
    private static bool Paso(string titulo, System.Func<bool> accion)
    {
        EditorUtility.DisplayProgressBar("MovU", titulo + "...", 0.5f);
        bool ok;
        try
        {
            ok = accion();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!ok)
        {
            EditorUtility.DisplayDialog(
                "MovU",
                "Me quede en el paso: " + titulo + ".\n\n" +
                "El motivo esta en la consola. No toco nada mas para no dejar la " +
                "escena a medias.",
                "Entendido");
        }
        return ok;
    }

    // ------------------------------------------------------------------
    // Escena
    // ------------------------------------------------------------------

    /// <summary>
    /// Deja abierta la escena del edificio. Si se estaba trabajando en otra
    /// (tipicamente DemoMaze, que tiene el laberinto y el sistema de misiones),
    /// la guarda COMO Edificio.unity: asi la del demo se queda como estaba.
    /// </summary>
    private static bool AsegurarEscenaDelEdificio(Scene escena)
    {
        if (escena.path == RutaEscena) return true;

        if (!AssetDatabase.IsValidFolder(CarpetaEscenas))
        {
            AssetDatabase.CreateFolder("Assets/MovU", "Scenes");
        }

        string origen = string.IsNullOrEmpty(escena.path) ? "(escena sin guardar)" : escena.path;

        bool ok = EditorUtility.DisplayDialog(
            "MovU",
            "La escena abierta es:\n" + origen + "\n\n" +
            "La voy a guardar como " + RutaEscena + " y trabajo ahi, para no " +
            "modificar la escena del demo.",
            "Guardar como Edificio.unity",
            "Cancelar");
        if (!ok) return false;

        if (!EditorSceneManager.SaveScene(escena, RutaEscena))
        {
            EditorUtility.DisplayDialog("MovU", "No pude guardar " + RutaEscena + ".", "Entendido");
            return false;
        }

        RegistrarEnBuildSettings(RutaEscena);
        return true;
    }

    private static void RegistrarEnBuildSettings(string ruta)
    {
        if (string.IsNullOrEmpty(ruta)) return;

        var lista = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var e in lista)
        {
            if (e.path == ruta)
            {
                if (!e.enabled) e.enabled = true;
                EditorBuildSettings.scenes = lista.ToArray();
                return;
            }
        }

        lista.Add(new EditorBuildSettingsScene(ruta, true));
        EditorBuildSettings.scenes = lista.ToArray();
        Debug.Log("[MovU] " + ruta + " anadida a Build Settings.");
    }

    // ------------------------------------------------------------------
    [MenuItem("MovU/Proyecto/Guardar escena como Edificio.unity", priority = 100)]
    public static void GuardarEscenaDelEdificio()
    {
        Scene escena = SceneManager.GetActiveScene();
        if (!AssetDatabase.IsValidFolder(CarpetaEscenas))
        {
            AssetDatabase.CreateFolder("Assets/MovU", "Scenes");
        }

        if (EditorSceneManager.SaveScene(escena, RutaEscena))
        {
            RegistrarEnBuildSettings(RutaEscena);
            Debug.Log("[MovU] Escena guardada en " + RutaEscena + ".");
        }
    }

    // ------------------------------------------------------------------
    // Serializacion en texto: sin esto git no puede hacer diff ni merge de las
    // escenas, y son tres personas en el repo.
    // ------------------------------------------------------------------
    [MenuItem("MovU/Proyecto/Reserializar escenas y prefabs a texto", priority = 101)]
    public static void ReserializarATexto()
    {
        EditorSettings.serializationMode = SerializationMode.ForceText;

        var rutas = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Scene t:Prefab", new[] { "Assets" }))
        {
            rutas.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        if (rutas.Count == 0)
        {
            Debug.Log("[MovU] No hay escenas ni prefabs que reserializar.");
            return;
        }

        AssetDatabase.ForceReserializeAssets(rutas);
        AssetDatabase.SaveAssets();

        Debug.Log("[MovU] Serializacion en texto forzada y " + rutas.Count +
                  " assets reescritos. A partir de ahora git puede hacer diff " +
                  "y merge de las escenas.");
    }

    // ------------------------------------------------------------------
    // Diagnostico: que hay y que falta, sin abrir seis ventanas.
    // ------------------------------------------------------------------
    [MenuItem("MovU/Proyecto/Revisar estado de la escena", priority = 102)]
    public static void RevisarEstado()
    {
        var lineas = new List<string>();
        Scene escena = SceneManager.GetActiveScene();

        lineas.Add("Escena: " + (string.IsNullOrEmpty(escena.path) ? "(sin guardar)" : escena.path));

        var jugador = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        lineas.Add(jugador != null
            ? "OK  Personaje en la escena."
            : "--  No hay personaje: corre 'MovU > Preparar todo'.");

        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (gestor != null && gestor.CantidadDePisos > 0)
        {
            lineas.Add($"OK  Edificio de {gestor.CantidadDePisos} pisos. " +
                       $"Suelo del piso 1 en y = {gestor.AlturaDelSuelo(0):F2} m, " +
                       $"separacion {gestor.SeparacionEntrePisos:F2} m.");
        }
        else
        {
            lineas.Add("--  No hay edificio construido.");
        }

        GameObject contenido = GameObject.Find("Contenido");
        lineas.Add(contenido != null
            ? "OK  Raiz 'Contenido' presente (" + contenido.transform.childCount + " pisos)."
            : "--  No hay raiz 'Contenido' todavia; la crea el menu del edificio.");

        // Vertices y no triangulos: leer .triangles exige que la malla tenga
        // Read/Write activado, y los OBJ importados no lo tienen.
        long vertices = 0;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (mf.sharedMesh != null) vertices += mf.sharedMesh.vertexCount;
        }
        lineas.Add($"    Vertices dibujandose ahora mismo: {vertices:N0} " +
                   "(un solo piso deberia rondar los 79.000).");

        lineas.Add(StaticOcclusionCulling.umbraDataSize > 0
            ? "OK  Occlusion Culling horneado."
            : "--  Occlusion Culling SIN hornear: Window > Rendering > Occlusion Culling > Bake.");

        NavMeshTriangulation nav = NavMesh.CalculateTriangulation();
        lineas.Add(nav.indices != null && nav.indices.Length > 0
            ? $"OK  NavMesh presente ({nav.indices.Length / 3:N0} triangulos)."
            : "--  NavMesh ausente: 'MovU > Juego > Hornear NavMesh de todos los pisos'. " +
              "Sin el, en el ejecutable no hay ruta optima ni flecha por ruta.");

        // Contenido: lo que hay en el JSON y lo que hay puesto a mano.
        ContenidoPiso datosJson = ContenidoLoader.Leer();
        if (datosJson != null)
        {
            int enEscena = Object.FindObjectsByType<PointOfInterest>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            lineas.Add($"OK  contenido_piso9.json: {datosJson.pois.Count} POIs, " +
                       $"{datosJson.misiones.Count} misiones, {datosJson.npcs.Count} NPC fijos" +
                       (enEscena > 0 ? $" (+ {enEscena} POIs puestos a mano en la escena)." : "."));

            foreach (MisionDef m in datosJson.misiones)
            {
                bool existe = datosJson.pois.Exists(p => p.id == m.poi);
                if (!existe)
                {
                    foreach (var poi in Object.FindObjectsByType<PointOfInterest>(
                                 FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        if (poi.Id == m.poi) { existe = true; break; }
                    }
                }
                if (!existe)
                {
                    lineas.Add($"--  La mision '{m.id}' apunta al POI '{m.poi}', que no existe.");
                }
            }
        }
        else
        {
            lineas.Add("--  Falta o esta mal escrito Assets/MovU/Resources/MovU/contenido_piso9.json: " +
                       "sin el no hay POIs ni misiones.");
        }

        lineas.Add(EditorSettings.serializationMode == SerializationMode.ForceText
            ? "OK  Serializacion en texto."
            : "--  Serializacion NO es texto: git no puede mergear escenas. " +
              "Corre 'MovU > Proyecto > Reserializar escenas y prefabs a texto'.");

        string informe = string.Join("\n", lineas);
        Debug.Log("[MovU] Estado de la escena\n" + informe);
        EditorUtility.DisplayDialog("MovU — estado de la escena", informe, "Entendido");
    }
}
