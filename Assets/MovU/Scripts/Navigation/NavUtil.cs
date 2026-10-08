using UnityEngine;
using UnityEngine.AI;

// ============================================================================
// NavUtil.cs — Consultas al NavMesh sin basura y con pisos
// ============================================================================
// Tres cosas que antes cada script resolvia por su cuenta:
//
//  - NavMeshPath.corners crea un arreglo nuevo cada vez que se lee. La flecha
//    lo leia varias veces cada 0,25 s. Aqui se usa GetCornersNonAlloc sobre un
//    buffer compartido.
//  - Se muestreaba desde la CAMARA con 5 m de radio. Con pisos apilados eso
//    puede engancharse al piso de arriba o a la corona de un muro. Aqui se
//    muestrea desde los PIES y se descarta lo que quede a otra altura.
//  - Rutas entre pisos: el NavMesh de un piso no se conecta con el de otro (el
//    ascensor teletransporta), asi que la ruta es "hasta el ascensor de este
//    piso" + "desde el ascensor del piso destino". Se elige el ascensor que
//    haga mas corta la suma.
//  - La escalera es otra forma de cambiar de piso, y a diferencia del ascensor
//    SI suma distancia: medio piso se camina (tramo visible y rellano). La ruta
//    optima entre pisos es la mas corta de las dos.
// ============================================================================

public static class NavUtil
{
    private const int MaxEsquinas = 256;
    private static readonly Vector3[] esquinas = new Vector3[MaxEsquinas];
    private static NavMeshPath ruta;
    private static int hayNavMesh = -1;      // -1 sin revisar, 0 no, 1 si

    // Por donde conviene cambiar de piso: se recalcula como mucho cada 0,75 s
    // (la flecha lo pregunta en cada frame cuando esta en modo directo).
    private static float entradaValidaHasta = -1f;
    private static int entradaPisoDesde = -1, entradaPisoHasta = -1;
    private static bool entradaPorEscalera;

    /// <summary>Esquinas de la ultima ruta calculada. Valido hasta la siguiente llamada.</summary>
    public static Vector3[] Esquinas => esquinas;
    public static int CantidadDeEsquinas { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reiniciar()
    {
        ruta = null;
        hayNavMesh = -1;
        CantidadDeEsquinas = 0;
        entradaValidaHasta = -1f;
    }

    /// <summary>Olvida si habia NavMesh (despues de hornear o cargar otra escena).</summary>
    public static void OlvidarEstado()
    {
        hayNavMesh = -1;
        entradaValidaHasta = -1f;
    }

    /// <summary>True si hay algun NavMesh cargado. Se revisa una sola vez.</summary>
    public static bool HayNavMesh
    {
        get
        {
            if (hayNavMesh < 0)
            {
                NavMeshTriangulation t = NavMesh.CalculateTriangulation();
                hayNavMesh = (t.indices != null && t.indices.Length > 0) ? 1 : 0;
            }
            return hayNavMesh == 1;
        }
    }

    /// <summary>El punto del NavMesh mas cercano a unos pies, en su mismo piso.</summary>
    public static bool Muestrear(Vector3 pies, out Vector3 punto, float radio = 2.5f)
    {
        if (NavMesh.SamplePosition(pies, out NavMeshHit hit, radio, NavMesh.AllAreas)
            && Mathf.Abs(hit.position.y - pies.y) < 1.2f)
        {
            punto = hit.position;
            return true;
        }
        punto = pies;
        return false;
    }

    /// <summary>
    /// Ruta caminable entre dos puntos del MISMO piso. Deja las esquinas en
    /// <see cref="Esquinas"/> y devuelve su longitud. False si no hay ruta completa.
    /// </summary>
    public static bool Ruta(Vector3 desde, Vector3 hasta, out float longitud)
    {
        longitud = 0f;
        CantidadDeEsquinas = 0;

        if (!Muestrear(desde, out Vector3 a) || !Muestrear(hasta, out Vector3 b)) return false;

        if (ruta == null) ruta = new NavMeshPath();
        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, ruta)) return false;
        if (ruta.status != NavMeshPathStatus.PathComplete) return false;

        int n = ruta.GetCornersNonAlloc(esquinas);
        CantidadDeEsquinas = n;
        for (int i = 0; i < n - 1; i++)
        {
            longitud += Vector3.Distance(esquinas[i], esquinas[i + 1]);
        }
        return n > 1;
    }

    /// <summary>
    /// Longitud de la ruta optima aunque el destino este en otro piso.
    /// 'usoAscensor' queda en true cuando la ruta cambia de piso.
    /// </summary>
    public static bool RutaEntrePisos(Vector3 desde, int pisoDesde,
                                      Vector3 hasta, int pisoHasta,
                                      out float longitud, out bool usoAscensor)
    {
        usoAscensor = false;

        if (pisoDesde == pisoHasta || FloorManager.Instance == null)
        {
            return Ruta(desde, hasta, out longitud);
        }

        usoAscensor = true;
        longitud = 0f;
        float mejor = float.MaxValue;

        var ascensores = ElevatorTrigger.Todos;
        for (int i = 0; i < ascensores.Count; i++)
        {
            ElevatorTrigger salida = ascensores[i];
            if (salida == null || salida.Piso != pisoDesde) continue;

            // El ascensor conserva el XZ: se llega al mismo punto del piso destino.
            Vector3 pieSalida = salida.PuntoDeAcceso;
            Vector3 pieLlegada = new Vector3(pieSalida.x,
                                             FloorManager.Instance.AlturaDelSuelo(pisoHasta),
                                             pieSalida.z);

            if (!Ruta(desde, pieSalida, out float tramo1)) continue;
            if (!Ruta(pieLlegada, hasta, out float tramo2)) continue;

            if (tramo1 + tramo2 < mejor) mejor = tramo1 + tramo2;
        }

        // La escalera compite con el ascensor: gana la ruta mas corta.
        if (RutaPorEscalera(desde, pisoDesde, hasta, pisoHasta, out float porEscalera) &&
            porEscalera < mejor)
        {
            mejor = porEscalera;
        }

        if (mejor == float.MaxValue) return false;
        longitud = mejor;
        return true;
    }

    /// <summary>
    /// Longitud de la ruta que cambia de piso por la escalera. Subiendo se entra
    /// por el pie del tramo y se sale por la puerta del hall del piso de arriba;
    /// bajando, al reves. Cada piso suma lo que se camina por el tramo y el
    /// rellano, y entre un piso y el siguiente hay que cruzar el hall.
    /// </summary>
    public static bool RutaPorEscalera(Vector3 desde, int pisoDesde, Vector3 hasta, int pisoHasta,
                                       out float longitud)
    {
        longitud = 0f;
        StairsManager escalera = StairsManager.Instance;
        if (escalera == null || !escalera.Disponible || pisoDesde == pisoHasta) return false;

        bool sube = pisoHasta > pisoDesde;
        int saltos = Mathf.Abs(pisoHasta - pisoDesde);

        Vector3 entrada = sube ? escalera.PieDelTramo(pisoDesde) : escalera.FrenteDelHall(pisoDesde);
        Vector3 salida = sube ? escalera.FrenteDelHall(pisoHasta) : escalera.PieDelTramo(pisoHasta);

        if (!Ruta(desde, entrada, out float tramo1)) return false;
        if (!Ruta(salida, hasta, out float tramo2)) return false;

        longitud = tramo1 + tramo2 + saltos * escalera.LargoDelRecorrido +
                   (saltos - 1) * escalera.EntreTramos;
        return true;
    }

    /// <summary>
    /// A donde hay que ir AHORA, en el piso del jugador, para llegar a un destino
    /// que esta en otro piso: el ascensor mas cercano o la escalera, lo que de la
    /// ruta mas corta. Es lo que sigue la flecha de guia.
    /// </summary>
    public static Vector3 EntradaParaCambiarDePiso(Vector3 pies, int pisoDesde, Vector3 destino, int pisoHasta)
    {
        StairsManager escalera = StairsManager.Instance;
        bool hayEscalera = escalera != null && escalera.Disponible;
        bool sube = pisoHasta > pisoDesde;

        // Ya va por la escalera: no tiene sentido mandarlo al ascensor (ademas,
        // ahi arriba no hay NavMesh con que comparar rutas).
        if (hayEscalera && escalera.EstaEnLaEscalera(pies, pisoDesde))
        {
            return sube ? escalera.FrenteDelRellano(pisoDesde) : escalera.FrenteDelHall(pisoDesde);
        }

        ElevatorTrigger ascensor = ElevatorTrigger.MasCercano(pisoDesde, pies);

        if (Time.time > entradaValidaHasta || entradaPisoDesde != pisoDesde || entradaPisoHasta != pisoHasta)
        {
            entradaValidaHasta = Time.time + 0.75f;
            entradaPisoDesde = pisoDesde;
            entradaPisoHasta = pisoHasta;
            entradaPorEscalera = false;

            if (hayEscalera && RutaPorEscalera(pies, pisoDesde, destino, pisoHasta, out float porEscalera))
            {
                float porAscensor = float.MaxValue;
                if (ascensor != null && FloorManager.Instance != null)
                {
                    Vector3 acceso = ascensor.PuntoDeAcceso;
                    Vector3 llegada = new Vector3(acceso.x, FloorManager.Instance.AlturaDelSuelo(pisoHasta), acceso.z);
                    if (Ruta(pies, acceso, out float a) && Ruta(llegada, destino, out float b)) porAscensor = a + b;
                }
                entradaPorEscalera = porEscalera < porAscensor;
            }
        }

        if (entradaPorEscalera && hayEscalera)
        {
            return sube ? escalera.PieDelTramo(pisoDesde) : escalera.FrenteDelHall(pisoDesde);
        }
        return ascensor != null ? ascensor.PuntoDeAcceso : destino;
    }

    /// <summary>
    /// Hacia donde hay que caminar AHORA para llegar a 'hasta' por el NavMesh:
    /// la primera esquina que no este ya encima del jugador.
    /// </summary>
    public static bool SiguienteTramo(Vector3 desde, Vector3 hasta, out Vector3 esquina)
    {
        esquina = hasta;
        if (!Ruta(desde, hasta, out _)) return false;

        int n = CantidadDeEsquinas;
        int indice = 1;
        if (n > 2)
        {
            Vector3 d = esquinas[1] - desde;
            d.y = 0f;
            if (d.sqrMagnitude < 1.5f * 1.5f) indice = 2;   // suavizado: mirar una esquina mas alla
        }
        esquina = esquinas[indice];
        return true;
    }
}
