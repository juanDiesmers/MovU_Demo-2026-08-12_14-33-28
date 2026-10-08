using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// PointOfInterest.cs — Un punto de interes (POI) del edificio
// ============================================================================
// Es la "capa de identificacion" del nivel: el modelo de Meshy trae los muros
// pero no dice que es cada espacio. Un POI le pone tipo, nombre y piso a un
// punto, y es a lo que apuntan las misiones.
//
// Hay dos formas de tener POIs, y conviven:
//   - Desde el JSON (contenido_piso9.json): se crean solos al dar Play.
//   - A mano: 'MovU > Contenido > Crear POI aqui' lo deja en Contenido/Piso_N.
//     Si un POI puesto a mano tiene el mismo id que uno del JSON, gana el de la
//     escena.
//
// No lleva collider ni Update: la llegada la comprueba el MissionManager
// midiendo la distancia SOLO al POI de la mision activa.
// ============================================================================

public enum PoiCategory
{
    Salon,
    Laboratorio,
    Oficina,
    PrestamoDeEquipos,
    SoporteTecnico,
    Bano,
    Cafeteria,
    ZonaDeDescanso,
    Ascensor,
    Escalera,
    Otro
}

[DisallowMultipleComponent]
[AddComponentMenu("MovU/Punto de interes (POI)")]
public class PointOfInterest : MonoBehaviour
{
    [Tooltip("Identificador unico, sin espacios. Es lo que usan las misiones.")]
    [SerializeField] private string id = "";

    [Tooltip("Nombre que ve el jugador en el rotulo y en el enunciado.")]
    [SerializeField] private string nombre = "";

    [SerializeField] private PoiCategory categoria = PoiCategory.Otro;

    [Tooltip("Radio de la zona de llegada en metros (SRS RF-4: 1,5 m).")]
    [SerializeField] private float radioDeLlegada = 1.5f;

    [Tooltip("Los espacios que el equipo marco como clave del piso.")]
    [SerializeField] private bool importante = false;

    [Tooltip("Mostrar un rotulo con el nombre.")]
    [SerializeField] private bool mostrarRotulo = true;

    [Tooltip("Donde va el rotulo respecto al POI (normalmente la puerta).")]
    [SerializeField] private Vector3 desplazamientoDelRotulo = new Vector3(0f, 2.45f, 0f);

    public string Id => id;
    public string Nombre => string.IsNullOrEmpty(nombre) ? id : nombre;
    public PoiCategory Categoria => categoria;
    public float RadioDeLlegada => Mathf.Max(0.25f, radioDeLlegada);
    public bool Importante => importante;
    public bool MostrarRotulo => mostrarRotulo;
    public Vector3 PosicionDelRotulo => transform.position + desplazamientoDelRotulo;

    /// <summary>Piso al que pertenece, contando desde 0. Sale de su altura.</summary>
    public int Piso
    {
        get
        {
            FloorManager g = FloorManager.Instance;
            return g != null ? g.PisoSegunAltura(transform.position.y + 0.5f) : 0;
        }
    }

    public void Configurar(string nuevoId, string nuevoNombre, PoiCategory nuevaCategoria,
                           float radio, bool esImportante, bool conRotulo,
                           Vector3 rotuloLocal)
    {
        id = nuevoId;
        nombre = nuevoNombre;
        categoria = nuevaCategoria;
        radioDeLlegada = radio;
        importante = esImportante;
        mostrarRotulo = conRotulo;
        desplazamientoDelRotulo = rotuloLocal;
    }

    // ------------------------------------------------------------------
    // Registro
    // ------------------------------------------------------------------
    // Se arma una vez por escena buscando tambien en objetos inactivos: los
    // POIs de un piso apagado siguen siendo destinos validos de una mision.
    private static readonly List<PointOfInterest> todos = new List<PointOfInterest>();
    private static bool registroListo;

    public static List<PointOfInterest> Todos
    {
        get
        {
            if (!registroListo)
            {
                todos.Clear();
                todos.AddRange(FindObjectsByType<PointOfInterest>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None));
                registroListo = true;
            }
            return todos;
        }
    }

    public static PointOfInterest Buscar(string idBuscado)
    {
        if (string.IsNullOrEmpty(idBuscado)) return null;
        var lista = Todos;
        for (int i = 0; i < lista.Count; i++)
        {
            if (lista[i] != null &&
                string.Equals(lista[i].id, idBuscado, StringComparison.OrdinalIgnoreCase))
            {
                return lista[i];
            }
        }
        return null;
    }

    /// <summary>Hay que llamarlo despues de crear o borrar POIs, y al cargar una escena.</summary>
    public static void OlvidarRegistro()
    {
        todos.Clear();
        registroListo = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        OlvidarRegistro();
    }

    public static bool CategoriaDesdeTexto(string texto, out PoiCategory categoria)
    {
        return Enum.TryParse(texto, true, out categoria);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = importante ? new Color(1f, 0.55f, 0.1f, 0.9f)
                                  : new Color(0.1f, 0.75f, 1f, 0.8f);
        Vector3 p = transform.position;
        Gizmos.DrawWireSphere(p + Vector3.up * 0.05f, RadioDeLlegada);
        Gizmos.DrawLine(p, PosicionDelRotulo);
        Gizmos.DrawSphere(PosicionDelRotulo, 0.12f);
        UnityEditor.Handles.Label(PosicionDelRotulo + Vector3.up * 0.25f, Nombre);
    }
#endif
}
