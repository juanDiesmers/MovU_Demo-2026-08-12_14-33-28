using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ============================================================================
// Piso3Builder.cs — El piso 3, a partir de su plano de evacuación
// ============================================================================
// La planta sale de Models/Piso3/planta_piso3.json, que genera
// Tools/piso3_planta.py a partir de la foto del plano, rectificada contra el
// contorno del piso 9. La construcción la hace PlantaBuilder (la misma que arma
// el piso 9); aquí queda la configuración del piso 3 y los menús de depuración.
// ============================================================================

public static class Piso3Builder
{
    public const string Planta = "piso3";

    public static readonly PlantaBuilder.Config Config = new PlantaBuilder.Config
    {
        planta = Planta,
        etiqueta = "Piso 3",
        rutaPlanta = "Assets/MovU/Models/Piso3/planta_piso3.json",
        carpetaMallas = "Assets/MovU/Models/Piso3/Mallas",
        carpetaMateriales = "Assets/MovU/Materials/Piso3",
        prefijoMaterial = "P3_",
        carpetasTexturas = new[] { "Assets/MovU/Textures/Piso3" },
        texturas = new System.Collections.Generic.Dictionary<string, string>
        {
            { "Concreto", "P3_Concreto.png" }, { "Madera", "P3_Madera.png" }, { "Piso", "P3_Piso.png" },
        },
    };

    public static bool HayPlanta => PlantaBuilder.HayPlanta(Config);

    /// <summary>
    /// Arma el piso 3 bajo 'padre'. 'pivote' es la posición de mundo que debe
    /// tener la raíz del piso (la del piso 9 menos una separación) y
    /// 'alturaLosa' la altura de la cara pisable sobre ese pivote, igual que en
    /// los demás pisos. 'planoBase' es el del piso 9.
    /// </summary>
    public static Transform Construir(Transform padre, string nombre, Vector3 pivote,
                                      float alturaLosa, PlanoDePlanta planoBase, float alturaDelTecho)
    {
        return PlantaBuilder.Construir(Config, "Piso3_", padre, nombre, pivote, alturaLosa, planoBase, alturaDelTecho);
    }

    // ------------------------------------------------------------------
    // Depuración en Play: ir y volver del piso 3 sin pasar por el ascensor
    // ------------------------------------------------------------------
    [MenuItem("MovU/Depuracion en Play/Ir al vestíbulo del piso 3")]
    private static void IrAlPiso3()
    {
        Llevar(Planta, 0.80f, 0.905f, 0f);    // vestíbulo de los ascensores, mirando al pasillo
    }

    [MenuItem("MovU/Depuracion en Play/Ir al lobby del piso 13")]
    private static void IrAlPiso13()
    {
        Llevar(Piso13Builder.Planta, 0.8181f, 0.9127f, 270f);   // mismo sitio que la aparición del 9
    }

    [MenuItem("MovU/Depuracion en Play/Ir al pasillo del piso 4 (genérico)")]
    private static void IrAlGenerico()
    {
        Llevar(FloorManager.PlantaGenerica, 0.55f, 0.88f, 180f);
    }

    /// <summary>
    /// Revisa la escalera de emergencia en toda la pila: qué tramos conectan y si hay
    /// ruta por escalera desde el piso más bajo con escalera hasta el más alto.
    /// </summary>
    [MenuItem("MovU/Depuracion en Play/Probar la escalera en todos los pisos")]
    private static void ProbarEscalera()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Escalera] Esto funciona en Play."); return; }
        FloorManager pisos = FloorManager.Instance;
        StairsManager esc = StairsManager.Instance;
        if (pisos == null || esc == null || !esc.Disponible) { Debug.LogWarning("[Escalera] No hay escalera."); return; }
        var sb = new System.Text.StringBuilder();
        int primero = -1, ultimo = -1;
        for (int i = 0; i < pisos.CantidadDePisos; i++)
        {
            if (!pisos.TieneEscalera(i)) { sb.AppendLine($"  piso {FloorManager.NumeroVisible(i)}: sin escalera funcional"); continue; }
            if (primero < 0) primero = i;
            ultimo = i;
            bool sube = pisos.EscaleraConecta(i, i + 1);
            bool baja = pisos.EscaleraConecta(i, i - 1);
            bool ruta = NavUtil.Ruta(esc.FrenteDelHall(i), esc.PieDelTramo(i), out float largo);
            sb.AppendLine($"  piso {FloorManager.NumeroVisible(i)}: sube {(sube ? "sí" : "no")}, baja {(baja ? "sí" : "no")}, " +
                          $"hall -> pie del tramo {(ruta ? largo.ToString("F1") + " m" : "SIN RUTA")}");
        }
        float total = 0f;
        bool completa = primero >= 0 && NavUtil.RutaPorEscalera(esc.FrenteDelHall(primero), primero,
                                                                 esc.PieDelTramo(ultimo), ultimo, out total);
        sb.Insert(0, $"[Escalera] Del piso {FloorManager.NumeroVisible(primero)} al {FloorManager.NumeroVisible(ultimo)} " +
                     $"por escalera: {(completa ? $"hay ruta ({total:F0} m)" : "SIN RUTA")}\n");
        Debug.Log(sb.ToString());
    }

    [MenuItem("MovU/Depuracion en Play/Ir al pie de la escalera principal")]
    private static void IrALaPrincipal()
    {
        ContenidoPiso d = ContenidoLoader.Datos;
        if (d == null || d.escaleraPrincipal == null) return;
        // Un par de metros antes del pie, mirando hacia donde se sube.
        Llevar(FloorManager.PlantaBase, d.escaleraPrincipal.pieU + 1.8f / 39.3f, d.escaleraPrincipal.pieV + 0.4f / 106.6f,
               d.escaleraPrincipal.rumbo - 15f);
    }

    [MenuItem("MovU/Depuracion en Play/Ir al rellano de la escalera principal")]
    private static void IrAlRellanoPrincipal()
    {
        if (!Application.isPlaying) return;
        StairsManager esc = StairsManager.Instance;
        FloorManager pisos = FloorManager.Instance;
        var pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (esc == null || !esc.HayPrincipal || pisos == null || pc == null) { Debug.LogWarning("[Escalera] No hay escalera principal."); return; }
        ContenidoPiso d = ContenidoLoader.Datos;
        pc.Teletransportar(esc.FrenteDelRellanoPrincipal(pisos.PisoActual) + Vector3.up * 0.05f, d.escaleraPrincipal.rumbo);
        Debug.Log("[Escalera] En el rellano de la principal (piso " + FloorManager.NumeroVisible(pisos.PisoActual) + ").");
    }

    [MenuItem("MovU/Depuracion en Play/Volver al piso 9 (aparición)")]
    private static void VolverAlPiso9()
    {
        ContenidoPiso d = ContenidoLoader.Datos;
        if (d == null || d.aparicion == null) return;
        Llevar(FloorManager.PlantaBase, d.aparicion.u, d.aparicion.v, d.aparicion.yaw);
    }

    /// <summary>
    /// Desde donde está el jugador: ruta por el NavMesh a cada POI de su piso y
    /// ruta entre pisos (con ascensor) a los POI de los demás. Sirve para
    /// revisar que el piso 3 quedó bien horneado y unido al resto por ascensor.
    /// </summary>
    [MenuItem("MovU/Depuracion en Play/Probar rutas desde aquí a todos los POI")]
    private static void ProbarRutas()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Piso 3] Esto funciona en Play."); return; }
        FloorManager pisos = FloorManager.Instance;
        var pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (pisos == null || pc == null) return;

        Vector3 pies = pc.transform.position;
        int piso = pisos.PisoActual;
        int bien = 0, mal = 0;
        var lineas = new System.Text.StringBuilder();
        foreach (PointOfInterest poi in PointOfInterest.Todos)
        {
            if (poi == null) continue;
            bool ok = NavUtil.RutaEntrePisos(pies, piso, poi.transform.position, poi.Piso,
                                             out float largo, out bool ascensor);
            if (ok) bien++; else mal++;
            lineas.AppendLine($"  {(ok ? "OK " : "SIN")}  {poi.Id} (piso {FloorManager.NumeroVisible(poi.Piso)}): " +
                              (ok ? $"{largo:F1} m{(ascensor ? ", con ascensor" : "")}" : "sin ruta"));
        }
        Debug.Log($"[Piso 3] Rutas desde {pies:F1} (piso {FloorManager.NumeroVisible(piso)}): " +
                  $"{bien} con ruta, {mal} sin ruta.\n{lineas}");
    }

    private static void Llevar(string planta, float u, float v, float yaw)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Piso 3] Esto funciona en Play."); return; }
        FloorManager pisos = FloorManager.Instance;
        if (pisos == null) return;
        int indice = -1;
        for (int i = 0; i < pisos.CantidadDePisos; i++) if (pisos.PlantaDe(i) == planta) { indice = i; break; }
        if (indice < 0) { Debug.LogWarning($"[Piso 3] No hay ningún piso con la planta '{planta}'."); return; }
        if (!ContenidoLoader.AMundo(pisos, indice + 1, u, v, out Vector3 p)) return;

        pisos.LlevarAlPiso(indice);
        var pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (pc != null) pc.Teletransportar(p, yaw);
        Debug.Log($"[Piso 3] Jugador en el piso {FloorManager.NumeroVisible(indice)} ({p:F1}).");
    }

    /// <summary>
    /// Reconstruye el edificio (pisos 3, 9 y 10), hornea el NavMesh de todos los
    /// pisos y guarda la escena, sin abrir diálogos. Sirve después de cambiar
    /// una planta (Tools/piso3_planta.py, Tools/piso9_planta.py) o el JSON.
    /// </summary>
    [MenuItem("MovU/Edificio/Reconstruir edificio y NavMesh (sin diálogos)", priority = 12)]
    public static void ReconstruirTodo()
    {
        AssetDatabase.Refresh();
        if (!BuildingSetup.ConstruirInterno(false))
        {
            Debug.LogError("[Piso 3] No se pudo construir el edificio; revisa los avisos de arriba.");
            return;
        }
        if (!MovUJuegoMenu.HornearNavMeshInterno(false))
        {
            Debug.LogError("[Piso 3] El edificio quedó construido pero no se pudo hornear el NavMesh.");
        }
        var escena = EditorSceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(escena.path)) EditorSceneManager.SaveScene(escena);
        Debug.Log("[Piso 3] Edificio reconstruido, NavMesh horneado y escena guardada.");
    }
}
