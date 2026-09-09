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
    [Tooltip("Altura de la cara superior de la losa, medida desde la base del piso.")]
    [SerializeField] private float alturaDeLaLosa = 0.51f;

    [Header("Carga")]
    [Tooltip("0 = solo el piso actual. 1 = tambien el de arriba y el de abajo.")]
    [SerializeField] private int pisosVecinosCargados = 0;

    [Header("Referencias")]
    [SerializeField] private Transform jugador;

    private int pisoActual;
    private CharacterController controlador;

    public int CantidadDePisos => pisos.Count;
    public int PisoActual => pisoActual;

    public void Configurar(List<Transform> lista, float alturaLosa,
                           Transform elJugador, int vecinos)
    {
        pisos = new List<Transform>(lista);
        alturaDeLaLosa = alturaLosa;
        jugador = elJugador;
        pisosVecinosCargados = vecinos;
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
        RefrescarActivos();
    }

    private void Update()
    {
        if (jugador == null || pisos.Count == 0) return;

        int detectado = PisoSegunAltura(jugador.position.y);
        if (detectado != pisoActual)
        {
            pisoActual = detectado;
            RefrescarActivos();
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
            if (pisos[i] == null) continue;
            bool visible = Mathf.Abs(i - pisoActual) <= pisosVecinosCargados;
            if (pisos[i].gameObject.activeSelf != visible)
            {
                pisos[i].gameObject.SetActive(visible);
            }
        }
    }

    /// <summary>Lleva al jugador al mismo punto XZ, pero en otro piso.</summary>
    public void LlevarAlPiso(int destino)
    {
        if (jugador == null || pisos.Count == 0) return;

        destino = Mathf.Clamp(destino, 0, pisos.Count - 1);

        // El piso destino tiene que estar activo ANTES de mover al jugador: si
        // no, se le suelta encima de colisiones que todavia no existen y cae.
        pisoActual = destino;
        RefrescarActivos();
        Physics.SyncTransforms();

        float altoCapsula = controlador != null ? controlador.height : 1.8f;
        Vector3 p = jugador.position;
        Vector3 destinoMundo = new Vector3(
            p.x,
            AlturaDelSuelo(destino) + altoCapsula * 0.5f + 0.1f,
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
    }
}
