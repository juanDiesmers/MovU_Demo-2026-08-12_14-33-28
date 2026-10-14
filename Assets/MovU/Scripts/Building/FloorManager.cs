using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// FloorManager.cs — Pisos del edificio y carga bajo demanda
// ============================================================================
// El edificio son tres copias del mismo piso, apiladas. Como desde dentro de un
// piso NUNCA se puede ver otro, mantener los tres dibujandose es trabajo tirado:
// triangulos, sombras en tiempo real y colisiones que nadie ve.
//
// Este componente deja activo solo el piso donde esta el jugador (o tambien los
// vecinos, si se sube 'pisosVecinosCargados'). Es la version util de "cargar
// solo lo que el jugador esta viendo": dentro de cada piso, Unity ya descarta
// por frustum automaticamente, y para lo que queda fuera de vista pero dentro
// del piso esta el Occlusion Culling horneado.
//
// Detecta el piso por la altura del jugador, asi que tambien funciona si se cae
// por un hueco o si se le mueve a mano desde el editor.
// ============================================================================

public class FloorManager : MonoBehaviour
{
    public static FloorManager Instance { get; private set; }

    [Header("Pisos, de abajo hacia arriba")]
    [SerializeField] private List<Transform> pisos = new List<Transform>();

    [Header("Geometria")]
    [Tooltip("Altura de la cara superior de la losa, medida desde el PIVOTE del objeto del " +
             "piso (no desde su base: el pivote queda donde estaba el origen del modelo).")]
    [SerializeField] private float alturaDeLaLosa = 0.51f;

    [Header("Carga")]
    [Tooltip("0 = solo el piso actual. 1 = tambien el de arriba y el de abajo.")]
    [SerializeField] private int pisosVecinosCargados = 0;

    [Header("Planta de cada piso")]
    [Tooltip("Qué planta usa cada piso, de abajo hacia arriba: 'piso9' (la planta del piso 9, " +
             "a la que se refiere todo el contenido del JSON) o 'piso3' (la planta propia del piso 3).")]
    [SerializeField] private List<string> plantas = new List<string>();

    [Header("Contenido por piso (Contenido/Piso_N)")]
    [Tooltip("Lo que el equipo pone a mano en cada piso: puertas, POIs, " +
             "objetivos. Se enciende y se apaga junto con su piso.")]
    [SerializeField] private List<Transform> contenidoPorPiso = new List<Transform>();

    [Header("Referencias")]
    [SerializeField] private Transform jugador;

    private int pisoActual;
    private bool iniciado;
    private CharacterController controlador;

    public int CantidadDePisos => pisos.Count;
    public int PisoActual => pisoActual;

    /// <summary>
    /// El jugador cambio de piso: (piso anterior, piso nuevo), contando desde 0.
    /// Lo escuchan las metricas (cambios de piso y pisos equivocados), el HUD
    /// (indicador de piso) y la flecha de guia.
    /// </summary>
    public event Action<int, int> OnPisoCambiado;

    public Transform Jugador => jugador;

    /// <summary>
    /// Número con el que se le MUESTRA al jugador el piso más bajo. La planta
    /// que tenemos es la del piso 9, así que el primer piso del modelo se rotula
    /// "Piso 9" (lo fija el JSON: ajustes.numeroDelPrimerPiso). Por dentro los
    /// pisos se siguen contando desde 0, y en el CSV desde 1.
    /// </summary>
    public static int NumeroDelPrimerPiso { get; set; } = 1;

    /// <summary>
    /// Número visible de cada piso, de abajo hacia arriba (ajustes.numerosDePiso
    /// del JSON: 3, 9 y 10). Si está vacío se cuenta desde NumeroDelPrimerPiso.
    /// </summary>
    public static readonly List<int> NumerosVisibles = new List<int>();

    /// <summary>El número que ve el jugador para el piso 'indice' (contando desde 0).</summary>
    public static int NumeroVisible(int indice)
    {
        if (indice >= 0 && indice < NumerosVisibles.Count) return NumerosVisibles[indice];
        return NumeroDelPrimerPiso + indice;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        NumeroDelPrimerPiso = 1;
        NumerosVisibles.Clear();
    }

    /// <summary>La planta del piso 9 (Models/Piso9/planta_piso9.json; antes el modelo de Meshy), a la que se refiere todo el contenido del JSON.</summary>
    public const string PlantaBase = "piso9";

    /// <summary>Planta del piso indicado; sin dato, la del piso 9.</summary>
    public string PlantaDe(int indice)
    {
        return (indice >= 0 && indice < plantas.Count && !string.IsNullOrEmpty(plantas[indice]))
            ? plantas[indice]
            : PlantaBase;
    }

    /// <summary>True si el piso usa la planta del piso 9 (copias incluidas).</summary>
    public bool EsPlantaBase(int indice) => PlantaDe(indice) == PlantaBase;

    /// <summary>Planta de los pisos genéricos (4 a 8 y 10 a 12): el núcleo del 9 con los salones cerrados.</summary>
    public const string PlantaGenerica = "generico";

    /// <summary>
    /// Plantas que llevan la escalera de emergencia funcional (StairsManager), en el
    /// mismo sitio: la del 9, la genérica y la del 13. La del 3 tiene la suya en otro
    /// sitio y solo se ve.
    /// </summary>
    public static readonly HashSet<string> PlantasConEscalera = new HashSet<string> { PlantaBase, PlantaGenerica, "piso13" };

    /// <summary>True si en este piso está la escalera funcional.</summary>
    public bool TieneEscalera(int indice) => PlantasConEscalera.Contains(PlantaDe(indice));

    /// <summary>El primer piso con la planta del piso 9: contra él se mide todo.</summary>
    public int IndiceBase
    {
        get
        {
            for (int i = 0; i < pisos.Count; i++) if (EsPlantaBase(i)) return i;
            return 0;
        }
    }

    /// <summary>
    /// True si la escalera lleva de un piso al otro: tienen que ser vecinos en la
    /// pila, los dos con la escalera funcional (pisos 4 a 13) y con números
    /// seguidos. Del 4 no se baja al 3 por la escalera.
    /// </summary>
    public bool EscaleraConecta(int desde, int hasta)
    {
        if (Mathf.Abs(desde - hasta) != 1) return false;
        if (desde < 0 || hasta < 0 || desde >= pisos.Count || hasta >= pisos.Count) return false;
        if (!TieneEscalera(desde) || !TieneEscalera(hasta)) return false;
        return Mathf.Abs(NumeroVisible(hasta) - NumeroVisible(desde)) == 1;
    }

    /// <summary>Raiz de la geometria del piso indicado (contando desde 0).</summary>
    public Transform Piso(int indice)
    {
        return (indice >= 0 && indice < pisos.Count) ? pisos[indice] : null;
    }

    /// <summary>Contenido/Piso_N si ya existe; null si no. No crea nada.</summary>
    public Transform ContenidoDelPisoSiExiste(int indice)
    {
        return (indice >= 0 && indice < contenidoPorPiso.Count) ? contenidoPorPiso[indice] : null;
    }

    /// <summary>
    /// Contenido/Piso_N del piso indicado. Si la escena es anterior a la raiz
    /// 'Contenido' (o alguien la borro), la crea: el contenido que se genera
    /// desde el JSON necesita un sitio que respete la carga por piso.
    /// </summary>
    public Transform ContenidoDelPiso(int indice)
    {
        if (indice < 0 || indice >= pisos.Count) return null;

        while (contenidoPorPiso.Count <= indice) contenidoPorPiso.Add(null);
        if (contenidoPorPiso[indice] != null) return contenidoPorPiso[indice];

        GameObject raiz = GameObject.Find("Contenido");
        if (raiz == null) raiz = new GameObject("Contenido");

        string nombre = $"Piso_{indice + 1}";
        Transform hijo = raiz.transform.Find(nombre);
        if (hijo == null)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(raiz.transform, false);
            hijo = go.transform;
        }

        contenidoPorPiso[indice] = hijo;
        if (iniciado) Encender(hijo, Mathf.Abs(indice - pisoActual) <= pisosVecinosCargados);
        return hijo;
    }

    /// <summary>Altura de la losa pisable medida desde la base de su piso.</summary>
    public float AlturaDeLaLosa => alturaDeLaLosa;

    /// <summary>Base del piso 1 en coordenadas de mundo.</summary>
    public float BaseDelPrimerPiso => (pisos.Count > 0 && pisos[0] != null)
        ? pisos[0].position.y
        : 0f;

    /// <summary>
    /// Distancia vertical entre dos pisos consecutivos. El shader la necesita
    /// para medir la altura DENTRO del piso y no desde la base del edificio.
    /// Con un solo piso devuelve 0, que es justo lo que el shader interpreta
    /// como "no hay pisos apilados".
    /// </summary>
    public float SeparacionEntrePisos
    {
        get
        {
            if (pisos.Count < 2 || pisos[0] == null || pisos[1] == null) return 0f;
            return Mathf.Abs(pisos[1].position.y - pisos[0].position.y);
        }
    }

    public void Configurar(List<Transform> lista, float alturaLosa,
                           Transform elJugador, int vecinos)
    {
        Configurar(lista, alturaLosa, elJugador, vecinos, null);
    }

    public void Configurar(List<Transform> lista, float alturaLosa,
                           Transform elJugador, int vecinos,
                           List<Transform> contenido, List<string> plantasPorPiso)
    {
        Configurar(lista, alturaLosa, elJugador, vecinos, contenido);
        plantas = plantasPorPiso != null ? new List<string>(plantasPorPiso) : new List<string>();
    }

    public void Configurar(List<Transform> lista, float alturaLosa,
                           Transform elJugador, int vecinos,
                           List<Transform> contenido)
    {
        pisos = new List<Transform>(lista);
        alturaDeLaLosa = alturaLosa;
        jugador = elJugador;
        pisosVecinosCargados = vecinos;
        contenidoPorPiso = contenido != null
            ? new List<Transform>(contenido)
            : new List<Transform>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (jugador != null)
        {
            controlador = jugador.GetComponent<CharacterController>();
        }

        pisoActual = jugador != null ? PisoSegunAltura(jugador.position.y) : 0;
        iniciado = true;
        RefrescarActivos();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (jugador == null || pisos.Count == 0) return;

        int detectado = PisoSegunAltura(jugador.position.y);
        if (detectado != pisoActual)
        {
            int anterior = pisoActual;
            pisoActual = detectado;
            RefrescarActivos();
            OnPisoCambiado?.Invoke(anterior, pisoActual);
        }
    }

    /// <summary>Altura del suelo pisable del piso indicado, en coordenadas de mundo.</summary>
    public float AlturaDelSuelo(int indice)
    {
        indice = Mathf.Clamp(indice, 0, pisos.Count - 1);
        return pisos[indice].position.y + alturaDeLaLosa;
    }

    /// <summary>El piso mas alto cuyo suelo queda por debajo del punto dado.</summary>
    public int PisoSegunAltura(float y)
    {
        int resultado = 0;
        for (int i = 0; i < pisos.Count; i++)
        {
            // El margen evita que un salto o un escalon cuenten como cambio de piso.
            if (y >= AlturaDelSuelo(i) - 0.75f) resultado = i;
        }
        return resultado;
    }

    private void RefrescarActivos()
    {
        for (int i = 0; i < pisos.Count; i++)
        {
            bool visible = Mathf.Abs(i - pisoActual) <= pisosVecinosCargados;
            Encender(pisos[i], visible);

            // El contenido puesto a mano vive fuera de la jerarquia del piso
            // (en Contenido/Piso_N, que BuildingSetup nunca borra), asi que hay
            // que encenderlo y apagarlo aparte. Si no, las puertas del piso 3
            // seguirian dibujandose estando el jugador en el 1.
            if (i < contenidoPorPiso.Count) Encender(contenidoPorPiso[i], visible);
        }
    }

    private static void Encender(Transform t, bool visible)
    {
        if (t == null) return;
        if (t.gameObject.activeSelf != visible) t.gameObject.SetActive(visible);
    }

    /// <summary>Lleva al jugador al mismo punto XZ, pero en otro piso.</summary>
    public void LlevarAlPiso(int destino)
    {
        if (jugador == null || pisos.Count == 0) return;

        destino = Mathf.Clamp(destino, 0, pisos.Count - 1);

        // El piso destino tiene que estar activo ANTES de mover al jugador: si
        // no, se le suelta encima de colisiones que todavia no existen y cae.
        int anterior = pisoActual;
        pisoActual = destino;
        RefrescarActivos();
        Physics.SyncTransforms();

        // Altura de los PIES respecto al pivote del jugador. El personaje tiene el
        // pivote en los pies (centro de la capsula a media altura), asi que vale 0;
        // antes se sumaba media capsula de mas y el jugador caia un metro al salir
        // del ascensor.
        float piesSobrePivote = controlador != null
            ? controlador.center.y - controlador.height * 0.5f
            : 0f;
        Vector3 p = jugador.position;
        Vector3 destinoMundo = new Vector3(
            p.x,
            AlturaDelSuelo(destino) - piesSobrePivote + 0.08f,
            p.z);

        if (controlador != null)
        {
            // Mover el transform con el CharacterController activo no funciona:
            // el componente reescribe la posicion en su propio Update.
            controlador.enabled = false;
            jugador.position = destinoMundo;
            controlador.enabled = true;
        }
        else
        {
            jugador.position = destinoMundo;
        }

        Debug.Log($"[FloorManager] Jugador movido al piso {destino + 1} " +
                  $"(y = {destinoMundo.y:F2}).");

        if (anterior != destino) OnPisoCambiado?.Invoke(anterior, destino);
    }
}
