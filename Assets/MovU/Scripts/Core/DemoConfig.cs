using UnityEngine;

// ============================================================================
// DemoConfig.cs — ScriptableObject con todos los parámetros ajustables
// ============================================================================

[CreateAssetMenu(fileName = "DemoConfig", menuName = "MovU/Demo Config")]
public class DemoConfig : ScriptableObject
{
    [Header("Laberinto")]
    [Tooltip("Semilla del generador aleatorio. Misma semilla = mismo laberinto.")]
    public int mazeSeed = 42;

    [Tooltip("Ancho del laberinto en celdas.")]
    public int mazeWidth = 13;

    [Tooltip("Alto del laberinto en celdas.")]
    public int mazeHeight = 13;

    [Tooltip("Fracción de callejones sin salida a eliminar (0.0 = perfecto, 1.0 = sin callejones).")]
    [Range(0f, 1f)]
    public float braidPercent = 0.15f;

    [Header("Geometría (1 unidad = 1 metro)")]
    [Tooltip("Tamaño de cada celda en metros.")]
    public float cellSize = 3.0f;

    [Tooltip("Altura de los muros en metros.")]
    public float wallHeight = 3.0f;

    [Tooltip("Grosor de los muros en metros.")]
    public float wallThickness = 0.2f;

    [Header("Jugador")]
    [Tooltip("Velocidad de caminata en m/s.")]
    public float walkSpeed = 3.0f;

    [Tooltip("Sensibilidad del mouse (Hallazgo #11: 0.15 para delta de píxeles).")]
    [Range(0.02f, 0.5f)]
    public float mouseSensitivity = 0.15f;

    [Tooltip("Altura de los ojos (cámara) en metros.")]
    public float cameraHeight = 1.65f;

    [Tooltip("Altura total de la cápsula del jugador en metros.")]
    public float capsuleHeight = 1.8f;

    [Tooltip("Radio de la cápsula del jugador en metros.")]
    public float capsuleRadius = 0.3f;

    [Tooltip("Límite del ángulo vertical de la cámara en grados.")]
    public float pitchLimit = 85f;

    [Tooltip("Aceleración gravitatoria en m/s².")]
    public float gravity = -9.81f;

    [Tooltip("Campo de visión de la cámara en grados.")]
    public float fieldOfView = 70f;
}
