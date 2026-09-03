using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// CameraRig.cs — Soporte de cámara en primera y tercera persona
// ============================================================================
// Jerarquía esperada:
//   Player (CharacterController, PlayerController, MouseLook, CameraRig)
//     └── CameraPivot        <- MouseLook aplica el pitch AQUÍ
//           └── MainCamera   <- se desplaza hacia atrás para tercera persona
//
// Con esta jerarquía, pasar a tercera persona es solo alejar la cámara sobre
// el eje -Z del pivote: la órbita sale gratis porque el pivote ya rota con el
// mouse. Tecla V para alternar.
// ============================================================================

public class CameraRig : MonoBehaviour
{
    public enum ViewMode { FirstPerson, ThirdPerson }

    [Header("Modo de cámara")]
    [SerializeField] private ViewMode viewMode = ViewMode.FirstPerson;
    [SerializeField] private bool allowToggleKey = true;

    [Header("Tercera persona")]
    [Tooltip("Distancia de la cámara detrás del personaje en metros.")]
    [SerializeField] private float thirdPersonDistance = 3.0f;

    [Tooltip("Altura adicional de la cámara sobre el pivote en metros.")]
    [SerializeField] private float thirdPersonHeight = 0.35f;

    [Tooltip("Margen que se deja frente a un muro para que la cámara no lo atraviese.")]
    [SerializeField] private float collisionPadding = 0.25f;

    [Header("Referencias")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Malla visible del personaje. Se oculta en primera persona.")]
    [SerializeField] private Renderer bodyRenderer;

    public ViewMode CurrentView => viewMode;

    private void Start()
    {
        ApplyViewMode();
    }

    private void Update()
    {
        if (allowToggleKey && Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
        {
            viewMode = viewMode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson;
            ApplyViewMode();
        }

        if (viewMode == ViewMode.ThirdPerson)
        {
            PlaceThirdPersonCamera();
        }
    }

    public void SetViewMode(ViewMode mode)
    {
        viewMode = mode;
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        if (cameraTransform == null || cameraPivot == null) return;

        if (viewMode == ViewMode.FirstPerson)
        {
            cameraTransform.localPosition = Vector3.zero;
            if (bodyRenderer != null) bodyRenderer.enabled = false;
        }
        else
        {
            if (bodyRenderer != null) bodyRenderer.enabled = true;
            PlaceThirdPersonCamera();
        }
    }

    /// <summary>
    /// Coloca la cámara detrás del pivote, acortando la distancia si hay un muro
    /// en medio para que no se meta dentro de la geometría.
    /// </summary>
    private void PlaceThirdPersonCamera()
    {
        if (cameraTransform == null || cameraPivot == null) return;

        Vector3 origin = cameraPivot.position;
        Vector3 direction = -cameraPivot.forward;
        float distance = thirdPersonDistance;

        if (Physics.SphereCast(origin, 0.2f, direction, out RaycastHit hit, thirdPersonDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(0.4f, hit.distance - collisionPadding);
        }

        cameraTransform.localPosition = new Vector3(0f, thirdPersonHeight, -distance);
    }

    /// <summary>Cableado desde el editor al construir la escena.</summary>
    public void Configure(Transform pivot, Transform cam, Renderer body)
    {
        cameraPivot = pivot;
        cameraTransform = cam;
        bodyRenderer = body;
    }
}
