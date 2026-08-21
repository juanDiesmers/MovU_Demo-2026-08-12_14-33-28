using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// PlayerController.cs — Control de movimiento FPS en primera persona
// ============================================================================

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Parámetros de Movimiento")]
    [SerializeField] private float walkSpeed = 3.0f; // m/s
    [SerializeField] private float gravity = -9.81f; // m/s²

    [Header("Configuración de Cápsula")]
    [SerializeField] private float capsuleHeight = 1.8f; // m
    [SerializeField] private float capsuleRadius = 0.3f; // m

    private CharacterController characterController;
    private Vector3 verticalVelocity = Vector3.zero;
    private bool isInputEnabled = true;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    private void Start()
    {
        LockCursor();
    }

    private void Update()
    {
        // Hallazgo #19: No procesar input de cursor si el control está desactivado (fin de partida)
        if (!isInputEnabled) return;

        HandleCursorLockInput();
        HandleMovement();
    }

    /// <summary>
    /// Configura dinámicamente los parámetros desde DemoConfig (Hallazgo #4).
    /// </summary>
    public void Configure(float speed, float grav, float height, float radius)
    {
        walkSpeed = speed;
        gravity = grav;
        capsuleHeight = height;
        capsuleRadius = radius;

        if (characterController != null)
        {
            characterController.height = capsuleHeight;
            characterController.radius = capsuleRadius;
            characterController.center = new Vector3(0f, capsuleHeight * 0.5f, 0f);
        }
    }

    private void HandleMovement()
    {
        if (Keyboard.current == null) return;

        float moveX = 0f;
        float moveZ = 0f;

        if (Keyboard.current.wKey.isPressed) moveZ += 1f;
        if (Keyboard.current.sKey.isPressed) moveZ -= 1f;
        if (Keyboard.current.dKey.isPressed) moveX += 1f;
        if (Keyboard.current.aKey.isPressed) moveX -= 1f;

        Vector3 moveInput = new Vector3(moveX, 0f, moveZ).normalized;
        Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.z;

        if (characterController.isGrounded && verticalVelocity.y < 0)
        {
            verticalVelocity.y = -2f;
        }

        verticalVelocity.y += gravity * Time.deltaTime;

        Vector3 totalMotion = (moveDirection * walkSpeed + verticalVelocity) * Time.deltaTime;
        characterController.Move(totalMotion);
    }

    private void HandleCursorLockInput()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                UnlockCursor();
            }
            else
            {
                LockCursor();
            }
        }
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void SetInputEnabled(bool enabled)
    {
        isInputEnabled = enabled;
        if (!enabled)
        {
            UnlockCursor();
        }
    }
}
