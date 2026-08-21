// ============================================================================
// GuidanceMode.cs — Modos de la flecha de orientación
// ============================================================================

public enum GuidanceMode
{
    Off,    // Sin ayuda (grupo de control)
    Direct, // Flecha directa al objetivo en línea recta (atraviesa muros)
    NavMesh // Flecha guiada por el camino del NavMesh (apunta a la siguiente esquina)
}
