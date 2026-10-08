using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// FormasMovU.cs — Cajas, placas y escalones generados por código
// ============================================================================
// Todo lo que se añade al edificio en tiempo de ejecución (puertas, escalera,
// lámparas, mostradores) sale de aquí. La razón es la misma que la de
// NpcMeshFactory: el presupuesto de triángulos (SRS RD-4: 100.000 por piso, de
// los que la planta ya gasta 93.628). Una caja son 12 triángulos y una placa 2;
// un cubo o un quad de Unity cuestan lo mismo, pero CreatePrimitive les pone un
// collider que aquí casi nunca se quiere.
//
// Lleva la cuenta de los triángulos que crea por piso, para que el montaje la
// escriba en consola y las pruebas puedan vigilar el presupuesto.
// ============================================================================

public static class FormasMovU
{
    public const int TriangulosDeCaja = 12;
    public const int TriangulosDePlaca = 2;

    private static Mesh cubo;
    private static Mesh placa;
    private static Material cieloRaso;
    private static Texture2D texturaDeCieloRaso;
    private static readonly Dictionary<long, Material> luminosos = new Dictionary<long, Material>();
    private static readonly Dictionary<int, int> triangulosPorPiso = new Dictionary<int, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        cubo = null;
        placa = null;
        cieloRaso = null;
        texturaDeCieloRaso = null;
        luminosos.Clear();
        triangulosPorPiso.Clear();
    }

    // ------------------------------------------------------------------
    // Presupuesto
    // ------------------------------------------------------------------
    public static void ReiniciarCuenta()
    {
        triangulosPorPiso.Clear();
    }

    public static void Contar(int piso, int triangulos)
    {
        triangulosPorPiso.TryGetValue(piso, out int antes);
        triangulosPorPiso[piso] = antes + triangulos;
    }

    /// <summary>Triángulos de ambientación creados en el piso indicado (contando desde 0).</summary>
    public static int TriangulosEn(int piso)
    {
        triangulosPorPiso.TryGetValue(piso, out int n);
        return n;
    }

    // ------------------------------------------------------------------
    // Mallas compartidas
    // ------------------------------------------------------------------
    /// <summary>Cubo de 1 m centrado en el origen, con caras planas (24 vértices).</summary>
    public static Mesh Cubo
    {
        get
        {
            if (cubo == null) cubo = ConstruirCubo();
            return cubo;
        }
    }

    /// <summary>Placa de 1 x 1 m en el plano XZ, mirando hacia ABAJO (para techos y lámparas).</summary>
    public static Mesh Placa
    {
        get
        {
            if (placa == null) placa = ConstruirPlaca();
            return placa;
        }
    }

    private static Mesh ConstruirCubo()
    {
        var v = new List<Vector3>(24);
        var t = new List<int>(36);
        const float m = 0.5f;

        Cara(v, t, new Vector3(-m, -m, m), new Vector3(m, -m, m), new Vector3(m, m, m), new Vector3(-m, m, m), Vector3.forward);
        Cara(v, t, new Vector3(-m, -m, -m), new Vector3(m, -m, -m), new Vector3(m, m, -m), new Vector3(-m, m, -m), Vector3.back);
        Cara(v, t, new Vector3(m, -m, -m), new Vector3(m, -m, m), new Vector3(m, m, m), new Vector3(m, m, -m), Vector3.right);
        Cara(v, t, new Vector3(-m, -m, -m), new Vector3(-m, -m, m), new Vector3(-m, m, m), new Vector3(-m, m, -m), Vector3.left);
        Cara(v, t, new Vector3(-m, m, -m), new Vector3(m, m, -m), new Vector3(m, m, m), new Vector3(-m, m, m), Vector3.up);
        Cara(v, t, new Vector3(-m, -m, -m), new Vector3(m, -m, -m), new Vector3(m, -m, m), new Vector3(-m, -m, m), Vector3.down);

        var malla = new Mesh { name = "MovU_Cubo" };
        malla.SetVertices(v);
        malla.SetTriangles(t, 0);
        malla.RecalculateNormals();
        malla.RecalculateBounds();
        return malla;
    }

    private static Mesh ConstruirPlaca()
    {
        var v = new List<Vector3>(4);
        var t = new List<int>(6);
        const float m = 0.5f;
        Cara(v, t, new Vector3(-m, 0f, -m), new Vector3(m, 0f, -m), new Vector3(m, 0f, m), new Vector3(-m, 0f, m), Vector3.down);

        // UV de 0 a 1 siguiendo X y Z: el cielo raso repite su retícula con el
        // 'tiling' del material. Se calculan a partir de la posición porque Cara()
        // puede reordenar los vértices.
        var uv = new List<Vector2>(4);
        for (int i = 0; i < v.Count; i++) uv.Add(new Vector2(v[i].x + m, v[i].z + m));

        var malla = new Mesh { name = "MovU_Placa" };
        malla.SetVertices(v);
        malla.SetUVs(0, uv);
        malla.SetTriangles(t, 0);
        malla.RecalculateNormals();
        malla.RecalculateBounds();
        return malla;
    }

    /// <summary>
    /// Añade un rectángulo (a, b, c, d en orden alrededor del borde) con sus
    /// propios vértices, mirando hacia 'normal'.
    /// </summary>
    public static void Cara(List<Vector3> vertices, List<int> triangulos,
                            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        // Unity dibuja la cara cuyo producto cruz (b-a) x (c-a) mira al observador.
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
        {
            Vector3 x = b; b = d; d = x;
        }

        int n = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        triangulos.Add(n); triangulos.Add(n + 1); triangulos.Add(n + 2);
        triangulos.Add(n); triangulos.Add(n + 2); triangulos.Add(n + 3);
    }

    /// <summary>
    /// Un tramo de escalera: sube en +Z desde el origen. 'peldanos' contrahuellas
    /// y una huella menos (la última contrahuella llega al rellano). Solo lleva
    /// las caras que se ven desde abajo y desde arriba: huellas y contrahuellas.
    /// Los costados se dejan abiertos porque van contra los muros.
    /// </summary>
    public static Mesh Escalones(int peldanos, float ancho, float largo, float alto, out int triangulos)
    {
        peldanos = Mathf.Max(2, peldanos);
        float huella = largo / (peldanos - 1);
        float contrahuella = alto / peldanos;

        var v = new List<Vector3>(peldanos * 8);
        var t = new List<int>(peldanos * 12);

        for (int k = 0; k < peldanos; k++)
        {
            float z = k * huella;
            float y0 = k * contrahuella;
            float y1 = (k + 1) * contrahuella;

            // Contrahuella: mira hacia quien sube (-Z).
            Cara(v, t, new Vector3(0f, y0, z), new Vector3(ancho, y0, z),
                       new Vector3(ancho, y1, z), new Vector3(0f, y1, z), Vector3.back);

            // Huella. La última contrahuella da directamente al rellano.
            if (k < peldanos - 1)
            {
                Cara(v, t, new Vector3(0f, y1, z), new Vector3(ancho, y1, z),
                           new Vector3(ancho, y1, z + huella), new Vector3(0f, y1, z + huella), Vector3.up);
            }
        }

        var malla = new Mesh { name = "MovU_Escalones" };
        malla.SetVertices(v);
        malla.SetTriangles(t, 0);
        malla.RecalculateNormals();
        malla.RecalculateBounds();
        triangulos = t.Count / 3;
        return malla;
    }

    // ------------------------------------------------------------------
    // Objetos
    // ------------------------------------------------------------------
    /// <summary>
    /// Una caja de 'tamano' metros centrada en 'posicionLocal' (respecto al padre).
    /// Sin collider salvo que se pida.
    /// </summary>
    public static GameObject Caja(Transform padre, string nombre, Vector3 posicionLocal,
                                  Vector3 tamano, Material material, int piso, bool conColision = false)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.localPosition = posicionLocal;
        go.transform.localScale = tamano;

        go.AddComponent<MeshFilter>().sharedMesh = Cubo;
        Vestir(go.AddComponent<MeshRenderer>(), material);
        if (conColision) go.AddComponent<BoxCollider>();

        Contar(piso, TriangulosDeCaja);
        return go;
    }

    /// <summary>Una placa horizontal que mira hacia abajo (techo, lámpara).</summary>
    public static GameObject PlacaHaciaAbajo(Transform padre, string nombre, Vector3 posicionMundo,
                                             float ancho, float largo, Material material, int piso)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.position = posicionMundo;
        go.transform.localScale = new Vector3(ancho, 1f, largo);

        go.AddComponent<MeshFilter>().sharedMesh = Placa;
        Vestir(go.AddComponent<MeshRenderer>(), material);

        Contar(piso, TriangulosDePlaca);
        return go;
    }

    /// <summary>
    /// Ajustes de dibujo iguales para toda la ambientación: bajo techo el sol no
    /// entra, así que una sombra en tiempo real por objeto sería un pase de
    /// dibujo más para algo que no se ve.
    /// </summary>
    public static void Vestir(MeshRenderer dibujo, Material material)
    {
        dibujo.sharedMaterial = material;
        dibujo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dibujo.receiveShadows = false;
        dibujo.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        dibujo.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // ------------------------------------------------------------------
    // Materiales
    // ------------------------------------------------------------------
    /// <summary>Un color mate, compartido (uno por color, con GPU instancing).</summary>
    public static Material Color(Color32 color)
    {
        return NpcMeshFactory.Material(color);
    }

    /// <summary>
    /// Un material que brilla por sí mismo (lámparas, letreros de emergencia).
    /// Usa la misma variante de URP/Lit con emisión que los NPC y la flecha, que
    /// ya está incluida en el ejecutable.
    /// </summary>
    public static Material Luminoso(Color32 color, float fuerza)
    {
        long clave = ((long)color.r << 40) | ((long)color.g << 32) | ((long)color.b << 24) |
                     (long)Mathf.RoundToInt(fuerza * 100f);
        if (luminosos.TryGetValue(clave, out Material existente) && existente != null) return existente;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (sh == null) sh = Shader.Find("Standard");

        var m = new Material(sh) { name = "Mat_Luz_" + ColorUtility.ToHtmlStringRGB(color) };
        Color c = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * fuerza);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.enableInstancing = true;

        luminosos[clave] = m;
        return m;
    }

    /// <summary>
    /// Cielo raso: placas con su perfilería (un módulo por unidad de UV). Sin la retícula el
    /// techo es un plano gris liso que se confunde con el cielo y no da ninguna
    /// referencia de distancia. Emite luz propia: bajo techo el sol no llega.
    /// </summary>
    public static Material CieloRaso()
    {
        if (cieloRaso == null)
        {
            const int lado = 64;
            texturaDeCieloRaso = new Texture2D(lado, lado, TextureFormat.RGBA32, true)
            {
                name = "MovU_CieloRaso",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var placaClara = new Color32(236, 236, 232, 255);
            var perfil = new Color32(176, 178, 180, 255);
            var pixeles = new Color32[lado * lado];
            for (int y = 0; y < lado; y++)
            {
                for (int x = 0; x < lado; x++)
                {
                    bool borde = x < 2 || y < 2;
                    pixeles[y * lado + x] = borde ? perfil : placaClara;
                }
            }
            texturaDeCieloRaso.SetPixels32(pixeles);
            texturaDeCieloRaso.Apply(true, false);

            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) sh = Shader.Find("Standard");

            cieloRaso = new Material(sh) { name = "Mat_CieloRaso" };
            if (cieloRaso.HasProperty("_BaseMap")) cieloRaso.SetTexture("_BaseMap", texturaDeCieloRaso);
            cieloRaso.mainTexture = texturaDeCieloRaso;
            if (cieloRaso.HasProperty("_BaseColor")) cieloRaso.SetColor("_BaseColor", UnityEngine.Color.white);
            if (cieloRaso.HasProperty("_Smoothness")) cieloRaso.SetFloat("_Smoothness", 0f);
            cieloRaso.EnableKeyword("_EMISSION");
            if (cieloRaso.HasProperty("_EmissionMap")) cieloRaso.SetTexture("_EmissionMap", texturaDeCieloRaso);
            cieloRaso.SetColor("_EmissionColor", new Color(0.62f, 0.62f, 0.60f));
            cieloRaso.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        return cieloRaso;
    }

    /// <summary>
    /// Una malla de placas horizontales que miran hacia ABAJO, una por rectángulo
    /// (x = X de mundo, y = Z de mundo), todas a y = 0. Las UV van en módulos del
    /// cielo raso medidos en el mundo, así la retícula sigue de una placa a la
    /// siguiente sin saltos aunque el techo tenga huecos.
    /// </summary>
    public static Mesh PlacasHaciaAbajo(IList<Rect> rectangulos, float modulo, string nombre,
                                        out int triangulos)
    {
        var v = new List<Vector3>(rectangulos.Count * 4);
        var t = new List<int>(rectangulos.Count * 6);
        for (int i = 0; i < rectangulos.Count; i++)
        {
            Rect r = rectangulos[i];
            Cara(v, t, new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMin),
                       new Vector3(r.xMax, 0f, r.yMax), new Vector3(r.xMin, 0f, r.yMax), Vector3.down);
        }

        float k = 1f / Mathf.Max(0.1f, modulo);
        var uv = new List<Vector2>(v.Count);
        for (int i = 0; i < v.Count; i++) uv.Add(new Vector2(v[i].x * k, v[i].z * k));

        var malla = new Mesh { name = nombre };
        malla.SetVertices(v);
        malla.SetUVs(0, uv);
        malla.SetTriangles(t, 0);
        malla.RecalculateNormals();
        malla.RecalculateBounds();
        triangulos = t.Count / 3;
        return malla;
    }

    /// <summary>Un objeto con una malla ya hecha (techo, escalones), sin collider.</summary>
    public static GameObject Pieza(Transform padre, string nombre, Vector3 posicionMundo, Mesh malla,
                                   Material material, int piso, int triangulos)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.position = posicionMundo;
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        Vestir(go.AddComponent<MeshRenderer>(), material);
        Contar(piso, triangulos);
        return go;
    }

    /// <summary>
    /// El material de los muros del edificio (el shader estilizado). Lo que se
    /// vista con él queda como parte de la obra: mismo degradado, mismo zócalo.
    /// </summary>
    public static Material DelEdificio(FloorManager pisos)
    {
        if (pisos != null)
        {
            for (int i = 0; i < pisos.CantidadDePisos; i++)
            {
                Transform piso = pisos.Piso(i);
                if (piso == null) continue;
                var dibujos = piso.GetComponentsInChildren<MeshRenderer>(true);
                for (int d = 0; d < dibujos.Length; d++)
                {
                    Material m = dibujos[d].sharedMaterial;
                    if (m != null && m.HasProperty("_WallColor")) return m;
                }
            }
        }
        return Color(new Color32(222, 222, 214, 255));
    }

    /// <summary>Convierte "1F4E9A" en un color. Devuelve 'porDefecto' si el texto no sirve.</summary>
    public static Color32 ColorDesdeHex(string hex, Color32 porDefecto)
    {
        if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out Color c))
        {
            return c;
        }
        return porDefecto;
    }
}
