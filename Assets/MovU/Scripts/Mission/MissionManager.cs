using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// ============================================================================
// MissionManager.cs — Gestor de misiones y cronómetro
// ============================================================================
// Tiene dos formas de trabajar, y elige sola:
//
//  - LEGADO (DemoMaze): un único objetivo con un ObjectiveTrigger. Es
//    exactamente el comportamiento de siempre.
//  - CATÁLOGO (Edificio): una lista de misiones que llega como datos desde
//    contenido_piso9.json. Una sola misión activa a la vez; cada una lleva a un
//    POI; se completa al entrar en su radio de llegada (1,5 m).
//
// En modo catálogo no hay colliders de por medio: cada frame se mide UNA
// distancia, la del jugador al POI de la misión activa.
// ============================================================================

/// <summary>Estado y medidas de la misión que se está jugando.</summary>
public class MisionEnCurso
{
    public MisionDef def;
    public int indice;                 // contando desde 0
    public int total;
    public PointOfInterest destino;

    public int pisoDeInicio;
    public int pisoDeDestino;
    public Vector3 puntoDeInicio;
    public float yawDeInicio;

    public float rutaOptima;           // metros, ya descontado el radio de llegada
    public bool rutaValida;            // false = no hubo NavMesh y se usó la línea recta
    public bool rutaConAscensor;
    public float radioDeLlegada;

    public int cambiosDePiso;
    public int pisosEquivocados;
    public int consultasANpc;
    public float tiempo;
    public bool completada;
}

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    public enum EstadoDeMisiones { Legado, Esperando, EnCurso, EntreMisiones, Terminado }

    [Header("Objetivo")]
    [SerializeField] private Objective activeObjective;

    /// <summary>Modo legado (DemoMaze): se completó el único objetivo.</summary>
    public event Action<Objective> OnObjectiveCompleted;

    /// <summary>Modo catálogo: empezó una misión (también al reiniciarla con R).</summary>
    public event Action<MisionEnCurso> OnMissionStarted;
    /// <summary>Modo catálogo: el jugador llegó al POI de la misión.</summary>
    public event Action<MisionEnCurso> OnMissionCompleted;
    /// <summary>Modo catálogo: no quedan misiones.</summary>
    public event Action OnAllMissionsCompleted;

    private float elapsedTime = 0f;
    private bool isTimerRunning = false;
    private float optimalPathLength = 0f;
    private bool isNavMeshValid = false;
    private float goalCaptureRadius = 0f;

    private readonly List<MisionDef> catalogo = new List<MisionDef>();
    private MisionEnCurso actual;
    private EstadoDeMisiones estado = EstadoDeMisiones.Legado;
    private Transform jugador;
    private FloorManager pisosSuscritos;

    public float ElapsedTime => elapsedTime;
    public float OptimalPathLength => optimalPathLength;
    public Objective ActiveObjective => activeObjective;
    public bool IsTimerRunning => isTimerRunning;
    public bool IsNavMeshValid => isNavMeshValid;
    public float GoalCaptureRadius => goalCaptureRadius;

    public bool ModoCatalogo => estado != EstadoDeMisiones.Legado;
    public EstadoDeMisiones Estado => estado;
    public MisionEnCurso MisionActual => actual;
    public int TotalDeMisiones => catalogo.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Pasa el gestor a modo catálogo. Hay que llamarlo antes de Start (lo hace
    /// MovUBootstrap justo después de crear el componente). Las misiones no
    /// arrancan solas: espera a <see cref="IniciarSiguiente"/>.
    /// </summary>
    public void ConfigurarCatalogo(List<MisionDef> misiones)
    {
        catalogo.Clear();
        if (misiones != null) catalogo.AddRange(misiones);
        estado = EstadoDeMisiones.Esperando;
        actual = null;
        isTimerRunning = false;
        elapsedTime = 0f;
    }

    private void Start()
    {
        if (ModoCatalogo)
        {
            SuscribirPisos();
            return;
        }

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
        if (pisosSuscritos != null) pisosSuscritos.OnPisoCambiado -= AlCambiarDePiso;

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

        if (!ModoCatalogo) return;

        if (estado == EstadoDeMisiones.EnCurso)
        {
            RevisarLlegada();
        }
        else if (estado == EstadoDeMisiones.EntreMisiones && Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                IniciarSiguiente();
            }
        }
    }

    // ------------------------------------------------------------------
    // Modo catálogo
    // ------------------------------------------------------------------

    /// <summary>Empieza la primera misión, o la que sigue a la que se acaba de completar.</summary>
    public void IniciarSiguiente()
    {
        if (!ModoCatalogo) return;
        if (estado == EstadoDeMisiones.EnCurso) return;

        int siguiente = (actual == null) ? 0 : actual.indice + 1;
        Iniciar(siguiente, false);
    }

    /// <summary>R: vuelve al punto de partida de la misión en curso, con tiempo y distancia en cero.</summary>
    public void ReiniciarMision()
    {
        if (!ModoCatalogo || actual == null) return;
        Iniciar(actual.indice, true);
    }

    private void Iniciar(int indice, bool esReinicio)
    {
        // Salta las misiones cuyo POI no exista, en vez de trabar la sesión.
        PointOfInterest destino = null;
        while (indice < catalogo.Count)
        {
            destino = PointOfInterest.Buscar(catalogo[indice].poi);
            if (destino != null) break;
            Debug.LogWarning($"[MissionManager] La misión '{catalogo[indice].id}' apunta al POI " +
                             $"'{catalogo[indice].poi}', que no existe. Se salta.");
            indice++;
        }

        if (indice >= catalogo.Count || destino == null)
        {
            estado = EstadoDeMisiones.Terminado;
            isTimerRunning = false;
            Debug.Log("[MissionManager] No quedan misiones.");
            OnAllMissionsCompleted?.Invoke();
            return;
        }

        BuscarJugador();
        MisionDef def = catalogo[indice];
        FloorManager pisos = FloorManager.Instance;

        var mision = new MisionEnCurso
        {
            def = def,
            indice = indice,
            total = catalogo.Count,
            destino = destino,
            pisoDeDestino = destino.Piso,
            radioDeLlegada = destino.RadioDeLlegada,
        };

        // --- Punto de partida ------------------------------------------
        if (esReinicio && actual != null && actual.indice == indice)
        {
            mision.puntoDeInicio = actual.puntoDeInicio;
            mision.yawDeInicio = actual.yawDeInicio;
            mision.pisoDeInicio = actual.pisoDeInicio;
            LlevarJugador(mision.puntoDeInicio, mision.yawDeInicio, mision.pisoDeInicio);
        }
        else
        {
            if (ResolverPartida(def.partida, out Vector3 punto, out float yaw, out int piso))
            {
                LlevarJugador(punto, yaw, piso);
            }

            mision.puntoDeInicio = jugador != null ? jugador.position : Vector3.zero;
            mision.yawDeInicio = jugador != null ? jugador.eulerAngles.y : 0f;
            mision.pisoDeInicio = (pisos != null && jugador != null)
                ? pisos.PisoSegunAltura(jugador.position.y)
                : 0;
        }

        // --- Ruta óptima: UNA sola vez, al empezar -----------------------
        CalcularRutaOptima(mision);

        actual = mision;
        activeObjective = new Objective(def.id, def.enunciado, destino.transform);
        optimalPathLength = mision.rutaOptima;
        isNavMeshValid = mision.rutaValida;
        goalCaptureRadius = mision.radioDeLlegada;

        elapsedTime = 0f;
        isTimerRunning = true;
        estado = EstadoDeMisiones.EnCurso;

        Debug.Log($"[MissionManager] Misión {indice + 1}/{catalogo.Count} '{def.id}': " +
                  $"{def.enunciado}  ->  {destino.Nombre} (piso {mision.pisoDeDestino + 1}). " +
                  $"Ruta óptima {mision.rutaOptima:F1} m" +
                  (mision.rutaValida ? "" : " (SIN NavMesh: línea recta)") +
                  (mision.rutaConAscensor ? ", con ascensor." : "."));

        OnMissionStarted?.Invoke(mision);
    }

    private void CalcularRutaOptima(MisionEnCurso mision)
    {
        Vector3 desde = mision.puntoDeInicio;
        Vector3 hasta = mision.destino.transform.position;

        if (NavUtil.RutaEntrePisos(desde, mision.pisoDeInicio, hasta, mision.pisoDeDestino,
                                   out float longitud, out bool conAscensor))
        {
            // El jugador completa la misión cuando su centro está a 'radio' del
            // POI, no encima: ese tramo final no lo camina, así que se descuenta.
            mision.rutaOptima = Mathf.Max(0.1f, longitud - mision.radioDeLlegada);
            mision.rutaValida = true;
            mision.rutaConAscensor = conAscensor;
        }
        else
        {
            Vector3 d = hasta - desde;
            d.y = 0f;
            mision.rutaOptima = Mathf.Max(0.1f, d.magnitude - mision.radioDeLlegada);
            mision.rutaValida = false;
            mision.rutaConAscensor = mision.pisoDeInicio != mision.pisoDeDestino;
            Debug.LogError("[MissionManager] No hay ruta por el NavMesh hasta '" +
                           mision.destino.Nombre + "'. El índice de desvío y el SPL de esta " +
                           "misión NO son válidos. Hornea el NavMesh: MovU > Juego > Hornear NavMesh.");
        }
    }

    private void RevisarLlegada()
    {
        if (actual == null || actual.destino == null) return;
        if (jugador == null)
        {
            BuscarJugador();
            if (jugador == null) return;
        }

        Vector3 d = jugador.position - actual.destino.transform.position;
        if (Mathf.Abs(d.y) > 2.5f) return;              // está en otro piso
        d.y = 0f;

        float r = actual.radioDeLlegada;
        if (d.sqrMagnitude > r * r) return;

        isTimerRunning = false;
        actual.tiempo = elapsedTime;
        actual.completada = true;

        bool quedan = false;
        for (int i = actual.indice + 1; i < catalogo.Count; i++)
        {
            if (PointOfInterest.Buscar(catalogo[i].poi) != null) { quedan = true; break; }
        }
        estado = quedan ? EstadoDeMisiones.EntreMisiones : EstadoDeMisiones.Terminado;

        Debug.Log($"[MissionManager] ¡Misión '{actual.def.id}' completada en {elapsedTime:F2} s!");
        OnMissionCompleted?.Invoke(actual);

        if (!quedan) OnAllMissionsCompleted?.Invoke();
    }

    /// <summary>La llama el NpcManager cada vez que el jugador le pide indicaciones a alguien.</summary>
    public void RegistrarConsultaANpc()
    {
        if (actual != null && estado == EstadoDeMisiones.EnCurso) actual.consultasANpc++;
    }

    private void AlCambiarDePiso(int anterior, int nuevo)
    {
        if (actual == null || estado != EstadoDeMisiones.EnCurso) return;

        actual.cambiosDePiso++;
        // "Piso equivocado": llegó a un piso que no es el del destino.
        if (nuevo != actual.pisoDeDestino) actual.pisosEquivocados++;
    }

    private void SuscribirPisos()
    {
        FloorManager pisos = FloorManager.Instance;
        if (pisos == null || pisos == pisosSuscritos) return;
        if (pisosSuscritos != null) pisosSuscritos.OnPisoCambiado -= AlCambiarDePiso;
        pisos.OnPisoCambiado += AlCambiarDePiso;
        pisosSuscritos = pisos;
    }

    private void BuscarJugador()
    {
        if (jugador != null) return;
        GameObject go = GameObject.FindWithTag("Player");
        if (go != null) jugador = go.transform;
    }

    private bool ResolverPartida(string partida, out Vector3 punto, out float yaw, out int piso)
    {
        punto = Vector3.zero;
        yaw = 0f;
        piso = 0;
        if (string.IsNullOrEmpty(partida)) return false;

        if (string.Equals(partida, "aparicion", StringComparison.OrdinalIgnoreCase))
        {
            if (!ContenidoLoader.HayAparicion) return false;
            punto = ContenidoLoader.PuntoDeAparicion;
            yaw = ContenidoLoader.YawDeAparicion;
            piso = ContenidoLoader.PisoDeAparicion;
            return true;
        }

        PointOfInterest poi = PointOfInterest.Buscar(partida);
        if (poi == null)
        {
            Debug.LogWarning($"[MissionManager] Punto de partida '{partida}' desconocido; " +
                             "la misión empieza donde está el jugador.");
            return false;
        }

        punto = poi.transform.position;
        yaw = jugador != null ? jugador.eulerAngles.y : 0f;
        piso = poi.Piso;
        return true;
    }

    private void LlevarJugador(Vector3 pies, float yaw, int piso)
    {
        if (jugador == null) return;

        // Primero el piso: hay que encenderlo antes de soltar al jugador encima.
        FloorManager pisos = FloorManager.Instance;
        if (pisos != null && pisos.CantidadDePisos > 0 &&
            pisos.PisoSegunAltura(jugador.position.y) != piso)
        {
            pisos.LlevarAlPiso(piso);
        }

        var pc = jugador.GetComponent<PlayerController>();
        if (pc != null) pc.Teletransportar(pies, yaw);
        else jugador.SetPositionAndRotation(pies, Quaternion.Euler(0f, yaw, 0f));
    }

    // ------------------------------------------------------------------
    // Modo legado (DemoMaze)
    // ------------------------------------------------------------------
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
                Vector3[] corners = path.corners;      // una sola copia, no una por vuelta
                float rawLength = 0f;
                for (int i = 0; i < corners.Length - 1; i++)
                {
                    rawLength += Vector3.Distance(corners[i], corners[i + 1]);
                }

                // Hallazgo #6 (corregido): descontar el radio REAL de captura del objetivo.
                // Antes se restaba una constante de 0.6 m que no coincidía con el
                // SphereCollider de 1.5 m creado por DemoSceneBuilder, lo que sesgaba
                // sistemáticamente detourRatio y SPL.
                goalCaptureRadius = MeasureGoalCaptureRadius();
                optimalPathLength = Mathf.Max(0.1f, rawLength - goalCaptureRadius);
                Debug.Log($"[MissionManager] Ruta óptima NavMesh: {optimalPathLength:F2} m (bruta {rawLength:F2} m − radio de captura {goalCaptureRadius:F2} m).");
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

    /// <summary>
    /// Mide el radio efectivo de captura del objetivo: el radio del SphereCollider
    /// del Goal ya escalado a mundo, más el radio de la cápsula del jugador.
    /// Es la distancia real que separa al jugador del centro del objetivo en el
    /// instante en que dispara OnTriggerEnter.
    /// </summary>
    private float MeasureGoalCaptureRadius()
    {
        float triggerRadius = 0f;

        if (activeObjective != null && activeObjective.target != null)
        {
            SphereCollider sphere = activeObjective.target.GetComponent<SphereCollider>();
            if (sphere != null)
            {
                Vector3 scale = sphere.transform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                triggerRadius = sphere.radius * maxScale;
            }
        }

        float playerRadius = 0f;
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            CharacterController cc = playerObj.GetComponent<CharacterController>();
            if (cc != null) playerRadius = cc.radius;
        }

        if (triggerRadius <= 0f)
        {
            Debug.LogWarning("[MissionManager] No se encontró SphereCollider en el objetivo; no se aplica corrección de radio.");
        }

        return triggerRadius + playerRadius;
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
        if (ModoCatalogo) return;          // en catálogo la llegada se mide por distancia
        if (!isTimerRunning) return;

        StopTimer();
        Debug.Log($"[MissionManager] ¡Objetivo '{activeObjective?.displayName}' completado en {elapsedTime:F2}s!");
        OnObjectiveCompleted?.Invoke(activeObjective);
    }
}
