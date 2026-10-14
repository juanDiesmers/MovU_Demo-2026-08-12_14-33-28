using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// ============================================================================
// PlantaBuilder.cs — Construye un piso a partir de una planta en JSON
// ============================================================================
// Lo usan Piso3Builder (planta del piso 3) y BuildingSetup (planta del piso 9 y
// su copia, el 10). Cada planta sale de un JSON que genera un script de Tools/:
//
//   Models/Piso3/planta_piso3.json   <- Tools/piso3_planta.py
//   Models/Piso9/planta_piso9.json   <- Tools/piso9_planta.py
//
// Las coordenadas del JSON son metros del diagrama del piso 9 (u a lo ancho,
// v a lo largo), así que se llevan al mundo con el MISMO PlanoDePlanta que usa
// el contenido: todos los pisos quedan alineados y los ascensores en la misma
// vertical.
//
// El piso se arma con mallas propias, una por material, y una malla de
// colisión aparte con cajas simples (por ella se hornea el NavMesh).
//
// Debajo del piso queda un hijo 'Planta' con una malla sin dibujo que ocupa la
// huella entera del edificio: PlanoDePlanta.Medir lo usa, y así las (u, v) del
// JSON de contenido caen en el mismo sitio en todos los pisos.
//
// Lo que entiende del JSON (todo opcional salvo 'muros'):
//   suelo, techos (polígonos con altura y material), pisos (polígonos de color
//   sobre el suelo), muros (concreto, madera, blanco, negro, granito, vitrina,
//   vitrina_negra, fachada, vidrio; 'lamas' les pone lamas de madera), bloques,
//   escaleras (solo se ven), ascensores, torniquetes, muebles, rotulos,
//   bandejas, luces (lineales, emisivas) y lamparas (luces puntuales).
// ============================================================================

public static class PlantaBuilder
{
    /// <summary>De dónde sale una planta y dónde deja sus mallas y materiales.</summary>
    public sealed class Config
    {
        public string planta;              // "piso3", "piso9": lo que se le pasa al FloorManager
        public string etiqueta;            // para la consola: "Piso 3"
        public string rutaPlanta;
        public string carpetaMallas;
        public string carpetaMateriales;
        public string prefijoMaterial;     // "P3_", "P9_"
        public string[] carpetasTexturas;  // se busca en orden
        public Dictionary<string, string> texturas = new Dictionary<string, string>();
    }

    // ------------------------------------------------------------------
    // Datos del JSON
    // ------------------------------------------------------------------
    [Serializable] private class PV { public float v, u; }
    [Serializable] private class Muro { public float[] a; public float[] b; public string tipo; public float g = 0.3f; public string nombre; public bool lamas; }
    [Serializable] private class Bloque { public float v0, v1, u0, u1; public string tipo; public string nombre; public float alto; }
    [Serializable] private class Tramo { public float v0, v1, u0, u1; public string sube; public float h0, h1; public int n; }
    [Serializable] private class Rect4 { public float v0, v1, u0, u1; }
    [Serializable] private class Linea { public float[] a; public float[] b; public float y; public float ancho; }
    [Serializable] private class Escalera { public string nombre; public string estilo; public string letrero; public List<Tramo> tramos; public Rect4 nucleo; public List<Linea> barreras; }
    [Serializable] private class Asc { public float v, u, ancho; }
    [Serializable] private class Torn { public float v, u; }
    [Serializable] private class Lampara { public float v, u, y, rango, intensidad; }
    [Serializable] private class Poli { public string tipo; public float h; public string nombre; public List<PV> pts; }
    [Serializable] private class Mueble { public string tipo; public float v0, v1, u0, u1, alto, y0; public bool solido = true; }
    [Serializable] private class Rotulo { public string texto; public float v, u, h; public string mira; public float tam; public string color; }
    [Serializable] private class PuertaCerrada { public float v, u, ancho; public string eje; public string mira; public string texto; }

    /// <summary>Dónde quedaba el piso en el mundo (lo guarda el JSON del piso 9 para armar sin Meshy).</summary>
    [Serializable]
    public class Marco
    {
        public float xMin, xMax, zMin, zMax;
        public float[] pivote;
        public float alturaLosa, separacion;

        public bool Valido => xMax - xMin > 1f && zMax - zMin > 1f && pivote != null && pivote.Length == 3 && separacion > 1f;
        public Vector3 Pivote => new Vector3(pivote[0], pivote[1], pivote[2]);
        public PlanoDePlanta Plano => new PlanoDePlanta { xMin = xMin, xMax = xMax, zMin = zMin, zMax = zMax, valido = Valido };
    }

    [Serializable]
    private class PlantaDatos
    {
        public float ancho = 39.3f, largo = 106.6f, alturaTecho = 5.6f;
        /// <summary>Si es mayor que 0: madera y frentes de vidrio llegan hasta aquí y arriba va pared gris (piso 13).</summary>
        public float alturaPanel;
        public Marco marco;
        public List<PV> suelo; public List<Muro> muros; public List<Bloque> bloques;
        public List<Escalera> escaleras; public List<Asc> ascensores; public List<Torn> torniquetes;
        public List<Linea> bandejas; public List<Linea> luces; public List<Lampara> lamparas;
        public List<Poli> techos; public List<Poli> pisos; public List<Mueble> muebles; public List<Rotulo> rotulos;
        public List<PuertaCerrada> puertasCerradas;
    }

    public static bool HayPlanta(Config cfg) => AssetDatabase.LoadAssetAtPath<TextAsset>(cfg.rutaPlanta) != null;

    /// <summary>El marco guardado en el JSON de la planta, o null si no lo trae.</summary>
    public static Marco LeerMarco(Config cfg)
    {
        var texto = AssetDatabase.LoadAssetAtPath<TextAsset>(cfg.rutaPlanta);
        if (texto == null) return null;
        PlantaDatos d = JsonUtility.FromJson<PlantaDatos>(texto.text);
        return d != null && d.marco != null && d.marco.Valido ? d.marco : null;
    }

    // ------------------------------------------------------------------
    // Mallas en construcción, una por material
    // ------------------------------------------------------------------
    private class Malla
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public float escalaUV = 2f;     // metros que cubre una repetición de la textura
        public int Triangulos => t.Count / 3;
    }

    private static Config cfg;
    private static Dictionary<string, Malla> mallas;
    private static Malla colision;
    private static PlantaDatos datos;
    private static PlanoDePlanta plano;
    private static float suelo;        // y de mundo de la cara pisable
    private static Vector3 origen;     // posición de mundo del hijo 'Geometria'
    private static Vector3 centroHuella;
    private static float techo;

    /// <summary>Altura hasta la que llegan madera y vidrio (todo el muro si la planta no la fija).</summary>
    private static float Panel => datos.alturaPanel > 0.5f && datos.alturaPanel < techo ? datos.alturaPanel : techo;

    private static Malla M(string clave)
    {
        if (!mallas.TryGetValue(clave, out Malla m))
        {
            m = new Malla();
            m.escalaUV = clave == "Piso" || clave == "Terrazo" ? 3f : 2f;
            mallas[clave] = m;
        }
        return m;
    }

    /// <summary>Metros del diagrama (v, u) a mundo, a la altura h sobre el suelo.</summary>
    private static Vector3 W(float v, float u, float h)
    {
        return plano.AMundo(u / datos.ancho, v / datos.largo, suelo + h);
    }

    // ------------------------------------------------------------------
    // Punto de entrada
    // ------------------------------------------------------------------
    /// <summary>
    /// Arma el piso bajo 'padre'. 'pivote' es la posición de mundo de la raíz del
    /// piso y 'alturaLosa' la altura de la cara pisable sobre ese pivote, igual
    /// que en los demás pisos. 'prefijoMallas' distingue las mallas guardadas de
    /// dos pisos con la misma planta (Piso9_, Piso10_).
    /// </summary>
    /// <summary>Número del piso que se está armando ("04", "10"...): reemplaza "{n}" en los rótulos.</summary>
    private static string numeroDelPiso = "";

    /// <summary>Como Construir, para un piso genérico: 'numero' va en los rótulos que dicen "{n}".</summary>
    public static Transform Construir(Config config, string prefijoMallas, Transform padre, string nombre,
                                      Vector3 pivote, float alturaLosa, PlanoDePlanta planoBase,
                                      float alturaDelTecho, string numero)
    {
        numeroDelPiso = numero ?? "";
        try { return Construir(config, prefijoMallas, padre, nombre, pivote, alturaLosa, planoBase, alturaDelTecho); }
        finally { numeroDelPiso = ""; }
    }

    public static Transform Construir(Config config, string prefijoMallas, Transform padre, string nombre,
                                      Vector3 pivote, float alturaLosa, PlanoDePlanta planoBase,
                                      float alturaDelTecho)
    {
        var texto = AssetDatabase.LoadAssetAtPath<TextAsset>(config.rutaPlanta);
        if (texto == null || !planoBase.valido) return null;

        cfg = config;
        datos = JsonUtility.FromJson<PlantaDatos>(texto.text);
        if (datos == null || datos.muros == null) return null;

        plano = planoBase;
        mallas = new Dictionary<string, Malla>();
        colision = new Malla();
        techo = alturaDelTecho > 2.5f ? alturaDelTecho : datos.alturaTecho;

        var raiz = new GameObject(nombre);
        raiz.transform.SetParent(padre, false);
        raiz.transform.position = pivote;
        suelo = pivote.y + alturaLosa;

        var geo = new GameObject("Geometria");
        geo.transform.SetParent(raiz.transform, false);
        geo.transform.position = new Vector3(pivote.x, suelo, pivote.z);
        origen = geo.transform.position;

        centroHuella = origen;
        if (datos.suelo != null && datos.suelo.Count > 0)
        {
            centroHuella = Vector3.zero;
            foreach (PV p in datos.suelo) centroHuella += W(p.v, p.u, 0f);
            centroHuella /= datos.suelo.Count;
        }

        ArmarSueloYTecho();
        if (datos.pisos != null) foreach (Poli p in datos.pisos) ArmarPoligono(p, true);
        if (datos.techos != null) foreach (Poli p in datos.techos) ArmarPoligono(p, false);
        foreach (Muro m in datos.muros) ArmarMuro(m);
        if (datos.bloques != null) foreach (Bloque b in datos.bloques) ArmarBloque(b);
        if (datos.escaleras != null) foreach (Escalera e in datos.escaleras) ArmarEscalera(e, geo.transform);
        if (datos.ascensores != null) foreach (Asc a in datos.ascensores) ArmarAscensor(a);
        if (datos.torniquetes != null) foreach (Torn t in datos.torniquetes) ArmarTorniquete(t);
        if (datos.muebles != null) foreach (Mueble m in datos.muebles) ArmarMueble(m);
        if (datos.bandejas != null) foreach (Linea l in datos.bandejas) ArmarBandeja(l);
        if (datos.luces != null) foreach (Linea l in datos.luces) ArmarLuz(l);
        if (datos.rotulos != null) foreach (Rotulo r in datos.rotulos) ArmarRotulo(r, geo.transform);
        if (datos.puertasCerradas != null) foreach (PuertaCerrada p in datos.puertasCerradas) ArmarPuertaCerrada(p, geo.transform);

        int total = Volcar(geo.transform, prefijoMallas);
        CrearLamparas(raiz.transform);
        CrearHuella(raiz.transform, prefijoMallas);

        Debug.Log($"[{cfg.etiqueta}] Construido desde {cfg.rutaPlanta}: {datos.muros.Count} muros, " +
                  $"{(datos.bloques != null ? datos.bloques.Count : 0)} bloques, " +
                  $"{(datos.muebles != null ? datos.muebles.Count : 0)} muebles, {total:N0} triángulos.");
        return raiz.transform;
    }

    // ------------------------------------------------------------------
    // Piezas
    // ------------------------------------------------------------------
    private static void ArmarSueloYTecho()
    {
        if (datos.suelo == null || datos.suelo.Count < 3) return;

        var puntos = new List<Vector3>();
        foreach (PV p in datos.suelo) puntos.Add(W(p.v, p.u, 0f));

        Poligono(M("Piso"), puntos, 0f, true);
        // Si la planta trae sus techos (con huecos y cielos rasos), se usan esos.
        if (datos.techos == null || datos.techos.Count == 0) Poligono(M("Techo"), puntos, techo, false);

        // Colisión del suelo: una losa de 30 cm bajo la huella.
        Bounds b = new Bounds(puntos[0], Vector3.zero);
        foreach (Vector3 p in puntos) b.Encapsulate(p);
        Caja(colision, new Vector3(b.center.x, suelo - 0.15f, b.center.z),
             new Vector3(b.size.x + 1f, 0.3f, b.size.z + 1f), Quaternion.identity);
    }

    /// <summary>Un polígono convexo de color: sobre el suelo (pisos) o mirando hacia abajo (techos).</summary>
    private static void ArmarPoligono(Poli p, bool haciaArriba)
    {
        if (p == null || p.pts == null || p.pts.Count < 3) return;
        var puntos = new List<Vector3>();
        foreach (PV q in p.pts) if (q != null) puntos.Add(W(q.v, q.u, 0f));
        if (puntos.Count < 3) return;
        float h = haciaArriba ? Mathf.Max(0.002f, p.h) : Mathf.Min(p.h > 0.5f ? p.h : techo, techo);
        string clave = string.IsNullOrEmpty(p.tipo) ? (haciaArriba ? "Piso" : "Techo") : p.tipo;
        Poligono(M(clave), puntos, h, haciaArriba);
    }

    private static void ArmarMuro(Muro m)
    {
        if (m.a == null || m.b == null || m.a.Length < 2 || m.b.Length < 2) return;
        Vector3 a = W(m.a[0], m.a[1], 0f);
        Vector3 b = W(m.b[0], m.b[1], 0f);
        float g = m.g > 0.01f ? m.g : 0.3f;

        // La colisión es siempre el muro entero, sea del material que sea.
        Segmento(colision, a, b, g, 0f, techo);

        switch (m.tipo)
        {
            case "madera":
                Segmento(M("Madera"), a, b, g, 0f, Panel);
                if (Panel < techo) Segmento(M("Gris"), a, b, g, Panel, techo);
                break;
            case "pizarra": Segmento(M("Pizarra"), a, b, g, 0f, techo); break;
            case "mostaza": Segmento(M("Mostaza"), a, b, g, 0f, techo); break;
            case "gris": Segmento(M("Gris"), a, b, g, 0f, techo); break;
            case "blanco": Segmento(M("Blanco"), a, b, g, 0f, techo); break;
            case "negro": Segmento(M("Negro"), a, b, g, 0f, techo); break;
            case "granito": Segmento(M("Granito"), a, b, g, 0f, techo); break;

            case "vitrina":                     // frente de salón: antepecho de madera y vidrio
                Segmento(M("Madera"), a, b, g, 0f, 0.9f);
                Segmento(M("MaderaClara"), a, b, g + 0.06f, 0.9f, 0.96f);
                Segmento(M("Vidrio"), a, b, 0.05f, 0.96f, techo);
                Montantes(a, b, 2.6f, 0.96f, techo, 0.09f);
                Segmento(M("Marco"), a, b, 0.1f, 3.05f, 3.13f);
                break;

            case "vitrina_negra":               // piso 9: antepecho negro, vidrio y franja negra hasta el techo
            {
                float arriba = Mathf.Min(3.2f, techo - 0.1f);
                Segmento(M("Negro"), a, b, g, 0f, 1.05f);
                Segmento(M("Marco"), a, b, g + 0.04f, 1.05f, 1.1f);
                Segmento(M("Vidrio"), a, b, 0.05f, 1.1f, arriba);
                Montantes(a, b, 1.8f, 1.1f, arriba, 0.07f);
                Segmento(M("Negro"), a, b, g, arriba, Mathf.Max(arriba, Panel));
                if (Panel < techo) Segmento(M("Gris"), a, b, g, Mathf.Max(arriba, Panel), techo);
                break;
            }

            case "vidrio_dorado":               // piso 13: vidrio claro con perfiles dorados (oficinas, Tejidos)
            {
                float arriba = Mathf.Min(3.0f, techo - 0.1f);
                Segmento(M("Vidrio"), a, b, 0.04f, 0.02f, arriba);
                Montantes(a, b, 1.6f, 0f, arriba, 0.06f, "Dorado");
                Segmento(M("Dorado"), a, b, 0.1f, arriba, arriba + 0.08f);
                Segmento(M("Dorado"), a, b, 0.1f, 0f, 0.06f);
                Segmento(M(datos.alturaPanel > 0.1f ? "Gris" : "Blanco"), a, b, g, arriba + 0.08f, techo);
                break;
            }

            case "fachada_dorada":              // piso 13: muro cortina de doble altura con perfiles dorados
            {
                Segmento(M("Concreto"), a, b, g, 0f, 0.15f);
                Segmento(M("Vidrio"), a, b, 0.05f, 0.15f, techo);
                Montantes(a, b, 2.4f, 0.15f, techo, 0.16f, "Dorado");
                float medio = Mathf.Min(3.4f, techo - 1f);
                Segmento(M("Dorado"), a, b, 0.5f, medio, medio + 0.5f);       // el travesaño ancho de las fotos
                Segmento(M("Dorado"), a, b, 0.2f, techo - 0.12f, techo);
                break;
            }

            case "fachada":                     // muro cortina hacia el exterior
                Segmento(M("Concreto"), a, b, g, 0f, 0.6f);
                Segmento(M("Vidrio"), a, b, 0.05f, 0.6f, techo);
                Montantes(a, b, 3.0f, 0.6f, techo, 0.12f);
                Segmento(M("Marco"), a, b, 0.12f, 3.2f, 3.3f);
                break;

            case "vidrio":
                Segmento(M("Vidrio"), a, b, 0.05f, 0f, techo);
                Montantes(a, b, 2.0f, 0f, techo, 0.08f);
                break;

            default:
                Segmento(M("Concreto"), a, b, g, 0f, techo);
                break;
        }

        if (m.lamas) Lamas(a, b, g);
    }

    /// <summary>Lamas verticales de madera, por fuera del vidrio (hacia el lado contrario al centro del edificio).</summary>
    private static void Lamas(Vector3 a, Vector3 b, float grueso)
    {
        Vector3 d = b - a; d.y = 0f;
        float largo = d.magnitude;
        if (largo < 0.5f) return;
        Vector3 dir = d / largo;
        Vector3 fuera = Vector3.Cross(Vector3.up, dir);
        Vector3 medio = (a + b) * 0.5f; medio.y = 0f;
        Vector3 haciaCentro = centroHuella - medio; haciaCentro.y = 0f;
        if (Vector3.Dot(fuera, haciaCentro) > 0f) fuera = -fuera;

        const float paso = 0.9f;
        int n = Mathf.Max(1, Mathf.FloorToInt(largo / paso));
        float sobra = (largo - (n - 1) * paso) * 0.5f;
        Quaternion rot = Quaternion.LookRotation(fuera, Vector3.up);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = a + dir * (sobra + i * paso) + fuera * (grueso * 0.5f + 0.22f);
            Caja(M("Lama"), new Vector3(p.x, suelo + techo * 0.5f, p.z), new Vector3(0.07f, techo, 0.34f), rot);
        }
    }

    private static void Montantes(Vector3 a, Vector3 b, float paso, float y0, float y1, float grueso,
                                  string clave = "Marco")
    {
        Vector3 d = b - a; d.y = 0f;
        float largo = d.magnitude;
        if (largo < 0.1f) return;
        int n = Mathf.Max(1, Mathf.RoundToInt(largo / paso));
        Quaternion rot = Quaternion.LookRotation(d.normalized, Vector3.up);
        for (int i = 0; i <= n; i++)
        {
            Vector3 p = a + d * (i / (float)n);
            Caja(M(clave), new Vector3(p.x, suelo + (y0 + y1) * 0.5f, p.z),
                 new Vector3(grueso, y1 - y0, grueso), rot);
        }
    }

    private static void ArmarBloque(Bloque b)
    {
        float alto = b.alto > 0.1f ? Mathf.Min(b.alto, techo) : techo;
        Vector3 p0 = W(b.v0, b.u0, 0f), p1 = W(b.v1, b.u1, 0f);
        Vector3 c = (p0 + p1) * 0.5f;
        Vector3 s = new Vector3(Mathf.Abs(p1.x - p0.x), alto, Mathf.Abs(p1.z - p0.z));
        c.y = suelo + alto * 0.5f;
        string clave;
        switch (b.tipo)
        {
            case "madera": clave = "Madera"; break;
            case "negro": clave = "Negro"; break;
            case "negro_brillante": clave = "NegroBrillante"; break;
            case "blanco": clave = "Blanco"; break;
            case "granito": clave = "Granito"; break;
            case "pizarra": clave = "Pizarra"; break;
            case "gris": clave = "Gris"; break;
            default: clave = "Concreto"; break;
        }
        Caja(M(clave), c, s, Quaternion.identity);
        Caja(colision, c, s, Quaternion.identity);
    }

    private static void ArmarMueble(Mueble m)
    {
        if (m == null || m.alto <= 0.005f) return;
        Vector3 p0 = W(m.v0, m.u0, 0f), p1 = W(m.v1, m.u1, 0f);
        Vector3 c = (p0 + p1) * 0.5f;
        Vector3 s = new Vector3(Mathf.Max(0.01f, Mathf.Abs(p1.x - p0.x)), m.alto, Mathf.Max(0.01f, Mathf.Abs(p1.z - p0.z)));
        c.y = suelo + m.y0 + m.alto * 0.5f;
        Caja(M(string.IsNullOrEmpty(m.tipo) ? "Mesa" : m.tipo), c, s, Quaternion.identity);
        if (m.solido) Caja(colision, c, s, Quaternion.identity);
    }

    /// <summary>
    /// Una puerta cerrada que tapa un vano (pisos genéricos: los salones todavía no
    /// tienen nada). Hoja de madera con marco, el muro de encima hasta el techo, y en
    /// la colisión el vano entero cerrado: no se puede entrar y el NavMesh no pasa.
    /// </summary>
    private static void ArmarPuertaCerrada(PuertaCerrada p, Transform geo)
    {
        if (p == null || p.ancho < 0.5f) return;
        Vector3 c = W(p.v, p.u, 0f);
        Vector3 d = (p.eje == "u" ? W(p.v, p.u + 1f, 0f) : W(p.v + 1f, p.u, 0f)) - c;
        d.y = 0f; d.Normalize();
        Vector3 a = c - d * (p.ancho * 0.5f), b = c + d * (p.ancho * 0.5f);
        Quaternion rot = Quaternion.LookRotation(d, Vector3.up);
        const float alto = 2.36f;

        Segmento(colision, a, b, 0.3f, 0f, techo);
        Caja(M("Madera"), new Vector3(c.x, suelo + alto * 0.5f, c.z), new Vector3(0.07f, alto - 0.06f, p.ancho - 0.14f), rot);
        for (int lado = -1; lado <= 1; lado += 2)
        {
            Vector3 j = c + d * (lado * (p.ancho * 0.5f - 0.045f));
            Caja(M("Marco"), new Vector3(j.x, suelo + alto * 0.5f, j.z), new Vector3(0.2f, alto, 0.09f), rot);
        }
        Caja(M("Marco"), new Vector3(c.x, suelo + alto - 0.045f, c.z), new Vector3(0.2f, 0.09f, p.ancho), rot);
        Segmento(M("Madera"), a, b, 0.3f, alto, Panel);
        if (Panel < techo) Segmento(M("Gris"), a, b, 0.3f, Panel, techo);

        // Manija y letrero del lado del pasillo.
        Vector3 fuera = p.eje == "u" ? (p.mira == "-v" ? -1f : 1f) * (W(p.v + 1f, p.u, 0f) - c)
                                     : (p.mira == "-u" ? -1f : 1f) * (W(p.v, p.u + 1f, 0f) - c);
        fuera.y = 0f; fuera.Normalize();
        Vector3 m = c + d * (p.ancho * 0.5f - 0.35f) + fuera * 0.06f;
        Caja(M("Metal"), new Vector3(m.x, suelo + 1.05f, m.z), new Vector3(0.04f, 0.32f, 0.04f), rot);
        Vector3 t = c + fuera * 0.05f;
        Letrero(geo, "Cerrado", string.IsNullOrEmpty(p.texto) ? "CERRADO" : p.texto,
                new Vector3(t.x, suelo + 1.75f, t.z), Quaternion.LookRotation(-fuera), 1.2f,
                new Color(0.95f, 0.92f, 0.85f), FontStyles.Bold);
    }

    private static void ArmarRotulo(Rotulo r, Transform geo)
    {
        if (r == null || string.IsNullOrEmpty(r.texto)) return;
        Vector3 p = W(r.v, r.u, r.h > 0.1f ? r.h : 2f);
        // El texto se lee mirándolo desde el lado 'mira': su adelante apunta al lado contrario.
        Vector3 haciaAtras;
        switch (r.mira)
        {
            case "-v": haciaAtras = W(r.v + 1f, r.u, 0f) - W(r.v, r.u, 0f); break;
            case "+u": haciaAtras = W(r.v, r.u - 1f, 0f) - W(r.v, r.u, 0f); break;
            case "-u": haciaAtras = W(r.v, r.u + 1f, 0f) - W(r.v, r.u, 0f); break;
            default: haciaAtras = W(r.v - 1f, r.u, 0f) - W(r.v, r.u, 0f); break;
        }
        haciaAtras.y = 0f;
        Color color = new Color(1f, 0.92f, 0.7f);
        if (!string.IsNullOrEmpty(r.color)) ColorUtility.TryParseHtmlString("#" + r.color.TrimStart('#'), out color);
        string texto = r.texto.Replace("{n}", numeroDelPiso);
        Letrero(geo, texto, texto, p, Quaternion.LookRotation(haciaAtras.normalized), r.tam > 0.1f ? r.tam : 2.2f,
                color, FontStyles.Normal);
    }

    private static void ArmarEscalera(Escalera e, Transform geo)
    {
        bool roja = e.estilo == "roja";
        string matLado = roja ? "Rojo" : "Concreto";
        Vector3 centroEscalera = Vector3.zero;
        foreach (Tramo t in e.tramos) centroEscalera += W((t.v0 + t.v1) * 0.5f, (t.u0 + t.u1) * 0.5f, 0f);
        if (e.tramos.Count > 0) centroEscalera /= e.tramos.Count;

        foreach (Tramo t in e.tramos)
        {
            Vector3 p0 = W(t.v0, t.u0, 0f), p1 = W(t.v1, t.u1, 0f);
            float xMin = Mathf.Min(p0.x, p1.x), xMax = Mathf.Max(p0.x, p1.x);
            float zMin = Mathf.Min(p0.z, p1.z), zMax = Mathf.Max(p0.z, p1.z);

            // No se camina por la escalera (no lleva a ningún piso del juego): su
            // huella entera es un obstáculo, alto hasta el techo.
            Caja(colision, new Vector3((xMin + xMax) * 0.5f, suelo + techo * 0.5f, (zMin + zMax) * 0.5f),
                 new Vector3(xMax - xMin, techo, zMax - zMin), Quaternion.identity);

            if (t.n <= 0 || string.IsNullOrEmpty(t.sube))
            {
                // Descanso: un bloque macizo hasta su altura.
                Caja(M("Escalon"), new Vector3((xMin + xMax) * 0.5f, suelo + t.h1 * 0.5f, (zMin + zMax) * 0.5f),
                     new Vector3(xMax - xMin, Mathf.Max(0.05f, t.h1), zMax - zMin), Quaternion.identity);
                continue;
            }

            // Dirección en la que sube, ya en mundo: se mide contra el plano.
            Vector3 desde, hacia;
            float cv = (t.v0 + t.v1) * 0.5f, cu = (t.u0 + t.u1) * 0.5f;
            switch (t.sube)
            {
                case "+v": desde = W(t.v0, cu, 0f); hacia = W(t.v1, cu, 0f); break;
                case "-v": desde = W(t.v1, cu, 0f); hacia = W(t.v0, cu, 0f); break;
                case "+u": desde = W(cv, t.u0, 0f); hacia = W(cv, t.u1, 0f); break;
                default:   desde = W(cv, t.u1, 0f); hacia = W(cv, t.u0, 0f); break;
            }
            Vector3 dir = hacia - desde; dir.y = 0f;
            float largo = dir.magnitude;
            dir /= Mathf.Max(0.001f, largo);
            bool alongX = Mathf.Abs(dir.x) > Mathf.Abs(dir.z);
            float anchoTramo = alongX ? (zMax - zMin) : (xMax - xMin);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            float paso = largo / t.n;
            float sube = (t.h1 - t.h0) / t.n;
            for (int k = 0; k < t.n; k++)
            {
                float alto = t.h0 + sube * (k + 1);
                Vector3 c = desde + dir * (paso * (k + 0.5f));
                c.y = suelo + alto * 0.5f;
                Caja(M("Escalon"), c, new Vector3(anchoTramo, alto, paso), rot);
            }

            // Barandas a los dos lados, inclinadas como el tramo.
            float pendiente = Mathf.Atan2(t.h1 - t.h0, largo) * Mathf.Rad2Deg;
            Vector3 lateral = Vector3.Cross(Vector3.up, dir) * (anchoTramo * 0.5f);
            Vector3 medio = (desde + hacia) * 0.5f;
            float hm = (t.h0 + t.h1) * 0.5f;
            Quaternion inclinada = rot * Quaternion.Euler(-pendiente, 0f, 0f);
            float largoInclinado = Mathf.Sqrt(largo * largo + (t.h1 - t.h0) * (t.h1 - t.h0));
            for (int lado = -1; lado <= 1; lado += 2)
            {
                Vector3 c = medio + lateral * lado;
                c.y = suelo + hm + 0.45f;
                Caja(M(matLado), c, new Vector3(0.08f, 0.9f, largoInclinado), inclinada);
                Vector3 pasamanos = c; pasamanos.y += 0.5f;
                Caja(M("Rojo"), pasamanos, new Vector3(0.07f, 0.07f, largoInclinado), inclinada);
            }
        }

        if (e.nucleo != null && e.nucleo.v1 - e.nucleo.v0 > 0.01f)
        {
            Vector3 p0 = W(e.nucleo.v0, e.nucleo.u0, 0f), p1 = W(e.nucleo.v1, e.nucleo.u1, 0f);
            Vector3 c = (p0 + p1) * 0.5f;
            Vector3 s = new Vector3(Mathf.Max(0.15f, Mathf.Abs(p1.x - p0.x)), techo, Mathf.Max(0.15f, Mathf.Abs(p1.z - p0.z)));
            c.y = suelo + techo * 0.5f;
            Caja(M(roja ? "Mostaza" : "Concreto"), c, s, Quaternion.identity);
            Caja(colision, c, s, Quaternion.identity);
        }

        // La escalera no lleva a ningún piso del juego: se cierra al pie de cada tramo.
        if (e.barreras == null) return;
        string aviso = string.IsNullOrEmpty(e.letrero)
            ? "Escalera a los pisos 2 y 4\nno disponible en MovU\nusa el ascensor"
            : e.letrero;
        for (int i = 0; i < e.barreras.Count; i++)
        {
            Linea l = e.barreras[i];
            if (l == null || l.a == null || l.b == null) continue;
            Vector3 a = W(l.a[0], l.a[1], 0f);
            Vector3 b = W(l.b[0], l.b[1], 0f);
            Segmento(M("Rojo"), a, b, 0.06f, 0.95f, 1.05f);
            Segmento(M("Metal"), a, a + (b - a) * 0.03f, 0.08f, 0f, 1.05f);
            Segmento(M("Metal"), b - (b - a) * 0.03f, b, 0.08f, 0f, 1.05f);
            Segmento(colision, a, b, 0.3f, 0f, 2.2f);

            // El letrero mira hacia quien llega: del lado contrario al núcleo de la escalera.
            Vector3 centro = (a + b) * 0.5f;
            Vector3 d = b - a; d.y = 0f;
            Vector3 normal = Vector3.Cross(d, Vector3.up).normalized;
            Vector3 haciaEscalera = centroEscalera - centro; haciaEscalera.y = 0f;
            if (Vector3.Dot(normal, haciaEscalera) < 0f) normal = -normal;
            Vector3 pos = centro - normal * 0.25f;
            if (i > 0) continue;               // un letrero por escalera basta
            Letrero(geo, $"{e.nombre}_{i + 1}", aviso,
                    new Vector3(pos.x, suelo + 1.75f, pos.z), Quaternion.LookRotation(normal),
                    2.2f, new Color(1f, 0.92f, 0.7f), FontStyles.Bold);
        }
    }

    private static void ArmarAscensor(Asc a)
    {
        float ancho = a.ancho > 0.5f ? a.ancho : 2.6f;
        Vector3 c = W(a.v, a.u, 0f);
        Vector3 hacia = W(a.v + 1f, a.u, 0f) - c; hacia.y = 0f; hacia.Normalize();
        Quaternion rot = Quaternion.LookRotation(hacia, Vector3.up);
        Vector3 frente = c + hacia * 0.04f;

        Caja(M("Marco"), new Vector3(frente.x, suelo + 1.55f, frente.z), new Vector3(ancho + 0.5f, 3.1f, 0.06f), rot);
        Vector3 hojas = c + hacia * 0.08f;
        Caja(M("Metal"), new Vector3(hojas.x, suelo + 1.45f, hojas.z), new Vector3(ancho, 2.9f, 0.05f), rot);
        Vector3 union = c + hacia * 0.11f;
        Caja(M("Marco"), new Vector3(union.x, suelo + 1.45f, union.z), new Vector3(0.03f, 2.9f, 0.02f), rot);
    }

    private static void ArmarTorniquete(Torn t)
    {
        Vector3 c = W(t.v, t.u, 0f);
        Vector3 s = new Vector3(0.9f, 1.0f, 0.3f);
        Vector3 p = new Vector3(c.x, suelo + 0.5f, c.z);
        Caja(M("Metal"), p, s, Quaternion.identity);
        Caja(colision, p, s, Quaternion.identity);
    }

    private static void ArmarBandeja(Linea l)
    {
        Vector3 a = W(l.a[0], l.a[1], 0f), b = W(l.b[0], l.b[1], 0f);
        float ancho = l.ancho > 0.05f ? l.ancho : 0.5f;
        Segmento(M("Metal"), a, b, ancho, l.y, l.y + 0.06f);
        // Tubos que cuelgan al lado: dan la lectura de "techo a la vista" de las fotos.
        Vector3 d = b - a; d.y = 0f;
        Vector3 lado = Vector3.Cross(Vector3.up, d.normalized) * (ancho * 0.5f + 0.25f);
        Segmento(M("TuboOscuro"), a + lado, b + lado, 0.14f, l.y + 0.18f, l.y + 0.32f);
    }

    private static void ArmarLuz(Linea l)
    {
        Vector3 a = W(l.a[0], l.a[1], 0f), b = W(l.b[0], l.b[1], 0f);
        float y = l.y > 0.1f ? Mathf.Min(l.y, techo - 0.05f) : techo - 0.1f;
        Segmento(M("Luz"), a, b, 0.07f, y - 0.04f, y);
    }

    private static void CrearLamparas(Transform raiz)
    {
        if (datos.lamparas == null || datos.lamparas.Count == 0) return;
        var grupo = new GameObject("Lamparas");
        grupo.transform.SetParent(raiz, false);
        foreach (Lampara l in datos.lamparas)
        {
            var go = new GameObject("Lampara");
            go.transform.SetParent(grupo.transform, false);
            go.transform.position = W(l.v, l.u, Mathf.Min(l.y > 0.1f ? l.y : 4.6f, techo - 0.3f));
            var luz = go.AddComponent<Light>();
            luz.type = LightType.Point;
            luz.range = l.rango > 0.5f ? l.rango : 12f;
            luz.intensity = l.intensidad > 0f ? l.intensidad : 1.3f;
            luz.color = new Color(1f, 0.95f, 0.86f);
            luz.shadows = LightShadows.None;
            luz.renderMode = LightRenderMode.Auto;
        }
    }

    private static void Letrero(Transform padre, string nombre, string texto, Vector3 pos, Quaternion rot,
                                float tam, Color color, FontStyles estilo)
    {
        var go = new GameObject("Letrero_" + nombre);
        go.transform.SetParent(padre, true);
        go.transform.SetPositionAndRotation(pos, rot);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = texto;
        tmp.fontSize = tam;
        tmp.fontStyle = estilo;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.rectTransform.sizeDelta = new Vector2(Mathf.Max(5f, tam * 1.6f), Mathf.Max(2f, tam * 0.9f));
    }

    // ------------------------------------------------------------------
    // Geometría básica
    // ------------------------------------------------------------------
    private static void Segmento(Malla m, Vector3 a, Vector3 b, float grueso, float y0, float y1)
    {
        Vector3 d = b - a; d.y = 0f;
        float largo = d.magnitude;
        if (largo < 0.02f || y1 - y0 < 0.005f) return;
        Vector3 c = (a + b) * 0.5f;
        c.y = suelo + (y0 + y1) * 0.5f;
        // Se alarga medio grueso por cada punta para cerrar las esquinas.
        Caja(m, c, new Vector3(grueso, y1 - y0, largo + grueso), Quaternion.LookRotation(d / largo, Vector3.up));
    }

    private static readonly Vector3[] Normales =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

    /// <summary>Caja orientada con UV en metros (proyección por cara, en mundo).</summary>
    private static void Caja(Malla m, Vector3 centro, Vector3 tam, Quaternion rot)
    {
        Vector3 h = tam * 0.5f;
        for (int f = 0; f < 6; f++)
        {
            Vector3 nl = Normales[f];
            Vector3 eje1, eje2;
            if (Mathf.Abs(nl.y) > 0.5f) { eje1 = Vector3.right; eje2 = Vector3.forward; }
            else if (Mathf.Abs(nl.x) > 0.5f) { eje1 = Vector3.forward; eje2 = Vector3.up; }
            else { eje1 = Vector3.right; eje2 = Vector3.up; }
            if (Vector3.Dot(Vector3.Cross(eje1, eje2), nl) < 0f) eje1 = -eje1;

            int baseIdx = m.v.Count;
            Vector3 cf = Vector3.Scale(nl, h);
            Vector3 e1 = Vector3.Scale(eje1, h), e2 = Vector3.Scale(eje2, h);
            Vector3[] esquinas = { cf - e1 - e2, cf + e1 - e2, cf + e1 + e2, cf - e1 + e2 };
            Vector3 nw = rot * nl;
            for (int k = 0; k < 4; k++)
            {
                Vector3 w = centro + rot * esquinas[k];
                m.v.Add(w - origen);
                m.n.Add(nw);
                // UV en metros sobre el plano de la cara.
                Vector2 uv;
                if (Mathf.Abs(nw.y) > 0.7f) uv = new Vector2(w.x, w.z);
                else
                {
                    Vector3 horizontal = Vector3.Cross(Vector3.up, nw).normalized;
                    uv = new Vector2(Vector3.Dot(w, horizontal), w.y - suelo);
                }
                m.uv.Add(uv / m.escalaUV);
            }
            m.t.Add(baseIdx); m.t.Add(baseIdx + 1); m.t.Add(baseIdx + 2);
            m.t.Add(baseIdx); m.t.Add(baseIdx + 2); m.t.Add(baseIdx + 3);
        }
    }

    /// <summary>Polígono convexo horizontal, mirando hacia arriba o hacia abajo.</summary>
    private static void Poligono(Malla m, List<Vector3> puntos, float h, bool haciaArriba)
    {
        int baseIdx = m.v.Count;
        Vector3 normal = haciaArriba ? Vector3.up : Vector3.down;
        foreach (Vector3 p in puntos)
        {
            Vector3 w = new Vector3(p.x, suelo + h, p.z);
            m.v.Add(w - origen);
            m.n.Add(normal);
            m.uv.Add(new Vector2(w.x, w.z) / m.escalaUV);
        }
        // Orden del giro: se decide con el área para que la cara mire hacia donde debe.
        float area = 0f;
        for (int i = 0; i < puntos.Count; i++)
        {
            Vector3 a = puntos[i], b = puntos[(i + 1) % puntos.Count];
            area += a.x * b.z - b.x * a.z;
        }
        bool invertir = (area > 0f) == haciaArriba;
        for (int i = 1; i < puntos.Count - 1; i++)
        {
            if (invertir) { m.t.Add(baseIdx); m.t.Add(baseIdx + i + 1); m.t.Add(baseIdx + i); }
            else { m.t.Add(baseIdx); m.t.Add(baseIdx + i); m.t.Add(baseIdx + i + 1); }
        }
    }

    // ------------------------------------------------------------------
    // Volcado a la escena
    // ------------------------------------------------------------------
    private static int Volcar(Transform geo, string prefijo)
    {
        Carpeta(cfg.carpetaMallas);
        int total = 0;
        foreach (var par in mallas)
        {
            Mesh malla = AMalla(par.Value, prefijo + par.Key);
            Guardar(malla, $"{cfg.carpetaMallas}/{prefijo}{par.Key}.asset");

            var go = new GameObject(par.Key);
            go.transform.SetParent(geo, false);
            go.AddComponent<MeshFilter>().sharedMesh = malla;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat(par.Key);
            bool transparente = par.Key == "Vidrio" || par.Key == "Luz";
            bool sinSombra = transparente || par.Key.StartsWith("Techo") || par.Key == "Terrazo" || par.Key == "BandaGris";
            r.shadowCastingMode = sinSombra ? ShadowCastingMode.Off : ShadowCastingMode.On;
            r.receiveShadows = !transparente;
            total += par.Value.Triangulos;
        }

        Mesh col = AMalla(colision, prefijo + "Colision");
        Guardar(col, $"{cfg.carpetaMallas}/{prefijo}Colision.asset");
        var goCol = new GameObject("Colision");
        goCol.transform.SetParent(geo, false);
        var mc = goCol.AddComponent<MeshCollider>();
        mc.sharedMesh = col;
        // Sin dibujo: solo choca y sirve para hornear el NavMesh.
        return total;
    }

    private static Mesh AMalla(Malla m, string nombre)
    {
        var malla = new Mesh { name = nombre };
        if (m.v.Count > 65000) malla.indexFormat = IndexFormat.UInt32;
        malla.SetVertices(m.v);
        malla.SetNormals(m.n);
        malla.SetUVs(0, m.uv);
        malla.SetTriangles(m.t, 0);
        malla.RecalculateBounds();
        malla.RecalculateTangents();
        return malla;
    }

    private static void Guardar(Mesh malla, string ruta)
    {
        if (AssetDatabase.LoadAssetAtPath<Mesh>(ruta) != null) AssetDatabase.DeleteAsset(ruta);
        AssetDatabase.CreateAsset(malla, ruta);
    }

    /// <summary>
    /// Malla sin dibujo con la huella completa del edificio, bajo un hijo
    /// 'Planta'. PlanoDePlanta.Medir la usa para que las (u, v) del contenido
    /// caigan en el mismo sitio en todos los pisos.
    /// </summary>
    private static void CrearHuella(Transform raiz, string prefijo)
    {
        var planta = new GameObject("Planta");
        planta.transform.SetParent(raiz, false);
        planta.transform.position = origen;

        var malla = new Mesh { name = prefijo + "Huella" };
        Vector3 a = new Vector3(plano.xMin, suelo, plano.zMin) - origen;
        Vector3 b = new Vector3(plano.xMax, suelo, plano.zMax) - origen;
        malla.vertices = new[] { a, new Vector3(b.x, a.y, a.z), b, new Vector3(a.x, a.y, b.z) };
        malla.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        malla.RecalculateBounds();
        Guardar(malla, $"{cfg.carpetaMallas}/{prefijo}Huella.asset");
        planta.AddComponent<MeshFilter>().sharedMesh = malla;
    }

    // ------------------------------------------------------------------
    // Materiales (URP Lit), como en las fotos
    // ------------------------------------------------------------------
    private static Material Mat(string clave)
    {
        Carpeta(cfg.carpetaMateriales);
        string ruta = $"{cfg.carpetaMateriales}/{cfg.prefijoMaterial}{clave}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(sh) { name = cfg.prefijoMaterial + clave };
            AssetDatabase.CreateAsset(mat, ruta);
        }

        switch (clave)
        {
            case "Concreto": Base(mat, new Color(0.86f, 0.85f, 0.83f), Tex(clave), 0.15f); break;
            case "Madera": Base(mat, Color.white, Tex(clave), 0.38f); break;
            case "MaderaClara": Base(mat, new Color(0.78f, 0.60f, 0.36f), null, 0.4f); break;
            case "Piso": Base(mat, Color.white, Tex(clave), 0.45f); break;
            case "Techo": Base(mat, new Color(0.11f, 0.11f, 0.12f), null, 0.1f); break;
            case "TechoOscuro": Base(mat, new Color(0.10f, 0.10f, 0.11f), null, 0.1f); break;
            case "TechoClaro":
                // Cielo raso blanco: casi no le llega luz directa (las lámparas quedan justo debajo),
                // así que emite un poco para leerse blanco como en las fotos.
                Base(mat, new Color(0.90f, 0.90f, 0.88f), null, 0.1f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.54f));
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                break;
            case "Marco": Base(mat, new Color(0.16f, 0.16f, 0.17f), null, 0.4f); break;
            case "Metal": Base(mat, new Color(0.62f, 0.64f, 0.66f), null, 0.6f, 0.75f); break;
            case "TuboOscuro": Base(mat, new Color(0.08f, 0.08f, 0.09f), null, 0.3f); break;
            case "Rojo": Base(mat, new Color(0.42f, 0.07f, 0.09f), null, 0.35f); break;
            case "Mostaza": Base(mat, new Color(0.62f, 0.50f, 0.22f), null, 0.3f); break;
            case "Escalon": Base(mat, new Color(0.56f, 0.57f, 0.58f), null, 0.3f); break;
            case "Blanco": Base(mat, new Color(0.87f, 0.87f, 0.85f), null, 0.15f); break;
            case "Negro": Base(mat, new Color(0.04f, 0.04f, 0.045f), null, 0.45f); break;
            case "NegroBrillante": Base(mat, new Color(0.02f, 0.02f, 0.025f), null, 0.9f); break;
            case "Granito": Base(mat, Color.white, Tex(clave), 0.5f); break;
            case "Terrazo": Base(mat, Color.white, Tex(clave), 0.55f); break;
            case "BandaGris": Base(mat, new Color(0.30f, 0.31f, 0.33f), null, 0.45f); break;
            case "Lama": Base(mat, new Color(0.74f, 0.53f, 0.30f), null, 0.4f); break;
            case "Mesa": Base(mat, new Color(0.92f, 0.92f, 0.90f), null, 0.3f); break;
            case "Monitor": Base(mat, new Color(0.05f, 0.05f, 0.06f), null, 0.7f); break;
            case "Silla": Base(mat, new Color(0.17f, 0.18f, 0.20f), null, 0.3f); break;
            case "Meson": Base(mat, new Color(0.82f, 0.82f, 0.80f), null, 0.65f); break;
            case "Sanitario": Base(mat, new Color(0.95f, 0.95f, 0.95f), null, 0.8f); break;
            case "Divisor": Base(mat, new Color(0.52f, 0.54f, 0.57f), null, 0.4f); break;
            case "Gris": Base(mat, new Color(0.30f, 0.30f, 0.31f), null, 0.15f); break;
            case "Dorado": Base(mat, new Color(0.56f, 0.46f, 0.30f), null, 0.5f, 0.45f); break;
            case "Pizarra": Base(mat, new Color(0.21f, 0.22f, 0.23f), null, 0.35f); break;
            case "Campana": Base(mat, new Color(0.86f, 0.86f, 0.84f), null, 0.55f, 0.3f); break;
            case "Vitrina": Base(mat, new Color(0.90f, 0.90f, 0.89f), null, 0.5f); break;
            case "Luz":
                Base(mat, Color.white, null, 0.5f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.96f, 0.88f) * 2.2f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                break;
            case "Vidrio":
                // El vidrio del piso 9 es más oscuro (tintado) que el del 3.
                Base(mat, cfg.prefijoMaterial == "P9_" ? new Color(0.42f, 0.50f, 0.54f, 0.30f)
                                                       : new Color(0.70f, 0.80f, 0.84f, 0.22f), null, 0.92f);
                Transparente(mat);
                break;
            default: Base(mat, new Color(0.7f, 0.7f, 0.7f), null, 0.2f); break;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>La textura de un material, buscada en las carpetas de la planta en orden.</summary>
    private static Texture2D Tex(string clave)
    {
        if (!cfg.texturas.TryGetValue(clave, out string archivo) || string.IsNullOrEmpty(archivo)) return null;
        foreach (string carpeta in cfg.carpetasTexturas)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{carpeta}/{archivo}");
            if (tex != null) return tex;
        }
        return null;
    }

    private static void Base(Material mat, Color color, Texture2D tex, float suavidad, float metal = 0f)
    {
        mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_Smoothness", suavidad);
        mat.SetFloat("_Metallic", metal);
    }

    private static void Transparente(Material mat)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetShaderPassEnabled("ShadowCaster", false);
    }

    private static void Carpeta(string ruta)
    {
        if (AssetDatabase.IsValidFolder(ruta)) return;
        string padre = System.IO.Path.GetDirectoryName(ruta).Replace('\\', '/');
        Carpeta(padre);
        AssetDatabase.CreateFolder(padre, System.IO.Path.GetFileName(ruta));
    }
}
