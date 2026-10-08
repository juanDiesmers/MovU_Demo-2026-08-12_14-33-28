using UnityEngine;

// ============================================================================
// SolSinSombras.cs — Recuerda cómo estaba el sol antes de quitarle las sombras
// ============================================================================
// Lo pone el menú 'MovU > Rendimiento > Quitar sombras del sol' en la luz
// direccional, para poder deshacer el cambio con 'Restaurar sombras del sol'
// aunque haya pasado tiempo y ya no sirva Ctrl+Z. No hace nada al jugar.
// ============================================================================

[DisallowMultipleComponent]
[AddComponentMenu("MovU/Sol sin sombras (recuerdo)")]
public class SolSinSombras : MonoBehaviour
{
    [SerializeField] private float intensidadOriginal = 1f;
    [SerializeField] private LightShadows sombrasOriginales = LightShadows.Soft;
    [SerializeField] private bool guardado;

    public void Guardar(Light luz)
    {
        if (guardado || luz == null) return;      // no pisar el original con un valor ya cambiado
        intensidadOriginal = luz.intensity;
        sombrasOriginales = luz.shadows;
        guardado = true;
    }

    public void Restaurar(Light luz)
    {
        if (!guardado || luz == null) return;
        luz.intensity = intensidadOriginal;
        luz.shadows = sombrasOriginales;
    }
}
