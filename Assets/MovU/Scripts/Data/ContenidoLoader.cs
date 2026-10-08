using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// ContenidoLoader.cs — Lee contenido_piso9.json y lo pone en la escena
// ============================================================================
// Lo que crea cuelga de Contenido/Piso_N, así que respeta la carga por piso sin
// programar nada: el FloorManager lo enciende y lo apaga con su piso.
//
// No pisa el trabajo hecho a mano: si en la escena ya hay un POI con el mismo
// id que uno del JSON, se queda el de la escena.
// ============================================================================

public static class ContenidoLoader
{
    /// <summary>Ruta dentro de una carpeta Resources, sin extensión.</summary>
    public const string Recurso = "MovU/contenido_piso9";

    public const string NombreDelGrupoDePois = "POIs (JSON)";

    public static ContenidoPiso Datos { get; private set; }

    public static bool HayAparicion { get; private set; }
    public static Vector3 PuntoDeAparicion { get; private set; }
    public static float YawDeAparicion { get; private set; }
    /// <summary>Contando desde 0.</summary>
    public static int PisoDeAparicion { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        Datos = null;
        HayAparicion = false;
    }

    /// <summary>Lee y valida el JSON. Devuelve null (con el motivo en consola) si no sirve.</summary>
    public static ContenidoPiso Leer()
    {
        TextAsset archivo = Resources.Load<TextAsset>(Recurso);
        if (archivo == null)
        {
            Debug.LogWarning("[Contenido] No encuentro Assets/MovU/Resources/" + Recurso +
                             ".json. Sin ese archivo no hay POIs ni misiones.");
            return null;
        }
        return Interpretar(archivo.text);
    }

    public static ContenidoPiso Interpretar(string json)
    {
        ContenidoPiso datos;
        try
        {
            datos = JsonUtility.FromJson<ContenidoPiso>(json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Contenido] contenido_piso9.json tiene un error de sintaxis " +
                           "(una coma o una llave de más o de menos): " + ex.Message);
            return null;
        }

        if (datos == null) return null;

        // JsonUtility deja en null lo que falte en el archivo.
        if (datos.ajustes == null) datos.ajustes = new AjustesDeContenido();
        if (datos.aparicion == null) datos.aparicion = new PuntoDeAparicion();
        if (datos.ascensor == null) datos.ascensor = new SitioDelAscensor();
        if (datos.pois == null) datos.pois = new List<PoiDef>();
        if (datos.misiones == null) datos.misiones = new List<MisionDef>();
        if (datos.npcs == null) datos.npcs = new List<NpcDef>();
        if (datos.multitud == null) datos.multitud = new MultitudDef();
        if (datos.recorridos == null) datos.recorridos = new List<PuntoUV>();

        Datos = datos;
        return datos;
    }

    /// <summary>Punto del suelo de un piso (contando desde 1) a partir de (u, v).</summary>
    public static bool AMundo(FloorManager pisos, int pisoDesdeUno, float u, float v, out Vector3 punto)
    {
        punto = Vector3.zero;
        if (pisos == null || pisos.CantidadDePisos == 0) return false;

        int indice = Mathf.Clamp(pisoDesdeUno - 1, 0, pisos.CantidadDePisos - 1);
        PlanoDePlanta plano = PlanoDePlanta.Medir(pisos.Piso(indice));
        if (!plano.valido) return false;

        punto = plano.AMundo(u, v, pisos.AlturaDelSuelo(indice));
        return true;
    }

    /// <summary>Calcula el punto de aparición del JSON. No mueve a nadie.</summary>
    public static bool ResolverAparicion(ContenidoPiso datos, FloorManager pisos)
    {
        HayAparicion = false;
        if (datos == null || datos.aparicion == null || !datos.aparicion.usar) return false;

        if (!AMundo(pisos, datos.aparicion.piso, datos.aparicion.u, datos.aparicion.v, out Vector3 p))
        {
            return false;
        }

        PuntoDeAparicion = p;
        YawDeAparicion = datos.aparicion.yaw;
        PisoDeAparicion = Mathf.Clamp(datos.aparicion.piso - 1, 0, pisos.CantidadDePisos - 1);
        HayAparicion = true;
        return true;
    }

    /// <summary>
    /// Crea los POIs del JSON que todavía no existan en la escena.
    /// Devuelve cuántos creó.
    /// </summary>
    public static int CrearPois(ContenidoPiso datos, FloorManager pisos)
    {
        if (datos == null || pisos == null) return 0;

        PointOfInterest.OlvidarRegistro();
        int creados = 0;

        for (int i = 0; i < datos.pois.Count; i++)
        {
            PoiDef def = datos.pois[i];
            if (def == null || string.IsNullOrEmpty(def.id)) continue;

            if (PointOfInterest.Buscar(def.id) != null) continue;   // gana el de la escena

            if (!AMundo(pisos, def.piso, def.u, def.v, out Vector3 punto))
            {
                Debug.LogWarning($"[Contenido] No pude ubicar el POI '{def.id}': no hay planta que medir.");
                continue;
            }

            int indice = Mathf.Clamp(def.piso - 1, 0, pisos.CantidadDePisos - 1);
            Transform grupo = Grupo(pisos.ContenidoDelPiso(indice), NombreDelGrupoDePois);
            if (grupo == null) continue;

            var go = new GameObject("POI_" + def.id);
            go.transform.SetParent(grupo, false);
            go.transform.position = punto;

            if (!PointOfInterest.CategoriaDesdeTexto(def.categoria, out PoiCategory categoria))
            {
                Debug.LogWarning($"[Contenido] Categoría '{def.categoria}' desconocida en el POI " +
                                 $"'{def.id}'. Se usa 'Otro'.");
                categoria = PoiCategory.Otro;
            }

            // El rótulo va sobre la puerta si el JSON dice dónde queda.
            Vector3 rotulo = new Vector3(0f, 2.45f, 0f);
            bool tienePuerta = Mathf.Abs(def.rotuloU) > 0.0001f || Mathf.Abs(def.rotuloV) > 0.0001f;
            if (tienePuerta && AMundo(pisos, def.piso, def.rotuloU, def.rotuloV, out Vector3 puerta))
            {
                rotulo = (puerta - punto) + new Vector3(0f, 2.45f, 0f);
            }

            var poi = go.AddComponent<PointOfInterest>();
            poi.Configurar(def.id, string.IsNullOrEmpty(def.nombre) ? def.id : def.nombre,
                           categoria, def.radio > 0f ? def.radio : 1.5f,
                           def.importante, def.rotulo, rotulo);
            creados++;
        }

        PointOfInterest.OlvidarRegistro();
        return creados;
    }

    /// <summary>Un hijo con ese nombre, creándolo si falta.</summary>
    public static Transform Grupo(Transform padre, string nombre)
    {
        if (padre == null) return null;
        Transform hijo = padre.Find(nombre);
        if (hijo != null) return hijo;

        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        return go.transform;
    }
}
