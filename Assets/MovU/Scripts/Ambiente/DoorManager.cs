using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// DoorManager.cs — Las puertas del piso
// ============================================================================
// Patrón Manager: un solo componente con UN Update para todas las puertas.
//
// Las puertas salen de contenido_piso9.json ("puertas"): centro del vano, hacia
// dónde mira y ancho. Cada una es de dos hojas batientes, con marco, y un
// dintel de muro que cierra el vano desde la puerta hasta el techo (los vanos
// del modelo de Meshy van de piso a techo).
//
// Dos decisiones que salen de que esto es un instrumento de medida:
//
//  - NO tienen collider. Se abren solas cuando alguien se acerca (el jugador o
//    un NPC que camina) y nunca frenan a nadie. Así la distancia y el tiempo
//    que se registran en el CSV son los mismos con puertas que sin ellas, y el
//    NavMesh horneado sigue valiendo: una puerta con collider estrecharía el
//    vano justo por donde pasa la ruta óptima.
//  - Sí cambian lo que se VE: una puerta cerrada tapa el interior del salón.
//    Eso es parte de orientarse en un edificio real, pero es una variable. Se
//    apagan con "puertas": false en los ajustes del JSON.
//
// Carga por piso: cuelgan de Contenido/Piso_N, así que el FloorManager las
// apaga con su piso. Aquí solo se revisan las que están activas.
// ============================================================================

public class DoorManager : MonoBehaviour
{
    public static DoorManager Instance { get; private set; }

    public const string NombreDelGrupo = "Puertas (JSON)";

    /// <summary>Marco (3 cajas) + dintel + dos hojas de 3 cajas: 10 cajas.</summary>
    public const int TriangulosPorPuerta = 10 * FormasMovU.TriangulosDeCaja;

    private const float AlturaDeHoja = 2.25f;
    private const float AlturaDeMarco = 2.36f;
    private const float RadioDeApertura = 2.6f;
    private const float RadioDeCierre = 3.3f;
    private const float AnguloAbierta = 98f;
    private const float GradosPorSegundo = 270f;
    private const float Intervalo = 0.1f;

    /// <summary>Una puerta empezó a abrirse (posición del vano).</summary>
    public event Action<Vector3> OnPuertaSeAbre;
    /// <summary>Una puerta terminó de cerrarse.</summary>
    public event Action<Vector3> OnPuertaSeCierra;

    private class Puerta
    {
        public string id;
        public string poi;
        public int piso;
        public GameObject raiz;
        public Vector3 centro;        // en el suelo, coordenadas de mundo
        public Vector3 normal;
        public Transform hojaIzquierda;
        public Transform hojaDerecha;
        public float angulo;          // 0 = cerrada
        public float objetivo;        // 0 o AnguloAbierta
        public float lado = 1f;       // hacia qué lado batieron
        public float cerrarEn = -1f;
    }

    private readonly List<Puerta> puertas = new List<Puerta>();
    private Transform jugador;
    private float proximaRevision;

    public int Cantidad => puertas.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Construcción
    // ------------------------------------------------------------------
    public void Construir(ContenidoPiso datos, FloorManager pisos, float alturaDelTecho)
    {
        if (datos == null || pisos == null || datos.puertas == null) return;

        Material muro = FormasMovU.DelEdificio(pisos);

        for (int i = 0; i < pisos.CantidadDePisos; i++)
        {
            PlanoDePlanta plano = PlanoDePlanta.Medir(pisos.Piso(i));
            if (!plano.valido) continue;

            Transform grupo = null;
            for (int p = 0; p < datos.puertas.Count; p++)
            {
                PuertaDef def = datos.puertas[p];
                if (def == null || def.ancho < 0.6f) continue;
                if (def.piso != 0 && def.piso != i + 1) continue;

                if (grupo == null) grupo = ContenidoLoader.Grupo(pisos.ContenidoDelPiso(i), NombreDelGrupo);
                if (grupo == null) break;

                Vector3 centro = ContenidoLoader.AMundo(plano, pisos, i, def.u, def.v);
                puertas.Add(Crear(grupo, def, i, centro, alturaDelTecho, muro));
            }
        }
    }

    private static Puerta Crear(Transform grupo, PuertaDef def, int piso, Vector3 centro,
                                float alturaDelTecho, Material muro)
    {
        string id = string.IsNullOrEmpty(def.id) ? "puerta" : def.id;

        var raiz = new GameObject("Puerta_" + id);
        raiz.transform.SetParent(grupo, false);
        raiz.transform.SetPositionAndRotation(centro, Quaternion.Euler(0f, def.yaw, 0f));
        Transform t = raiz.transform;

        ColoresDe(def.estilo, out Color32 colorHoja, out Color32 colorMarco, out Color32 colorVidrio);
        Material matHoja = FormasMovU.Color(colorHoja);
        Material matMarco = FormasMovU.Color(colorMarco);
        Material matVidrio = FormasMovU.Color(colorVidrio);
        Material matMetal = FormasMovU.Color(new Color32(196, 200, 206, 255));

        float w = def.ancho;
        const float jamba = 0.09f;

        // --- Marco -------------------------------------------------------
        FormasMovU.Caja(t, "Jamba_izq", new Vector3(-w * 0.5f + jamba * 0.5f, AlturaDeMarco * 0.5f, 0f),
                        new Vector3(jamba, AlturaDeMarco, 0.18f), matMarco, piso);
        FormasMovU.Caja(t, "Jamba_der", new Vector3(w * 0.5f - jamba * 0.5f, AlturaDeMarco * 0.5f, 0f),
                        new Vector3(jamba, AlturaDeMarco, 0.18f), matMarco, piso);
        FormasMovU.Caja(t, "Travesano", new Vector3(0f, AlturaDeMarco - jamba * 0.5f, 0f),
                        new Vector3(w - jamba * 2f, jamba, 0.18f), matMarco, piso);

        // --- Dintel: cierra el vano desde el marco hasta el techo ---------
        float altoDintel = alturaDelTecho - AlturaDeMarco;
        if (altoDintel > 0.05f)
        {
            FormasMovU.Caja(t, "Dintel", new Vector3(0f, AlturaDeMarco + altoDintel * 0.5f, 0f),
                            new Vector3(w, altoDintel, 0.24f), muro, piso);
        }

        // --- Hojas ---------------------------------------------------------
        float anchoHoja = (w - jamba * 2f) * 0.5f - 0.006f;
        Transform izquierda = CrearHoja(t, "Hoja_izq", -w * 0.5f + jamba, anchoHoja, +1f,
                                        matHoja, matVidrio, matMetal, piso);
        Transform derecha = CrearHoja(t, "Hoja_der", w * 0.5f - jamba, anchoHoja, -1f,
                                      matHoja, matVidrio, matMetal, piso);

        return new Puerta
        {
            id = id,
            poi = def.poi ?? "",
            piso = piso,
            raiz = raiz,
            centro = centro,
            normal = t.forward,
            hojaIzquierda = izquierda,
            hojaDerecha = derecha,
        };
    }

    /// <summary>
    /// Una hoja: el objeto que devuelve es la BISAGRA (gira sobre Y); la hoja se
    /// extiende desde ahí hacia 'sentido' en el eje X de la puerta.
    /// </summary>
    private static Transform CrearHoja(Transform puerta, string nombre, float xBisagra, float ancho,
                                       float sentido, Material hoja, Material vidrio, Material metal, int piso)
    {
        var bisagra = new GameObject(nombre);
        bisagra.transform.SetParent(puerta, false);
        bisagra.transform.localPosition = new Vector3(xBisagra, 0f, 0f);
        Transform b = bisagra.transform;

        float cx = sentido * ancho * 0.5f;
        FormasMovU.Caja(b, "Tablero", new Vector3(cx, AlturaDeHoja * 0.5f + 0.02f, 0f),
                        new Vector3(ancho, AlturaDeHoja, 0.05f), hoja, piso);

        // Mirilla: un vidrio oscuro arriba, como en las puertas de salón.
        float anchoVidrio = Mathf.Min(0.34f, ancho * 0.36f);
        FormasMovU.Caja(b, "Mirilla", new Vector3(sentido * (ancho - anchoVidrio * 0.5f - 0.16f), 1.52f, 0f),
                        new Vector3(anchoVidrio, 0.62f, 0.058f), vidrio, piso);

        // Manija: una barra vertical junto al borde libre.
        FormasMovU.Caja(b, "Manija", new Vector3(sentido * (ancho - 0.09f), 1.05f, 0f),
                        new Vector3(0.035f, 0.30f, 0.12f), metal, piso);
        return b;
    }

    private static void ColoresDe(string estilo, out Color32 hoja, out Color32 marco, out Color32 vidrio)
    {
        vidrio = new Color32(52, 74, 92, 255);
        switch ((estilo ?? "").ToLowerInvariant())
        {
            case "dti":
                hoja = new Color32(0, 122, 138, 255);        // el turquesa del personal de DTI
                marco = new Color32(40, 48, 58, 255);
                break;
            case "emergencia":
                hoja = new Color32(176, 58, 46, 255);        // rojo de salida de emergencia
                marco = new Color32(60, 60, 62, 255);
                break;
            default:
                hoja = new Color32(158, 112, 72, 255);       // madera
                marco = new Color32(74, 62, 54, 255);
                break;
        }
    }

    /// <summary>
    /// Plano de la puerta que da paso al POI indicado, en su mismo piso. Lo usa
    /// PoiSignage para colgar el rótulo del dintel en vez de dejarlo flotando
    /// dentro del muro.
    /// </summary>
    public bool PlanoDe(string poiId, float alturaDelPoi, out Vector3 centro, out Vector3 normal)
    {
        centro = Vector3.zero;
        normal = Vector3.forward;
        if (string.IsNullOrEmpty(poiId)) return false;

        for (int i = 0; i < puertas.Count; i++)
        {
            Puerta p = puertas[i];
            if (!string.Equals(p.poi, poiId, StringComparison.OrdinalIgnoreCase)) continue;
            if (Mathf.Abs(p.centro.y - alturaDelPoi) > 1.5f) continue;      // es la del otro piso

            centro = p.centro;
            normal = p.normal;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Cada frame
    // ------------------------------------------------------------------
    private void Update()
    {
        if (puertas.Count == 0) return;

        float ahora = Time.time;
        if (ahora >= proximaRevision)
        {
            proximaRevision = ahora + Intervalo;
            Revisar(ahora);
        }

        float paso = GradosPorSegundo * Time.deltaTime;
        for (int i = 0; i < puertas.Count; i++)
        {
            Puerta p = puertas[i];
            if (Mathf.Approximately(p.angulo, p.objetivo)) continue;
            if (!p.raiz.activeInHierarchy)
            {
                p.angulo = p.objetivo = 0f;      // su piso se apagó: queda cerrada
                Girar(p);
                continue;
            }

            p.angulo = Mathf.MoveTowards(p.angulo, p.objetivo, paso);
            Girar(p);

            if (p.angulo <= 0.001f && p.objetivo <= 0.001f) OnPuertaSeCierra?.Invoke(p.centro);
        }
    }

    private static void Girar(Puerta p)
    {
        // Abren alejándose de quien llega: 'lado' es de qué lado estaba.
        p.hojaIzquierda.localRotation = Quaternion.Euler(0f, p.angulo * p.lado, 0f);
        p.hojaDerecha.localRotation = Quaternion.Euler(0f, -p.angulo * p.lado, 0f);
    }

    private void Revisar(float ahora)
    {
        if (jugador == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go != null) jugador = go.transform;
        }

        NpcManager gente = NpcManager.Instance;
        IReadOnlyList<NpcCharacter> npcs = gente != null ? gente.Personajes : null;

        float abrir2 = RadioDeApertura * RadioDeApertura;
        float cerrar2 = RadioDeCierre * RadioDeCierre;

        for (int i = 0; i < puertas.Count; i++)
        {
            Puerta p = puertas[i];
            if (!p.raiz.activeInHierarchy) continue;

            bool abierta = p.objetivo > 0f;
            float limite = abierta ? cerrar2 : abrir2;
            bool hayAlguien = false;
            Vector3 quien = Vector3.zero;

            if (jugador != null && Cerca(jugador.position, p.centro, limite))
            {
                hayAlguien = true;
                quien = jugador.position;
            }

            if (!hayAlguien && npcs != null)
            {
                for (int n = 0; n < npcs.Count; n++)
                {
                    NpcCharacter npc = npcs[n];
                    // Solo los que caminan: uno de pie junto a un vano la tendría abierta siempre.
                    if (npc == null || npc.Conducta != NpcConducta.Deambula) continue;
                    if (!npc.gameObject.activeInHierarchy) continue;
                    if (Cerca(npc.transform.position, p.centro, limite))
                    {
                        hayAlguien = true;
                        quien = npc.transform.position;
                        break;
                    }
                }
            }

            if (hayAlguien)
            {
                p.cerrarEn = -1f;
                if (!abierta)
                {
                    float lado = Vector3.Dot(quien - p.centro, p.normal);
                    p.lado = lado >= 0f ? 1f : -1f;
                    p.objetivo = AnguloAbierta;
                    OnPuertaSeAbre?.Invoke(p.centro);
                }
            }
            else if (abierta)
            {
                if (p.cerrarEn < 0f) p.cerrarEn = ahora + 0.7f;
                else if (ahora >= p.cerrarEn)
                {
                    p.cerrarEn = -1f;
                    p.objetivo = 0f;
                }
            }
        }
    }

    private static bool Cerca(Vector3 a, Vector3 centro, float limite2)
    {
        Vector3 d = a - centro;
        if (d.y < -1f || d.y > 2.2f) return false;       // rellano de la escalera, otro piso
        d.y = 0f;
        return d.sqrMagnitude <= limite2;
    }
}
