using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// ============================================================================
// MovUJuegoMenu.cs — Menús de juego, contenido y rendimiento
// ============================================================================
//   MovU > Juego        NavMesh y carpeta de métricas
//   MovU > Contenido    POIs, vista previa del JSON, ascensor y aparición
//   MovU > Rendimiento  sombras, occlusion culling, presupuesto de triángulos
//
// Los POIs, las misiones y los NPC NO se construyen desde aquí: salen de
// Assets/MovU/Resources/MovU/contenido_piso9.json y se montan solos al dar
// Play (MovUBootstrap). Estos menús son para lo que sí hay que dejar guardado
// en la escena (el NavMesh, el ascensor) y para ver y ajustar el contenido.
// ============================================================================

public static class MovUJuegoMenu
{
    private const string RutaJson = "Assets/MovU/Resources/MovU/contenido_piso9.json";
    private const string CarpetaEscenas = "Assets/MovU/Scenes";
    private const string NombreNavegacion = "Navegacion";
    private const string ClaveVistaPrevia = "MovU.VerContenidoJson";
    private const int PresupuestoDeTriangulos = 100000;      // SRS, RD-4

    // ==================================================================
    // JUEGO
    // ==================================================================
    [MenuItem("MovU/Juego/Hornear NavMesh de todos los pisos", priority = 50)]
    public static void HornearNavMesh()
    {
        HornearNavMeshInterno(true);
    }

    /// <summary>
    /// Hornea UN NavMesh con todos los pisos y lo guarda como asset junto a la
    /// escena. Guardarlo como asset es lo que hace que sobreviva al cerrar
    /// Unity y que exista en el ejecutable.
    ///
    /// Va en un objeto aparte ('Navegacion') que el FloorManager no apaga: el
    /// NavMesh de los tres pisos está siempre cargado, y por eso se puede
    /// calcular una ruta hacia un piso que en ese momento no se está dibujando.
    /// </summary>
    internal static bool HornearNavMeshInterno(bool interactivo)
    {
        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (gestor == null || gestor.CantidadDePisos == 0)
        {
            Aviso(interactivo, "No hay edificio en esta escena. Corre primero 'MovU > Preparar todo'.");
            return false;
        }

        // Todos los pisos tienen que estar encendidos para que sus colisiones
        // entren al horneado. En modo edición lo normal es que ya lo estén.
        for (int i = 0; i < gestor.CantidadDePisos; i++)
        {
            Transform piso = gestor.Piso(i);
            if (piso != null && !piso.gameObject.activeSelf) piso.gameObject.SetActive(true);
        }
        Physics.SyncTransforms();

        GameObject go = GameObject.Find(NombreNavegacion);
        if (go == null)
        {
            go = new GameObject(NombreNavegacion);
            Undo.RegisterCreatedObjectUndo(go, "Hornear NavMesh");
        }
        // Es regenerable: al reconstruir el edificio hay que volver a hornear.
        GeneradoPorMovU.Marcar(go, "NavMesh");

        var superficie = go.GetComponent<NavMeshSurface>();
        if (superficie == null) superficie = go.AddComponent<NavMeshSurface>();
        MovUBootstrap.ConfigurarSuperficie(superficie);

        Scene escena = go.scene;
        string nombre = string.IsNullOrEmpty(escena.name) ? "Escena" : escena.name;
        string carpeta = string.IsNullOrEmpty(escena.path)
            ? CarpetaEscenas
            : Path.GetDirectoryName(escena.path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(carpeta)) carpeta = "Assets";
        string ruta = carpeta + "/" + nombre + "_NavMesh.asset";

        superficie.RemoveData();
        superficie.navMeshData = null;
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(ruta) != null) AssetDatabase.DeleteAsset(ruta);

        EditorUtility.DisplayProgressBar("MovU", "Horneando el NavMesh...", 0.5f);
        try
        {
            superficie.BuildNavMesh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (superficie.navMeshData == null)
        {
            Aviso(interactivo, "El horneado no produjo datos. Revisa que el edificio tenga MeshCollider.");
            return false;
        }

        AssetDatabase.CreateAsset(superficie.navMeshData, ruta);
        EditorUtility.SetDirty(superficie);
        EditorSceneManager.MarkSceneDirty(escena);
        AssetDatabase.SaveAssets();
        NavUtil.OlvidarEstado();

        NavMeshTriangulation t = NavMesh.CalculateTriangulation();
        int triangulos = t.indices != null ? t.indices.Length / 3 : 0;
        if (triangulos == 0)
        {
            Aviso(interactivo, "El NavMesh salió vacío: no encontró ninguna superficie caminable.");
            return false;
        }

        string mensaje = $"NavMesh horneado: {triangulos:N0} triángulos, guardado en {ruta}.\n\n" +
                         "Guarda la escena (Ctrl+S) para que quede enlazado.";
        Debug.Log("[MovU] " + mensaje);
        if (interactivo) EditorUtility.DisplayDialog("MovU", mensaje, "Entendido");
        return true;
    }

    [MenuItem("MovU/Juego/Abrir carpeta de métricas (CSV)", priority = 51)]
    public static void AbrirCarpetaDeMetricas()
    {
        string ruta = MetricsLogger.CSVPath;
        EditorUtility.RevealInFinder(File.Exists(ruta) ? ruta : Application.persistentDataPath);
    }

    // ==================================================================
    // CONTENIDO
    // ==================================================================
    [MenuItem("MovU/Contenido/Abrir contenido_piso9.json", priority = 60)]
    public static void AbrirJson()
    {
        var archivo = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaJson);
        if (archivo == null)
        {
            Dialogo("No encuentro " + RutaJson + ".");
            return;
        }
        AssetDatabase.OpenAsset(archivo);
    }

    private static ContenidoPiso LeerJson()
    {
        var archivo = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaJson);
        return archivo != null ? ContenidoLoader.Interpretar(archivo.text) : null;
    }

    // ------------------------------------------------------------------
    [MenuItem("MovU/Contenido/Ver contenido del JSON en la escena", priority = 61)]
    public static void AlternarVistaPrevia()
    {
        bool nuevo = !EditorPrefs.GetBool(ClaveVistaPrevia, false);
        EditorPrefs.SetBool(ClaveVistaPrevia, nuevo);
        vistaPrevia = null;                       // volver a leer el archivo
        SceneView.RepaintAll();
    }

    [MenuItem("MovU/Contenido/Ver contenido del JSON en la escena", true)]
    private static bool AlternarVistaPreviaValidar()
    {
        Menu.SetChecked("MovU/Contenido/Ver contenido del JSON en la escena",
                        EditorPrefs.GetBool(ClaveVistaPrevia, false));
        return true;
    }

    private static ContenidoPiso vistaPrevia;
    private static double vistaPreviaLeida;

    [InitializeOnLoadMethod]
    private static void EngancharVistaPrevia()
    {
        SceneView.duringSceneGui -= DibujarVistaPrevia;
        SceneView.duringSceneGui += DibujarVistaPrevia;
    }

    /// <summary>
    /// Dibuja en la vista de escena lo que hay en el JSON, sin crear ningún
    /// objeto: POIs (con su radio de llegada y su rótulo), NPC, recorridos,
    /// aparición y ascensor. Sirve para comprobar sobre la planta que cada
    /// cosa cae donde debe antes de dar Play.
    /// </summary>
    private static void DibujarVistaPrevia(SceneView vista)
    {
        if (Application.isPlaying) return;
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        if (!EditorPrefs.GetBool(ClaveVistaPrevia, false)) return;

        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (gestor == null || gestor.CantidadDePisos == 0) return;

        // Se relee cada 2 s: basta guardar el JSON para ver el cambio.
        if (vistaPrevia == null || EditorApplication.timeSinceStartup - vistaPreviaLeida > 2.0)
        {
            vistaPrevia = LeerJson();
            vistaPreviaLeida = EditorApplication.timeSinceStartup;
        }
        ContenidoPiso d = vistaPrevia;
        if (d == null) return;

        Vector3 arriba = Vector3.up;

        foreach (PoiDef p in d.pois)
        {
            if (!ContenidoLoader.AMundo(gestor, p.piso, p.u, p.v, out Vector3 w)) continue;
            Handles.color = p.importante ? new Color(1f, 0.55f, 0.1f) : new Color(0.1f, 0.75f, 1f);
            Handles.DrawWireDisc(w + arriba * 0.05f, arriba, p.radio > 0f ? p.radio : 1.5f);
            Handles.DrawSolidDisc(w + arriba * 0.05f, arriba, 0.18f);

            Vector3 rotulo = w;
            if ((Mathf.Abs(p.rotuloU) > 0.0001f || Mathf.Abs(p.rotuloV) > 0.0001f) &&
                ContenidoLoader.AMundo(gestor, p.piso, p.rotuloU, p.rotuloV, out Vector3 puerta))
            {
                rotulo = puerta;
                Handles.DrawDottedLine(w + arriba * 0.05f, puerta + arriba * 0.05f, 3f);
            }
            Handles.Label(rotulo + arriba * 2.45f, p.nombre + "  [" + p.id + "]");
        }

        Handles.color = new Color(0.4f, 1f, 0.5f);
        foreach (NpcDef n in d.npcs)
        {
            if (!ContenidoLoader.AMundo(gestor, n.piso, n.u, n.v, out Vector3 w)) continue;
            Handles.DrawWireDisc(w + arriba * 0.05f, arriba, 0.3f);
            Vector3 frente = Quaternion.Euler(0f, n.yaw, 0f) * Vector3.forward;
            Handles.DrawLine(w + arriba * 0.05f, w + arriba * 0.05f + frente * 0.8f);
            Handles.Label(w + arriba * 1.9f, string.IsNullOrEmpty(n.nombre) ? n.rol : n.nombre);
        }

        Handles.color = new Color(1f, 1f, 1f, 0.5f);
        int pisoMultitud = d.multitud != null ? d.multitud.piso : 1;
        foreach (PuntoUV r in d.recorridos)
        {
            if (ContenidoLoader.AMundo(gestor, pisoMultitud, r.u, r.v, out Vector3 w))
            {
                Handles.DrawWireDisc(w + arriba * 0.05f, arriba, 0.2f);
            }
        }

        if (d.aparicion != null && d.aparicion.usar &&
            ContenidoLoader.AMundo(gestor, d.aparicion.piso, d.aparicion.u, d.aparicion.v, out Vector3 a))
        {
            Handles.color = new Color(0.2f, 0.5f, 1f);
            Vector3 frente = Quaternion.Euler(0f, d.aparicion.yaw, 0f) * Vector3.forward;
            Handles.ArrowHandleCap(0, a + arriba * 0.1f, Quaternion.LookRotation(frente), 1.6f, EventType.Repaint);
            Handles.Label(a + arriba * 2f, "Aparición");
        }

        if (d.ascensor != null && d.ascensor.usar &&
            ContenidoLoader.AMundo(gestor, 1, d.ascensor.u, d.ascensor.v, out Vector3 e))
        {
            Handles.color = new Color(0.95f, 0.72f, 0.15f);
            Handles.DrawWireCube(e + arriba * 1.1f, new Vector3(1.2f, 2.2f, 1.2f));
            Handles.Label(e + arriba * 2.5f, "Ascensor (JSON)");
        }
    }

    // ------------------------------------------------------------------
    [MenuItem("MovU/Contenido/Copiar posición de la selección como u,v", priority = 62)]
    public static void CopiarUV()
    {
        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        Transform seleccion = Selection.activeTransform;
        if (gestor == null || seleccion == null)
        {
            Dialogo("Selecciona un objeto de la escena del edificio. Un objeto vacío puesto " +
                    "donde quieres el POI sirve.");
            return;
        }

        int piso = gestor.PisoSegunAltura(seleccion.position.y + 0.5f);
        PlanoDePlanta plano = PlanoDePlanta.Medir(gestor.Piso(piso));
        if (!plano.valido)
        {
            Dialogo("No pude medir la planta del piso.");
            return;
        }

        Vector2 uv = plano.AUV(seleccion.position);
        string texto = string.Format(CultureInfo.InvariantCulture,
            "\"piso\": {0}, \"u\": {1:F4}, \"v\": {2:F4}", piso + 1, uv.x, uv.y);
        EditorGUIUtility.systemCopyBuffer = texto;
        Debug.Log("[MovU] Copiado al portapapeles:  " + texto);
    }

    // ------------------------------------------------------------------
    [MenuItem("MovU/Contenido/Crear POI aquí (centro de la vista de escena)", priority = 63)]
    public static void CrearPoiAqui()
    {
        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        SceneView vista = SceneView.lastActiveSceneView;
        if (gestor == null || vista == null)
        {
            Dialogo("Abre la escena del edificio y una vista de escena.");
            return;
        }

        Vector3 pivote = vista.pivot;
        int piso = gestor.PisoSegunAltura(pivote.y + 0.5f);
        Vector3 punto = new Vector3(pivote.x, gestor.AlturaDelSuelo(piso), pivote.z);

        // A mano = se queda en la escena. Va en Contenido/Piso_N, que los
        // menús de construcción nunca borran.
        Transform padre = gestor.ContenidoDelPiso(piso);
        var go = new GameObject("POI_nuevo");
        Undo.RegisterCreatedObjectUndo(go, "Crear POI");
        go.transform.SetParent(padre, false);
        go.transform.position = punto;

        var poi = go.AddComponent<PointOfInterest>();
        poi.Configurar("poi_nuevo", "Nuevo espacio", PoiCategory.Otro, 1.5f, false, true,
                       new Vector3(0f, 2.45f, 0f));

        EditorUtility.SetDirty(gestor);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("[MovU] POI creado en Contenido/Piso_" + (piso + 1) +
                  ". Ponle id y nombre en el Inspector; para usarlo en una misión, " +
                  "escribe ese id en 'poi' dentro del JSON.");
    }

    // ------------------------------------------------------------------
    [MenuItem("MovU/Contenido/Llevar ascensor y aparición al sitio del JSON", priority = 64)]
    public static void AplicarAscensorYAparicion()
    {
        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        ContenidoPiso d = LeerJson();
        if (gestor == null || d == null)
        {
            Dialogo("Hace falta la escena del edificio abierta y un contenido_piso9.json válido.");
            return;
        }

        var hecho = new List<string>();

        if (d.ascensor != null && d.ascensor.usar)
        {
            int movidos = 0;
            foreach (var zona in Object.FindObjectsByType<ElevatorTrigger>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // El ElevatorTrigger está en el hijo 'Zona'; lo que se mueve es
                // la raíz del ascensor (Ascensor_PisoN).
                Transform raiz = zona.transform.parent != null ? zona.transform.parent : zona.transform;
                int piso = gestor.PisoSegunAltura(zona.transform.position.y);
                if (!ContenidoLoader.AMundo(gestor, piso + 1, d.ascensor.u, d.ascensor.v, out Vector3 p)) continue;

                Undo.RecordObject(raiz, "Mover ascensor");
                raiz.SetPositionAndRotation(p, Quaternion.Euler(0f, d.ascensor.yaw, 0f));
                movidos++;
            }
            hecho.Add(movidos + " ascensor(es) movidos.");
        }

        var jugador = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (jugador != null && d.aparicion != null && d.aparicion.usar &&
            ContenidoLoader.AMundo(gestor, d.aparicion.piso, d.aparicion.u, d.aparicion.v, out Vector3 a))
        {
            Undo.RecordObject(jugador.transform, "Mover jugador");
            jugador.transform.SetPositionAndRotation(a + Vector3.up * 0.05f,
                                                     Quaternion.Euler(0f, d.aparicion.yaw, 0f));
            hecho.Add("Personaje en el punto de aparición.");
        }

        if (hecho.Count == 0)
        {
            Dialogo("El JSON no tiene 'ascensor' ni 'aparicion' con \"usar\": true.");
            return;
        }

        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Dialogo(string.Join("\n", hecho) + "\n\nEl ascensor es un obstáculo del NavMesh: " +
                "vuelve a hornearlo (MovU > Juego > Hornear NavMesh) y guarda la escena.");
    }

    // ==================================================================
    // RENDIMIENTO
    // ==================================================================

    /// <summary>
    /// Bajo techo el sol no llega a ningún lado, pero Unity igual dibuja el
    /// edificio otra vez (hasta cuatro, una por cascada) solo para calcular sus
    /// sombras. Es el gasto más grande de la escena y no aporta nada visible.
    ///
    /// Para que el interior se vea IGUAL que antes, la intensidad del sol se
    /// baja a la fracción que dejaba pasar la sombra (1 - fuerza de la sombra).
    /// De paso desaparece el corte de luz que se veía al fondo del pasillo,
    /// donde se acababa la distancia de sombras.
    /// </summary>
    [MenuItem("MovU/Rendimiento/Quitar sombras del sol (interior)", priority = 70)]
    public static void QuitarSombrasDelSol()
    {
        int cambiadas = 0;
        foreach (Light luz in Object.FindObjectsByType<Light>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (luz.type != LightType.Directional) continue;
            if (luz.shadows == LightShadows.None) continue;

            var recuerdo = luz.GetComponent<SolSinSombras>();
            if (recuerdo == null) recuerdo = Undo.AddComponent<SolSinSombras>(luz.gameObject);
            recuerdo.Guardar(luz);

            Undo.RecordObject(luz, "Quitar sombras del sol");
            luz.intensity = luz.intensity * Mathf.Clamp01(1f - luz.shadowStrength);
            luz.shadows = LightShadows.None;
            EditorUtility.SetDirty(luz);
            cambiadas++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Dialogo(cambiadas > 0
            ? "Sombras del sol apagadas en " + cambiadas + " luz/luces. El interior debería " +
              "verse igual; si no te gusta, 'MovU > Rendimiento > Restaurar sombras del sol'."
            : "No había ninguna luz direccional con sombras.");
    }

    [MenuItem("MovU/Rendimiento/Restaurar sombras del sol", priority = 71)]
    public static void RestaurarSombrasDelSol()
    {
        int restauradas = 0;
        foreach (var recuerdo in Object.FindObjectsByType<SolSinSombras>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Light luz = recuerdo.GetComponent<Light>();
            if (luz != null)
            {
                Undo.RecordObject(luz, "Restaurar sombras del sol");
                recuerdo.Restaurar(luz);
                EditorUtility.SetDirty(luz);
                restauradas++;
            }
            Undo.DestroyObjectImmediate(recuerdo);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Dialogo(restauradas > 0 ? "Sombras restauradas." : "No había nada que restaurar.");
    }

    [MenuItem("MovU/Rendimiento/Hornear Occlusion Culling", priority = 72)]
    public static void HornearOcclusion()
    {
        EditorUtility.DisplayProgressBar("MovU", "Horneando Occlusion Culling...", 0.5f);
        bool ok;
        try
        {
            ok = StaticOcclusionCulling.Compute();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Dialogo(ok
            ? "Occlusion Culling horneado (" + (StaticOcclusionCulling.umbraDataSize / 1024) + " KB).\n\n" +
              "Ojo con lo que se gana: cada piso es UNA sola malla, así que el piso entero se " +
              "dibuja o no se dibuja. Lo que sí se deja de dibujar detrás de los muros es lo " +
              "que está suelto: NPC, rótulos, puertas y muebles."
            : "El horneado falló. Revisa la consola.");
    }

    // ------------------------------------------------------------------
    [MenuItem("MovU/Rendimiento/Informe de presupuesto (RD-4)", priority = 73)]
    public static void InformeDePresupuesto()
    {
        var lineas = new List<string>();
        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);

        long mayorPiso = 0;
        if (gestor != null)
        {
            for (int i = 0; i < gestor.CantidadDePisos; i++)
            {
                long t = Triangulos(gestor.Piso(i)) + Triangulos(gestor.ContenidoDelPisoSiExiste(i));
                if (t > mayorPiso) mayorPiso = t;
                lineas.Add($"Piso {i + 1}: {t:N0} triángulos (geometría + contenido de la escena).");
            }
        }
        else
        {
            lineas.Add("No hay edificio en la escena.");
        }

        ContenidoPiso d = LeerJson();
        long npc = 0;
        if (d != null)
        {
            int cantidad = d.npcs.Count + (d.multitud != null ? d.multitud.estudiantes + d.multitud.visitantes : 0);
            npc = (long)cantidad * NpcMeshFactory.Triangulos;
            lineas.Add($"NPC del JSON: {cantidad} × {NpcMeshFactory.Triangulos} = {npc:N0} triángulos.");
        }

        long total = mayorPiso + npc;
        lineas.Add("");
        lineas.Add($"Peor caso con un piso cargado: {total:N0} de {PresupuestoDeTriangulos:N0} " +
                   (total <= PresupuestoDeTriangulos ? "(dentro del presupuesto)." : "(SE PASA del presupuesto)."));
        lineas.Add($"Margen libre: {PresupuestoDeTriangulos - total:N0} triángulos.");

        int conSombras = 0;
        foreach (Light luz in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (luz.shadows != LightShadows.None) conSombras++;
        }
        lineas.Add("");
        lineas.Add(conSombras == 0
            ? "OK  Ninguna luz calcula sombras en tiempo real."
            : $"--  {conSombras} luz/luces con sombras en tiempo real: el edificio se dibuja de más. " +
              "Ver 'MovU > Rendimiento > Quitar sombras del sol'.");

        lineas.Add(StaticOcclusionCulling.umbraDataSize > 0
            ? "OK  Occlusion Culling horneado."
            : "--  Occlusion Culling sin hornear.");

        // Modelos que pesan en el repo y que la escena abierta no usa.
        Scene escena = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(escena.path))
        {
            var usados = new HashSet<string>(AssetDatabase.GetDependencies(escena.path, true));
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/MovU/Models" }))
            {
                string ruta = AssetDatabase.GUIDToAssetPath(guid);
                if (usados.Contains(ruta)) continue;
                var info = new FileInfo(ruta);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    lineas.Add($"--  {Path.GetFileName(ruta)} ({info.Length / 1048576f:F1} MB) no se usa en " +
                               "esta escena: no entra al ejecutable, pero sí pesa en el repositorio.");
                }
            }
        }

        string informe = string.Join("\n", lineas);
        Debug.Log("[MovU] Presupuesto de rendimiento\n" + informe);
        EditorUtility.DisplayDialog("MovU — presupuesto", informe, "Entendido");
    }

    /// <summary>
    /// Triángulos de todo lo que cuelga de una raíz. Con GetIndexCount no hace
    /// falta que la malla tenga Read/Write activado.
    /// </summary>
    internal static long Triangulos(Transform raiz)
    {
        if (raiz == null) return 0;
        long total = 0;
        foreach (var mf in raiz.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = mf.sharedMesh;
            if (m == null) continue;
            for (int s = 0; s < m.subMeshCount; s++) total += m.GetIndexCount(s) / 3;
        }
        return total;
    }

    // ------------------------------------------------------------------
    private static void Dialogo(string mensaje)
    {
        EditorUtility.DisplayDialog("MovU", mensaje, "Entendido");
    }

    private static void Aviso(bool interactivo, string mensaje)
    {
        if (interactivo) Dialogo(mensaje);
        else Debug.LogError("[MovU] " + mensaje);
    }
}
