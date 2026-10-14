using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================================
// StairsManager.cs — La escalera, funcional
// ============================================================================
// El modelo de Meshy trae una escalera en el espacio L, pero no se podía usar:
// el primer peldaño mide medio metro, el segundo tramo arranca a un metro del
// suelo, y arriba no llevaba a ningún sitio (el rellano queda a media altura
// entre dos pisos).
//
// Qué se hace con ella, en cada piso:
//
//   - TRAMO VISIBLE (el de la derecha): escalones nuevos, parejos, encima de los
//     del modelo, y una rampa de colisión invisible para que el personaje suba
//     sin trabarse. Lleva del suelo al rellano.
//   - RELLANO: una losa pareja a media altura, con baranda donde hay vacío.
//   - TRAMO OCULTO (el de la izquierda): en un edificio real es el que sigue
//     subiendo del rellano al piso de arriba. Aquí no se puede caminar de verdad
//     entre pisos (cada piso es una copia que se apaga cuando no se usa, y la
//     losa de arriba no tiene hueco), así que queda tras dos tabiques con puerta:
//
//         puerta del RELLANO  -> "Subir":  sale por la puerta del hall del piso de arriba
//         puerta del HALL     -> "Bajar":  sale por la puerta del rellano del piso de abajo
//
//     Es la topología de una escalera en U de verdad: medio piso se camina y el
//     otro medio se cruza con un fundido corto.
//   - CAJA: el rellano queda a media altura entre dos pisos, más arriba de lo que
//     deja el techo normal. Sobre la escalera el cielo raso se abre y la caja
//     lleva su propio techo, más alto (el relleno de corona también deja ese
//     hueco: Tools/tapar_corona.py lee las mismas medidas del JSON).
//
// No hay colliders de disparo: como en las misiones, cada frame se mide la
// distancia del jugador a las dos puertas del piso en el que está.
//
// La geometría sale de "escalera" en contenido_piso9.json y cuelga de
// Contenido/Piso_N, así que respeta la carga por piso.
// ============================================================================

public class StairsManager : MonoBehaviour
{
    public static StairsManager Instance { get; private set; }

    public const string NombreDelGrupo = "Escalera (JSON)";

    /// <summary>
    /// Triángulos de la escalera de un piso con 'peldanos' contrahuellas: los
    /// escalones (2 por contrahuella y 2 por huella), seis cajas de obra (rellano,
    /// su prolongación, machón, dos tabiques y baranda), dos zancas, dos puertas de 4 cajas, y
    /// la caja: su techo (2 placas) y los seis paños del antepecho.
    /// </summary>
    public static int Triangulos(int peldanos)
    {
        return peldanos * 2 + (peldanos - 1) * 2 + (6 + 2 + 2 * 4 + 2) * FormasMovU.TriangulosDeCaja +
               2 * FormasMovU.TriangulosDePlaca + 6 * FormasMovU.TriangulosDeCaja +     // la caja
               TriangulosDeLaPrincipal;
    }

    /// <summary>
    /// La escalera principal (16 peldaños): escalones, 8 cajas de parapetos, zanca,
    /// pasamanos y rellano, 2 puertas de 4 cajas y unos 37 cables de 8 triángulos.
    /// </summary>
    public const int TriangulosDeLaPrincipal = 16 * 2 + 15 * 2 + (8 + 2 * 4) * FormasMovU.TriangulosDeCaja + 37 * 8;

    private const float AlcanceDePuerta = 1.9f;
    private const float AltoDePuerta = 2.1f;
    private const float AnchoDePuerta = 1.05f;
    private const float DuracionDelFundido = 0.38f;

    /// <summary>Texto del aviso de interacción. Vacío = no hay puerta a mano.</summary>
    public event Action<string> OnAvisoCambiado;
    /// <summary>Se cruzó el tramo oculto: (true = subiendo, piso de llegada contando desde 0).</summary>
    public event Action<bool, int> OnTramoRecorrido;
    /// <summary>La puerta no lleva a ningún sitio (último piso hacia arriba o hacia abajo).</summary>
    public event Action OnPuertaCerrada;

    /// <summary>True mientras el jugador tiene a mano una puerta de la escalera
    /// (los NPC lo consultan para no pelearse por la tecla E).</summary>
    public static bool JugadorEnPuerta { get; private set; }

    /// <summary>True durante el fundido: el cambio de piso que avisa el
    /// FloorManager en ese momento es de la escalera y no del ascensor.</summary>
    public static bool EnTransicion { get; private set; }

    private FloorManager pisos;
    private PlayerController jugador;
    private Transform jugadorT;
    private bool lista;

    // Geometría, igual en todos los pisos (XZ de mundo y alturas sobre el suelo).
    private float xPuerta, zHall, zRellano, alturaRellano;
    private Vector3 pieDelTramo;          // XZ; la Y se pone por piso
    private float largoDelRecorrido;      // metros en horizontal: pie -> cima -> puerta del rellano
    private float entreTramos;            // puerta del hall -> pie del tramo

    private Image velo;
    private string avisoActual = "";
    private int puertaActual;             // 0 ninguna, +1 la del rellano (subir), -1 la del hall (bajar)
    private int escaleraActual;           // 0 la de emergencia, 1 la principal

    // Escalera principal (la roja del lobby): marco local que mira hacia donde se sube.
    private EscaleraPrincipalDef principal;
    private bool principalLista;
    private Vector3 pPie, pAdelante, pDerecha;   // XZ de mundo; la Y se pone por piso

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        JugadorEnPuerta = false;
        EnTransicion = false;
    }

    public bool Disponible => lista;
    /// <summary>Metros en horizontal que se caminan por el tramo visible y el rellano.</summary>
    public float LargoDelRecorrido => largoDelRecorrido;
    /// <summary>Metros entre la puerta del hall y el pie del tramo (para encadenar dos pisos).</summary>
    public float EntreTramos => entreTramos;

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
        if (Instance == this)
        {
            Instance = null;
            JugadorEnPuerta = false;
            EnTransicion = false;
        }
    }

    // ------------------------------------------------------------------
    // Puntos que usan la ruta óptima y la flecha de guía
    // ------------------------------------------------------------------
    /// <summary>Punto del suelo al pie del tramo visible (por donde se empieza a subir).</summary>
    public Vector3 PieDelTramo(int piso)
    {
        return new Vector3(pieDelTramo.x, pisos.AlturaDelSuelo(piso), pieDelTramo.z);
    }

    /// <summary>Punto del suelo frente a la puerta del hall (por donde se baja y a donde se llega al subir).</summary>
    public Vector3 FrenteDelHall(int piso)
    {
        return new Vector3(xPuerta, pisos.AlturaDelSuelo(piso), zHall - 0.95f);
    }

    /// <summary>Punto del rellano frente a su puerta (por donde se sube y a donde se llega al bajar).</summary>
    public Vector3 FrenteDelRellano(int piso)
    {
        return new Vector3(xPuerta, pisos.AlturaDelSuelo(piso) + alturaRellano, zRellano + 0.95f);
    }

    /// <summary>Un punto del marco de la escalera principal (x a la derecha, z hacia arriba del tramo), en el piso indicado.</summary>
    public Vector3 Principal(int piso, float x, float z, float h)
    {
        Vector3 p = pPie + pDerecha * x + pAdelante * z;
        p.y = pisos.AlturaDelSuelo(piso) + h;
        return p;
    }

    /// <summary>Frente a la puerta de abajo de la escalera principal (la del hall).</summary>
    public Vector3 FrenteDelHallPrincipal(int piso) => Principal(piso, principal.columnaX, -0.95f, 0f);

    /// <summary>Frente a la puerta del rellano de la escalera principal.</summary>
    public Vector3 FrenteDelRellanoPrincipal(int piso) =>
        Principal(piso, principal.columnaX, principal.largo + 0.95f, principal.alturaDelRellano);

    /// <summary>True si la escalera principal también es funcional en este juego.</summary>
    public bool HayPrincipal => principalLista;

    /// <summary>True si esos pies están sobre el tramo o el rellano (fuera del NavMesh).</summary>
    public bool EstaEnLaEscalera(Vector3 pies, int piso)
    {
        if (!lista) return false;
        float h = pies.y - pisos.AlturaDelSuelo(piso);
        return h > 0.6f && h < alturaRellano + 1f;
    }

    // ------------------------------------------------------------------
    // La caja de la escalera vista desde el techo
    // ------------------------------------------------------------------
    /// <summary>
    /// Los dos rectángulos (x = X de mundo, y = Z de mundo) que la escalera ocupa
    /// en planta: los tramos y el rellano. Ahí no va cielo raso. Lista vacía si
    /// la escalera está apagada.
    /// </summary>
    public static List<Rect> HuecoEnElTecho(EscaleraDef def, PlanoDePlanta plano)
    {
        var huecos = new List<Rect>(2);
        if (def == null || !def.usar || !plano.valido) return huecos;

        float xOculto = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.ocultoU0);
        float x1 = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.tramo.u1);
        float zPie = Mathf.LerpUnclamped(plano.zMin, plano.zMax, def.tramo.v0);
        float rx0 = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.rellano.u0);
        float rx1 = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.rellano.u1);
        float rz0 = Mathf.LerpUnclamped(plano.zMin, plano.zMax, def.rellano.v0);
        float rz1 = Mathf.LerpUnclamped(plano.zMin, plano.zMax, def.rellano.v1);

        huecos.Add(Rect.MinMaxRect(xOculto, zPie, x1, rz0));     // los dos tramos
        huecos.Add(Rect.MinMaxRect(rx0, rz0, rx1, rz1));         // el rellano
        return huecos;
    }

    /// <summary>
    /// El techo de la caja y el antepecho que sube del cielo raso hasta él.
    /// </summary>
    private static void CrearCaja(Transform grupo, List<Rect> huecos, float suelo, float alturaDelTecho,
                                  float alturaDeLaCaja, Mesh tapa, int triangulosDeLaTapa,
                                  Material muro, int piso)
    {
        if (huecos.Count < 2 || alturaDeLaCaja <= alturaDelTecho + 0.05f) return;

        FormasMovU.Pieza(grupo, "Caja_techo", new Vector3(0f, suelo + alturaDeLaCaja, 0f), tapa,
                         FormasMovU.CieloRaso(), piso, triangulosDeLaTapa);

        // Antepecho: seis paños que siguen el contorno en L de los dos rectángulos.
        Rect a = huecos[0];      // tramos
        Rect r = huecos[1];      // rellano (más ancho hacia -X)
        float y0 = suelo + alturaDelTecho - 0.06f;
        float y1 = suelo + alturaDeLaCaja + 0.02f;

        Pano(grupo, a.xMin, a.yMin, a.xMax, a.yMin, 0f, -1f, y0, y1, muro, piso);     // sur de los tramos
        Pano(grupo, a.xMax, a.yMin, a.xMax, r.yMax, +1f, 0f, y0, y1, muro, piso);     // este, corrido
        Pano(grupo, r.xMin, r.yMax, r.xMax, r.yMax, 0f, +1f, y0, y1, muro, piso);     // norte del rellano
        Pano(grupo, r.xMin, r.yMin, r.xMin, r.yMax, -1f, 0f, y0, y1, muro, piso);     // oeste del rellano
        Pano(grupo, a.xMin, a.yMin, a.xMin, a.yMax, -1f, 0f, y0, y1, muro, piso);     // oeste de los tramos
        if (a.xMin - r.xMin > 0.05f)
        {
            Pano(grupo, r.xMin, r.yMin, a.xMin, r.yMin, 0f, -1f, y0, y1, muro, piso); // el escalón de la L
        }
    }

    /// <summary>Un paño vertical del antepecho, por fuera del hueco.</summary>
    private static void Pano(Transform grupo, float x0, float z0, float x1, float z1,
                             float fueraX, float fueraZ, float y0, float y1, Material muro, int piso)
    {
        const float grosor = 0.12f;
        var centro = new Vector3((x0 + x1) * 0.5f + fueraX * grosor * 0.5f, (y0 + y1) * 0.5f,
                                 (z0 + z1) * 0.5f + fueraZ * grosor * 0.5f);
        var tamano = new Vector3(Mathf.Abs(x1 - x0) + (fueraX == 0f ? grosor * 2f : grosor),
                                 y1 - y0,
                                 Mathf.Abs(z1 - z0) + (fueraZ == 0f ? grosor * 2f : grosor));
        FormasMovU.Caja(grupo, "Caja_antepecho", centro, tamano, muro, piso);
    }

    // ------------------------------------------------------------------
    // Construcción
    // ------------------------------------------------------------------
    public void Construir(ContenidoPiso datos, FloorManager losPisos, PlayerController elJugador,
                          float alturaDelTecho)
    {
        pisos = losPisos;
        jugador = elJugador;
        jugadorT = elJugador != null ? elJugador.transform : null;

        EscaleraDef def = datos != null ? datos.escalera : null;
        if (def == null || !def.usar || pisos == null || pisos.CantidadDePisos == 0) return;

        PlanoDePlanta plano = PlanoDePlanta.Medir(pisos.Piso(0));
        if (!plano.valido) return;

        // Todo en metros de mundo. Los pisos son copias, así que el XZ vale para todos.
        float x0 = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.tramo.u0);
        float x1 = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.tramo.u1);
        float zPie = Mathf.LerpUnclamped(plano.zMin, plano.zMax, def.tramo.v0);
        float zCima = Mathf.LerpUnclamped(plano.zMin, plano.zMax, def.tramo.v1);

        float rx0 = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.rellano.u0);
        float rx1 = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.rellano.u1);
        float rz0 = Mathf.LerpUnclamped(plano.zMin, plano.zMax, def.rellano.v0);
        float rz1 = Mathf.LerpUnclamped(plano.zMin, plano.zMax, def.rellano.v1);

        float xOculto = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.ocultoU0);
        xPuerta = Mathf.LerpUnclamped(plano.xMin, plano.xMax, def.puertaU);

        if (x1 - x0 < 0.8f || zCima - zPie < 2f || def.alturaDelRellano < 1f)
        {
            Debug.LogWarning("[Escalera] Las medidas de 'escalera' en el JSON no sirven; no se construye.");
            return;
        }

        alturaRellano = def.alturaDelRellano;
        zHall = zPie;
        zRellano = rz0;
        pieDelTramo = new Vector3((x0 + x1) * 0.5f, 0f, zPie - 0.6f);

        Vector3 cima = new Vector3(pieDelTramo.x, 0f, zCima);
        Vector3 frenteRellano = new Vector3(xPuerta, 0f, zRellano + 0.95f);
        largoDelRecorrido = (zCima - pieDelTramo.z) + Vector3.Distance(cima, frenteRellano);
        entreTramos = Vector3.Distance(new Vector3(xPuerta, 0f, zHall - 0.95f), pieDelTramo);

        Material muro = FormasMovU.DelEdificio(pisos);
        int peldanos = Mathf.Clamp(def.peldanos, 6, 40);
        Mesh escalones = FormasMovU.Escalones(peldanos, x1 - x0, zCima - zPie, alturaRellano,
                                              out int triangulosDeEscalones);

        // La caja: su techo va más alto que el cielo raso del piso.
        float alturaDeLaCaja = Mathf.Max(alturaDelTecho, alturaRellano + Mathf.Max(2.2f, def.alturaLibreSobreElRellano));
        List<Rect> huecos = HuecoEnElTecho(def, plano);
        Mesh tapa = FormasMovU.PlacasHaciaAbajo(huecos, AmbientacionBuilder.ModuloDelCieloRaso,
                                                "MovU_CajaDeEscalera", out int triangulosDeLaTapa);

        for (int i = 0; i < pisos.CantidadDePisos; i++)
        {
            // La escalera va en los pisos con la planta del 9, la genérica o la del 13 (el 3 tiene las suyas).
            if (!pisos.TieneEscalera(i)) continue;

            Transform grupo = ContenidoLoader.Grupo(pisos.ContenidoDelPiso(i), NombreDelGrupo);
            if (grupo == null) continue;

            float suelo = pisos.AlturaDelSuelo(i);
            // Del suelo al techo de ESTE piso: el 13 tiene doble altura y su techo ya
            // queda por encima de la caja, que entonces no hace falta.
            float altoLibre = FormasMovU.AlturaLibre(pisos.Piso(i), suelo, alturaDelTecho);
            float cajaDelPiso = Mathf.Max(alturaDeLaCaja, altoLibre);

            // Con la planta del 9 en JSON la escalera se viste como la de verdad: muros de
            // concreto, peldaños grises y zancas y barandas de acero rojo.
            Transform piso = pisos.Piso(i);
            Material muroP = FormasMovU.DelPiso(piso, "Concreto") ?? muro;
            Material peldano = FormasMovU.DelPiso(piso, "Escalon") ?? muro;
            Material acero = FormasMovU.DelPiso(piso, "Rojo");

            if (altoLibre < alturaDeLaCaja - 0.05f)
                CrearCaja(grupo, huecos, suelo, altoLibre, alturaDeLaCaja, tapa, triangulosDeLaTapa, muroP, i);

            // --- Tramo visible: escalones + rampa de colisión ---------------
            var tramo = new GameObject("Tramo");
            tramo.transform.SetParent(grupo, false);
            tramo.transform.position = new Vector3(x0, suelo, zPie);
            tramo.AddComponent<MeshFilter>().sharedMesh = escalones;
            FormasMovU.Vestir(tramo.AddComponent<MeshRenderer>(), peldano);
            FormasMovU.Contar(i, triangulosDeEscalones);

            CrearRampa(grupo, (x0 + x1) * 0.5f, x1 - x0 + 0.5f, suelo, zPie - 0.2f, zCima, alturaRellano);

            // Zancas: el zócalo inclinado a cada lado. Además de rematar el tramo,
            // tapan las puntas de los escalones del modelo que asoman junto al muro.
            Material zocalo = acero ?? FormasMovU.Color(new Color32(72, 80, 92, 255));
            CrearZanca(grupo, "Zanca_izq", x0 + 0.09f, suelo, zPie, zCima, alturaRellano, zocalo, i);
            if (acero != null)
            {
                // Parapeto macizo rojo con pasamanos, como el de la foto de la escalera.
                float pl = zCima - zPie;
                float pa = Mathf.Atan2(alturaRellano, pl) * Mathf.Rad2Deg;
                float plargo = Mathf.Sqrt(pl * pl + alturaRellano * alturaRellano);
                GameObject par = FormasMovU.Caja(grupo, "Parapeto",
                    new Vector3(x0 + 0.06f, suelo + alturaRellano * 0.5f + 0.55f, (zPie + zCima) * 0.5f),
                    new Vector3(0.06f, 1.1f, plargo), acero, i);
                par.transform.rotation = Quaternion.Euler(-pa, 0f, 0f);
                GameObject pas = FormasMovU.Caja(grupo, "Pasamanos",
                    new Vector3(x0 + 0.06f, suelo + alturaRellano * 0.5f + 1.12f, (zPie + zCima) * 0.5f),
                    new Vector3(0.1f, 0.08f, plargo), acero, i);
                pas.transform.rotation = Quaternion.Euler(-pa, 0f, 0f);
            }
            CrearZanca(grupo, "Zanca_der", x1 - 0.09f, suelo, zPie, zCima, alturaRellano, zocalo, i);

            // --- Rellano: la losa y su prolongación sobre la cima del tramo --
            const float canto = 0.22f;
            Losa(grupo, "Rellano", rx0, rx1, rz0, rz1, suelo + alturaRellano, canto, peldano, i);
            if (rz0 - zCima > 0.05f)
            {
                Losa(grupo, "Rellano_cima", x0 - 0.4f, x1, zCima, rz0, suelo + alturaRellano, canto, peldano, i);
            }

            // --- Machón: el muro macizo entre los dos tramos, hasta el techo de
            // la caja. Tapa el muro divisor del modelo (irregular) y el tramo oculto.
            float altoZanca = cajaDelPiso;
            FormasMovU.Caja(grupo, "Machon",
                            new Vector3(x0 - 0.2f, suelo + altoZanca * 0.5f, (zPie + rz0) * 0.5f),
                            new Vector3(0.42f, altoZanca, rz0 - zPie), muroP, i, true);

            // --- Tabiques del tramo oculto ----------------------------------
            // En el hall, de suelo a techo.
            FormasMovU.Caja(grupo, "Tabique_hall",
                            new Vector3((xOculto + x0) * 0.5f, suelo + altoLibre * 0.5f, zHall + 0.02f),
                            new Vector3(x0 - xOculto, altoLibre, 0.14f), muroP, i, true);
            // En el rellano, del rellano al techo de la caja, y cerrando también el borde abierto.
            float altoArriba = cajaDelPiso - alturaRellano;
            if (altoArriba > 0.2f)
            {
                FormasMovU.Caja(grupo, "Tabique_rellano",
                                new Vector3((rx0 + x0) * 0.5f, suelo + alturaRellano + altoArriba * 0.5f, zRellano - 0.02f),
                                new Vector3(x0 - rx0, altoArriba, 0.14f), muroP, i, true);
            }

            // --- Baranda del borde abierto del rellano ----------------------
            FormasMovU.Caja(grupo, "Baranda",
                            new Vector3(rx0 + 0.05f, suelo + alturaRellano + 0.55f, (rz0 + rz1) * 0.5f),
                            new Vector3(0.10f, 1.10f, rz1 - rz0), acero ?? muro, i, true);

            // --- Las dos puertas --------------------------------------------
            bool haySubida = pisos.EscaleraConecta(i, i + 1);
            bool hayBajada = pisos.EscaleraConecta(i, i - 1);

            CrearPuerta(grupo, "Puerta_bajar", new Vector3(xPuerta, suelo, zHall - 0.06f), 180f,
                        hayBajada ? $"BAJAR · Piso {FloorManager.NumeroVisible(i - 1)}"
                                  : (i > 0 ? "CERRADO · USA EL ASCENSOR" : "SIN SALIDA"),
                        hayBajada, i);
            CrearPuerta(grupo, "Puerta_subir", new Vector3(xPuerta, suelo + alturaRellano, zRellano + 0.06f), 0f,
                        haySubida ? $"SUBIR · Piso {FloorManager.NumeroVisible(i + 1)}"
                                  : (i + 1 < pisos.CantidadDePisos ? "CERRADO · USA EL ASCENSOR" : "AZOTEA · CERRADO"),
                        haySubida, i);

            // Número de piso bien visible al pie de la escalera.
            Letrero(grupo, "Letrero_piso", $"PISO {FloorManager.NumeroVisible(i)}",
                    new Vector3(xPuerta, suelo + 3.42f, zHall - 0.07f), 180f, 4.4f,
                    new Color(0.10f, 0.12f, 0.16f));
        }

        ConstruirPrincipal(datos != null ? datos.escaleraPrincipal : null, plano, muro, alturaDelTecho);

        CrearVelo();
        lista = true;

        Debug.Log($"[Escalera] Construida en {pisos.CantidadDePisos} pisos: {peldanos} peldaños de " +
                  $"{alturaRellano / peldanos * 100f:F0} cm hasta el rellano a {alturaRellano:F2} m. " +
                  $"Recorrido {largoDelRecorrido:F1} m por piso.");
    }

    // ------------------------------------------------------------------
    // Escalera principal (la roja del lobby, como en la foto del 14 oct 2026)
    // ------------------------------------------------------------------
    /// <summary>
    /// Arma la escalera principal en cada piso con escalera funcional: peldaños
    /// grises, parapeto macizo rojo con pasamanos, cortina de cables de acero del
    /// suelo al techo, muro mostaza, rellano junto a las ventanas y las dos puertas
    /// (la del rellano sube, la de abajo baja), con la misma lógica que la de emergencia.
    /// La columna del tramo oculto y los macizos los pone la planta de cada piso.
    /// </summary>
    private void ConstruirPrincipal(EscaleraPrincipalDef def, PlanoDePlanta plano, Material muro, float alturaDelTecho)
    {
        principal = def;
        principalLista = false;
        if (def == null || !def.usar || def.largo < 2f || def.ancho < 0.8f) return;

        Vector3 pie = plano.AMundo(def.pieU, def.pieV, 0f);
        Quaternion giro = Quaternion.Euler(0f, def.rumbo, 0f);
        pPie = new Vector3(pie.x, 0f, pie.z);
        pAdelante = giro * Vector3.forward;
        pDerecha = giro * Vector3.right;

        float alto = def.alturaDelRellano;
        int peldanos = Mathf.Clamp(def.peldanos, 6, 40);
        Mesh escalones = FormasMovU.Escalones(peldanos, def.ancho, def.largo, alto, out int triEscalones);
        float angulo = Mathf.Atan2(alto, def.largo) * Mathf.Rad2Deg;
        float largoInclinado = Mathf.Sqrt(def.largo * def.largo + alto * alto);
        Quaternion inclinado = Quaternion.Euler(-angulo, 0f, 0f);
        var mallasDeCables = new Dictionary<int, Mesh>();

        Material rojoPorDefecto = FormasMovU.Color(new Color32(112, 26, 32, 255));
        Material cable = FormasMovU.Color(new Color32(176, 180, 186, 255));
        int construidos = 0;

        for (int i = 0; i < pisos.CantidadDePisos; i++)
        {
            if (!pisos.TieneEscalera(i)) continue;
            Transform grupo = ContenidoLoader.Grupo(pisos.ContenidoDelPiso(i), "Escalera principal (JSON)");
            if (grupo == null) continue;

            Transform piso = pisos.Piso(i);
            float suelo = pisos.AlturaDelSuelo(i);
            float techo = FormasMovU.AlturaLibre(piso, suelo, alturaDelTecho);
            Material peldano = FormasMovU.DelPiso(piso, "Escalon") ?? muro;
            Material rojo = FormasMovU.DelPiso(piso, "Rojo") ?? rojoPorDefecto;

            var marco = new GameObject("Marco");
            marco.transform.SetParent(grupo, false);
            marco.transform.SetPositionAndRotation(new Vector3(pPie.x, suelo, pPie.z), giro);
            Transform m = marco.transform;

            // --- Tramo y rampa de colisión ----------------------------------
            var tramo = new GameObject("Tramo");
            tramo.transform.SetParent(m, false);
            tramo.transform.localPosition = new Vector3(-def.ancho * 0.5f, 0f, 0f);
            tramo.AddComponent<MeshFilter>().sharedMesh = escalones;
            FormasMovU.Vestir(tramo.AddComponent<MeshRenderer>(), peldano);
            FormasMovU.Contar(i, triEscalones);

            var rampa = new GameObject("Rampa (colisión)");
            rampa.transform.SetParent(m, false);
            rampa.transform.localRotation = inclinado;
            float largoRampa = Mathf.Sqrt((def.largo + 0.2f) * (def.largo + 0.2f) + alto * alto);
            rampa.transform.localPosition = new Vector3(0f, alto * 0.5f, (def.largo - 0.2f) * 0.5f)
                                            - (inclinado * Vector3.up) * 0.15f;
            rampa.AddComponent<BoxCollider>().size = new Vector3(def.ancho + 0.3f, 0.3f, largoRampa);

            // --- Parapeto macizo rojo del lado del vacío, con su pasamanos ---
            var medio = new Vector3(0f, alto * 0.5f, def.largo * 0.5f);
            Inclinada(m, "Parapeto", medio + new Vector3(def.ancho * 0.5f + 0.04f, 0.25f, 0f),
                      new Vector3(0.07f, 1.5f, largoInclinado + 0.2f), inclinado, rojo, i);
            Inclinada(m, "Pasamanos", medio + new Vector3(def.ancho * 0.5f + 0.04f, 1.03f, 0f),
                      new Vector3(0.1f, 0.08f, largoInclinado + 0.2f), inclinado, rojo, i);
            // Del lado del muro: zanca roja y pasamanos sobre ménsulas.
            Inclinada(m, "Zanca", medio + new Vector3(-def.ancho * 0.5f - 0.05f, 0.1f, 0f),
                      new Vector3(0.1f, 0.5f, largoInclinado + 0.2f), inclinado, rojo, i);
            Inclinada(m, "Pasamanos_muro", medio + new Vector3(-def.ancho * 0.5f + 0.1f, 0.92f, 0f),
                      new Vector3(0.06f, 0.06f, largoInclinado), inclinado, rojo, i);

            // --- Rellano: losa roja de canto, piso gris encima, y su parapeto --
            float x0 = -def.ancho * 0.5f, x1 = def.columnaX + def.anchoColumna * 0.5f;
            float z0 = def.largo, z1 = def.largo + def.fondoDelRellano;
            FormasMovU.Caja(m, "Rellano", new Vector3((x0 + x1) * 0.5f, alto - 0.13f, (z0 + z1) * 0.5f),
                            new Vector3(x1 - x0, 0.26f, z1 - z0), rojo, i, true);
            FormasMovU.Caja(m, "Rellano_piso", new Vector3((x0 + x1) * 0.5f, alto + 0.006f, (z0 + z1) * 0.5f),
                            new Vector3(x1 - x0 - 0.02f, 0.012f, z1 - z0 - 0.02f), peldano, i);
            FormasMovU.Caja(m, "Parapeto_rellano", new Vector3(x1 - 0.04f, alto + 0.55f, (z0 + z1) * 0.5f),
                            new Vector3(0.07f, 1.1f, z1 - z0), rojo, i, true);
            FormasMovU.Caja(m, "Pasamanos_rellano", new Vector3(x1 - 0.04f, alto + 1.12f, (z0 + z1) * 0.5f),
                            new Vector3(0.1f, 0.08f, z1 - z0), rojo, i);

            // --- Cortina de cables de acero, del suelo al techo de ESTE piso ----
            int clave = Mathf.RoundToInt(techo * 100f);
            if (!mallasDeCables.TryGetValue(clave, out Mesh cables))
            {
                cables = MallaDeCables(def.largo, techo - 0.05f, 0.2f, out int tc);
                mallasDeCables[clave] = cables;
            }
            var cortina = new GameObject("Cables");
            cortina.transform.SetParent(m, false);
            cortina.transform.localPosition = new Vector3(def.cablesX, 0f, 0f);
            cortina.AddComponent<MeshFilter>().sharedMesh = cables;
            FormasMovU.Vestir(cortina.AddComponent<MeshRenderer>(), cable);
            FormasMovU.Contar(i, cables.triangles.Length / 3);

            // --- Puertas: la de abajo baja, la del rellano sube --------------
            bool haySubida = pisos.EscaleraConecta(i, i + 1);
            bool hayBajada = pisos.EscaleraConecta(i, i - 1);
            CrearPuerta(grupo, "Puerta_bajar_principal", Principal(i, def.columnaX, -0.06f, 0f), def.rumbo + 180f,
                        hayBajada ? $"BAJAR · Piso {FloorManager.NumeroVisible(i - 1)}"
                                  : (i > 0 ? "CERRADO · USA EL ASCENSOR" : "SIN SALIDA"), hayBajada, i);
            CrearPuerta(grupo, "Puerta_subir_principal", Principal(i, def.columnaX, def.largo + 0.06f, alto), def.rumbo,
                        haySubida ? $"SUBIR · Piso {FloorManager.NumeroVisible(i + 1)}"
                                  : (i + 1 < pisos.CantidadDePisos ? "CERRADO · USA EL ASCENSOR" : "AZOTEA · CERRADO"),
                        haySubida, i);
            Letrero(grupo, "Letrero_piso_principal", $"PISO {FloorManager.NumeroVisible(i)}",
                    Principal(i, def.columnaX, -0.07f, 3.05f), def.rumbo + 180f, 3.6f, new Color(0.10f, 0.12f, 0.16f));
            construidos++;
        }

        principalLista = construidos > 0;
        if (principalLista)
            Debug.Log($"[Escalera] Principal construida en {construidos} pisos: {peldanos} peldaños de " +
                      $"{alto / peldanos * 100f:F0} cm hasta el rellano a {alto:F2} m.");
    }

    /// <summary>Una caja girada como el tramo, en el marco de la escalera.</summary>
    private static void Inclinada(Transform marco, string nombre, Vector3 centro, Vector3 tamano,
                                  Quaternion giro, Material material, int piso)
    {
        GameObject go = FormasMovU.Caja(marco, nombre, centro, tamano, material, piso);
        go.transform.localRotation = giro;
    }

    /// <summary>Cables verticales cada 'paso' metros a lo largo de 'largo', del suelo a 'alto', en una malla.</summary>
    private static Mesh MallaDeCables(float largo, float alto, float paso, out int triangulos)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        int n = Mathf.Max(1, Mathf.FloorToInt(largo / paso));
        const float g = 0.008f;
        for (int k = 0; k < n; k++)
        {
            float z = (k + 0.5f) * largo / n;
            // Dos caras cruzadas por cable: se ve desde cualquier lado con 4 triángulos.
            for (int c = 0; c < 2; c++)
            {
                Vector3 d = c == 0 ? new Vector3(g, 0f, 0f) : new Vector3(0f, 0f, g);
                int b = v.Count;
                v.Add(new Vector3(0f, 0f, z) - d); v.Add(new Vector3(0f, 0f, z) + d);
                v.Add(new Vector3(0f, alto, z) + d); v.Add(new Vector3(0f, alto, z) - d);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
        }
        var malla = new Mesh { name = "MovU_Cables" };
        malla.SetVertices(v);
        malla.SetTriangles(t, 0);
        malla.RecalculateNormals();
        malla.RecalculateBounds();
        triangulos = t.Count / 3;
        return malla;
    }

    /// <summary>
    /// Rampa invisible sobre los escalones: el CharacterController sube por ella
    /// sin depender de que cada contrahuella quepa en su 'step offset'.
    /// </summary>
    private static void CrearRampa(Transform grupo, float xCentro, float ancho, float suelo,
                                   float zInicio, float zFin, float alto)
    {
        float largoPlano = zFin - zInicio;
        float largo = Mathf.Sqrt(largoPlano * largoPlano + alto * alto);
        float angulo = Mathf.Atan2(alto, largoPlano) * Mathf.Rad2Deg;
        const float grosor = 0.30f;

        var rampa = new GameObject("Rampa (colisión)");
        rampa.transform.SetParent(grupo, false);
        rampa.transform.rotation = Quaternion.Euler(-angulo, 0f, 0f);

        Vector3 medio = new Vector3(xCentro, suelo + alto * 0.5f, (zInicio + zFin) * 0.5f);
        rampa.transform.position = medio - rampa.transform.up * (grosor * 0.5f);

        var caja = rampa.AddComponent<BoxCollider>();
        caja.size = new Vector3(ancho, grosor, largo);
    }

    /// <summary>Una tabla inclinada que sigue la línea de las narices de los escalones.</summary>
    private static void CrearZanca(Transform grupo, string nombre, float x, float suelo,
                                   float zPie, float zCima, float alto, Material material, int piso)
    {
        float largoPlano = zCima - zPie;
        float largo = Mathf.Sqrt(largoPlano * largoPlano + alto * alto);
        float angulo = Mathf.Atan2(alto, largoPlano) * Mathf.Rad2Deg;

        GameObject zanca = FormasMovU.Caja(grupo, nombre,
                                           new Vector3(x, suelo + alto * 0.5f + 0.16f, (zPie + zCima) * 0.5f),
                                           new Vector3(0.18f, 0.42f, largo + 0.3f), material, piso);
        zanca.transform.rotation = Quaternion.Euler(-angulo, 0f, 0f);
    }

    private static void Losa(Transform grupo, string nombre, float x0, float x1, float z0, float z1,
                             float yArriba, float canto, Material material, int piso)
    {
        FormasMovU.Caja(grupo, nombre,
                        new Vector3((x0 + x1) * 0.5f, yArriba - canto * 0.5f, (z0 + z1) * 0.5f),
                        new Vector3(x1 - x0, canto, z1 - z0), material, piso, true);
    }

    /// <summary>Una puerta cortafuego cerrada, con su letrero. 'yaw' es hacia dónde mira.</summary>
    private static void CrearPuerta(Transform grupo, string nombre, Vector3 base_, float yaw,
                                    string texto, bool abre, int piso)
    {
        var raiz = new GameObject(nombre);
        raiz.transform.SetParent(grupo, false);
        raiz.transform.SetPositionAndRotation(base_, Quaternion.Euler(0f, yaw, 0f));
        Transform t = raiz.transform;

        Material hoja = FormasMovU.Color(abre ? new Color32(44, 122, 84, 255) : new Color32(120, 124, 130, 255));
        Material marco = FormasMovU.Color(new Color32(46, 50, 56, 255));
        Material metal = FormasMovU.Color(new Color32(200, 204, 210, 255));

        FormasMovU.Caja(t, "Marco", new Vector3(0f, (AltoDePuerta + 0.10f) * 0.5f, 0f),
                        new Vector3(AnchoDePuerta + 0.20f, AltoDePuerta + 0.10f, 0.05f), marco, piso);
        FormasMovU.Caja(t, "Hoja", new Vector3(0f, AltoDePuerta * 0.5f, 0.02f),
                        new Vector3(AnchoDePuerta, AltoDePuerta, 0.06f), hoja, piso);
        FormasMovU.Caja(t, "Barra", new Vector3(0f, 1.05f, 0.07f),
                        new Vector3(AnchoDePuerta * 0.78f, 0.06f, 0.05f), metal, piso);

        // Letrero luminoso encima, como los de salida de emergencia.
        Material placa = abre
            ? FormasMovU.Luminoso(new Color32(30, 150, 90, 255), 0.9f)
            : FormasMovU.Luminoso(new Color32(120, 124, 130, 255), 0.25f);
        FormasMovU.Caja(t, "Placa", new Vector3(0f, AltoDePuerta + 0.42f, 0.02f),
                        new Vector3(1.9f, 0.36f, 0.05f), placa, piso);

        Letrero(t, "Texto", texto, t.TransformPoint(new Vector3(0f, AltoDePuerta + 0.42f, 0.055f)),
                yaw, 2.0f, Color.white);
    }

    /// <summary>Texto fijo en el mundo, legible mirando en contra de 'yaw'.</summary>
    private static void Letrero(Transform padre, string nombre, string texto, Vector3 posicion,
                                float yaw, float tamano, Color color)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, true);
        // TextMeshPro se lee mirando a lo largo de su +Z: se gira media vuelta.
        go.transform.SetPositionAndRotation(posicion, Quaternion.Euler(0f, yaw + 180f, 0f));
        go.transform.localScale = Vector3.one;

        var t = go.AddComponent<TextMeshPro>();
        t.text = texto;
        t.fontSize = tamano;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = color;
        t.rectTransform.sizeDelta = new Vector2(6f, 1f);
    }

    private void CrearVelo()
    {
        var go = new GameObject("Velo de transición");
        go.transform.SetParent(transform, false);

        var lienzo = go.AddComponent<Canvas>();
        lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
        lienzo.sortingOrder = 150;          // sobre el HUD (100), bajo el panel del ascensor (200)

        var imagen = new GameObject("Negro", typeof(RectTransform));
        imagen.transform.SetParent(go.transform, false);
        var rt = imagen.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        velo = imagen.AddComponent<Image>();
        velo.color = new Color(0f, 0f, 0f, 0f);
        velo.raycastTarget = false;
        velo.enabled = false;
    }

    // ------------------------------------------------------------------
    // Cada frame
    // ------------------------------------------------------------------
    private void Update()
    {
        if (!lista || jugadorT == null || EnTransicion) return;

        int puerta = 0;
        if (!TestSession.EsperandoInicio && jugador != null && jugador.InputEnabled)
        {
            int piso = pisos.PisoActual;
            Vector3 p = jugadorT.position;

            if (ALaMano(p, FrenteDelRellano(piso))) { puerta = +1; escaleraActual = 0; }
            else if (ALaMano(p, FrenteDelHall(piso))) { puerta = -1; escaleraActual = 0; }
            else if (principalLista && pisos.TieneEscalera(piso))
            {
                if (ALaMano(p, FrenteDelRellanoPrincipal(piso))) { puerta = +1; escaleraActual = 1; }
                else if (ALaMano(p, FrenteDelHallPrincipal(piso))) { puerta = -1; escaleraActual = 1; }
            }
        }

        if (puerta != puertaActual)
        {
            puertaActual = puerta;
            JugadorEnPuerta = puerta != 0;
            Avisar(TextoDelAviso(puerta));
        }

        if (puerta != 0 && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            Usar();
        }
    }

    /// <summary>
    /// Lo que hace la tecla E frente a una puerta de la escalera. Devuelve false
    /// si no hay puerta a mano o si no lleva a ningún piso.
    /// </summary>
    public bool Usar()
    {
        if (!lista || EnTransicion || puertaActual == 0) return false;

        int destino = pisos.PisoActual + puertaActual;
        if (!pisos.EscaleraConecta(pisos.PisoActual, destino))
        {
            OnPuertaCerrada?.Invoke();
            return false;
        }
        StartCoroutine(Cruzar(puertaActual > 0, destino));
        return true;
    }

    private static bool ALaMano(Vector3 pies, Vector3 frente)
    {
        Vector3 d = pies - frente;
        if (Mathf.Abs(d.y) > 1.1f) return false;
        d.y = 0f;
        return d.sqrMagnitude <= AlcanceDePuerta * AlcanceDePuerta;
    }

    private string TextoDelAviso(int puerta)
    {
        if (puerta == 0) return "";

        int destino = pisos.PisoActual + puerta;
        if (destino >= pisos.CantidadDePisos) return "La azotea está cerrada";
        if (!pisos.EscaleraConecta(pisos.PisoActual, destino)) return "Esta puerta no lleva a ningún piso";

        return puerta > 0
            ? $"E  —  Subir al piso {FloorManager.NumeroVisible(destino)}"
            : $"E  —  Bajar al piso {FloorManager.NumeroVisible(destino)}";
    }

    private void Avisar(string texto)
    {
        if (texto == avisoActual) return;
        avisoActual = texto;
        OnAvisoCambiado?.Invoke(texto);
    }

    /// <summary>Cruza el tramo oculto: fundido, cambio de piso, fundido.</summary>
    private IEnumerator Cruzar(bool subiendo, int destino)
    {
        EnTransicion = true;
        JugadorEnPuerta = false;
        puertaActual = 0;
        Avisar("");

        yield return Fundir(0f, 1f);

        Llevar(subiendo, destino);
        OnTramoRecorrido?.Invoke(subiendo, destino);

        // Un instante a oscuras: el tramo oculto no es instantáneo.
        yield return new WaitForSecondsRealtime(0.25f);
        yield return Fundir(1f, 0f);

        EnTransicion = false;
    }

    private void Llevar(bool subiendo, int destino)
    {
        // Subiendo se sale por la puerta del hall del piso de arriba, mirando hacia
        // la salida del hall; bajando, por la del rellano del piso de abajo.
        Vector3 pies = subiendo ? FrenteDelHall(destino) : FrenteDelRellano(destino);
        // Al bajar se queda mirando hacia la cabeza del tramo visible, que es por donde sigue.
        float yaw = subiendo ? 250f : 100f;
        if (escaleraActual == 1 && principalLista)
        {
            pies = subiendo ? FrenteDelHallPrincipal(destino) : FrenteDelRellanoPrincipal(destino);
            // Subiendo se sale de espaldas a la puerta, girado hacia la salida al pasillo;
            // bajando, de cara a la cabeza del tramo.
            yaw = subiendo ? principal.rumbo + 180f + principal.giroAlLlegar : principal.rumbo - 107f;
        }

        // Primero el piso (hay que encenderlo antes de soltar al jugador encima) y
        // luego el punto exacto.
        pisos.LlevarAlPiso(destino);
        if (jugador != null) jugador.Teletransportar(pies, yaw);
        else if (jugadorT != null) jugadorT.SetPositionAndRotation(pies, Quaternion.Euler(0f, yaw, 0f));
        Physics.SyncTransforms();
    }

    /// <summary>
    /// Para las pruebas automáticas y el menú de depuración: cruza sin fundido.
    /// Devuelve false si no hay piso en esa dirección.
    /// </summary>
    public bool CruzarAhora(bool subiendo)
    {
        if (!lista) return false;
        int destino = pisos.PisoActual + (subiendo ? 1 : -1);
        if (!pisos.EscaleraConecta(pisos.PisoActual, destino)) return false;

        EnTransicion = true;
        escaleraActual = 0;
        Llevar(subiendo, destino);
        OnTramoRecorrido?.Invoke(subiendo, destino);
        EnTransicion = false;
        return true;
    }

    private IEnumerator Fundir(float desde, float hasta)
    {
        if (velo == null) yield break;

        velo.enabled = true;
        float t = 0f;
        while (t < DuracionDelFundido)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(desde, hasta, Mathf.Clamp01(t / DuracionDelFundido));
            velo.color = new Color(0f, 0f, 0f, a);
            yield return null;
        }
        velo.color = new Color(0f, 0f, 0f, hasta);
        if (hasta <= 0.001f) velo.enabled = false;
    }
}
