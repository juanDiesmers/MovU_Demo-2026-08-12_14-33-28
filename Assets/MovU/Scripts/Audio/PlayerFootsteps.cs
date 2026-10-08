using UnityEngine;

// ============================================================================
// PlayerFootsteps.cs — Cuándo suena un paso del jugador
// ============================================================================
// Mide lo que avanza el personaje en horizontal y pide un paso cada 'zancada'
// metros. Va por DISTANCIA y no por tiempo: así el ritmo sigue a la velocidad
// real (pegado a un muro casi no avanza, y casi no suena).
//
// No reproduce nada: le pide el sonido al AudioManager. Lo añade MovUBootstrap
// al personaje; no hay que ponerlo a mano en la escena.
// ============================================================================

[RequireComponent(typeof(CharacterController))]
public class PlayerFootsteps : MonoBehaviour
{
    [Tooltip("Metros entre un paso y el siguiente. A 3 m/s, 0,85 m son unos 3,5 pasos por segundo.")]
    [SerializeField] private float zancada = 0.85f;

    private CharacterController controlador;
    private Vector3 ultima;
    private float acumulado;
    private float alturaDelUltimoPaso;

    private void Awake()
    {
        controlador = GetComponent<CharacterController>();
        ultima = transform.position;
        alturaDelUltimoPaso = ultima.y;
    }

    private void Update()
    {
        Vector3 ahora = transform.position;
        Vector3 d = ahora - ultima;
        ultima = ahora;
        d.y = 0f;

        float avance = d.magnitude;
        // Un salto así en un frame es un teletransporte (ascensor, inicio de misión).
        if (avance > 1f)
        {
            acumulado = 0f;
            alturaDelUltimoPaso = ahora.y;
            return;
        }

        if (controlador == null || !controlador.enabled || !controlador.isGrounded) return;

        acumulado += avance;
        if (acumulado < zancada) return;
        acumulado -= zancada;

        // Si entre un paso y otro subió o bajó, va por la escalera.
        bool enEscalera = Mathf.Abs(ahora.y - alturaDelUltimoPaso) > 0.12f;
        alturaDelUltimoPaso = ahora.y;

        if (AudioManager.Instance != null) AudioManager.Instance.Paso(enEscalera);
    }
}
