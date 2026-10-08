using UnityEngine;

// ============================================================================
// GeneradoPorMovU.cs — Marca lo que construyen los menus de MovU
// ============================================================================
// Los menus de MovU vuelven a armar la escena desde cero cada vez, asi que
// tienen que borrar lo que construyeron la vez anterior. La primera version
// borraba "toda malla grande que no sea del edificio", y eso se comia tambien
// las puertas, los POIs y cualquier cosa que el equipo hubiera puesto a mano.
//
// Con este marcador la regla es explicita: se borra lo que lleva este
// componente, y NADA mas. Lo que ustedes agreguen no lo lleva, asi que
// sobrevive a cualquier reconstruccion.
//
// Va en el objeto RAIZ de cada cosa generada (el edificio, la planta suelta,
// el cubo de referencia). No hace falta ponerlo en los hijos.
// ============================================================================

[DisallowMultipleComponent]
[AddComponentMenu("MovU/Generado por MovU (marcador)")]
public class GeneradoPorMovU : MonoBehaviour
{
    [Tooltip("Que menu lo creo. Solo informativo, para saber de donde salio.")]
    [SerializeField] private string herramienta = "";

    [Tooltip("Cuando se genero, en texto. Solo informativo.")]
    [SerializeField] private string fecha = "";

    public string Herramienta => herramienta;

    /// <summary>Marca el objeto como generado. Idempotente.</summary>
    public static GeneradoPorMovU Marcar(GameObject objetivo, string herramienta)
    {
        if (objetivo == null) return null;

        var marca = objetivo.GetComponent<GeneradoPorMovU>();
        if (marca == null) marca = objetivo.AddComponent<GeneradoPorMovU>();

        marca.herramienta = herramienta;
        marca.fecha = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        return marca;
    }

    /// <summary>True si el objeto, o alguno de sus padres, esta marcado.</summary>
    public static bool EstaMarcado(GameObject objetivo)
    {
        return objetivo != null && objetivo.GetComponentInParent<GeneradoPorMovU>(true) != null;
    }
}
