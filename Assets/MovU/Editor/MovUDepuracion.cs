using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ============================================================================
// MovUDepuracion.cs — Probar el juego sin teclado (solo dentro de Unity)
// ============================================================================
// Menú  MovU > Depuracion en Play. Sirve para QA y para las pruebas que se
// lanzan por control remoto (MCP), donde no hay quien pulse teclas:
//
//   - Empezar la sesión sin escribir en el panel.
//   - Hacer que el personaje CAMINE solo (con su CharacterController, a su
//     velocidad) hasta el destino de la misión, o por la escalera. Así se
//     prueban de verdad las colisiones, las puertas, los pasos y la llegada.
//   - Cruzar la escalera, abrir el inventario, preguntarle a un NPC.
//
// No forma parte del ejecutable: está en una carpeta Editor.
// ============================================================================

public static class MovUDepuracion
{
    private const string Menu = "MovU/Depuracion en Play/";
    private const float Velocidad = 3f;

    private static readonly List<Vector3> ruta = new List<Vector3>();
    private static int siguiente;
    private static string nombreDeLaRuta = "";
    private static float empezoEn;
    private static float metros;
    private static Vector3 ultima;
    private static float atascadoDesde = -1f;

    // Los récords ("Mejor tiempo") que había antes de la sesión automática. Una
    // caminata a 3 m/s en línea de ruta óptima batiría el récord de cualquier
    // persona, y ese número se le muestra a los participantes.
    private static readonly Dictionary<string, float> recordsDeAntes = new Dictionary<string, float>();
    private const string ClaveDeRecord = "MovU_BestTime_M_";

    [InitializeOnLoadMethod]
    private static void Enganchar()
    {
        EditorApplication.playModeStateChanged -= AlCambiarDeModo;
        EditorApplication.playModeStateChanged += AlCambiarDeModo;
    }

    private static void AlCambiarDeModo(PlayModeStateChange cambio)
    {
        if (cambio != PlayModeStateChange.ExitingPlayMode) return;
        Detener();
        RestaurarRecords();
    }

    private static void GuardarRecords()
    {
        if (recordsDeAntes.Count > 0) return;          // ya se guardaron en esta sesión
        ContenidoPiso datos = ContenidoLoader.Datos;
        if (datos == null) return;
        foreach (MisionDef m in datos.misiones)
        {
            if (!string.IsNullOrEmpty(m.id)) recordsDeAntes[m.id] = SaveSystem.LoadBestTime(m.id);
        }
    }

    private static void RestaurarRecords()
    {
        if (recordsDeAntes.Count == 0) return;
        foreach (KeyValuePair<string, float> r in recordsDeAntes)
        {
            if (r.Value >= 9999f) PlayerPrefs.DeleteKey(ClaveDeRecord + r.Key);
            else PlayerPrefs.SetFloat(ClaveDeRecord + r.Key, r.Value);
        }
        PlayerPrefs.Save();
        Debug.Log($"[Depuración] Récords restaurados ({recordsDeAntes.Count} misiones): la sesión " +
                  "automática no deja marca. Sus filas del CSV llevan el participante 'auto'.");
        recordsDeAntes.Clear();
    }

    private static bool EnPlay()
    {
        if (Application.isPlaying) return true;
        Debug.LogWarning("[Depuración] Esto solo funciona con el juego en Play.");
        return false;
    }

    private static PlayerController Jugador()
    {
        return Object.FindFirstObjectByType<PlayerController>();
    }

    // ------------------------------------------------------------------
    [MenuItem(Menu + "1 Empezar sesion")]
    private static void EmpezarSesion()
    {
        if (!EnPlay()) return;
        TestSession sesion = Object.FindFirstObjectByType<TestSession>();
        if (sesion == null) { Debug.LogWarning("[Depuración] No hay TestSession."); return; }
        GuardarRecords();
        sesion.Empezar("auto", 0);
        Debug.Log("[Depuración] Sesión pedida.");
    }

    [MenuItem(Menu + "2 Caminar hasta el destino de la mision")]
    private static void CaminarALaMision()
    {
        if (!EnPlay()) return;
        MissionManager misiones = MissionManager.Instance;
        PlayerController jugador = Jugador();
        if (misiones == null || jugador == null || misiones.MisionActual == null ||
            misiones.MisionActual.destino == null)
        {
            Debug.LogWarning("[Depuración] No hay misión en curso.");
            return;
        }

        Vector3 destino = misiones.MisionActual.destino.transform.position;
        if (!NavUtil.Ruta(jugador.transform.position, destino, out float largo))
        {
            Debug.LogWarning("[Depuración] No hay ruta por el NavMesh hasta el destino.");
            return;
        }

        var puntos = new List<Vector3>();
        for (int i = 1; i < NavUtil.CantidadDeEsquinas; i++) puntos.Add(NavUtil.Esquinas[i]);
        Empezar($"misión {misiones.MisionActual.def.id} ({largo:F1} m por el NavMesh)", puntos);
    }

    [MenuItem(Menu + "3 Siguiente mision")]
    private static void SiguienteMision()
    {
        if (!EnPlay() || MissionManager.Instance == null) return;
        MissionManager.Instance.IniciarSiguiente();
    }

    [MenuItem(Menu + "4 Ir al pie de la escalera")]
    private static void IrALaEscalera()
    {
        if (!EnPlay()) return;
        StairsManager escalera = StairsManager.Instance;
        PlayerController jugador = Jugador();
        if (escalera == null || !escalera.Disponible || jugador == null)
        {
            Debug.LogWarning("[Depuración] No hay escalera construida.");
            return;
        }
        Detener();
        int piso = FloorManager.Instance.PisoActual;
        jugador.Teletransportar(escalera.PieDelTramo(piso) + new Vector3(0f, 0f, -1.2f), 0f);
        Debug.Log("[Depuración] Jugador al pie de la escalera.");
    }

    [MenuItem(Menu + "5 Subir el tramo a pie")]
    private static void SubirElTramo()
    {
        if (!EnPlay()) return;
        StairsManager escalera = StairsManager.Instance;
        if (escalera == null || !escalera.Disponible) return;

        int piso = FloorManager.Instance.PisoActual;
        Vector3 pie = escalera.PieDelTramo(piso);
        Vector3 frente = escalera.FrenteDelRellano(piso);
        var puntos = new List<Vector3>
        {
            pie,
            new Vector3(pie.x, frente.y, frente.z - 2.6f),     // cima del tramo, ya sobre la losa
            new Vector3(pie.x, frente.y, frente.z),
            frente,
        };
        Empezar("subir el tramo", puntos);
    }

    [MenuItem(Menu + "6 Bajar el tramo a pie")]
    private static void BajarElTramo()
    {
        if (!EnPlay()) return;
        StairsManager escalera = StairsManager.Instance;
        if (escalera == null || !escalera.Disponible) return;

        int piso = FloorManager.Instance.PisoActual;
        Vector3 pie = escalera.PieDelTramo(piso);
        Vector3 frente = escalera.FrenteDelRellano(piso);
        var puntos = new List<Vector3>
        {
            new Vector3(pie.x, frente.y, frente.z),
            new Vector3(pie.x, frente.y, frente.z - 2.6f),
            pie,
            escalera.FrenteDelHall(piso),
        };
        Empezar("bajar el tramo", puntos);
    }

    [MenuItem(Menu + "7 Cruzar la puerta y subir un piso")]
    private static void Subir()
    {
        if (!EnPlay() || StairsManager.Instance == null) return;
        Detener();
        Debug.Log("[Depuración] Subir: " + (StairsManager.Instance.CruzarAhora(true) ? "hecho" : "no hay piso arriba"));
    }

    [MenuItem(Menu + "8 Cruzar la puerta y bajar un piso")]
    private static void Bajar()
    {
        if (!EnPlay() || StairsManager.Instance == null) return;
        Detener();
        Debug.Log("[Depuración] Bajar: " + (StairsManager.Instance.CruzarAhora(false) ? "hecho" : "no hay piso abajo"));
    }

    [MenuItem(Menu + "Usar la puerta de la escalera como con E")]
    private static void UsarPuerta()
    {
        if (!EnPlay() || StairsManager.Instance == null) return;
        Detener();
        Debug.Log("[Depuración] Usar la puerta: " +
                  (StairsManager.Instance.Usar() ? "cruzando" : "no hay puerta a mano o está cerrada"));
    }

    [MenuItem(Menu + "Ir al rellano mirando su puerta")]
    private static void IrAlRellano()
    {
        if (!EnPlay()) return;
        StairsManager escalera = StairsManager.Instance;
        PlayerController jugador = Jugador();
        if (escalera == null || !escalera.Disponible || jugador == null) return;
        Detener();
        int piso = FloorManager.Instance.PisoActual;
        jugador.Teletransportar(escalera.FrenteDelRellano(piso) + new Vector3(1.2f, 0f, 0.6f), 218f);
    }

    [MenuItem(Menu + "Ir al hall mirando la puerta de bajar")]
    private static void IrAlHall()
    {
        if (!EnPlay()) return;
        StairsManager escalera = StairsManager.Instance;
        PlayerController jugador = Jugador();
        if (escalera == null || !escalera.Disponible || jugador == null) return;
        Detener();
        int piso = FloorManager.Instance.PisoActual;
        jugador.Teletransportar(escalera.FrenteDelHall(piso) + new Vector3(0.6f, 0f, -1.4f), 345f);
    }

    /// <summary>
    /// Compara, desde donde está el jugador, la ruta por ascensor y por escalera
    /// hasta cada POI importante puesto en el piso de arriba. Sirve para revisar
    /// la ruta óptima entre pisos sin tener que escribir una misión de prueba.
    /// </summary>
    [MenuItem(Menu + "Comparar rutas al piso de arriba")]
    private static void CompararRutas()
    {
        if (!EnPlay()) return;
        FloorManager pisos = FloorManager.Instance;
        PlayerController jugador = Jugador();
        if (pisos == null || jugador == null || pisos.CantidadDePisos < 2) return;

        int desde = pisos.PisoActual;
        int hasta = desde + 1 < pisos.CantidadDePisos ? desde + 1 : desde - 1;
        float salto = pisos.AlturaDelSuelo(hasta) - pisos.AlturaDelSuelo(desde);
        Vector3 pies = jugador.transform.position;

        foreach (PointOfInterest poi in PointOfInterest.Todos)
        {
            if (poi == null || !poi.Importante) continue;
            Vector3 destino = poi.transform.position + Vector3.up * salto;

            bool hayOptima = NavUtil.RutaEntrePisos(pies, desde, destino, hasta, out float optima, out _);
            bool hayEscalera = NavUtil.RutaPorEscalera(pies, desde, destino, hasta, out float porEscalera);
            Vector3 entrada = NavUtil.EntradaParaCambiarDePiso(pies, desde, destino, hasta);

            Debug.Log($"[Depuración] {poi.Id} en el piso {hasta + 1} desde {pies.ToString("F1")}: " +
                      $"óptima {(hayOptima ? optima.ToString("F1") + " m" : "sin ruta")}, " +
                      $"por escalera {(hayEscalera ? porEscalera.ToString("F1") + " m" : "sin ruta")}, " +
                      $"la flecha manda a {entrada.ToString("F1")}.");
        }
    }

    [MenuItem(Menu + "9 Abrir o cerrar el inventario")]
    private static void Inventario()
    {
        if (!EnPlay() || InventoryManager.Instance == null) return;
        InventoryManager.Instance.AbrirPanel(!InventoryManager.Instance.PanelAbierto);
    }

    [MenuItem(Menu + "Preguntar al NPC mas cercano")]
    private static void Preguntar()
    {
        if (!EnPlay() || NpcManager.Instance == null) return;
        Debug.Log("[Depuración] Preguntar: " + (NpcManager.Instance.PreguntarAlMasCercano(30f) ? "hecho" : "no hay nadie"));
    }

    [MenuItem(Menu + "Alternar tercera persona")]
    private static void Camara()
    {
        if (!EnPlay()) return;
        CameraRig rig = Object.FindFirstObjectByType<CameraRig>();
        if (rig == null) return;
        rig.SetViewMode(rig.CurrentView == CameraRig.ViewMode.FirstPerson
            ? CameraRig.ViewMode.ThirdPerson
            : CameraRig.ViewMode.FirstPerson);
    }

    [MenuItem(Menu + "Estado a la consola")]
    private static void Estado()
    {
        if (!EnPlay()) return;
        PlayerController j = Jugador();
        FloorManager pisos = FloorManager.Instance;
        MissionManager m = MissionManager.Instance;
        var cc = j != null ? j.GetComponent<CharacterController>() : null;
        Debug.Log($"[Depuración] t={Time.time:F1} pos={(j != null ? j.transform.position.ToString("F2") : "-")} " +
                  $"suelo={(cc != null && cc.isGrounded)} piso={(pisos != null ? pisos.PisoActual : -1)} " +
                  $"misiones={(m != null ? m.Estado.ToString() : "-")} " +
                  $"caminando={(ruta.Count > 0 ? nombreDeLaRuta + " " + siguiente + "/" + ruta.Count : "no")} " +
                  $"puertaEscalera={StairsManager.JugadorEnPuerta} " +
                  $"inventario={(InventoryManager.Instance != null ? InventoryManager.Instance.Objetos.Count : -1)} " +
                  $"tris={(pisos != null ? FormasMovU.TriangulosEn(pisos.PisoActual) : 0)}");
    }

    [MenuItem(Menu + "Detener la caminata")]
    private static void Detener()
    {
        ruta.Clear();
        EditorApplication.update -= Avanzar;
    }

    // ------------------------------------------------------------------
    // Caminata automática
    // ------------------------------------------------------------------
    private static void Empezar(string nombre, List<Vector3> puntos)
    {
        Detener();
        PlayerController jugador = Jugador();
        if (jugador == null || puntos.Count == 0) return;

        ruta.AddRange(puntos);
        siguiente = 0;
        nombreDeLaRuta = nombre;
        empezoEn = Time.time;
        metros = 0f;
        ultima = jugador.transform.position;
        atascadoDesde = -1f;
        EditorApplication.update += Avanzar;
        Debug.Log($"[Depuración] Caminando: {nombre}, {puntos.Count} puntos.");
    }

    private static void Avanzar()
    {
        if (!Application.isPlaying || ruta.Count == 0)
        {
            Detener();
            return;
        }
        if (EditorApplication.isPaused) return;

        PlayerController jugador = Jugador();
        if (jugador == null) { Detener(); return; }
        var cc = jugador.GetComponent<CharacterController>();
        if (cc == null || !cc.enabled) return;

        Vector3 pos = jugador.transform.position;
        Vector3 objetivo = ruta[siguiente];
        Vector3 d = objetivo - pos;
        d.y = 0f;

        if (d.magnitude < 0.25f)
        {
            siguiente++;
            if (siguiente >= ruta.Count)
            {
                Debug.Log($"[Depuración] Llegó ({nombreDeLaRuta}) en {Time.time - empezoEn:F1} s, " +
                          $"{metros:F1} m caminados, pos={pos.ToString("F2")}.");
                Detener();
            }
            return;
        }

        // Solo el avance horizontal: la gravedad la sigue poniendo PlayerController.
        Vector3 dir = d.normalized;
        jugador.transform.rotation = Quaternion.Slerp(jugador.transform.rotation,
                                                      Quaternion.LookRotation(dir, Vector3.up), 0.2f);
        cc.Move(dir * (Velocidad * Time.deltaTime));

        Vector3 nueva = jugador.transform.position;
        Vector3 paso = nueva - ultima;
        paso.y = 0f;
        metros += paso.magnitude;

        // Si no avanza, se dice dónde se trabó en vez de quedarse empujando.
        if (paso.magnitude < 0.2f * Velocidad * Time.deltaTime)
        {
            if (atascadoDesde < 0f) atascadoDesde = Time.time;
            else if (Time.time - atascadoDesde > 2f)
            {
                Debug.LogWarning($"[Depuración] ATASCADO ({nombreDeLaRuta}) en {nueva.ToString("F2")}, " +
                                 $"yendo al punto {siguiente + 1}/{ruta.Count} {objetivo.ToString("F2")}.");
                Detener();
            }
        }
        else atascadoDesde = -1f;

        ultima = nueva;
    }
}
