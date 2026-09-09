using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// ElevatorTrigger.cs — El cubo que cambia de piso
// ============================================================================
// Un disparador junto al cubo. Cuando el jugador entra en su radio, aparece el
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

    private bool jugadorCerca;

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
        jugadorCerca = true;
        ElevatorPanel.Obtener().MostrarAviso(true);
    }

    private void OnTriggerExit(Collider otro)
    {
        if (otro.GetComponentInParent<CharacterController>() == null) return;
        jugadorCerca = false;
        ElevatorPanel.Obtener().MostrarAviso(false);
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
