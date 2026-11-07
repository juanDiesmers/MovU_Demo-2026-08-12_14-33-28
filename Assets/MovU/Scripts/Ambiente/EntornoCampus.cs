using System.Collections.Generic;
using UnityEngine;
using Rng = System.Random;

// ============================================================================
// EntornoCampus.cs — El campus visto desde las ventanas del edificio
// ============================================================================
// Hasta ahora, por las ventanas se veía el cielo por defecto de Unity y nada
// más: plano. Este montaje rodea el edificio con un fondo generado por código
// que sigue la geografía real del campus de la Javeriana en Chapinero:
//
//   ORIENTE   Cerros Orientales: dos cordilleras boscosas, una más cercana y
//             oscura y otra más lejana y azulada, y bosque en la falda.
//   SUR       Edificios del campus, canchas de tenis y el arbolado del Parque
//             Nacional Enrique Olaya Herrera.
//   CAMPUS    Los edificios y la cancha de fútbol están puestos según el mapa
//             oficial del campus (ver "El campus según el mapa").
//   NORTE     Chapinero: edificios medianos de ladrillo y concreto.
//   OCCIDENTE Carrera Séptima y la ciudad, con torres más altas y lejanas.
//   ALREDEDOR Árboles y edificios bajos de ladrillo del propio campus.
//
// Fuentes: ubicación y linderos del campus (Carrera 7 al occidente, Parque
// Nacional al sur, Cerros Orientales y Circunvalar al oriente, calle 45 al
// norte), tomados del sitio de la universidad y de Wikipedia. NO se usan
// fotos ni modelos de terceros: todo es geometría low-poly propia.
//
// Cómo se dibuja, para no tocar el resto del juego:
//   - Un shader sin luz (MovU/FondoUnlit) que pinta el color de cada vértice.
//     La sombra de cada cara y la bruma de la distancia van horneadas en esos
//     colores, así la niebla de la escena y los interiores NO cambian.
//   - Sin colliders, sin sombras, sin entrar al NavMesh.
//   - Dos mallas y dos materiales en total (mundo y cielo).
//   - Se sube el plano lejano de las cámaras, porque los cerros están a más
//     de 1 km y la cámara venía con 500 a 1000 m.
//
// Es un fondo, no un mapa fiel: la disposición de los edificios de la ciudad
// es inventada. Lo que sí es real es qué hay en cada punto cardinal. Hacia
// dónde queda el oriente en la escena se fija con 'entornoYawOriente'.
// ============================================================================

public static partial class EntornoCampus
{
    public const string NombreDeLaRaiz = "Entorno_Campus";
    private const string NombreDelShader = "MovU/FondoUnlit";

    private static readonly Color Bruma = new Color(0.80f, 0.86f, 0.93f);
    private static readonly Color CieloAlto = new Color(0.27f, 0.50f, 0.85f);
    private static readonly Vector3 Sol = new Vector3(0.35f, 0.80f, -0.45f).normalized;

    private const float RadioDelCielo = 2800f;
    private const float PlanoLejano = 3600f;

    // ------------------------------------------------------------------
    public static GameObject Construir(Transform padre, FloorManager pisos, float yawOriente)
    {
        Shader shader = Shader.Find(NombreDelShader);
        if (shader == null)
        {
            Debug.LogWarning("[Entorno] No encuentro el shader " + NombreDelShader +
                             ": el fondo del campus no se construye.");
            return null;
        }

        if (!MedirEdificio(pisos, out Bounds huella, out float sueloDelCampus, out float baseDelEdificio))
        {
            Debug.LogWarning("[Entorno] No pude medir el edificio: el fondo no se construye.");
            return null;
        }

        var raiz = new GameObject(NombreDeLaRaiz);
        raiz.transform.SetParent(padre, false);

        var ctx = new Contexto
        {
            centro = new Vector3(huella.center.x, sueloDelCampus, huella.center.z),
            huella = huella,
            yawOriente = yawOriente,
            suelo = sueloDelCampus,
        };

        // Mitades de la huella proyectadas sobre los ejes oriente y sur.
        Vector3 ejeE = Rumbo(ctx, 0f), ejeS = Rumbo(ctx, 90f);
        ctx.mitadE = (Mathf.Abs(ejeE.x) * huella.size.x + Mathf.Abs(ejeE.z) * huella.size.z) * 0.5f;
        ctx.mitadS = (Mathf.Abs(ejeS.x) * huella.size.x + Mathf.Abs(ejeS.z) * huella.size.z) * 0.5f;

        var mundo = new Malla();
        var cielo = new Malla();

        ConstruirSuelo(mundo, ctx);
        ocupados.Clear();
        DatosOsm osm = CargarOsm();
        if (osm != null) ConstruirDesdeOsm(mundo, ctx, osm);
        else ConstruirCampus(mundo, ctx);
        ConstruirBase(mundo, ctx, baseDelEdificio);
        ConstruirCerros(mundo, ctx);
        ConstruirCiudadYArboles(mundo, ctx, osm != null);
        ConstruirNubes(mundo, ctx);
        ConstruirCielo(cielo, ctx);

        CrearObjeto(raiz.transform, "Mundo", mundo, shader, 2f);
        CrearObjeto(raiz.transform, "Cielo", cielo, shader, 1f);   // se ve por dentro

        SubirPlanoLejano();

        Debug.Log($"[Entorno] Fondo del campus: {(mundo.indices.Count + cielo.indices.Count) / 3:N0} triángulos, " +
                  $"suelo a y = {sueloDelCampus:F1} m, oriente a {yawOriente:F0}°.");
        return raiz;
    }

    private class Contexto
    {
        public Vector3 centro;
        public Bounds huella;
        public float yawOriente;
        public float suelo;
        public float mitadE, mitadS;     // mitad de la huella en los ejes oriente y sur
    }

    /// <summary>Dirección horizontal de un rumbo: 0 = oriente, 90 = sur, 180 = occidente, 270 = norte.</summary>
    private static Vector3 Rumbo(Contexto c, float grados)
    {
        return Quaternion.Euler(0f, c.yawOriente + grados, 0f) * Vector3.forward;
    }

    // ------------------------------------------------------------------
    // Medidas del edificio
    // ------------------------------------------------------------------
    private static bool MedirEdificio(FloorManager pisos, out Bounds huella, out float suelo, out float baseDelEdificio)
    {
        huella = new Bounds();
        suelo = 0f;
        baseDelEdificio = 0f;
        if (pisos == null || pisos.CantidadDePisos == 0) return false;

        Transform piso = pisos.Piso(pisos.IndiceBase);
        if (piso == null) return false;

        bool hay = false;
        var filtros = piso.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filtros.Length; i++)
        {
            Mesh m = filtros[i].sharedMesh;
            if (m == null) continue;
            Bounds lb = m.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 esquina = lb.center + Vector3.Scale(lb.extents,
                    new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 w = filtros[i].transform.TransformPoint(esquina);
                if (!hay) { huella = new Bounds(w, Vector3.zero); hay = true; }
                else huella.Encapsulate(w);
            }
        }
        if (!hay || huella.size.x < 5f || huella.size.z < 5f) return false;

        // El piso de más abajo del juego no es la planta baja: el edificio va del
        // piso 2 al 14, así que la calle queda un piso por debajo del primero.
        float separacion = pisos.SeparacionEntrePisos > 1f ? pisos.SeparacionEntrePisos : 3.7f;
        int pisosAbajo = Mathf.Max(0, FloorManager.NumeroDelPrimerPiso - 2);
        baseDelEdificio = pisos.AlturaDelSuelo(0) - 0.05f;
        suelo = baseDelEdificio - separacion * pisosAbajo;
        return true;
    }

    private static void SubirPlanoLejano()
    {
        foreach (Camera cam in Camera.allCameras)
        {
            if (cam.farClipPlane < PlanoLejano) cam.farClipPlane = PlanoLejano;
        }
    }

    // ------------------------------------------------------------------
    // Suelo, base del edificio
    // ------------------------------------------------------------------
    private static void ConstruirSuelo(Malla m, Contexto c)
    {
        float[] radios = { 0f, 150f, 450f, 1100f, 2200f, RadioDelCielo };
        const int segmentos = 40;
        var colorPasto = new Color(0.36f, 0.43f, 0.31f);

        for (int j = 0; j < radios.Length - 1; j++)
        {
            for (int i = 0; i < segmentos; i++)
            {
                float a0 = 360f * i / segmentos, a1 = 360f * (i + 1) / segmentos;
                Vector3 p00 = c.centro + Rumbo(c, a0) * radios[j];
                Vector3 p01 = c.centro + Rumbo(c, a1) * radios[j];
                Vector3 p10 = c.centro + Rumbo(c, a0) * radios[j + 1];
                Vector3 p11 = c.centro + Rumbo(c, a1) * radios[j + 1];
                m.Cuad(c, p00, p10, p11, p01, colorPasto, Vector3.up);
            }
        }
    }

    private static void ConstruirBase(Malla m, Contexto c, float baseDelEdificio)
    {
        float alto = baseDelEdificio - c.suelo;
        if (alto < 0.3f) return;
        var concreto = new Color(0.60f, 0.60f, 0.58f);
        Caja(m, c, new Vector3(c.huella.center.x, c.suelo, c.huella.center.z),
             c.huella.size.x, c.huella.size.z, alto, 0f, concreto, false);
    }

    // ------------------------------------------------------------------
    // El campus según el mapa de la universidad
    // ------------------------------------------------------------------
    // Posiciones medidas sobre el mapa oblicuo del campus (el de los puntos
    // numerados, con la Av. Circunvalar arriba, la Carrera 7 abajo a la izquierda,
    // la calle 45 al norte y el Parque Nacional al sur). Origen: la torre de
    // Ingeniería (el edificio 12 del mapa). 'e' crece hacia el oriente y 's'
    // hacia el sur, en metros; la escala (~0,55 m por píxel) sale de la cancha y
    // de la propia torre, así que son aproximadas, no un levantamiento.
    //
    // La torre se supone en el extremo oriental del edificio modelado (donde
    // están los ascensores, que miran a los cerros).
    private const float OrigenTorreE = 40f;

    private struct Hito
    {
        public float e, s, ancho, fondo, alto;   // ancho: de oriente a occidente; fondo: de norte a sur
        public int color;                        // índice en Fachadas
    }

    private static Hito H(float e, float s, float ancho, float fondo, float alto, int color)
    {
        return new Hito { e = e, s = s, ancho = ancho, fondo = fondo, alto = alto, color = color };
    }

    // Número del mapa en el comentario.
    private static readonly Hito[] Hitos =
    {
        H(-15,  65, 40, 34, 16, 5),    // 11: bloque bajo, junto a la torre
        H( 36, 111, 34, 42, 30, 3),    // 2
        H(-26, 122, 26, 26, 16, 4),    // 4: cubo blanco
        H(-115, 146, 32, 36, 28, 0),   // 3
        H(-176,  99, 32, 32, 22, 4),   // 8
        H(-199,  -4, 30, 110, 22, 5),  // 20: bloque largo dorado sobre la Carrera 7
        H(-170,   3, 20, 20, 12, 0),   // 15
        H(-170, -136, 38, 38, 28, 0),  // 21
        H(-169,  -94, 30, 30, 25, 0),  // 23
        H(-90, -177, 30, 70, 28, 3),   // 24
        H(-80,  -95, 40, 38, 25, 2),   // 25
        H( 64,  -90, 56, 80, 18, 3),   // 115: los dos bloques grises largos
        H( 78, -210, 30, 40, 32, 0),   // 67: junto a la Circunvalar
        H(-19, -179, 22, 22, 14, 2),   // 31
        H(-18, -152, 22, 22, 14, 2),   // 30
        H(  4, -204, 22, 22, 14, 2),   // 32
        H(160,   31, 40, 26,  9, 4),   // 91: pabellón deportivo
        H(-282,  57, 30, 30, 15, 2),   // 116: al otro lado de la Carrera 7
        H(-255, -338, 22, 22, 34, 0),  // 7: torre aislada al otro lado de la Carrera 7
        H(-141, -416, 40, 40, 34, 1),  // 44
        H(-192, -482, 24, 30, 21, 0),  // 50
        H(-142, -465, 24, 30, 21, 1),  // 51
        H(-119, -470, 24, 30, 21, 0),  // 52
        H( -84, -456, 24, 30, 21, 1),  // 53
    };

    private const float CarreraSieteE = -240f;      // eje de la Carrera 7 (relativo a la torre)

    // Círculos (e, s, radio) ocupados por edificios y canchas, para no sembrar árboles encima.
    private static readonly List<Vector3> ocupados = new List<Vector3>();

    private static Vector3 Local(Contexto c, float e, float s, float y)
    {
        return c.centro + Rumbo(c, 0f) * e + Rumbo(c, 90f) * s + Vector3.up * y;
    }

    /// <summary>Rectángulo plano y girado 'grados' (de 'e' hacia 's') sobre el suelo.</summary>
    private static void Rectangulo(Malla m, Contexto c, float e, float s, float largo, float ancho,
                                   float grados, float y, Color color)
    {
        float a = grados * Mathf.Deg2Rad;
        Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), v = new Vector2(-u.y, u.x);
        Vector3 P(float i, float j) => Local(c, e + u.x * i + v.x * j, s + u.y * i + v.y * j, y);
        m.Cuad(c, P(-largo / 2, -ancho / 2), P(largo / 2, -ancho / 2), P(largo / 2, ancho / 2), P(-largo / 2, ancho / 2),
               color, Vector3.up);
    }

    /// <summary>Si el círculo (e, s, radio) pisa el edificio modelado, lo corre hacia afuera.</summary>
    private static void Empujar(Contexto c, ref float e, ref float s, float radio)
    {
        const float margen = 18f;
        for (int i = 0; i < 80; i++)
        {
            float dx = Mathf.Max(Mathf.Abs(e) - c.mitadE, 0f), dy = Mathf.Max(Mathf.Abs(s) - c.mitadS, 0f);
            if (Mathf.Sqrt(dx * dx + dy * dy) >= radio + margen) return;
            Vector2 d = new Vector2(e, s);
            d = d.sqrMagnitude < 0.01f ? Vector2.right : d.normalized;
            e += d.x * 4f;
            s += d.y * 4f;
        }
    }

    private static void ConstruirCampus(Malla m, Contexto c)
    {
        ocupados.Clear();
        float y = 0.08f, yLinea = 0.16f;

        // Carrera 7: franja de asfalto con su línea central, por el occidente del campus.
        float eRuta = CarreraSieteE + OrigenTorreE;
        Rectangulo(m, c, eRuta, 0f, 1600f, 26f, 90f, y, new Color(0.30f, 0.31f, 0.33f));
        Rectangulo(m, c, eRuta, 0f, 1600f, 0.4f, 90f, yLinea, new Color(0.90f, 0.78f, 0.25f));

        // Edificios del mapa.
        foreach (Hito h in Hitos)
        {
            float e = h.e + OrigenTorreE, s = h.s;
            float radio = Mathf.Max(h.ancho, h.fondo) * 0.5f;
            Empujar(c, ref e, ref s, radio);
            ocupados.Add(new Vector3(e, s, radio));

            // Caja(): 'ancho' va de norte a sur y 'fondo' de oriente a occidente.
            Caja(m, c, Local(c, e, s, 0f), h.fondo, h.ancho, h.alto, c.yawOriente,
                 Fachadas[h.color], h.alto > 12f);
        }

        // Cancha de fútbol (90): pista, pasto y líneas. Su lado largo va a ~57° del oriente hacia el sur.
        {
            float e = 61f + OrigenTorreE, s = 26f;
            Empujar(c, ref e, ref s, 66f);
            ocupados.Add(new Vector3(e, s, 66f));
            const float ang = 57f;
            Rectangulo(m, c, e, s, 128f, 88f, ang, y, new Color(0.62f, 0.33f, 0.25f));         // pista
            Rectangulo(m, c, e, s, 106f, 66f, ang, y + 0.04f, new Color(0.30f, 0.55f, 0.26f)); // pasto
            var linea = new Color(0.92f, 0.94f, 0.90f);
            Rectangulo(m, c, e, s, 0.3f, 66f, ang, yLinea, linea);                             // mitad de cancha
            Rectangulo(m, c, e, s, 106f, 0.3f, ang, yLinea, linea);                            // línea central larga
        }

        // Canchas de tenis, junto al Parque Nacional: arcilla y cemento verde.
        {
            float e = -19f + OrigenTorreE, s = 241f;
            ocupados.Add(new Vector3(e, s, 28f));
            Rectangulo(m, c, e, s, 36f, 20f, 57f, y, new Color(0.78f, 0.40f, 0.22f));
            Rectangulo(m, c, e + 15f, s + 16f, 30f, 18f, 57f, y, new Color(0.30f, 0.52f, 0.40f));
        }
    }

    private static bool EnLugarOcupado(float e, float s, float margen)
    {
        for (int i = 0; i < ocupados.Count; i++)
        {
            Vector3 o = ocupados[i];
            float de = e - o.x, ds = s - o.y;
            if (de * de + ds * ds < (o.z + margen) * (o.z + margen)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Cerros Orientales
    // ------------------------------------------------------------------
    private struct Cordillera
    {
        public float r0, r1, altura, semiancho;
        public Color bajo, alto;
        public float semilla;
    }

    private static void ConstruirCerros(Malla m, Contexto c)
    {
        var capas = new[]
        {
            // Falda: lomas bajas y boscosas junto al campus.
            new Cordillera { r0 = 420f, r1 = 900f, altura = 70f, semiancho = 115f, semilla = 1.3f,
                             bajo = new Color(0.17f, 0.30f, 0.16f), alto = new Color(0.27f, 0.40f, 0.21f) },
            // Cordillera cercana: bosque oscuro (eucaliptos y pinos).
            new Cordillera { r0 = 760f, r1 = 1450f, altura = 380f, semiancho = 85f, semilla = 4.1f,
                             bajo = new Color(0.15f, 0.28f, 0.15f), alto = new Color(0.30f, 0.43f, 0.24f) },
            // Cordillera lejana: más alta y más azul por la bruma.
            new Cordillera { r0 = 1400f, r1 = 2200f, altura = 560f, semiancho = 75f, semilla = 7.7f,
                             bajo = new Color(0.22f, 0.36f, 0.30f), alto = new Color(0.45f, 0.52f, 0.42f) },
        };

        foreach (Cordillera cd in capas) Cordilleras(m, c, cd);
    }

    private static float Ruido(float a, float b)
    {
        return Mathf.Sin(a * 1.7f + b * 0.9f) * 0.50f +
               Mathf.Sin(a * 3.1f - b * 2.3f + 1.3f) * 0.30f +
               Mathf.Sin(a * 6.3f + b * 5.1f + 2.1f) * 0.20f;      // -1 a 1
    }

    private static void Cordilleras(Malla m, Contexto c, Cordillera cd)
    {
        const int filas = 14, columnas = 64;
        var puntos = new Vector3[columnas + 1, filas + 1];
        var colores = new Color[columnas + 1, filas + 1];

        for (int i = 0; i <= columnas; i++)
        {
            float u = (float)i / columnas;                       // 0..1 a lo ancho
            float az = Mathf.Lerp(-cd.semiancho, cd.semiancho, u);
            float envolvente = Mathf.Pow(Mathf.Cos(u * Mathf.PI - Mathf.PI * 0.5f), 0.7f);

            for (int j = 0; j <= filas; j++)
            {
                float t = (float)j / filas;
                float r = Mathf.Lerp(cd.r0, cd.r1, t);

                float subida = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f));
                float caida = 1f - 0.75f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.78f) / 0.22f));
                float cresta = 0.62f + 0.38f * (Ruido(az * 0.09f + cd.semilla, r * 0.004f) * 0.5f + 0.5f);
                float h = cd.altura * subida * caida * envolvente * cresta;

                Vector3 p = c.centro + Rumbo(c, az) * r;
                p.y = c.suelo - 3f + h;
                puntos[i, j] = p;

                float f = Mathf.Clamp01(h / Mathf.Max(1f, cd.altura * 0.85f));
                float parche = 0.5f + 0.5f * Ruido(az * 0.5f + cd.semilla, r * 0.02f);
                Color col = Color.Lerp(cd.bajo, cd.alto, f * 0.8f + parche * 0.2f);
                colores[i, j] = col;
            }
        }

        for (int i = 0; i < columnas; i++)
        {
            for (int j = 0; j < filas; j++)
            {
                m.Tri(c, puntos[i, j], puntos[i + 1, j], puntos[i, j + 1],
                      colores[i, j], colores[i + 1, j], colores[i, j + 1], Vector3.up, true);
                m.Tri(c, puntos[i + 1, j], puntos[i + 1, j + 1], puntos[i, j + 1],
                      colores[i + 1, j], colores[i + 1, j + 1], colores[i, j + 1], Vector3.up, true);
            }
        }
    }

    // ------------------------------------------------------------------
    // Ciudad y árboles
    // ------------------------------------------------------------------
    private static readonly Color[] Fachadas =
    {
        new Color(0.62f, 0.34f, 0.26f),   // ladrillo
        new Color(0.58f, 0.31f, 0.24f),   // ladrillo oscuro
        new Color(0.80f, 0.74f, 0.62f),   // crema
        new Color(0.62f, 0.64f, 0.66f),   // concreto
        new Color(0.82f, 0.82f, 0.80f),   // blanco sucio
        new Color(0.75f, 0.62f, 0.40f),   // ocre
    };

    private static void ConstruirCiudadYArboles(Malla m, Contexto c, bool hayOsm)
    {
        var rnd = new Rng(20261107);
        var ocupado = new HashSet<long>();
        Vector3 ejeE = Rumbo(c, 0f), ejeS = Rumbo(c, 90f);
        float eRuta = CarreraSieteE + OrigenTorreE;

        int edificios = 0, arboles = 0;

        for (int n = 0; n < 3600; n++)
        {
            float az = (float)rnd.NextDouble() * 360f;
            float r = 40f + Mathf.Pow((float)rnd.NextDouble(), 1.5f) * 1750f;
            Vector3 p = c.centro + Rumbo(c, az) * r;
            Vector3 d = p - c.centro;
            float e = Vector3.Dot(d, ejeE), s = Vector3.Dot(d, ejeS);

            // El edificio modelado y los sitios del mapa ya ocupados.
            if (hayOsm && (p - (c.centro + Vector3.forward * OrigenTorre)).magnitude < RadioOsm + 20f) continue;   // ahí mandan los datos reales
            if (Mathf.Abs(e) < c.mitadE + 14f && Mathf.Abs(s) < c.mitadS + 14f) continue;
            if (EnLugarOcupado(e, s, 6f)) continue;
            if (Mathf.Abs(e - eRuta) < 22f) continue;                  // la Carrera 7

            // Zonas, según el mapa.
            bool campus = e > eRuta + 22f && e < 205f && s > -450f && s < 345f;
            bool oeste = e <= eRuta - 22f;                             // al otro lado de la Carrera 7
            bool norte = !campus && !oeste && s <= -450f && e < 205f;  // pasada la calle 45
            bool sur = !campus && !oeste && s >= 345f && e < 205f;     // Parque Nacional y más allá
            bool este = e >= 205f;                                     // Circunvalar y cerros

            bool parque = (e + 95f) * (e + 95f) + (s - 237f) * (s - 237f) < 190f * 190f;   // Parque Nacional

            bool arbol = false, edificio = false;
            float altoMin = 12f, altoMax = 34f;
            double dado = rnd.NextDouble();

            if (campus)
            {
                arbol = dado < (parque ? 0.95 : 0.72);                 // el campus es muy arbolado
            }
            else if (oeste)
            {
                arbol = r < 450f && dado < 0.12;
                edificio = !arbol && dado < 0.72;
                altoMin = 10f + r * 0.006f; altoMax = 30f + r * 0.025f;
            }
            else if (norte)
            {
                arbol = dado < 0.2;
                edificio = !arbol && dado < 0.75;
                altoMin = 12f + r * 0.006f; altoMax = 36f + r * 0.022f;
            }
            else if (sur)
            {
                arbol = r < 900f && dado < 0.9;                        // Parque Nacional y su bosque
                edificio = !arbol && r >= 900f && dado < 0.6;
                altoMin = 12f + r * 0.005f; altoMax = 32f + r * 0.02f;
            }
            else if (este)
            {
                arbol = r < 720f && dado < 0.92;                       // bosque de la falda
            }

            if (arbol)
            {
                long clave = Celda(p, 9f, 1);
                if (!ocupado.Add(clave)) continue;
                Arbol(m, c, p, 8f + (float)rnd.NextDouble() * 10f, 3f + (float)rnd.NextDouble() * 2.5f,
                      (float)rnd.NextDouble());
                arboles++;
            }
            else if (edificio)
            {
                long clave = Celda(p, 42f, 2);
                if (!ocupado.Add(clave)) continue;
                float ancho = 16f + (float)rnd.NextDouble() * 20f;
                float fondo = 16f + (float)rnd.NextDouble() * 20f;
                float alto = Mathf.Lerp(altoMin, altoMax, Mathf.Pow((float)rnd.NextDouble(), 1.4f));
                float giro = c.yawOriente;                              // manzanas paralelas a la Carrera 7
                Color col = Fachadas[(int)(rnd.NextDouble() * Fachadas.Length)];
                p.y = c.suelo;
                Caja(m, c, p, ancho, fondo, alto, giro, col, r < 550f && alto > 12f);
                edificios++;
            }
        }

        Debug.Log($"[Entorno] Fondo inventado: {edificios} edificios de ciudad y {arboles} árboles " +
                  (hayOsm ? "(más allá de los datos reales)." : "(sin datos reales: se usa el mapa a mano)."));
    }

    private static long Celda(Vector3 p, float lado, int tipo)
    {
        long x = Mathf.FloorToInt(p.x / lado) + 100000;
        long z = Mathf.FloorToInt(p.z / lado) + 100000;
        return (x * 400000L + z) * 4L + tipo;
    }

    /// <summary>Árbol: cono de seis lados, más claro hacia la punta.</summary>
    private static void Arbol(Malla m, Contexto c, Vector3 pie, float alto, float radio, float variacion)
    {
        pie.y = c.suelo;
        Color abajo = Color.Lerp(new Color(0.13f, 0.27f, 0.13f), new Color(0.20f, 0.34f, 0.15f), variacion);
        Color arriba = Color.Lerp(abajo, new Color(0.38f, 0.52f, 0.26f), 0.6f);
        Vector3 punta = pie + Vector3.up * alto;

        const int lados = 6;
        for (int i = 0; i < lados; i++)
        {
            float a0 = Mathf.PI * 2f * i / lados, a1 = Mathf.PI * 2f * (i + 1) / lados;
            Vector3 b0 = pie + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radio + Vector3.up * (alto * 0.18f);
            Vector3 b1 = pie + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radio + Vector3.up * (alto * 0.18f);
            Vector3 fuera = (b0 + b1) * 0.5f - (pie + Vector3.up * (alto * 0.18f));
            m.Tri(c, b0, b1, punta, abajo, abajo, arriba, fuera + Vector3.up * 0.4f, true);
        }
    }

    /// <summary>
    /// Caja con paredes y techo (sin fondo). Con 'bandas' cada piso lleva una
    /// franja oscura de ventanas, que es lo que le quita lo liso de cerca.
    /// </summary>
    private static void Caja(Malla m, Contexto c, Vector3 baseCentro, float ancho, float fondo,
                             float alto, float giroY, Color color, bool bandas)
    {
        Quaternion q = Quaternion.Euler(0f, giroY, 0f);
        Vector3 hx = q * Vector3.right * (ancho * 0.5f);
        Vector3 hz = q * Vector3.forward * (fondo * 0.5f);

        Vector3[] esq =
        {
            baseCentro - hx - hz, baseCentro + hx - hz, baseCentro + hx + hz, baseCentro - hx + hz,
        };

        const float pisoAlto = 3.4f;
        int pisos = bandas ? Mathf.Max(1, Mathf.FloorToInt(alto / pisoAlto)) : 1;
        float paso = alto / pisos;
        // Franja de ventanas: oscura pero emparentada con la fachada, para que no parezcan rayas.
        Color ventana = Color.Lerp(color, new Color(0.34f, 0.40f, 0.48f), 0.5f);

        for (int lado = 0; lado < 4; lado++)
        {
            Vector3 a = esq[lado], b = esq[(lado + 1) % 4];
            Vector3 fuera = ((a + b) * 0.5f) - baseCentro;
            fuera.y = 0f;

            for (int k = 0; k < pisos; k++)
            {
                float y0 = k * paso, y1 = (k + 1) * paso;
                if (!bandas)
                {
                    m.Cuad(c, a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1,
                           color, fuera);
                    continue;
                }
                float ym = y0 + paso * 0.30f, yv = y0 + paso * 0.80f;
                m.Cuad(c, a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * ym, a + Vector3.up * ym, color, fuera);
                m.Cuad(c, a + Vector3.up * ym, b + Vector3.up * ym, b + Vector3.up * yv, a + Vector3.up * yv, ventana, fuera);
                m.Cuad(c, a + Vector3.up * yv, b + Vector3.up * yv, b + Vector3.up * y1, a + Vector3.up * y1, color, fuera);
            }
        }

        Color techo = color * 0.85f;
        m.Cuad(c, esq[0] + Vector3.up * alto, esq[1] + Vector3.up * alto,
               esq[2] + Vector3.up * alto, esq[3] + Vector3.up * alto, techo, Vector3.up);
    }

    // ------------------------------------------------------------------
    // Nubes y cielo
    // ------------------------------------------------------------------
    private static void ConstruirNubes(Malla m, Contexto c)
    {
        var rnd = new Rng(3141);
        for (int k = 0; k < 16; k++)
        {
            float az = (float)rnd.NextDouble() * 360f;
            float r = 1300f + (float)rnd.NextDouble() * 1000f;
            float altura = 850f + (float)rnd.NextDouble() * 550f;
            Vector3 centro = c.centro + Rumbo(c, az) * r;
            centro.y = c.suelo + altura;
            Vector3 lateral = Vector3.Cross(Vector3.up, Rumbo(c, az)).normalized;

            int blobs = 3 + (int)(rnd.NextDouble() * 3);
            for (int b = 0; b < blobs; b++)
            {
                Vector3 pos = centro + lateral * ((b - blobs * 0.5f) * 130f) +
                              Vector3.up * ((float)rnd.NextDouble() * 20f);
                float rx = 90f + (float)rnd.NextDouble() * 80f;
                float ry = 24f + (float)rnd.NextDouble() * 20f;
                float rz = 70f + (float)rnd.NextDouble() * 50f;
                Elipsoide(m, c, pos, rx, ry, rz);
            }
        }
    }

    private static void Elipsoide(Malla m, Contexto c, Vector3 centro, float rx, float ry, float rz)
    {
        const int segs = 8, anillos = 5;
        var arriba = new Color(0.98f, 0.98f, 0.99f);
        var abajo = new Color(0.74f, 0.78f, 0.86f);

        for (int a = 0; a < anillos; a++)
        {
            float v0 = Mathf.PI * a / anillos - Mathf.PI * 0.5f;
            float v1 = Mathf.PI * (a + 1) / anillos - Mathf.PI * 0.5f;
            for (int s = 0; s < segs; s++)
            {
                float u0 = Mathf.PI * 2f * s / segs, u1 = Mathf.PI * 2f * (s + 1) / segs;
                Vector3 p00 = Punto(centro, rx, ry, rz, u0, v0), p10 = Punto(centro, rx, ry, rz, u1, v0);
                Vector3 p01 = Punto(centro, rx, ry, rz, u0, v1), p11 = Punto(centro, rx, ry, rz, u1, v1);
                Color c0 = Color.Lerp(abajo, arriba, (v0 / Mathf.PI) + 0.5f);
                Color c1 = Color.Lerp(abajo, arriba, (v1 / Mathf.PI) + 0.5f);
                Vector3 fuera = Punto(centro, 1f, 1f, 1f, (u0 + u1) * 0.5f, (v0 + v1) * 0.5f) - centro;
                m.Tri(c, p00, p10, p01, c0, c0, c1, fuera, false);
                m.Tri(c, p10, p11, p01, c0, c1, c1, fuera, false);
            }
        }
    }

    private static Vector3 Punto(Vector3 centro, float rx, float ry, float rz, float u, float v)
    {
        return centro + new Vector3(Mathf.Cos(v) * Mathf.Cos(u) * rx, Mathf.Sin(v) * ry, Mathf.Cos(v) * Mathf.Sin(u) * rz);
    }

    private static void ConstruirCielo(Malla m, Contexto c)
    {
        const int segs = 32;
        float[] elevaciones = { -25f, -8f, 0f, 6f, 14f, 25f, 40f, 58f, 75f, 90f };

        for (int e = 0; e < elevaciones.Length - 1; e++)
        {
            for (int s = 0; s < segs; s++)
            {
                float u0 = 360f * s / segs, u1 = 360f * (s + 1) / segs;
                Vector3 p00 = PuntoDelCielo(c, u0, elevaciones[e]), p10 = PuntoDelCielo(c, u1, elevaciones[e]);
                Vector3 p01 = PuntoDelCielo(c, u0, elevaciones[e + 1]), p11 = PuntoDelCielo(c, u1, elevaciones[e + 1]);
                Color c0 = ColorDelCielo(elevaciones[e]), c1 = ColorDelCielo(elevaciones[e + 1]);
                Vector3 fuera = ((p00 + p10 + p01 + p11) * 0.25f) - c.centro;
                m.TriPlano(p00, p10, p01, c0, c0, c1, fuera);
                m.TriPlano(p10, p11, p01, c0, c1, c1, fuera);
            }
        }
    }

    private static Vector3 PuntoDelCielo(Contexto c, float rumbo, float elevacion)
    {
        float el = elevacion * Mathf.Deg2Rad;
        Vector3 horizontal = Rumbo(c, rumbo) * Mathf.Cos(el);
        return c.centro + (horizontal + Vector3.up * Mathf.Sin(el)) * RadioDelCielo;
    }

    private static Color ColorDelCielo(float elevacion)
    {
        if (elevacion <= 0f) return Bruma;
        float t = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(elevacion * Mathf.Deg2Rad)), 0.55f);
        return Color.Lerp(Bruma, CieloAlto, t);
    }

    // ------------------------------------------------------------------
    // Malla con color por vértice
    // ------------------------------------------------------------------
    private static void CrearObjeto(Transform padre, string nombre, Malla datos, Shader shader, float cull)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);

        var mesh = datos.Crear(nombre);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mat = new Material(shader) { name = "Mat_Fondo_" + nombre };
        mat.SetFloat("_Cull", cull);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private class Malla
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Color> colores = new List<Color>();
        public readonly List<int> indices = new List<int>();

        /// <summary>Triángulo con la cara frontal hacia 'hacia'; con 'sombrear' lleva sombra de cara y bruma.</summary>
        public void Tri(Contexto c, Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd,
                        Vector3 hacia, bool sombrear)
        {
            Vector3 n = Vector3.Cross(b - a, d - a);
            if (Vector3.Dot(n, hacia) < 0f)
            {
                (b, d) = (d, b);
                (cb, cd) = (cd, cb);
                n = -n;
            }
            n.Normalize();

            if (sombrear)
            {
                float sombra = 0.68f + 0.32f * Mathf.Clamp01(Vector3.Dot(n, Sol));
                ca = Velar(c, a, ca * sombra);
                cb = Velar(c, b, cb * sombra);
                cd = Velar(c, d, cd * sombra);
            }
            else
            {
                ca = Velar(c, a, ca);
                cb = Velar(c, b, cb);
                cd = Velar(c, d, cd);
            }
            Agregar(a, b, d, ca, cb, cd);
        }

        /// <summary>Sin sombra ni bruma: el cielo ya trae su color.</summary>
        public void TriPlano(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd, Vector3 hacia)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), hacia) < 0f)
            {
                (b, d) = (d, b);
                (cb, cd) = (cd, cb);
            }
            Agregar(a, b, d, ca, cb, cd);
        }

        public void Cuad(Contexto c, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color color, Vector3 hacia)
        {
            Tri(c, p0, p1, p2, color, color, color, hacia, true);
            Tri(c, p0, p2, p3, color, color, color, hacia, true);
        }

        private static Color Velar(Contexto c, Vector3 p, Color color)
        {
            Vector3 d = p - c.centro;
            d.y *= 0.3f;                                     // la altura pesa menos que la distancia
            float t = Mathf.Clamp01((d.magnitude - 120f) / 2600f);
            t = Mathf.Pow(t, 0.75f) * 0.85f;
            color.a = 1f;
            return Color.Lerp(color, Bruma, t);
        }

        private void Agregar(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(d);
            colores.Add(ca); colores.Add(cb); colores.Add(cd);
            indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
        }

        public Mesh Crear(string nombre)
        {
            var mesh = new Mesh { name = "Fondo_" + nombre };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colores);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
