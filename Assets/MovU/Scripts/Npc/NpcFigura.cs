using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// NpcFigura.cs — El cuerpo de una persona, por partes y animado
// ============================================================================
// Reemplaza a la figura de 91 triángulos (un perfil girado, sin brazos ni
// piernas) que se veía muy básica al lado del edificio. Ahora cada persona
// tiene torso con hombros, cuello, cabeza con pelo y ojos, y brazos y piernas
// sobre pivotes (cadera, hombros, cuello) para que se muevan al caminar y la
// cabeza pueda mirar alrededor.
//
// Sigue siendo low-poly y barato (~500 triángulos): las mallas se generan por
// código y se COMPARTEN (una por combinación de complexión, peinado y
// accesorio), y los materiales son uno por color con GPU instancing.
//
// No tiene Update: NpcCharacter llama a Animar cuando el NPC está activo y a la
// vista. Al jugador (tercera persona) lo anima FiguraDelJugador.
// ============================================================================

public enum Peinado { Corto, Largo, Coleta, Rapado }
public enum AccesorioNpc { Ninguno, Mochila, Carne, Gorra, Delantal, Bolso, Saco }

/// <summary>Cómo se ve una persona: colores, peinado, complexión y lo que lleva encima.</summary>
public struct Apariencia
{
    public Color32 ropa, pantalon, piel, pelo, zapatos, accesorio;
    public Peinado peinado;
    public AccesorioNpc extra;
    /// <summary>0 = hombros angostos, 1 = anchos.</summary>
    public float complexion;
    public bool mangaCorta;
}

[DisallowMultipleComponent]
public class NpcFigura : MonoBehaviour
{
    // Submallas de cada parte.
    private const int RopaS = 0, PielS = 1, PantalonS = 2, PeloS = 3, ZapatoS = 4, AccS = 5, OjosS = 6;
    private const int Submallas = 7;

    private Transform raiz, cabeza, piernaIzq, piernaDer, brazoIzq, brazoDer;
    private Renderer torso;
    private float fase, mirarActual, mirarObjetivo, abrir;

    /// <summary>True si alguna cámara lo está viendo (para no animar lo que no se ve).</summary>
    public bool Visible => torso != null && torso.isVisible;

    // ------------------------------------------------------------------
    // Construcción
    // ------------------------------------------------------------------
    public static NpcFigura Construir(Transform padre, Apariencia a, float talla, string nombre = "Figura")
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.localScale = Vector3.one * talla;
        var f = go.AddComponent<NpcFigura>();
        f.raiz = new GameObject("Cuerpo").transform;
        f.raiz.SetParent(go.transform, false);

        Material[] mats = Materiales(a);
        float hombro = Hombro(a.complexion);

        f.torso = Parte(f.raiz, "Torso", Vector3.zero, Malla($"torso_{Mathf.RoundToInt(a.complexion * 2)}_{a.extra}",
                        () => MallaTorso(a.complexion, a.extra)), mats);
        f.cabeza = Pivote(f.raiz, "Cabeza", new Vector3(0f, 1.55f, 0f));
        Parte(f.cabeza, "CabezaMalla", Vector3.zero, Malla($"cabeza_{a.peinado}_{a.extra == AccesorioNpc.Gorra}",
              () => MallaCabeza(a.peinado, a.extra == AccesorioNpc.Gorra)), mats);

        Mesh pierna = Malla("pierna", MallaPierna);
        f.piernaIzq = Pivote(f.raiz, "PiernaIzq", new Vector3(-0.095f, 0.92f, 0f));
        f.piernaDer = Pivote(f.raiz, "PiernaDer", new Vector3(0.095f, 0.92f, 0f));
        Parte(f.piernaIzq, "Malla", Vector3.zero, pierna, mats);
        Parte(f.piernaDer, "Malla", Vector3.zero, pierna, mats);

        bool manga = a.mangaCorta && a.extra != AccesorioNpc.Saco;
        Mesh brazo = Malla($"brazo_{manga}", () => MallaBrazo(manga));
        f.brazoIzq = Pivote(f.raiz, "BrazoIzq", new Vector3(-hombro - 0.03f, 1.41f, 0f));
        f.brazoDer = Pivote(f.raiz, "BrazoDer", new Vector3(hombro + 0.03f, 1.41f, 0f));
        Parte(f.brazoIzq, "Malla", Vector3.zero, brazo, mats);
        Parte(f.brazoDer, "Malla", Vector3.zero, brazo, mats);

        f.fase = Random.value * 6.28f;
        f.Animar(0f, 0f, 0f);
        return f;
    }

    private static Transform Pivote(Transform padre, string nombre, Vector3 pos)
    {
        var t = new GameObject(nombre).transform;
        t.SetParent(padre, false);
        t.localPosition = pos;
        return t;
    }

    private static Renderer Parte(Transform padre, string nombre, Vector3 pos, Mesh malla, Material[] mats)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        // Bajo techo no entra el sol: una sombra por persona sería un pase más que no se ve.
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return r;
    }

    private static Material[] Materiales(Apariencia a)
    {
        var m = new Material[Submallas];
        m[RopaS] = NpcMeshFactory.Material(a.ropa);
        m[PielS] = NpcMeshFactory.Material(a.piel);
        m[PantalonS] = NpcMeshFactory.Material(a.pantalon);
        m[PeloS] = NpcMeshFactory.Material(a.pelo);
        m[ZapatoS] = NpcMeshFactory.Material(a.zapatos);
        m[AccS] = NpcMeshFactory.Material(a.accesorio);
        m[OjosS] = NpcMeshFactory.Material(new Color32(30, 24, 22, 255));
        return m;
    }

    // ------------------------------------------------------------------
    // Animación
    // ------------------------------------------------------------------
    /// <summary>
    /// velocidad en m/s (0 = quieto). Camina balanceando piernas y brazos; quieto,
    /// respira y deja caer los brazos. La cabeza gira hacia 'MirarHacia'.
    /// </summary>
    public void Animar(float velocidad, float ahora, float dt)
    {
        if (raiz == null) return;
        float paso = Mathf.Clamp01(velocidad / 1.3f);
        fase += dt * (2.2f + velocidad * 3.6f);
        abrir = Mathf.MoveTowards(abrir, paso, dt * 3f);

        float s = Mathf.Sin(fase);
        float piernas = s * 30f * abrir;
        float brazos = s * 24f * abrir;
        float reposo = Mathf.Sin(ahora * 1.4f + fase * 0.1f) * 2.5f * (1f - abrir);

        piernaIzq.localRotation = Quaternion.Euler(piernas, 0f, 0f);
        piernaDer.localRotation = Quaternion.Euler(-piernas, 0f, 0f);
        brazoIzq.localRotation = Quaternion.Euler(-brazos + reposo, 0f, -4f);
        brazoDer.localRotation = Quaternion.Euler(brazos + reposo, 0f, 4f);
        raiz.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Cos(fase)) * 0.03f * abrir +
                                              Mathf.Sin(ahora * 1.4f) * 0.004f * (1f - abrir), 0f);

        mirarActual = Mathf.MoveTowards(mirarActual, mirarObjetivo, dt * 70f);
        cabeza.localRotation = Quaternion.Euler(abrir > 0.5f ? 0f : 4f, mirarActual, 0f);
    }

    /// <summary>Gira la cabeza (grados, + a la derecha) respecto al cuerpo. Se limita a ±70°.</summary>
    public void MirarHacia(float grados) => mirarObjetivo = Mathf.Clamp(grados, -70f, 70f);

    /// <summary>Triángulos de una figura típica (estudiante con mochila y pelo largo).</summary>
    public static int TriangulosDeReferencia
    {
        get
        {
            int t = Malla($"torso_1_{AccesorioNpc.Mochila}", () => MallaTorso(0.5f, AccesorioNpc.Mochila)).triangles.Length;
            t += Malla($"cabeza_{Peinado.Largo}_False", () => MallaCabeza(Peinado.Largo, false)).triangles.Length;
            t += 2 * Malla("pierna", MallaPierna).triangles.Length;
            t += 2 * Malla("brazo_False", () => MallaBrazo(false)).triangles.Length;
            return t / 3;
        }
    }

    // ------------------------------------------------------------------
    // Mallas (compartidas)
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, Mesh> mallas = new Dictionary<string, Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos() => mallas.Clear();

    private static Mesh Malla(string clave, System.Func<Mesh> crear)
    {
        if (mallas.TryGetValue(clave, out Mesh m) && m != null) return m;
        m = crear();
        m.name = "MovU_Npc_" + clave;
        mallas[clave] = m;
        return m;
    }

    private static float Hombro(float complexion) => 0.19f + 0.04f * Mathf.Clamp01(complexion);

    private static Mesh MallaTorso(float complexion, AccesorioNpc extra)
    {
        var c = new Constructor();
        float h = Hombro(complexion);
        float pecho = 0.17f + 0.025f * complexion;
        int ropa = RopaS;
        // Cadera (pantalón) y torso (ropa), con sección elíptica: más ancho que profundo.
        c.Loft(PantalonS, new[] { new Anillo(0.84f, 0.165f, 0.105f), new Anillo(0.98f, 0.162f, 0.10f) }, 8, true, false);
        c.Loft(ropa, new[] {
            new Anillo(0.98f, 0.165f, 0.105f), new Anillo(1.12f, 0.15f, 0.095f),
            new Anillo(1.29f, pecho, 0.115f, 0.01f), new Anillo(1.40f, h, 0.105f),
            new Anillo(1.46f, 0.09f, 0.075f) }, 8, false, true);
        c.Loft(PielS, new[] { new Anillo(1.44f, 0.05f, 0.05f, 0.005f), new Anillo(1.57f, 0.045f, 0.045f, 0.01f) }, 6, false, false);

        switch (extra)
        {
            case AccesorioNpc.Mochila:
                c.Caja(AccS, new Vector3(0f, 1.20f, -0.175f), new Vector3(0.29f, 0.38f, 0.13f));
                c.Caja(AccS, new Vector3(0f, 1.11f, -0.245f), new Vector3(0.22f, 0.15f, 0.04f));      // bolsillo
                for (int l = -1; l <= 1; l += 2)
                    c.Caja(AccS, new Vector3(l * 0.095f, 1.28f, 0.11f), new Vector3(0.04f, 0.26f, 0.02f));   // correas
                break;
            case AccesorioNpc.Carne:
                for (int l = -1; l <= 1; l += 2)
                    c.Caja(AccS, new Vector3(l * 0.04f, 1.33f, 0.112f), new Vector3(0.018f, 0.2f, 0.012f), Quaternion.Euler(0f, 0f, l * 14f));
                c.Caja(AccS, new Vector3(0f, 1.20f, 0.122f), new Vector3(0.07f, 0.095f, 0.012f));
                break;
            case AccesorioNpc.Delantal:
                c.Caja(AccS, new Vector3(0f, 1.02f, 0.115f), new Vector3(0.30f, 0.46f, 0.018f));
                c.Caja(AccS, new Vector3(0f, 1.30f, 0.106f), new Vector3(0.18f, 0.16f, 0.016f));
                break;
            case AccesorioNpc.Bolso:
                c.Caja(AccS, new Vector3(0.20f, 0.97f, 0.02f), new Vector3(0.08f, 0.2f, 0.25f));
                c.Caja(AccS, new Vector3(0.02f, 1.20f, 0.11f), new Vector3(0.035f, 0.56f, 0.015f), Quaternion.Euler(0f, 0f, 36f));
                break;
            case AccesorioNpc.Saco:
                // Camisa clara al centro y solapas del saco (el saco es la 'ropa').
                c.Caja(AccS, new Vector3(0f, 1.26f, 0.112f), new Vector3(0.09f, 0.32f, 0.012f));
                for (int l = -1; l <= 1; l += 2)
                    c.Caja(RopaS, new Vector3(l * 0.06f, 1.30f, 0.118f), new Vector3(0.05f, 0.2f, 0.012f), Quaternion.Euler(0f, 0f, -l * 12f));
                break;
        }
        return c.Crear();
    }

    private static Mesh MallaCabeza(Peinado peinado, bool gorra)
    {
        var c = new Constructor();
        // Cabeza: un poco más honda que ancha, con la mandíbula más angosta.
        c.Loft(PielS, new[] {
            new Anillo(0.00f, 0.055f, 0.06f, 0.01f), new Anillo(0.05f, 0.085f, 0.095f, 0.015f),
            new Anillo(0.12f, 0.098f, 0.108f, 0.01f), new Anillo(0.19f, 0.096f, 0.106f, 0.005f),
            new Anillo(0.25f, 0.072f, 0.082f), new Anillo(0.28f, 0f, 0f) }, 8, true, false);
        // Nariz y ojos.
        c.Caja(PielS, new Vector3(0f, 0.105f, 0.118f), new Vector3(0.026f, 0.04f, 0.03f));
        for (int l = -1; l <= 1; l += 2)
            c.Caja(OjosS, new Vector3(l * 0.036f, 0.135f, 0.112f), new Vector3(0.022f, 0.02f, 0.012f));

        if (gorra)
        {
            c.Loft(AccS, new[] { new Anillo(0.17f, 0.105f, 0.115f), new Anillo(0.24f, 0.098f, 0.108f), new Anillo(0.30f, 0f, 0f) }, 8, false, false);
            c.Caja(AccS, new Vector3(0f, 0.175f, 0.15f), new Vector3(0.17f, 0.015f, 0.09f));
            c.Caja(PeloS, new Vector3(0f, 0.12f, -0.07f), new Vector3(0.18f, 0.1f, 0.06f));
        }
        else if (peinado != Peinado.Rapado)
        {
            // Casquete: tapa la coronilla y baja por atrás.
            c.Loft(PeloS, new[] { new Anillo(0.15f, 0.104f, 0.114f, -0.005f), new Anillo(0.22f, 0.1f, 0.11f),
                                  new Anillo(0.27f, 0.07f, 0.08f), new Anillo(0.30f, 0f, 0f) }, 8, false, false);
            c.Caja(PeloS, new Vector3(0f, 0.12f, -0.07f), new Vector3(0.19f, 0.13f, 0.07f));
            if (peinado == Peinado.Largo)
                c.Caja(PeloS, new Vector3(0f, 0.0f, -0.08f), new Vector3(0.21f, 0.3f, 0.07f));
            if (peinado == Peinado.Coleta)
                c.Caja(PeloS, new Vector3(0f, 0.08f, -0.16f), new Vector3(0.06f, 0.17f, 0.06f), Quaternion.Euler(-20f, 0f, 0f));
        }
        else
        {
            c.Loft(PeloS, new[] { new Anillo(0.2f, 0.098f, 0.108f), new Anillo(0.26f, 0.074f, 0.084f), new Anillo(0.285f, 0f, 0f) }, 8, false, false);
        }
        return c.Crear();
    }

    private static Mesh MallaPierna()
    {
        var c = new Constructor();
        c.Loft(PantalonS, new[] {
            new Anillo(0.02f, 0.085f, 0.09f), new Anillo(-0.42f, 0.064f, 0.07f, 0.01f),
            new Anillo(-0.62f, 0.06f, 0.066f), new Anillo(-0.84f, 0.048f, 0.052f) }, 6, false, true);
        c.Caja(ZapatoS, new Vector3(0f, -0.885f, 0.035f), new Vector3(0.105f, 0.075f, 0.26f));
        return c.Crear();
    }

    private static Mesh MallaBrazo(bool mangaCorta)
    {
        var c = new Constructor();
        if (mangaCorta)
        {
            c.Loft(RopaS, new[] { new Anillo(0.02f, 0.058f, 0.058f), new Anillo(-0.22f, 0.052f, 0.052f) }, 6, false, true);
            c.Loft(PielS, new[] { new Anillo(-0.2f, 0.042f, 0.042f), new Anillo(-0.30f, 0.04f, 0.04f),
                                  new Anillo(-0.55f, 0.033f, 0.031f) }, 6, false, true);
        }
        else
        {
            c.Loft(RopaS, new[] { new Anillo(0.02f, 0.058f, 0.058f), new Anillo(-0.30f, 0.048f, 0.048f),
                                  new Anillo(-0.54f, 0.04f, 0.038f) }, 6, false, true);
        }
        c.Caja(PielS, new Vector3(0f, -0.6f, 0.005f), new Vector3(0.06f, 0.1f, 0.04f));
        return c.Crear();
    }

    // ------------------------------------------------------------------
    // Constructor de mallas low-poly (caras planas, normales hacia afuera)
    // ------------------------------------------------------------------
    private struct Anillo
    {
        public float y, rx, rz, cz;
        public Anillo(float y, float rx, float rz, float cz = 0f) { this.y = y; this.rx = rx; this.rz = rz; this.cz = cz; }
    }

    private sealed class Constructor
    {
        private readonly List<Vector3> v = new List<Vector3>(512);
        private readonly List<int>[] sub = new List<int>[Submallas];

        public Constructor() { for (int i = 0; i < Submallas; i++) sub[i] = new List<int>(); }

        /// <summary>Triángulo con la cara apuntando hacia 'afuera'.</summary>
        private void Tri(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 afuera)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), afuera) < 0f) { Vector3 t = b; b = c; c = t; }
            int n = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            sub[s].Add(n); sub[s].Add(n + 1); sub[s].Add(n + 2);
        }

        private static Vector3 P(Anillo r, float ang) =>
            new Vector3(Mathf.Cos(ang) * r.rx, r.y, r.cz + Mathf.Sin(ang) * r.rz);

        public void Loft(int s, Anillo[] anillos, int lados, bool tapaAbajo, bool tapaArriba)
        {
            for (int k = 0; k < anillos.Length - 1; k++)
            {
                Anillo a = anillos[k], b = anillos[k + 1];
                for (int i = 0; i < lados; i++)
                {
                    float a0 = (i + 0.5f) / lados * Mathf.PI * 2f, a1 = (i + 1.5f) / lados * Mathf.PI * 2f;
                    Vector3 b0 = P(a, a0), b1 = P(a, a1), t0 = P(b, a0), t1 = P(b, a1);
                    Vector3 centro = (b0 + b1 + t0 + t1) * 0.25f;
                    Vector3 eje = new Vector3(0f, centro.y, (a.cz + b.cz) * 0.5f);
                    Vector3 afuera = centro - eje;
                    if (afuera.sqrMagnitude < 1e-6f) afuera = b.y > a.y ? Vector3.up : Vector3.down;
                    // Cerca de la punta (radio ~0) el "afuera" radial es pobre: se le suma la subida.
                    if (b.rx < 0.001f) afuera += (b.y > a.y ? Vector3.up : Vector3.down) * 0.05f;
                    if (b.rx > 0.001f) Tri(s, b0, t0, t1, afuera);
                    if (a.rx > 0.001f) Tri(s, b0, t1, b1, afuera);
                }
            }
            if (tapaAbajo) Tapa(s, anillos[0], lados, Vector3.down);
            if (tapaArriba) Tapa(s, anillos[anillos.Length - 1], lados, Vector3.up);
        }

        private void Tapa(int s, Anillo r, int lados, Vector3 afuera)
        {
            if (r.rx < 0.001f) return;
            Vector3 c = new Vector3(0f, r.y, r.cz);
            for (int i = 0; i < lados; i++)
            {
                float a0 = (i + 0.5f) / lados * Mathf.PI * 2f, a1 = (i + 1.5f) / lados * Mathf.PI * 2f;
                Tri(s, c, P(r, a0), P(r, a1), afuera);
            }
        }

        public void Caja(int s, Vector3 centro, Vector3 tam) => Caja(s, centro, tam, Quaternion.identity);

        public void Caja(int s, Vector3 centro, Vector3 tam, Quaternion rot)
        {
            Vector3 h = tam * 0.5f;
            Vector3[] ejes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int e = 0; e < 3; e++)
            {
                for (int signo = -1; signo <= 1; signo += 2)
                {
                    Vector3 n = ejes[e] * signo;
                    Vector3 u = ejes[(e + 1) % 3], w = ejes[(e + 2) % 3];
                    float hn = Vector3.Scale(h, ejes[e]).magnitude, hu = Vector3.Scale(h, u).magnitude, hw = Vector3.Scale(h, w).magnitude;
                    Vector3 c0 = n * hn;
                    Vector3 p0 = centro + rot * (c0 - u * hu - w * hw), p1 = centro + rot * (c0 + u * hu - w * hw);
                    Vector3 p2 = centro + rot * (c0 + u * hu + w * hw), p3 = centro + rot * (c0 - u * hu + w * hw);
                    Vector3 afuera = rot * n;
                    Tri(s, p0, p1, p2, afuera);
                    Tri(s, p0, p2, p3, afuera);
                }
            }
        }

        public Mesh Crear()
        {
            var m = new Mesh();
            m.SetVertices(v);
            m.subMeshCount = Submallas;
            for (int i = 0; i < Submallas; i++) m.SetTriangles(sub[i], i);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

