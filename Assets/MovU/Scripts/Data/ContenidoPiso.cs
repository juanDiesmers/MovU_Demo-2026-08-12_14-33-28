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

    // --- Ambientacion, inventario y escalera (octubre de 2026) -----------
    public List<ObjetoDef> objetos = new List<ObjetoDef>();
    public List<PuertaDef> puertas = new List<PuertaDef>();
    public List<MuebleDef> mobiliario = new List<MuebleDef>();
    public EscaleraDef escalera = new EscaleraDef();
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

    [Tooltip("Cielo raso con reticula y lamparas. Sin esto queda la losa lisa del relleno.")]
    public bool techo = true;
    [Tooltip("Puertas en los vanos. Cerradas tapan la vista al interior de los salones: " +
             "apagalas si la prueba necesita los vanos abiertos.")]
    public bool puertas = true;
    [Tooltip("Mostradores y demas mobiliario del JSON.")]
    public bool mobiliario = true;
    [Tooltip("Pasos, voces, puertas y avisos sonoros.")]
    public bool sonido = true;
    [Tooltip("Volumen general de 0 a 1.")]
    public float volumen = 0.8f;
    [Tooltip("Inventario del jugador (tecla I).")]
    public bool inventario = true;
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
    [Tooltip("id del objeto que el jugador recibe al completar la mision. Vacio = ninguno.")]
    public string entrega = "";
    [Tooltip("Lo que se le dice al jugador al recibir el objeto.")]
    public string mensajeDeEntrega = "";
}

/// <summary>Un objeto del inventario (el carne, el portatil prestado...).</summary>
[Serializable]
public class ObjetoDef
{
    public string id = "";
    public string nombre = "";
    [Tooltip("Admite {participante}: se cambia por el identificador de la sesion.")]
    public string descripcion = "";
    [Tooltip("Dos o tres letras que se dibujan en la casilla (no hay iconos).")]
    public string sigla = "";
    [Tooltip("Color de la casilla en hexadecimal, sin '#'. Por ejemplo 1F4E9A.")]
    public string color = "5A6B7C";
    [Tooltip("El jugador lo trae desde el principio.")]
    public bool inicial = false;
}

/// <summary>
/// Una puerta de dos hojas en un vano. (u, v) es el centro del vano; 'yaw' es
/// hacia donde mira la normal de la puerta (90 = el vano corre a lo largo de Z).
/// </summary>
[Serializable]
public class PuertaDef
{
    public string id = "";
    [Tooltip("id del POI al que da paso. Su rotulo se cuelga del dintel. Vacio = ninguno.")]
    public string poi = "";
    [Tooltip("0 = en todos los pisos (son copias del mismo). 1..N = solo en ese.")]
    public int piso = 0;
    public float u = 0.5f;
    public float v = 0.5f;
    public float yaw = 90f;
    [Tooltip("Ancho libre del vano en metros.")]
    public float ancho = 2.4f;
    [Tooltip("'salon', 'dti' o 'emergencia': cambia el color de las hojas.")]
    public string estilo = "salon";
}

[Serializable]
public class MuebleDef
{
    [Tooltip("'mostrador' (con portatiles) es el unico tipo por ahora.")]
    public string tipo = "mostrador";
    public int piso = 1;
    public float u = 0.5f;
    public float v = 0.5f;
    public float yaw = 0f;
}

[Serializable]
public class RectUV
{
    public float u0, u1, v0, v1;
}

/// <summary>
/// La escalera del modelo, hecha funcional. Es una escalera en U:
///   - 'tramo': el tramo visible, que sube desde el suelo (vPie) hasta la altura
///     del rellano (vCima) avanzando en +v.
///   - 'rellano': el descanso a media altura.
///   - El segundo tramo (del rellano al piso de arriba) queda oculto tras dos
///     tabiques con puerta: la del rellano ("subir") y la del hall ("bajar").
/// Las alturas van en metros y dependen de la escala vertical del edificio: si
/// cambia, hay que volver a medir con 'MovU > Contenido > Sondear la escalera'.
/// </summary>
[Serializable]
public class EscaleraDef
{
    public bool usar = false;
    public float alturaDelRellano = 3.55f;
    [Tooltip("Altura libre sobre el rellano. El rellano queda a media altura entre dos " +
             "pisos, mas arriba que el techo normal le dejaria: la caja de la escalera " +
             "lleva su propio techo, mas alto.")]
    public float alturaLibreSobreElRellano = 2.85f;
    public int peldanos = 18;
    public RectUV tramo = new RectUV();
    public RectUV rellano = new RectUV();
    [Tooltip("u donde empieza el hueco del tramo oculto (los tabiques van de aqui a tramo.u0).")]
    public float ocultoU0 = 0f;
    [Tooltip("u del centro de las dos puertas de la escalera.")]
    public float puertaU = 0f;
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
