using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// ElevatorTrigger.cs — Las puertas de ascensor que cambian de piso
// ============================================================================
// Una zona delante de cada puerta de ascensor del lobby (21 oct 2026; antes era
// un cubo suelto en medio del lobby). El ascensor sale por la misma puerta en
// el piso de destino. Cuando el jugador entra en su radio, aparece el
// aviso; con E se abre el panel de pisos.
//
// Detecta al jugador por su CharacterController en vez de por tag, porque la
// escena no tiene el tag 'Player' asignado y un tag mal puesto falla en
// silencio, que es la peor forma de fallar.
// ============================================================================

[RequireComponent(typeof(Collider))]
public class ElevatorTrigger : MonoBehaviour
{
    [SerializeField] private FloorManager pisos;
    [Tooltip("Cuál de las puertas del lobby es (0, 1, 2...). El ascensor sale por la misma puerta en el piso de destino.")]
    [SerializeField] private int puerta = -1;

    private bool jugadorCerca;

    /// <summary>Número de puerta en el lobby; -1 = el ascensor viejo de un solo cubo.</summary>
    public int Puerta => puerta;

    /// <summary>La zona en la que está parado el jugador (null si en ninguna).</summary>
    public static ElevatorTrigger EnUso { get; private set; }

    /// <summary>Lo usa BuildingSetup al crear la zona de cada puerta.</summary>
    public void Configurar(FloorManager gestor, int numeroDePuerta)
    {
        pisos = gestor;
        puerta = numeroDePuerta;
    }

    /// <summary>La puerta 'numero' del piso indicado; null si ese piso no la tiene.</summary>
    public static ElevatorTrigger DeLaPuerta(int piso, int numero)
    {
        if (numero < 0) return null;
        var lista = Todos;
        for (int i = 0; i < lista.Count; i++)
        {
            ElevatorTrigger e = lista[i];
            if (e != null && e.puerta == numero && e.Piso == piso) return e;
        }
        return null;
    }

    /// <summary>Dónde se aparece al salir de este ascensor viniendo de 'salida'
    /// (la misma puerta del piso destino; si no la hay, el mismo XZ).</summary>
    public static Vector3 Llegada(ElevatorTrigger salida, int pisoDestino, out float yaw)
    {
        ElevatorTrigger e = salida != null ? DeLaPuerta(pisoDestino, salida.puerta) : null;
        if (e != null)
        {
            yaw = e.transform.eulerAngles.y;
            return e.PuntoDeAcceso;
        }
        yaw = salida != null ? salida.transform.eulerAngles.y : 0f;
        Vector3 p = salida != null ? salida.PuntoDeAcceso : Vector3.zero;
        if (FloorManager.Instance != null) p.y = FloorManager.Instance.AlturaDelSuelo(pisoDestino);
        return p;
    }

    // ------------------------------------------------------------------
    // Registro de ascensores: lo usan la flecha de guia y el calculo de la
    // ruta optima cuando el destino esta en otro piso.
    // Se arma buscando tambien en objetos INACTIVOS, porque los ascensores de
    // los pisos apagados por el FloorManager no reciben Awake ni OnEnable.
    // ------------------------------------------------------------------
    private static readonly List<ElevatorTrigger> todos = new List<ElevatorTrigger>();
    private static bool registroListo;
    private static int zonasConJugador;

    public static List<ElevatorTrigger> Todos
    {
        get
        {
            if (!registroListo)
            {
                todos.Clear();
                todos.AddRange(FindObjectsByType<ElevatorTrigger>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None));
                registroListo = true;
            }
            return todos;
        }
    }

    /// <summary>True mientras el jugador esta dentro de la zona de algun ascensor.
    /// Los NPC lo consultan para no pelearse por la tecla E.</summary>
    public static bool JugadorEnAlgunaZona => zonasConJugador > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        todos.Clear();
        registroListo = false;
        zonasConJugador = 0;
        EnUso = null;
    }

    /// <summary>Hay que llamarlo al cargar una escena: el registro es por escena.</summary>
    public static void OlvidarRegistro()
    {
        todos.Clear();
        registroListo = false;
        zonasConJugador = 0;
    }

    /// <summary>Piso al que pertenece este ascensor, contando desde 0.</summary>
    public int Piso
    {
        get
        {
            FloorManager g = pisos != null ? pisos : FloorManager.Instance;
            return g != null ? g.PisoSegunAltura(transform.position.y) : 0;
        }
    }

    /// <summary>Punto del suelo desde el que se toma el ascensor.</summary>
    public Vector3 PuntoDeAcceso
    {
        get
        {
            FloorManager g = pisos != null ? pisos : FloorManager.Instance;
            Vector3 p = transform.position;
            if (g != null) p.y = g.AlturaDelSuelo(Piso);
            return p;
        }
    }

    /// <summary>El ascensor del piso indicado mas cercano a un punto. Null si no hay.</summary>
    public static ElevatorTrigger MasCercano(int piso, Vector3 desde)
    {
        ElevatorTrigger mejor = null;
        float mejorDistancia = float.MaxValue;
        var lista = Todos;
        for (int i = 0; i < lista.Count; i++)
        {
            ElevatorTrigger e = lista[i];
            if (e == null || e.Piso != piso) continue;
            float d = (e.transform.position - desde).sqrMagnitude;
            if (d < mejorDistancia)
            {
                mejorDistancia = d;
                mejor = e;
            }
        }
        return mejor;
    }

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        if (pisos == null) pisos = FloorManager.Instance;
    }

    private void OnTriggerEnter(Collider otro)
    {
        if (otro.GetComponentInParent<CharacterController>() == null) return;
        if (!jugadorCerca) zonasConJugador++;
        jugadorCerca = true;
        EnUso = this;
        ElevatorPanel.Obtener().MostrarAviso(true);
    }

    private void OnTriggerExit(Collider otro)
    {
        if (otro.GetComponentInParent<CharacterController>() == null) return;
        SalirDeLaZona();
        ElevatorPanel.Obtener().MostrarAviso(false);
    }

    private void OnDisable()
    {
        // Al cambiar de piso este ascensor se apaga con su piso y OnTriggerExit
        // no llega nunca: sin esto el contador quedaria en 1 para siempre.
        SalirDeLaZona();
    }

    private void SalirDeLaZona()
    {
        if (jugadorCerca) zonasConJugador = Mathf.Max(0, zonasConJugador - 1);
        jugadorCerca = false;
        if (EnUso == this) EnUso = null;
    }

    private void Update()
    {
        if (!jugadorCerca || Keyboard.current == null) return;
        if (!Keyboard.current.eKey.wasPressedThisFrame) return;

        if (pisos == null) pisos = FloorManager.Instance;
        if (pisos == null)
        {
            Debug.LogWarning("[Ascensor] No hay FloorManager en la escena.");
            return;
        }

        var panel = ElevatorPanel.Obtener();
        if (panel.Abierto) panel.Cerrar();
        else panel.Abrir(pisos);
    }
}
