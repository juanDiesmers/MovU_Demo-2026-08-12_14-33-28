using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// MouseLook.cs — Rotación de cámara FPS con el mouse
// ============================================================================

public class MouseLook : MonoBehaviour
{
    [Header("Sensibilidad y Cámara")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float pitchLimit = 85.0f;
    [SerializeField] private Transform playerCamera;

    private float xRotation = 0f;
    private bool isLookEnabled = true;

    private void Start()
    {
        if (playerCamera == null)
        {
            Camera mainCam = GetComponentInChildren<Camera>();
            if (mainCam != null)
            {
                playerCamera = mainCam.transform;
            }
        }
    }

    private void Update()
    {
        if (!isLookEnabled) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseX = mouseDelta.x * mouseSensitivity;
        float mouseY = mouseDelta.y * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -pitchLimit, pitchLimit);

        if (playerCamera != null)
        {
            playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }

        transform.Rotate(Vector3.up * mouseX);
    }

    public void Configure(float sensitivity, float limit, Transform camTransform)
    {
        mouseSensitivity = sensitivity;
        pitchLimit = limit;
        playerCamera = camTransform;
    }

    public void SetSensitivity(float newSensitivity)
    {
        mouseSensitivity = newSensitivity;
    }

    public void SetLookEnabled(bool enabled)
    {
        isLookEnabled = enabled;
    }
}
