using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================================
// MovUBootstrap.cs — Pone a andar el juego en la escena del edificio
// ============================================================================
// Al cargar una escena que tenga un FloorManager (es decir, el edificio) y que
// NO tenga ya un MissionManager, monta todo lo que hace falta para jugar:
//
//   contenido del JSON (POIs, punto de aparición)  ->  NavMesh  ->  sonido  ->
//   ambientación (techo, lámparas, puertas, escalera)  ->  gestores (misiones,
//   juego, NPC, inventario, rótulos)  ->  flecha de guía  ->  HUD  ->  sesión
//
// Así la escena del edificio solo guarda geometría, jugador y ascensores; nada
// de esto hay que armarlo a mano ni se pierde al reconstruir el edificio.
//
// No toca DemoMaze: esa escena ya trae sus gestores y aquí se detecta y se sale.
// Si alguien prefiere montar los gestores a mano en la escena, basta con que
// exista un MissionManager para que este arranque automático no haga nada.
// ============================================================================

public static class MovUBootstrap
{
    public const string NombreDeLaRaiz = "MovU_Juego";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Registrar()
    {
        SceneManager.sceneLoaded -= AlCargarEscena;
        SceneManager.sceneLoaded += AlCargarEscena;
    }

    // Red de seguridad para la primera escena, por si el evento de arriba no
    // alcanzó a engancharse: Montar() no hace nada si ya se montó.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PrimeraEscena()
    {
        Montar();
    }

    private static void AlCargarEscena(Scene escena, LoadSceneMode modo)
    {
        if (modo == LoadSceneMode.Single) Montar();
    }

    private static void Montar()
    {
        FloorManager pisos = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (pisos == null || pisos.CantidadDePisos == 0) return;             // no es el edificio
        if (GameObject.Find(NombreDeLaRaiz) != null) return;                 // ya está montado
        if (Object.FindFirstObjectByType<MissionManager>() != null)
        {
            // Pasa si Edificio.unity se guardó a partir de DemoMaze: trae los
            // gestores del laberinto (un solo objetivo 'Goal') y este montaje
            // automático no los pisa.
            Debug.LogWarning("[MovU] La escena del edificio ya trae un MissionManager, así que NO " +
                             "monto las misiones del JSON. Si son restos del demo (GameManager, " +
                             "MissionManager, el Canvas del HUD, GuidanceArrow, Goal), bórralos de " +
                             "la escena y vuelve a dar Play.");
            return;
        }

        // Los registros estáticos son por escena.
        PointOfInterest.OlvidarRegistro();
        ElevatorTrigger.OlvidarRegistro();
        NavUtil.OlvidarEstado();

        ContenidoPiso datos = ContenidoLoader.Leer();
        if (datos == null)
        {
            Debug.LogWarning("[MovU] Sin contenido_piso9.json el edificio se puede recorrer, " +
                             "pero no hay misiones.");
            return;
        }

        var raiz = new GameObject(NombreDeLaRaiz);
        FloorManager.NumeroDelPrimerPiso = Mathf.Max(1, datos.ajustes.numeroDelPrimerPiso);

        // --- Jugador ----------------------------------------------------
        PlayerController jugador = Object.FindFirstObjectByType<PlayerController>();
        if (jugador == null)
        {
            Debug.LogError("[MovU] No hay personaje en la escena. Corre 'MovU > Preparar todo'.");
            return;
        }
        // Misiones, HUD y flecha encuentran al jugador por su tag.
        if (!jugador.CompareTag("Player")) jugador.gameObject.tag = "Player";

        // --- Contenido --------------------------------------------------
        int pois = ContenidoLoader.CrearPois(datos, pisos);

        if (ContenidoLoader.ResolverAparicion(datos, pisos))
        {
            // Antes del Start del FloorManager, que es quien decide qué piso
            // encender según dónde esté el jugador.
            jugador.Teletransportar(ContenidoLoader.PuntoDeAparicion, ContenidoLoader.YawDeAparicion);
        }

        AvisarSiElAscensorNoCoincide(datos, pisos);

        // --- NavMesh ----------------------------------------------------
        AsegurarNavMesh(raiz);

        // --- Sonido -----------------------------------------------------
        var sonido = raiz.AddComponent<AudioManager>();
        sonido.Configurar(datos.ajustes);
        if (jugador.GetComponent<PlayerFootsteps>() == null)
        {
            jugador.gameObject.AddComponent<PlayerFootsteps>();
        }

        // --- Ambientación -----------------------------------------------
        // Después del NavMesh a propósito: nada de esto debe entrar en él. Las
        // puertas no tienen collider y la escalera queda fuera de la malla
        // horneada; la ruta óptima la cuenta aparte (NavUtil.RutaPorEscalera).
        FormasMovU.ReiniciarCuenta();
        float techo = AmbientacionBuilder.AlturaDelTecho(pisos);
        AmbientacionBuilder.Construir(datos, pisos, jugador, techo);

        var puertas = raiz.AddComponent<DoorManager>();
        if (datos.ajustes.puertas) puertas.Construir(datos, pisos, techo);

        var escalera = raiz.AddComponent<StairsManager>();
        escalera.Construir(datos, pisos, jugador, techo);

        // --- Gestores ---------------------------------------------------
        var misiones = raiz.AddComponent<MissionManager>();
        misiones.ConfigurarCatalogo(datos.misiones);

        raiz.AddComponent<GameManager>();

        var npcs = raiz.AddComponent<NpcManager>();
        npcs.Poblar(datos, pisos);

        if (datos.ajustes.rotulos)
        {
            var rotulos = raiz.AddComponent<PoiSignage>();
            rotulos.Configurar(datos.ajustes.distanciaRotulos);
        }

        var flecha = new GameObject("GuidanceArrow");
        flecha.transform.SetParent(raiz.transform, false);
        flecha.AddComponent<GuidanceArrow>();

        var inventario = raiz.AddComponent<InventoryManager>();
        inventario.Configurar(datos);

        HUDController hud = HudBuilder.Construir();
        hud.transform.SetParent(raiz.transform, false);
        if (datos.ajustes.inventario) InventoryUI.Construir(hud.transform, inventario);

        var sesion = raiz.AddComponent<TestSession>();
        sesion.Configurar(datos.ajustes.pedirParticipante);

        Debug.Log($"[MovU] Juego montado sobre '{datos.nombre}': {PointOfInterest.Todos.Count} POIs " +
                  $"({pois} del JSON), {datos.misiones.Count} misiones, {npcs.Cantidad} NPC. " +
                  (NavUtil.HayNavMesh ? "NavMesh listo." : "SIN NavMesh."));

        int piso = ContenidoLoader.HayAparicion ? ContenidoLoader.PisoDeAparicion : 0;
        Debug.Log($"[MovU] Ambientación: {puertas.Cantidad} puertas en total, techo a {techo:F2} m, " +
                  $"escalera {(escalera.Disponible ? "funcional" : "sin construir")}, " +
                  $"{inventario.Objetos.Count} objetos en el inventario. " +
                  $"Triángulos añadidos en el piso de aparición: {FormasMovU.TriangulosEn(piso):N0}.");
    }

    /// <summary>
    /// Lo normal es que el NavMesh venga horneado en la escena
    /// (MovU > Juego > Hornear NavMesh). Si no viene, se hornea aquí, al vuelo,
    /// para que dar Play funcione igual.
    ///
    /// Ojo: en el EJECUTABLE el horneado al vuelo solo funciona si las mallas
    /// tienen Read/Write activado, y las del edificio no lo tienen. Por eso
    /// para el build hay que hornearlo en el editor; este respaldo es para
    /// trabajar dentro de Unity.
    /// </summary>
    private static void AsegurarNavMesh(GameObject raiz)
    {
        if (NavUtil.HayNavMesh) return;

        Debug.LogWarning("[MovU] La escena no trae NavMesh horneado. Lo horneo ahora (tarda un " +
                         "momento). Para dejarlo guardado: MovU > Juego > Hornear NavMesh.");

        var go = new GameObject("NavMesh (al vuelo)");
        go.transform.SetParent(raiz.transform, false);

        var superficie = go.AddComponent<NavMeshSurface>();
        ConfigurarSuperficie(superficie);
        superficie.BuildNavMesh();

        NavUtil.OlvidarEstado();
    }

    /// <summary>
    /// El ascensor no se mueve al dar Play (el edificio es geometría estática):
    /// si el JSON dice otra cosa, se avisa y se indica el menú que lo corrige.
    /// </summary>
    private static void AvisarSiElAscensorNoCoincide(ContenidoPiso datos, FloorManager pisos)
    {
        if (datos.ascensor == null || !datos.ascensor.usar) return;
        if (!ContenidoLoader.AMundo(pisos, 1, datos.ascensor.u, datos.ascensor.v, out Vector3 sitio)) return;

        var ascensores = ElevatorTrigger.Todos;
        for (int i = 0; i < ascensores.Count; i++)
        {
            if (ascensores[i] == null) continue;
            Vector3 d = ascensores[i].transform.position - sitio;
            d.y = 0f;
            if (d.sqrMagnitude > 1f)
            {
                Debug.LogWarning("[MovU] El ascensor de la escena no está donde dice el JSON " +
                                 $"(a {d.magnitude:F1} m). Para moverlo: MovU > Contenido > " +
                                 "Llevar ascensor y aparición al sitio del JSON.");
                return;
            }
        }
    }

    /// <summary>La misma configuración para el horneado del editor y el de respaldo.</summary>
    public static void ConfigurarSuperficie(NavMeshSurface superficie)
    {
        superficie.collectObjects = CollectObjects.All;
        // Colisiones y no mallas de dibujo: es por donde de verdad se puede
        // caminar, y deja fuera el suelo exterior (que no tiene collider).
        superficie.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        superficie.layerMask = ~0;
        superficie.overrideVoxelSize = false;
        superficie.overrideTileSize = false;
    }
}
