using System;
using System.Collections.Generic;
using UnityEngine;
using Rng = System.Random;

// ============================================================================
// EntornoOsm.cs — El campus con datos reales de OpenStreetMap
// ============================================================================
// Las huellas de los edificios, sus alturas, las calles, las canchas, los
// parques y los árboles de los alrededores salen de OpenStreetMap
// (© colaboradores de OpenStreetMap, licencia ODbL: hay que citarlo en la
// memoria). El script Python que los baja y los pasa a metros locales dejó el
// resultado en Resources/MovU/entorno_campus.json:
//
//   - Origen: el centro del edificio 12 de la Javeriana, "José Gabriel
//     Maldonado S.J. – Laboratorios", 15 niveles y 70 m: la torre de Ingeniería.
//   - 'e' crece hacia el oriente y 's' hacia el sur, en metros.
//
// Cómo encaja con el modelo del edificio: el modelo mide 106 x 39 m y su eje
// largo es +Z, con los ascensores en el extremo +Z (el oriental). La torre real
// mide 47 x 14 m y su eje largo va unos 15° al sur del este, así que
// 'entornoYawOriente' vale -15 y la torre se pone OrigenTorre metros hacia +Z
// del centro del modelo. Los edificios cuyo centro cae dentro de la huella del
// modelo no se dibujan: ya están representados por el modelo.
//
// Si el archivo no existe, EntornoCampus cae al fondo inventado de antes.
// ============================================================================

public static partial class EntornoCampus
{
    private const string RecursoOsm = "MovU/entorno_campus";
    private const float OrigenTorre = 30f;           // metros de +Z desde el centro del modelo hasta el centro de la torre
    private const float RadioOsm = 760f;             // hasta aquí hay datos reales; más lejos, fondo inventado

    [Serializable] public class EdificioOsm { public long id; public int u; public float h; public float[] p; }
    [Serializable] public class CalleOsm { public float w; public float[] p; }
    [Serializable] public class ZonaOsm { public int t; public float[] p; }
    [Serializable] public class DatosOsm
    {
        public List<EdificioOsm> edificios;
        public List<CalleOsm> calles;
        public List<ZonaOsm> zonas;
        public float[] arboles;
    }

    private static DatosOsm CargarOsm()
    {
        TextAsset texto = Resources.Load<TextAsset>(RecursoOsm);
        if (texto == null) return null;
        try
        {
            DatosOsm d = JsonUtility.FromJson<DatosOsm>(texto.text);
            return d != null && d.edificios != null && d.edificios.Count > 0 ? d : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Entorno] No pude leer " + RecursoOsm + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>Punto del mundo para (e, s) medidos desde el centro de la torre.</summary>
    private static Vector3 Torre(Contexto c, float e, float s, float y)
    {
        return c.centro + Vector3.forward * OrigenTorre + Rumbo(c, 0f) * e + Rumbo(c, 90f) * s + Vector3.up * y;
    }

    private static Vector2 Par(float[] p, int i) { return new Vector2(p[2 * i], p[2 * i + 1]); }

    // ------------------------------------------------------------------
    private static void ConstruirDesdeOsm(Malla m, Contexto c, DatosOsm d)
    {
        Bounds modelo = c.huella;
        modelo.Expand(new Vector3(12f, 0f, 12f));       // margen de 6 m por lado

        ConstruirZonas(m, c, d);
        ConstruirCalles(m, c, d);

        var cajas = new List<Vector4>();                // (minE, minS, maxE, maxS) de lo construido
        int dibujados = 0, omitidos = 0;
        foreach (EdificioOsm ed in d.edificios)
        {
            int n = ed.p.Length / 2;
            Vector2 centro = Vector2.zero;
            for (int i = 0; i < n; i++) centro += Par(ed.p, i);
            centro /= n;

            Vector3 w = Torre(c, centro.x, centro.y, 0f);
            if (w.x > modelo.min.x && w.x < modelo.max.x && w.z > modelo.min.z && w.z < modelo.max.z)
            {
                omitidos++;
                continue;
            }

            ExtruirEdificio(m, c, ed, centro);
            dibujados++;

            Vector4 caja = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            for (int i = 0; i < n; i++)
            {
                Vector2 q = Par(ed.p, i);
                caja.x = Mathf.Min(caja.x, q.x); caja.y = Mathf.Min(caja.y, q.y);
                caja.z = Mathf.Max(caja.z, q.x); caja.w = Mathf.Max(caja.w, q.y);
            }
            cajas.Add(caja);
        }

        int arboles = SembrarArboles(m, c, d, cajas);
        Debug.Log($"[Entorno] OpenStreetMap: {dibujados} edificios ({omitidos} dentro del modelo omitidos), " +
                  $"{d.calles.Count} calles, {d.zonas.Count} zonas y {arboles} árboles.");
    }

    // ------------------------------------------------------------------
    // Edificios
    // ------------------------------------------------------------------
    private static void ExtruirEdificio(Malla m, Contexto c, EdificioOsm ed, Vector2 centro)
    {
        int n = ed.p.Length / 2;
        var pts = new List<Vector2>(n);
        for (int i = 0; i < n; i++) pts.Add(Par(ed.p, i));

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        if (Mathf.Abs(area) < 4f) return;                          // casetas y basura de datos

        float alto = Mathf.Clamp(ed.h, 3f, 90f);
        long hash = ed.id * 2654435761L;
        int cual = (int)(((hash >> 8) & 0x7fffffff) % 1000);

        Color color;
        if (ed.u == 1)
        {
            // El campus de la Javeriana es sobre todo de ladrillo.
            color = cual < 70 ? Fachadas[3] : cual < 140 ? Fachadas[2] : cual < 520 ? Fachadas[0] : Fachadas[1];
        }
        else
        {
            color = Fachadas[cual % Fachadas.Length];
        }

        float distancia = centro.magnitude;
        bool bandas = distancia < 420f && alto > 9f;

        // Paredes. Con el área positiva en (e, s) la normal hacia afuera de cada arista es (ds, -dx).
        float signo = area > 0f ? 1f : -1f;
        Color ventana = Color.Lerp(color, new Color(0.34f, 0.40f, 0.48f), 0.5f);
        const float pisoAlto = 3.4f;
        int pisos = bandas ? Mathf.Max(1, Mathf.FloorToInt(alto / pisoAlto)) : 1;
        float paso = alto / pisos;

        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = pts[i], p1 = pts[(i + 1) % n];
            Vector2 borde = p1 - p0;
            if (borde.sqrMagnitude < 0.04f) continue;
            Vector2 normal = signo * new Vector2(borde.y, -borde.x);
            Vector3 fuera = Rumbo(c, 0f) * normal.x + Rumbo(c, 90f) * normal.y;

            Vector3 a = Torre(c, p0.x, p0.y, 0f), b = Torre(c, p1.x, p1.y, 0f);
            for (int k = 0; k < pisos; k++)
            {
                float y0 = k * paso, y1 = (k + 1) * paso;
                if (!bandas)
                {
                    m.Cuad(c, a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, color, fuera);
                    continue;
                }
                float ym = y0 + paso * 0.30f, yv = y0 + paso * 0.80f;
                m.Cuad(c, a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * ym, a + Vector3.up * ym, color, fuera);
                m.Cuad(c, a + Vector3.up * ym, b + Vector3.up * ym, b + Vector3.up * yv, a + Vector3.up * yv, ventana, fuera);
                m.Cuad(c, a + Vector3.up * yv, b + Vector3.up * yv, b + Vector3.up * y1, a + Vector3.up * y1, color, fuera);
            }
        }

        // Techo.
        List<int> tri = Triangular(pts);
        Color techo = color * 0.85f;
        for (int t = 0; t + 2 < tri.Count; t += 3)
        {
            Vector2 a = pts[tri[t]], b = pts[tri[t + 1]], d = pts[tri[t + 2]];
            m.Tri(c, Torre(c, a.x, a.y, alto), Torre(c, b.x, b.y, alto), Torre(c, d.x, d.y, alto),
                  techo, techo, techo, Vector3.up, true);
        }
    }

    // ------------------------------------------------------------------
    // Calles
    // ------------------------------------------------------------------
    private static void ConstruirCalles(Malla m, Contexto c, DatosOsm d)
    {
        var asfalto = new Color(0.31f, 0.32f, 0.34f);
        foreach (CalleOsm calle in d.calles)
        {
            int n = calle.p.Length / 2;
            for (int i = 0; i + 1 < n; i++)
            {
                Vector2 a = Par(calle.p, i), b = Par(calle.p, i + 1);
                Vector2 dir = b - a;
                if (dir.sqrMagnitude < 0.04f) continue;
                dir.Normalize();
                Vector2 lado = new Vector2(-dir.y, dir.x) * (calle.w * 0.5f);
                Vector2 ext = dir * (calle.w * 0.25f);       // se prolonga un poco para tapar las juntas
                a -= ext; b += ext;
                m.Cuad(c, Torre(c, a.x - lado.x, a.y - lado.y, 0.06f), Torre(c, b.x - lado.x, b.y - lado.y, 0.06f),
                       Torre(c, b.x + lado.x, b.y + lado.y, 0.06f), Torre(c, a.x + lado.x, a.y + lado.y, 0.06f),
                       asfalto, Vector3.up);
            }
        }
    }

    // ------------------------------------------------------------------
    // Zonas: parques, bosque, parqueaderos y canchas
    // ------------------------------------------------------------------
    private static void ConstruirZonas(Malla m, Contexto c, DatosOsm d)
    {
        // Se dibujan de abajo hacia arriba para que las canchas queden sobre los parques.
        for (int capa = 4; capa <= 7; capa++)
        {
            foreach (ZonaOsm z in d.zonas)
            {
                int tipoCapa = z.t == 4 ? 4 : z.t == 5 ? 5 : z.t == 6 ? 6 : 7;
                if (tipoCapa != capa) continue;
                PintarZona(m, c, z);
            }
        }
    }

    private static void PintarZona(Malla m, Contexto c, ZonaOsm z)
    {
        Color color; float y;
        switch (z.t)
        {
            case 1: color = new Color(0.30f, 0.58f, 0.27f); y = 0.11f; break;     // cancha de fútbol
            case 2: color = new Color(0.76f, 0.38f, 0.22f); y = 0.11f; break;     // tenis (arcilla)
            case 3: color = new Color(0.45f, 0.52f, 0.60f); y = 0.11f; break;     // otras canchas
            case 4: color = new Color(0.38f, 0.52f, 0.30f); y = 0.03f; break;     // pasto y parque
            case 5: color = new Color(0.24f, 0.40f, 0.22f); y = 0.04f; break;     // bosque
            case 6: color = new Color(0.42f, 0.43f, 0.45f); y = 0.08f; break;     // parqueadero
            default: color = new Color(0.80f, 0.60f, 0.30f); y = 0.09f; break;    // juegos
        }

        int n = z.p.Length / 2;
        var pts = new List<Vector2>(n);
        for (int i = 0; i < n; i++) pts.Add(Par(z.p, i));
        List<int> tri = Triangular(pts);
        for (int t = 0; t + 2 < tri.Count; t += 3)
        {
            Vector2 a = pts[tri[t]], b = pts[tri[t + 1]], d = pts[tri[t + 2]];
            m.Tri(c, Torre(c, a.x, a.y, y), Torre(c, b.x, b.y, y), Torre(c, d.x, d.y, y),
                  color, color, color, Vector3.up, true);
        }

        if (z.t == 1 || z.t == 2)
        {
            // Líneas de la cancha.
            var linea = new Color(0.93f, 0.95f, 0.91f);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % n];
                Vector2 dir = b - a;
                if (dir.sqrMagnitude < 0.04f) continue;
                dir.Normalize();
                Vector2 lado = new Vector2(-dir.y, dir.x) * 0.12f;
                m.Cuad(c, Torre(c, a.x - lado.x, a.y - lado.y, y + 0.04f), Torre(c, b.x - lado.x, b.y - lado.y, y + 0.04f),
                       Torre(c, b.x + lado.x, b.y + lado.y, y + 0.04f), Torre(c, a.x + lado.x, a.y + lado.y, y + 0.04f),
                       linea, Vector3.up);
            }
        }
    }

    // ------------------------------------------------------------------
    // Árboles: los mapeados uno por uno y un reparto dentro del bosque y los parques
    // ------------------------------------------------------------------
    private static int SembrarArboles(Malla m, Contexto c, DatosOsm d, List<Vector4> cajas)
    {
        var rnd = new Rng(4242);
        int total = 0;
        const int tope = 6500;

        // Los árboles que están en el mapa.
        for (int i = 0; i + 1 < d.arboles.Length && total < tope; i += 2)
        {
            float e = d.arboles[i], s = d.arboles[i + 1];
            if (EnConstruido(e, s, cajas) || EnModelo(c, e, s)) continue;
            SembrarUno(m, c, e, s, rnd);
            total++;
        }

        // Reparto dentro de bosques (denso) y parques (ralo).
        foreach (ZonaOsm z in d.zonas)
        {
            if (z.t != 4 && z.t != 5) continue;
            int n = z.p.Length / 2;
            var pts = new List<Vector2>(n);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < n; i++)
            {
                Vector2 q = Par(z.p, i);
                pts.Add(q);
                min = Vector2.Min(min, q); max = Vector2.Max(max, q);
            }
            float paso = z.t == 5 ? 13f : 22f;
            for (float e = min.x; e < max.x && total < tope; e += paso)
            {
                for (float s = min.y; s < max.y && total < tope; s += paso)
                {
                    float je = e + ((float)rnd.NextDouble() - 0.5f) * paso;
                    float js = s + ((float)rnd.NextDouble() - 0.5f) * paso;
                    if (z.t == 4 && rnd.NextDouble() < 0.45) continue;        // los parques son más claros
                    if (!PuntoEnPoligono(new Vector2(je, js), pts)) continue;
                    if (EnConstruido(je, js, cajas) || EnModelo(c, je, js)) continue;
                    SembrarUno(m, c, je, js, rnd);
                    total++;
                }
            }
        }
        return total;
    }

    private static void SembrarUno(Malla m, Contexto c, float e, float s, Rng rnd)
    {
        Vector3 p = Torre(c, e, s, 0f);
        Arbol(m, c, p, 8f + (float)rnd.NextDouble() * 10f, 3f + (float)rnd.NextDouble() * 2.5f, (float)rnd.NextDouble());
    }

    private static bool EnConstruido(float e, float s, List<Vector4> cajas)
    {
        for (int i = 0; i < cajas.Count; i++)
        {
            Vector4 b = cajas[i];
            if (e > b.x - 2f && e < b.z + 2f && s > b.y - 2f && s < b.w + 2f) return true;
        }
        return false;
    }

    private static bool EnModelo(Contexto c, float e, float s)
    {
        Vector3 w = Torre(c, e, s, 0f);
        Bounds b = c.huella;
        b.Expand(new Vector3(16f, 0f, 16f));
        return w.x > b.min.x && w.x < b.max.x && w.z > b.min.z && w.z < b.max.z;
    }

    private static bool PuntoEnPoligono(Vector2 p, List<Vector2> poly)
    {
        bool dentro = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) dentro = !dentro;
        }
        return dentro;
    }

    // ------------------------------------------------------------------
    // Triangulación de polígonos simples (orejas)
    // ------------------------------------------------------------------
    private static List<int> Triangular(List<Vector2> p)
    {
        var res = new List<int>();
        int n = p.Count;
        if (n < 3) return res;

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = p[i], b = p[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        bool ccw = area > 0f;

        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);

        int guardia = 0;
        while (idx.Count > 3 && guardia++ < 4000)
        {
            bool cortada = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                Vector2 a = p[i0], b = p[i1], c = p[i2];
                float cruz = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (ccw ? cruz <= 1e-5f : cruz >= -1e-5f) continue;       // vértice cóncavo o degenerado

                bool hayOtro = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ik = idx[k];
                    if (ik == i0 || ik == i1 || ik == i2) continue;
                    if (EnTriangulo(p[ik], a, b, c)) { hayOtro = true; break; }
                }
                if (hayOtro) continue;

                res.Add(i0); res.Add(i1); res.Add(i2);
                idx.RemoveAt(i);
                cortada = true;
                break;
            }
            if (!cortada) idx.RemoveAt(0);          // polígono raro: se descarta un vértice y se sigue
        }
        if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
        return res;
    }

    private static bool EnTriangulo(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (q.x - b.x) * (a.y - b.y) - (a.x - b.x) * (q.y - b.y);
        float d2 = (q.x - c.x) * (b.y - c.y) - (b.x - c.x) * (q.y - c.y);
        float d3 = (q.x - a.x) * (c.y - a.y) - (c.x - a.x) * (q.y - a.y);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }
}
