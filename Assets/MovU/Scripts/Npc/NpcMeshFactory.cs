using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// NpcMeshFactory.cs — La figura de los NPC, generada por código
// ============================================================================
// Todos los NPC comparten UNA sola malla de unos 90 triángulos: un perfil
// girado alrededor del eje vertical (piernas, torso, cabeza), en tres
// submallas para poder dar un color a la ropa, otro a la piel y otro al
// pantalón.
//
// Por qué no cápsulas y esferas de Unity: una cápsula tiene 832 triángulos y
// una esfera 768. Veinte personajes así serían 32.000 triángulos, y el SRS deja
// 100.000 por piso (RD-4) cuando la planta sola ya gasta 94.440. Con esta malla
// los mismos veinte cuestan menos de 2.000.
//
// Los materiales también se comparten: uno por color, con GPU instancing.
// ============================================================================

public static class NpcMeshFactory
{
    public const int SubmallaRopa = 0;
    public const int SubmallaPiel = 1;
    public const int SubmallaPantalon = 2;

    private const int Lados = 7;

    private static Mesh malla;
    private static readonly Dictionary<Color32, Material> materiales = new Dictionary<Color32, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        malla = null;
        materiales.Clear();
    }

    /// <summary>Cantidad de triángulos de la figura (para el informe de presupuesto).</summary>
    public static int Triangulos => Malla.triangles.Length / 3;

    public static Mesh Malla
    {
        get
        {
            if (malla == null) malla = Construir();
            return malla;
        }
    }

    public static Material Material(Color32 color)
    {
        if (materiales.TryGetValue(color, out Material existente) && existente != null)
        {
            return existente;
        }

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (sh == null) sh = Shader.Find("Standard");

        var m = new Material(sh) { name = "Mat_Npc_" + ColorUtility.ToHtmlStringRGB(color) };
        Color c = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);

        // Un poco de emisión propia: el interior se ilumina con luz ambiente y
        // sin ella los personajes quedan apagados contra los muros claros.
        // Además es la misma variante del shader que ya usan la flecha y el
        // ascensor, así que seguro está incluida en el ejecutable.
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 0.22f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        m.enableInstancing = true;
        materiales[color] = m;
        return m;
    }

    // ------------------------------------------------------------------
    // Perfil: (radio, altura). Mide 1,80 m de alto, pies en y = 0.
    // ------------------------------------------------------------------
    private static readonly Vector2[] PerfilPantalon =
    {
        new Vector2(0.15f, 0.00f), new Vector2(0.19f, 0.86f),
    };

    private static readonly Vector2[] PerfilRopa =
    {
        new Vector2(0.21f, 0.86f), new Vector2(0.25f, 1.36f), new Vector2(0.09f, 1.47f),
    };

    private static readonly Vector2[] PerfilPiel =
    {
        new Vector2(0.07f, 1.47f), new Vector2(0.13f, 1.55f), new Vector2(0.15f, 1.66f),
        new Vector2(0.11f, 1.76f), new Vector2(0.00f, 1.80f),
    };

    private static Mesh Construir()
    {
        var vertices = new List<Vector3>(256);
        var ropa = new List<int>(128);
        var piel = new List<int>(128);
        var pantalon = new List<int>(64);

        Girar(PerfilRopa, vertices, ropa);
        Girar(PerfilPiel, vertices, piel);
        Girar(PerfilPantalon, vertices, pantalon);

        var m = new Mesh { name = "MovU_Npc" };
        m.SetVertices(vertices);
        m.subMeshCount = 3;
        m.SetTriangles(ropa, SubmallaRopa);
        m.SetTriangles(piel, SubmallaPiel);
        m.SetTriangles(pantalon, SubmallaPantalon);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// Gira un perfil alrededor del eje Y. Cada cara lleva sus propios vértices,
    /// así las normales salen planas (facetas low-poly) y no suavizadas.
    /// </summary>
    private static void Girar(Vector2[] perfil, List<Vector3> vertices, List<int> triangulos)
    {
        for (int s = 0; s < perfil.Length - 1; s++)
        {
            Vector2 abajo = perfil[s];
            Vector2 arriba = perfil[s + 1];

            for (int i = 0; i < Lados; i++)
            {
                float a0 = (i / (float)Lados) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)Lados) * Mathf.PI * 2f;

                Vector3 b0 = Punto(abajo, a0), b1 = Punto(abajo, a1);
                Vector3 t0 = Punto(arriba, a0), t1 = Punto(arriba, a1);

                if (arriba.x > 0.0001f) Triangulo(vertices, triangulos, b0, t0, t1);
                if (abajo.x > 0.0001f) Triangulo(vertices, triangulos, b0, t1, b1);
            }
        }
    }

    private static Vector3 Punto(Vector2 p, float angulo)
    {
        return new Vector3(Mathf.Cos(angulo) * p.x, p.y, Mathf.Sin(angulo) * p.x);
    }

    /// <summary>Añade un triángulo y lo deja mirando hacia AFUERA del eje.</summary>
    private static void Triangulo(List<Vector3> vertices, List<int> triangulos,
                                  Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        Vector3 centro = (a + b + c) / 3f;
        Vector3 haciaAfuera = new Vector3(centro.x, 0f, centro.z);

        // En la tapa de la cabeza el centro queda casi sobre el eje: ahí
        // "afuera" es hacia arriba.
        if (haciaAfuera.sqrMagnitude < 0.0004f) haciaAfuera = Vector3.up;

        if (Vector3.Dot(normal, haciaAfuera) < 0f)
        {
            Vector3 t = b; b = c; c = t;
        }

        int n = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        triangulos.Add(n); triangulos.Add(n + 1); triangulos.Add(n + 2);
    }
}
