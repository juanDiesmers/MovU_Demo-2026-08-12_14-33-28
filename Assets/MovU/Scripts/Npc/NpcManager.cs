using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// ============================================================================
// NpcManager.cs — Crea a la gente del piso y atiende las conversaciones
// ============================================================================
// Patrón Manager: un solo componente con UN Update para todos los NPC, en vez
// de un Update por personaje.
//
//  - Población: sale de contenido_piso9.json. Los NPC con puesto fijo (el
//    personal de DTI, un profesor, el vigilante) y una "multitud" de
//    estudiantes y visitantes que deambulan. La multitud usa una SEMILLA: con
//    la misma semilla todos los participantes de las pruebas ven a la misma
//    gente saliendo de los mismos sitios.
//  - Carga por piso: cuelgan de Contenido/Piso_N, así que el FloorManager los
//    apaga con su piso. Aquí solo se actualizan los que están activos.
//  - Conversación: con E, el NPC que el jugador tiene delante le dice hacia
//    dónde queda el destino de la misión y se gira para señalarlo. No da la
//    ruta completa: da lo que daría una persona.
//
// No llama al HUD: publica eventos (OnAvisoCambiado, OnNpcHabla) y el HUD se
// suscribe.
// ============================================================================

public class NpcManager : MonoBehaviour
{
    public static NpcManager Instance { get; private set; }

    public const string NombreDelGrupo = "NPCs (JSON)";

    private const float AlcanceDeCharla = 2.6f;
    private const float IntervaloDeBusqueda = 0.15f;

    /// <summary>Texto del aviso de interacción. Vacío = no hay nadie a quien preguntar.</summary>
    public event Action<string> OnAvisoCambiado;
    /// <summary>(quién, qué dijo).</summary>
    public event Action<string, string> OnNpcHabla;

    private readonly List<NpcCharacter> npcs = new List<NpcCharacter>();
    private bool danIndicaciones = true;

    private Transform jugador;
    private NpcCharacter candidato;
    private float proximaBusqueda;

    public int Cantidad => npcs.Count;

    /// <summary>Todos los personajes (también los de pisos apagados). Lo usan las puertas.</summary>
    public IReadOnlyList<NpcCharacter> Personajes => npcs;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Población
    // ------------------------------------------------------------------
    public void Poblar(ContenidoPiso datos, FloorManager pisos)
    {
        if (datos == null || pisos == null) return;

        danIndicaciones = datos.ajustes == null || datos.ajustes.npcDanIndicaciones;
        bool hayNavMesh = NavUtil.HayNavMesh;
        if (!hayNavMesh)
        {
            Debug.LogWarning("[NPC] No hay NavMesh: los NPC que deambulan se quedan quietos. " +
                             "Hornéalo con MovU > Juego > Hornear NavMesh.");
        }

        // --- Puestos fijos ---------------------------------------------
        for (int i = 0; i < datos.npcs.Count; i++)
        {
            NpcDef def = datos.npcs[i];
            if (def == null) continue;

            if (!ContenidoLoader.AMundo(pisos, def.piso, def.u, def.v, out Vector3 punto)) continue;

            if (!NpcCharacter.RolDesdeTexto(def.rol, out NpcRole rol))
            {
                Debug.LogWarning($"[NPC] Rol '{def.rol}' desconocido en '{def.id}'. Se usa Estudiante.");
                rol = NpcRole.Estudiante;
            }

            bool deambula = string.Equals(def.conducta, "deambula", StringComparison.OrdinalIgnoreCase);
            string id = string.IsNullOrEmpty(def.id) ? $"npc_{i:00}" : def.id;

            Crear(pisos, def.piso, id, rol, def.nombre, deambula && hayNavMesh, def.frases,
                  punto, def.yaw, RecorridoDelPiso(datos, pisos, def.piso), 1000 + i);
        }

        // --- Multitudes: la del piso 9 y una por cada uno de los demás pisos --
        var todas = new List<MultitudDef>();
        if (datos.multitud != null) todas.Add(datos.multitud);
        if (datos.multitudes != null) todas.AddRange(datos.multitudes);
        foreach (MultitudDef m in todas)
        {
            if (m != null) PoblarMultitud(datos, pisos, m, hayNavMesh);
        }

        Debug.Log($"[NPC] {npcs.Count} personajes creados en {todas.Count + 1} grupos " +
                  $"(~{NpcFigura.TriangulosDeReferencia} triángulos cada uno; solo se dibujan los del piso activo).");
    }

    private void PoblarMultitud(ContenidoPiso datos, FloorManager pisos, MultitudDef m, bool hayNavMesh)
    {
        if (m.estudiantes <= 0 && m.visitantes <= 0 && (m.parejas == null || m.parejas.Count == 0)) return;
        Vector3[] recorrido = (m.recorrido != null && m.recorrido.Count > 0)
            ? Recorrido(m.recorrido, pisos, m.piso)
            : Recorrido(datos.recorridos, pisos, m.piso);
        var azar = new System.Random(m.semilla);
        string prefijo = $"p{m.piso}_";

        if (recorrido.Length > 0)
        {
            int total = Mathf.Max(0, m.estudiantes) + Mathf.Max(0, m.visitantes);
            for (int i = 0; i < total; i++)
            {
                NpcRole rol = i < m.estudiantes ? NpcRole.Estudiante : NpcRole.Visitante;
                Vector3 punto = recorrido[azar.Next(recorrido.Length)];
                punto.x += (float)(azar.NextDouble() - 0.5) * 1.6f;
                punto.z += (float)(azar.NextDouble() - 0.5) * 1.6f;
                float yaw = (float)azar.NextDouble() * 360f;
                Crear(pisos, m.piso, $"{prefijo}{(rol == NpcRole.Estudiante ? "est" : "vis")}_{i:00}",
                      rol, "", hayNavMesh, null, punto, yaw, recorrido, m.semilla * 31 + i);
            }
        }
        else if (m.estudiantes + m.visitantes > 0)
        {
            Debug.LogWarning($"[NPC] La multitud del piso {m.piso} no tiene recorrido.");
        }

        // Parejas que conversan: dos estudiantes quietos, frente a frente.
        if (m.parejas == null) return;
        for (int k = 0; k < m.parejas.Count; k++)
        {
            PuntoUV c = m.parejas[k];
            if (c == null || !ContenidoLoader.AMundo(pisos, m.piso, c.u, c.v, out Vector3 centro)) continue;
            float giro = (float)azar.NextDouble() * 180f;
            Vector3 lado = Quaternion.Euler(0f, giro, 0f) * Vector3.right * 0.55f;
            Crear(pisos, m.piso, $"{prefijo}charla_{k}a", NpcRole.Estudiante, "", false, null,
                  centro - lado, giro + 90f, null, m.semilla * 57 + k * 2);
            Crear(pisos, m.piso, $"{prefijo}charla_{k}b", NpcRole.Estudiante, "", false, null,
                  centro + lado, giro - 90f, null, m.semilla * 57 + k * 2 + 1);
        }
    }

    /// <summary>Por dónde caminan los NPC de un piso: el recorrido de su multitud o, si no hay, el general.</summary>
    private static Vector3[] RecorridoDelPiso(ContenidoPiso datos, FloorManager pisos, int pisoDesdeUno)
    {
        if (datos.multitudes != null)
            foreach (MultitudDef m in datos.multitudes)
                if (m != null && m.piso == pisoDesdeUno && m.recorrido != null && m.recorrido.Count > 0)
                    return Recorrido(m.recorrido, pisos, pisoDesdeUno);
        return Recorrido(datos.recorridos, pisos, pisoDesdeUno);
    }

    private static Vector3[] Recorrido(List<PuntoUV> lista, FloorManager pisos, int pisoDesdeUno)
    {
        var puntos = new List<Vector3>(lista.Count);
        for (int i = 0; i < lista.Count; i++)
        {
            PuntoUV uv = lista[i];
            if (uv != null && ContenidoLoader.AMundo(pisos, pisoDesdeUno, uv.u, uv.v, out Vector3 p))
            {
                puntos.Add(p);
            }
        }
        return puntos.ToArray();
    }

    private void Crear(FloorManager pisos, int pisoDesdeUno, string id, NpcRole rol, string nombre,
                       bool deambula, List<string> frases, Vector3 punto, float yaw,
                       Vector3[] recorrido, int semilla)
    {
        int indice = Mathf.Clamp(pisoDesdeUno - 1, 0, pisos.CantidadDePisos - 1);
        Transform grupo = ContenidoLoader.Grupo(pisos.ContenidoDelPiso(indice), NombreDelGrupo);
        if (grupo == null) return;

        // Un NavMeshAgent creado lejos del NavMesh llena la consola de errores:
        // si el punto no cae en zona caminable, el personaje se queda quieto.
        if (deambula)
        {
            if (NavUtil.Muestrear(punto, out Vector3 sobreElNavMesh, 3f)) punto = sobreElNavMesh;
            else deambula = false;
        }

        var azar = new System.Random(semilla);

        var go = new GameObject("NPC_" + id);
        go.transform.SetParent(grupo, false);
        go.transform.SetPositionAndRotation(punto, Quaternion.Euler(0f, yaw, 0f));

        float talla = 0.93f + (float)azar.NextDouble() * 0.1f;      // de 1,67 a 1,85 m
        NpcFigura figura = NpcFigura.Construir(go.transform, AparienciaDe(rol, azar), talla);

        NavMeshAgent agente = null;
        if (deambula)
        {
            agente = go.AddComponent<NavMeshAgent>();
            agente.radius = 0.25f;
            agente.height = 1.75f;
            agente.speed = rol == NpcRole.Visitante
                ? 0.65f + (float)azar.NextDouble() * 0.25f           // pasean, mirando
                : 1.0f + (float)azar.NextDouble() * 0.45f;
            agente.angularSpeed = 260f;
            agente.acceleration = 6f;
            agente.stoppingDistance = 0.4f;
            agente.autoBraking = true;
            agente.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agente.avoidancePriority = 30 + azar.Next(60);
        }

        var npc = go.AddComponent<NpcCharacter>();
        npc.Configurar(id, rol, nombre, deambula ? NpcConducta.Deambula : NpcConducta.Quieto,
                       frases, figura, agente, recorrido, semilla);
        npcs.Add(npc);
    }

    // ------------------------------------------------------------------
    // Colores (uno por material compartido; pocos a propósito)
    // ------------------------------------------------------------------
    private static readonly Color32[] Piel =
    {
        new Color32(241, 204, 170, 255), new Color32(214, 164, 124, 255),
        new Color32(172, 120, 84, 255), new Color32(116, 78, 56, 255),
    };

    private static readonly Color32[] Pantalon =
    {
        new Color32(48, 58, 82, 255), new Color32(64, 64, 68, 255), new Color32(104, 88, 70, 255),
    };

    private static readonly Color32[] RopaDeEstudiante =
    {
        new Color32(214, 76, 68, 255), new Color32(238, 182, 60, 255), new Color32(86, 160, 96, 255),
        new Color32(96, 140, 214, 255), new Color32(160, 110, 190, 255), new Color32(232, 232, 226, 255),
    };

    private static readonly Color32[] Pelo =
    {
        new Color32(24, 20, 18, 255), new Color32(46, 32, 24, 255), new Color32(84, 56, 34, 255),
        new Color32(132, 96, 58, 255), new Color32(30, 26, 24, 255),
    };

    private static readonly Color32[] Jean =
    {
        new Color32(52, 70, 104, 255), new Color32(38, 48, 72, 255), new Color32(30, 30, 34, 255),
        new Color32(170, 150, 118, 255), new Color32(84, 88, 92, 255),
    };

    private static readonly Color32[] Mochila =
    {
        new Color32(26, 26, 30, 255), new Color32(40, 52, 86, 255), new Color32(120, 40, 42, 255),
        new Color32(70, 86, 74, 255), new Color32(96, 96, 100, 255),
    };

    private static T Uno<T>(T[] lista, System.Random azar) => lista[azar.Next(lista.Length)];

    /// <summary>Cómo se ve cada quien según su rol (con algo de azar, siempre igual para la misma semilla).</summary>
    public static Apariencia AparienciaDe(NpcRole rol, System.Random azar)
    {
        var a = new Apariencia
        {
            ropa = Uno(RopaDeEstudiante, azar),
            pantalon = Uno(Jean, azar),
            piel = Uno(Piel, azar),
            pelo = Uno(Pelo, azar),
            zapatos = azar.NextDouble() < 0.5 ? new Color32(236, 236, 232, 255) : new Color32(34, 32, 32, 255),
            accesorio = Uno(Mochila, azar),
            peinado = (Peinado)azar.Next(4),
            extra = AccesorioNpc.Ninguno,
            complexion = (float)azar.NextDouble(),
            mangaCorta = azar.NextDouble() < 0.4,
        };

        switch (rol)
        {
            case NpcRole.Estudiante:
                if (azar.NextDouble() < 0.7) a.extra = AccesorioNpc.Mochila;
                break;
            case NpcRole.Profesor:
                a.ropa = Uno(new[] { new Color32(70, 78, 96, 255), new Color32(92, 74, 58, 255), new Color32(58, 60, 64, 255) }, azar);
                a.accesorio = Uno(new[] { new Color32(236, 236, 232, 255), new Color32(186, 206, 232, 255) }, azar);
                a.pantalon = new Color32(44, 46, 54, 255);
                a.zapatos = new Color32(58, 40, 30, 255);
                a.extra = AccesorioNpc.Saco;
                a.peinado = azar.NextDouble() < 0.5 ? Peinado.Corto : Peinado.Rapado;
                if (azar.NextDouble() < 0.35) a.pelo = new Color32(150, 148, 144, 255);       // canas
                break;
            case NpcRole.SoporteDTI:
                a.ropa = new Color32(0, 134, 150, 255);
                a.accesorio = new Color32(30, 78, 154, 255);
                a.extra = AccesorioNpc.Carne;
                a.mangaCorta = true;
                break;
            case NpcRole.Administrativo:
                a.ropa = Uno(new[] { new Color32(132, 52, 70, 255), new Color32(64, 92, 130, 255), new Color32(230, 226, 214, 255) }, azar);
                a.accesorio = new Color32(30, 78, 154, 255);
                a.pantalon = new Color32(44, 46, 54, 255);
                a.extra = AccesorioNpc.Carne;
                break;
            case NpcRole.Vigilante:
                a.ropa = new Color32(28, 36, 60, 255);
                a.pantalon = new Color32(24, 28, 42, 255);
                a.accesorio = new Color32(20, 24, 36, 255);
                a.zapatos = new Color32(20, 20, 22, 255);
                a.extra = AccesorioNpc.Gorra;
                a.mangaCorta = false;
                a.complexion = 0.7f + (float)azar.NextDouble() * 0.3f;
                break;
            case NpcRole.Aseo:
                a.ropa = new Color32(60, 130, 96, 255);
                a.pantalon = new Color32(48, 100, 76, 255);
                a.accesorio = new Color32(120, 176, 140, 255);
                a.extra = AccesorioNpc.Delantal;
                a.peinado = azar.NextDouble() < 0.6 ? Peinado.Coleta : Peinado.Corto;
                break;
            case NpcRole.Visitante:
                a.ropa = Uno(new[] { new Color32(226, 140, 76, 255), new Color32(206, 190, 150, 255),
                                     new Color32(110, 150, 120, 255), new Color32(196, 92, 110, 255) }, azar);
                a.accesorio = Uno(new[] { new Color32(80, 56, 40, 255), new Color32(30, 30, 30, 255) }, azar);
                a.extra = AccesorioNpc.Bolso;
                if (azar.NextDouble() < 0.3) a.pelo = new Color32(150, 148, 144, 255);
                break;
        }
        return a;
    }

    // ------------------------------------------------------------------
    // Cada frame
    // ------------------------------------------------------------------
    private void Update()
    {
        float ahora = Time.time;

        for (int i = 0; i < npcs.Count; i++)
        {
            NpcCharacter n = npcs[i];
            if (n != null && n.gameObject.activeInHierarchy) n.Tick(ahora);
        }

        if (jugador == null)
        {
            if (ahora < proximaBusqueda) return;
            proximaBusqueda = ahora + 0.5f;
            GameObject go = GameObject.FindWithTag("Player");
            if (go == null) return;
            jugador = go.transform;
        }

        if (ahora >= proximaBusqueda)
        {
            proximaBusqueda = ahora + IntervaloDeBusqueda;
            BuscarCandidato();
        }

        if (candidato != null && !StairsManager.JugadorEnPuerta && Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            Hablar(candidato, ahora);
        }
    }

    /// <summary>El NPC activo más cercano que el jugador tiene delante y a mano.</summary>
    private void BuscarCandidato()
    {
        NpcCharacter mejor = null;

        // Junto al ascensor la tecla E es del ascensor; y antes de empezar la
        // sesión no hay a quién preguntarle nada.
        // Lo mismo junto a una puerta de la escalera.
        if (!ElevatorTrigger.JugadorEnAlgunaZona && !StairsManager.JugadorEnPuerta &&
            !TestSession.EsperandoInicio)
        {
            Vector3 pos = jugador.position;
            Vector3 frente = jugador.forward;
            float mejorDistancia = AlcanceDeCharla * AlcanceDeCharla;

            for (int i = 0; i < npcs.Count; i++)
            {
                NpcCharacter n = npcs[i];
                if (n == null || !n.gameObject.activeInHierarchy) continue;

                Vector3 d = n.transform.position - pos;
                if (Mathf.Abs(d.y) > 1.5f) continue;
                d.y = 0f;

                float d2 = d.sqrMagnitude;
                if (d2 > mejorDistancia) continue;
                if (d2 > 0.5f && Vector3.Dot(d.normalized, frente) < 0.25f) continue;   // está a la espalda

                mejorDistancia = d2;
                mejor = n;
            }
        }

        if (mejor == candidato) return;
        candidato = mejor;
        OnAvisoCambiado?.Invoke(mejor != null ? $"E  —  Preguntar a {mejor.Nombre}" : "");
    }

    /// <summary>
    /// Para las pruebas y el menú de depuración: le pregunta al NPC activo más
    /// cercano al jugador, esté o no de frente. Devuelve false si no hay ninguno
    /// a menos de 'alcance' metros.
    /// </summary>
    public bool PreguntarAlMasCercano(float alcance)
    {
        if (jugador == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go == null) return false;
            jugador = go.transform;
        }

        NpcCharacter mejor = null;
        float mejorDistancia = alcance * alcance;
        for (int i = 0; i < npcs.Count; i++)
        {
            NpcCharacter n = npcs[i];
            if (n == null || !n.gameObject.activeInHierarchy) continue;
            float d2 = (n.transform.position - jugador.position).sqrMagnitude;
            if (d2 < mejorDistancia) { mejorDistancia = d2; mejor = n; }
        }
        if (mejor == null) return false;

        Hablar(mejor, Time.time);
        return true;
    }

    private void Hablar(NpcCharacter npc, float ahora)
    {
        MissionManager misiones = MissionManager.Instance;
        MisionEnCurso mision = (misiones != null &&
                                misiones.Estado == MissionManager.EstadoDeMisiones.EnCurso)
            ? misiones.MisionActual
            : null;

        string frase;
        Vector3 mirarA = jugador.position;

        if (mision != null && mision.destino != null && npc.Rol == NpcRole.Visitante)
        {
            // Los visitantes no saben dónde quedan las cosas: están conociendo el edificio.
            // Se cuenta como consulta (el jugador sí preguntó), aunque no le sirva.
            frase = npc.NoSeDondeQueda(mision.destino.Nombre);
            misiones.RegistrarConsultaANpc();
        }
        else if (mision != null && mision.destino != null && danIndicaciones)
        {
            frase = Indicaciones(npc, mision, ref mirarA);
            misiones.RegistrarConsultaANpc();
        }
        else
        {
            frase = npc.FrasePropia();
        }

        npc.AtenderYMirar(mirarA, 5f, ahora);
        OnNpcHabla?.Invoke(npc.Nombre, frase);
    }

    /// <summary>
    /// Lo que diría una persona: hacia dónde queda, más o menos a cuánto y de
    /// qué lado. 'mirarA' sale con el punto hacia el que el NPC se gira.
    /// </summary>
    private static string Indicaciones(NpcCharacter npc, MisionEnCurso mision, ref Vector3 mirarA)
    {
        string lugar = mision.destino.Nombre;
        Vector3 aqui = npc.transform.position;
        Vector3 destino = mision.destino.transform.position;

        FloorManager pisos = FloorManager.Instance;
        if (pisos != null)
        {
            int pisoDelNpc = pisos.PisoSegunAltura(aqui.y + 0.5f);
            if (pisoDelNpc != mision.pisoDeDestino)
            {
                ElevatorTrigger ascensor = ElevatorTrigger.MasCercano(pisoDelNpc, aqui);
                if (ascensor != null) mirarA = ascensor.PuntoDeAcceso;
                string verbo = mision.pisoDeDestino > pisoDelNpc ? "sube" : "baja";
                return $"{lugar} no es en este piso: toma el ascensor y {verbo} al piso " +
                       $"{FloorManager.NumeroVisible(mision.pisoDeDestino)}.";
            }
        }

        if (!NavUtil.Ruta(aqui, destino, out float longitud))
        {
            return $"¿{lugar}? Sé que es en este piso, pero no sabría decirte por dónde.";
        }

        Vector3[] esquinas = NavUtil.Esquinas;
        int n = NavUtil.CantidadDeEsquinas;

        // Hacia dónde señala: la primera esquina que no esté encima del NPC.
        for (int i = 1; i < n; i++)
        {
            Vector3 d = esquinas[i] - aqui;
            d.y = 0f;
            if (d.sqrMagnitude > 2.25f || i == n - 1)
            {
                mirarA = esquinas[i];
                break;
            }
        }

        if (longitud < 8f)
        {
            return $"{lugar} es aquí mismo, a unos pasos.";
        }

        // De qué lado queda: el giro entre el tramo más largo del camino (el
        // pasillo) y lo que falta desde el final de ese tramo hasta el destino.
        string lado = "";
        int largo = -1;
        float mayor = 0f;
        for (int i = 0; i < n - 1; i++)
        {
            float tramo = (esquinas[i + 1] - esquinas[i]).sqrMagnitude;
            if (tramo > mayor) { mayor = tramo; largo = i; }
        }
        if (largo >= 0)
        {
            Vector3 pasillo = esquinas[largo + 1] - esquinas[largo];
            Vector3 resto = destino - esquinas[largo + 1];
            pasillo.y = 0f;
            resto.y = 0f;
            if (resto.sqrMagnitude > 2.25f && pasillo.sqrMagnitude > 1f)
            {
                float giro = Vector3.SignedAngle(pasillo, resto, Vector3.up);
                if (giro > 25f) lado = ", a mano derecha";
                else if (giro < -25f) lado = ", a mano izquierda";
            }
        }

        int metros = Mathf.Max(10, Mathf.RoundToInt(longitud / 10f) * 10);
        return $"{lugar} queda por allá, a unos {metros} metros{lado}.";
    }
}
