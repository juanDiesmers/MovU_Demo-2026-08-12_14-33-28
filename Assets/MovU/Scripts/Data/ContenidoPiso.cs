using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// ContenidoPiso.cs — El contenido del piso como DATOS (POIs, misiones, NPCs)
// ============================================================================
// Todo lo que cambia cuando el equipo hace el levantamiento de campo vive en un
// archivo de texto:  Assets/MovU/Resources/MovU/contenido_piso9.json
//
// Agregar un POI, una mision o un NPC es anadir un bloque a ese archivo: no hay
// que tocar codigo ni la escena (SRS, RM-2: "las misiones se definen como
// datos").
//
// Las posiciones van NORMALIZADAS sobre la planta: u = 0..1 a lo ancho (eje X)
// y v = 0..1 a lo largo (eje Z). Asi siguen valiendo aunque cambie la escala
// del edificio, que sigue siendo una decision abierta. El menu
// 'MovU > Contenido > Copiar posicion de la seleccion como u,v' da esos dos
// numeros para cualquier objeto de la escena.
// ============================================================================

[Serializable]
public class ContenidoPiso
{
    public int version = 1;
    public string nombre = "Piso 9";
    public AjustesDeContenido ajustes = new AjustesDeContenido();
    public PuntoDeAparicion aparicion = new PuntoDeAparicion();
    public SitioDelAscensor ascensor = new SitioDelAscensor();
    public List<PoiDef> pois = new List<PoiDef>();
    public List<MisionDef> misiones = new List<MisionDef>();
    public List<NpcDef> npcs = new List<NpcDef>();
    public MultitudDef multitud = new MultitudDef();
    public List<PuntoUV> recorridos = new List<PuntoUV>();
}

[Serializable]
public class AjustesDeContenido
{
    [Tooltip("Pide el identificador del participante antes de la primera mision.")]
    public bool pedirParticipante = true;
    [Tooltip("Rotulos con el nombre de cada espacio sobre su puerta.")]
    public bool rotulos = true;
    public float distanciaRotulos = 16f;
    [Tooltip("Si es false, los NPC saludan pero no dicen por donde se va.")]
    public bool npcDanIndicaciones = true;
    [Tooltip("Numero que ve el jugador para el piso mas bajo del modelo (9 = 'Piso 9').")]
    public int numeroDelPrimerPiso = 1;
}

[Serializable]
public class PuntoDeAparicion
{
    public bool usar = false;
    public int piso = 1;
    public float u = 0.5f;
    public float v = 0.5f;
    public float yaw = 0f;
}

/// <summary>
/// Dónde va el ascensor en la planta (el mismo punto en todos los pisos).
/// Lo usa el menú que construye el edificio; en tiempo de ejecución no se
/// mueve, porque la geometría del edificio es estática.
/// </summary>
[Serializable]
public class SitioDelAscensor
{
    public bool usar = false;
    public float u = 0.5f;
    public float v = 0.5f;
    [Tooltip("Hacia dónde mira la puerta, en grados.")]
    public float yaw = 0f;
}

[Serializable]
public class PuntoUV
{
    public float u;
    public float v;
}

[Serializable]
public class PoiDef
{
    public string id = "";
    public string nombre = "";
    public string categoria = "Otro";
    public int piso = 1;
    public float u = 0.5f;
    public float v = 0.5f;
    [Tooltip("Donde va el rotulo (normalmente la puerta). Si los dos son 0, va sobre el POI.")]
    public float rotuloU = 0f;
    public float rotuloV = 0f;
    public float radio = 1.5f;
    public bool importante = false;
    public bool rotulo = true;
}

[Serializable]
public class MisionDef
{
    public string id = "";
    public string enunciado = "";
    [Tooltip("id del POI de destino.")]
    public string poi = "";
    [Tooltip("id del POI desde donde empieza. Vacio = donde este el jugador. " +
             "'aparicion' = el punto de aparicion del piso.")]
    public string partida = "";
    [Tooltip("Modo de guia con el que arranca: Off, Direct o NavMesh. Vacio = no cambiarlo.")]
    public string guia = "";
}

[Serializable]
public class NpcDef
{
    public string id = "";
    public string rol = "Estudiante";
    public string nombre = "";
    public int piso = 1;
    public float u = 0.5f;
    public float v = 0.5f;
    public float yaw = 0f;
    [Tooltip("'quieto' o 'deambula'.")]
    public string conducta = "quieto";
    public List<string> frases = new List<string>();
}

[Serializable]
public class MultitudDef
{
    [Tooltip("Misma semilla = misma gente en los mismos sitios para todos los participantes.")]
    public int semilla = 9;
    public int piso = 1;
    public int estudiantes = 0;
    public int visitantes = 0;
}

/// <summary>Convierte entre (u, v) de la planta y metros de mundo.</summary>
public struct PlanoDePlanta
{
    public float xMin, xMax, zMin, zMax;
    public bool valido;

    public Vector3 AMundo(float u, float v, float y)
    {
        return new Vector3(Mathf.LerpUnclamped(xMin, xMax, u), y,
                           Mathf.LerpUnclamped(zMin, zMax, v));
    }

    public Vector2 AUV(Vector3 mundo)
    {
        float ancho = Mathf.Max(0.0001f, xMax - xMin);
        float largo = Mathf.Max(0.0001f, zMax - zMin);
        return new Vector2((mundo.x - xMin) / ancho, (mundo.z - zMin) / largo);
    }

    /// <summary>
    /// Mide la planta de un piso. Usa los limites de la MALLA llevados a mundo y
    /// no Renderer.bounds, porque un piso apagado por el FloorManager no
    /// actualiza los limites de sus renderers.
    /// Si existe un hijo 'Planta' se mide solo ese: es el modelo de Meshy, que
    /// es contra el que se sacaron las coordenadas del JSON.
    /// </summary>
    public static PlanoDePlanta Medir(Transform piso)
    {
        var p = new PlanoDePlanta { valido = false };
        if (piso == null) return p;

        Transform raiz = piso.Find("Planta");
        if (raiz == null) raiz = piso;

        bool primero = true;
        var filtros = raiz.GetComponentsInChildren<MeshFilter>(true);
        for (int f = 0; f < filtros.Length; f++)
        {
            Mesh malla = filtros[f].sharedMesh;
            if (malla == null) continue;

            Bounds b = malla.bounds;
            Matrix4x4 m = filtros[f].transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 esquina = b.center + Vector3.Scale(b.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
                Vector3 w = m.MultiplyPoint3x4(esquina);
                if (primero)
                {
                    p.xMin = p.xMax = w.x;
                    p.zMin = p.zMax = w.z;
                    primero = false;
                }
                else
                {
                    if (w.x < p.xMin) p.xMin = w.x;
                    if (w.x > p.xMax) p.xMax = w.x;
                    if (w.z < p.zMin) p.zMin = w.z;
                    if (w.z > p.zMax) p.zMax = w.z;
                }
            }
        }

        p.valido = !primero && (p.xMax - p.xMin) > 0.01f && (p.zMax - p.zMin) > 0.01f;
        return p;
    }
}
