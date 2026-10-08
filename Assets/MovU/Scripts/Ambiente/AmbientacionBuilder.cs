using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ============================================================================
// AmbientacionBuilder.cs — Lo que hace que el piso parezca un edificio
// ============================================================================
// Se ejecuta una vez al montar el juego (MovUBootstrap) y añade, en cada piso:
//
//   - TECHO: un cielo raso justo por debajo de la losa que ya trae el relleno
//     de corona. Esa losa es un plano gris liso: en pantalla se confunde con un
//     cielo nublado y no da referencia de distancia. El cielo raso la cubre con
//     una retícula de placas de 1,20 m, y deja abierto el hueco de la escalera.
//   - LÁMPARAS: paneles de luz en una retícula, solo donde debajo hay suelo.
//   - MOSTRADORES: los del JSON ("mobiliario"), con sus portátiles. Son el
//     punto de referencia de los dos espacios de DTI.
//   - ASCENSOR: marco y letrero alrededor del cubo.
//   - El cuerpo del JUGADOR en tercera persona: la misma figura de 91
//     triángulos de los NPC en vez de una cápsula de 832.
//
// Nada de esto lleva collider ni entra en el NavMesh: no cambia por dónde se
// puede caminar ni la ruta óptima. Todo cuelga de Contenido/Piso_N.
//
// Triángulos: el cielo raso son 26, una lámpara 2, un mostrador 72. El total por piso
// se escribe en consola y lo vigila una prueba (RD-4).
// ============================================================================

public static class AmbientacionBuilder
{
    public const string NombreDelGrupo = "Ambientación (JSON)";

    private const float PasoDeLamparas = 6f;
    public const float ModuloDelCieloRaso = 1.2f;
    private const float AlturaPorDefecto = 5.6f;

    /// <summary>
    /// Altura libre de un piso: del suelo a la cara de abajo del techo. Sale de la
    /// corona del relleno (hasta ahí llegan todos los muros). Vale para todos los
    /// pisos porque son copias.
    /// </summary>
    public static float AlturaDelTecho(FloorManager pisos)
    {
        float porSeparacion = pisos.SeparacionEntrePisos > 1f
            ? pisos.SeparacionEntrePisos - 0.75f
            : AlturaPorDefecto;

        Transform piso = pisos.Piso(0);
        if (piso == null) return porSeparacion;

        float cima = float.MinValue;
        var filtros = piso.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filtros.Length; i++)
        {
            // Solo la geometría del edificio: la planta y su relleno.
            if (filtros[i].sharedMesh == null) continue;
            if (filtros[i].GetComponentInParent<ElevatorTrigger>() != null) continue;
            if (filtros[i].name == "Cubo") continue;

            Bounds b = filtros[i].sharedMesh.bounds;
            Matrix4x4 m = filtros[i].transform.localToWorldMatrix;
            for (int e = 0; e < 8; e++)
            {
                Vector3 esquina = b.center + Vector3.Scale(b.extents, new Vector3(
                    (e & 1) == 0 ? -1f : 1f, (e & 2) == 0 ? -1f : 1f, (e & 4) == 0 ? -1f : 1f));
                float y = m.MultiplyPoint3x4(esquina).y;
                if (y > cima) cima = y;
            }
        }

        float altura = cima - pisos.AlturaDelSuelo(0);
        // Un valor disparatado quiere decir que la medida no sirvió.
        if (altura < 2.4f || altura > porSeparacion + 0.5f) return Mathf.Min(porSeparacion, AlturaPorDefecto);
        return altura - 0.02f;       // un pelo por debajo de la corona: la tapa y no parpadea contra ella
    }

    public static void Construir(ContenidoPiso datos, FloorManager pisos, PlayerController jugador,
                                 float alturaDelTecho)
    {
        if (datos == null || pisos == null) return;

        // El cielo raso es la misma malla en todos los pisos (son copias): se hace una vez.
        Mesh cieloRaso = null;
        int triangulosDelCieloRaso = 0;

        for (int i = 0; i < pisos.CantidadDePisos; i++)
        {
            PlanoDePlanta plano = PlanoDePlanta.Medir(pisos.Piso(i));
            if (!plano.valido) continue;

            Transform grupo = ContenidoLoader.Grupo(pisos.ContenidoDelPiso(i), NombreDelGrupo);
            if (grupo == null) continue;

            float suelo = pisos.AlturaDelSuelo(i);

            if (datos.ajustes.techo)
            {
                if (cieloRaso == null)
                {
                    cieloRaso = MallaDelCieloRaso(plano, StairsManager.HuecoEnElTecho(datos.escalera, plano),
                                                  out triangulosDelCieloRaso);
                }
                // 2 cm por debajo de la losa del relleno.
                FormasMovU.Pieza(grupo, "Cielo raso", new Vector3(0f, suelo + alturaDelTecho, 0f),
                                 cieloRaso, FormasMovU.CieloRaso(), i, triangulosDelCieloRaso);
                CrearLamparas(grupo, plano, suelo, alturaDelTecho, i);
            }

            if (datos.ajustes.mobiliario)
            {
                for (int m = 0; m < datos.mobiliario.Count; m++)
                {
                    MuebleDef def = datos.mobiliario[m];
                    if (def == null || def.piso != i + 1) continue;
                    CrearMostrador(grupo, ContenidoLoader.AMundo(plano, pisos, i, def.u, def.v), def.yaw, i);
                }
            }
        }

        VestirAscensores(pisos);
        VestirJugador(jugador);
    }

    // ------------------------------------------------------------------
    // Techo y lámparas
    // ------------------------------------------------------------------
    /// <summary>
    /// El cielo raso de un piso: la huella entera menos los huecos (la caja de la
    /// escalera, que lleva su propio techo más alto). Se parte en una rejilla con
    /// los bordes de los huecos: 16 placas como mucho, 32 triángulos.
    /// </summary>
    public static Mesh MallaDelCieloRaso(PlanoDePlanta plano, List<Rect> huecos, out int triangulos)
    {
        var xs = new List<float> { plano.xMin, plano.xMax };
        var zs = new List<float> { plano.zMin, plano.zMax };
        for (int h = 0; h < huecos.Count; h++)
        {
            xs.Add(Mathf.Clamp(huecos[h].xMin, plano.xMin, plano.xMax));
            xs.Add(Mathf.Clamp(huecos[h].xMax, plano.xMin, plano.xMax));
            zs.Add(Mathf.Clamp(huecos[h].yMin, plano.zMin, plano.zMax));
            zs.Add(Mathf.Clamp(huecos[h].yMax, plano.zMin, plano.zMax));
        }
        xs.Sort();
        zs.Sort();

        var placas = new List<Rect>();
        for (int ix = 0; ix < xs.Count - 1; ix++)
        {
            if (xs[ix + 1] - xs[ix] < 0.01f) continue;
            for (int iz = 0; iz < zs.Count - 1; iz++)
            {
                if (zs[iz + 1] - zs[iz] < 0.01f) continue;

                var centro = new Vector2((xs[ix] + xs[ix + 1]) * 0.5f, (zs[iz] + zs[iz + 1]) * 0.5f);
                bool enHueco = false;
                for (int h = 0; h < huecos.Count && !enHueco; h++) enHueco = huecos[h].Contains(centro);
                if (enHueco) continue;

                placas.Add(Rect.MinMaxRect(xs[ix], zs[iz], xs[ix + 1], zs[iz + 1]));
            }
        }

        return FormasMovU.PlacasHaciaAbajo(placas, ModuloDelCieloRaso, "MovU_CieloRaso", out triangulos);
    }

    private static void CrearLamparas(Transform grupo, PlanoDePlanta plano, float suelo,
                                      float alturaDelTecho, int piso)
    {
        Material luz = FormasMovU.Luminoso(new Color32(255, 250, 236, 255), 1.5f);

        var lamparas = new GameObject("Lámparas");
        lamparas.transform.SetParent(grupo, false);

        // Los colliders recién cargados pueden no estar aún en el motor de física.
        Physics.SyncTransforms();

        float y = suelo + alturaDelTecho - 0.03f;
        int puestas = 0;
        for (float z = plano.zMin + PasoDeLamparas * 0.5f; z < plano.zMax; z += PasoDeLamparas)
        {
            for (float x = plano.xMin + PasoDeLamparas * 0.5f; x < plano.xMax; x += PasoDeLamparas)
            {
                // Solo sobre suelo despejado: una lámpara encima de un muro no se vería.
                var desde = new Vector3(x, y - 0.2f, z);
                if (!Physics.Raycast(desde, Vector3.down, out RaycastHit golpe, alturaDelTecho + 1f,
                                     ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                if (golpe.point.y - suelo > 0.3f) continue;

                FormasMovU.PlacaHaciaAbajo(lamparas.transform, "Lámpara", new Vector3(x, y, z),
                                           1.3f, 0.6f, luz, piso);
                puestas++;
            }
        }

        if (puestas == 0)
        {
            Debug.LogWarning($"[Ambientación] No se pudo poner ninguna lámpara en el piso {piso + 1} " +
                             "(el trazado de rayos no encontró suelo).");
        }
    }

    // ------------------------------------------------------------------
    // Mobiliario
    // ------------------------------------------------------------------
    /// <summary>Un mostrador de atención con dos portátiles. 'yaw' es hacia dónde atiende.</summary>
    private static void CrearMostrador(Transform grupo, Vector3 pie, float yaw, int piso)
    {
        var raiz = new GameObject("Mostrador");
        raiz.transform.SetParent(grupo, false);
        raiz.transform.SetPositionAndRotation(pie, Quaternion.Euler(0f, yaw, 0f));
        Transform t = raiz.transform;

        Material cuerpo = FormasMovU.Color(new Color32(232, 232, 226, 255));
        Material tapa = FormasMovU.Color(new Color32(0, 122, 138, 255));        // turquesa DTI
        Material equipo = FormasMovU.Color(new Color32(52, 56, 64, 255));
        Material pantalla = FormasMovU.Luminoso(new Color32(120, 190, 230, 255), 0.7f);

        FormasMovU.Caja(t, "Cuerpo", new Vector3(0f, 0.46f, 0f), new Vector3(1.9f, 0.92f, 0.6f), cuerpo, piso);
        FormasMovU.Caja(t, "Tapa", new Vector3(0f, 0.95f, 0f), new Vector3(2.0f, 0.06f, 0.7f), tapa, piso);

        for (int k = -1; k <= 1; k += 2)
        {
            float x = k * 0.48f;
            FormasMovU.Caja(t, "Portátil", new Vector3(x, 0.995f, 0.02f), new Vector3(0.34f, 0.02f, 0.24f), equipo, piso);
            // La pantalla, levantada y mirando hacia quien atiende (atrás del mostrador).
            GameObject p = FormasMovU.Caja(t, "Pantalla", new Vector3(x, 1.10f, 0.15f),
                                           new Vector3(0.34f, 0.22f, 0.015f), pantalla, piso);
            p.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
        }
    }

    // ------------------------------------------------------------------
    // Ascensor
    // ------------------------------------------------------------------
    private static void VestirAscensores(FloorManager pisos)
    {
        Material marco = FormasMovU.Color(new Color32(58, 62, 70, 255));
        Material boton = FormasMovU.Luminoso(new Color32(255, 196, 60, 255), 1.1f);

        var ascensores = ElevatorTrigger.Todos;
        for (int a = 0; a < ascensores.Count; a++)
        {
            ElevatorTrigger e = ascensores[a];
            if (e == null) continue;

            int piso = e.Piso;
            Transform grupo = ContenidoLoader.Grupo(pisos.ContenidoDelPiso(piso), NombreDelGrupo);
            if (grupo == null) continue;

            // El cubo del ascensor es el hermano 'Cubo' de la zona de disparo.
            Transform cubo = e.transform.parent != null ? e.transform.parent.Find("Cubo") : null;
            if (cubo == null) continue;

            var raiz = new GameObject("Ascensor_marco");
            raiz.transform.SetParent(grupo, false);
            float suelo = pisos.AlturaDelSuelo(piso);
            raiz.transform.SetPositionAndRotation(new Vector3(cubo.position.x, suelo, cubo.position.z),
                                                  cubo.rotation);
            Transform t = raiz.transform;

            float ancho = cubo.lossyScale.x;
            float alto = cubo.position.y - suelo + cubo.lossyScale.y * 0.5f;
            float fondo = cubo.lossyScale.z;

            FormasMovU.Caja(t, "Jamba_izq", new Vector3(-ancho * 0.5f - 0.07f, alto * 0.5f, 0f),
                            new Vector3(0.14f, alto + 0.14f, fondo + 0.10f), marco, piso);
            FormasMovU.Caja(t, "Jamba_der", new Vector3(ancho * 0.5f + 0.07f, alto * 0.5f, 0f),
                            new Vector3(0.14f, alto + 0.14f, fondo + 0.10f), marco, piso);
            FormasMovU.Caja(t, "Cabezal", new Vector3(0f, alto + 0.25f, 0f),
                            new Vector3(ancho + 0.28f, 0.50f, fondo + 0.10f), marco, piso);
            // La unión de las dos hojas.
            FormasMovU.Caja(t, "Union", new Vector3(0f, alto * 0.5f, 0f),
                            new Vector3(0.025f, alto, fondo + 0.02f), marco, piso);

            // Botón de llamada a cada cara: no se sabe de qué lado llega el jugador.
            for (int lado = -1; lado <= 1; lado += 2)
            {
                float z = lado * (fondo * 0.5f + 0.055f);
                FormasMovU.Caja(t, "Boton", new Vector3(ancho * 0.5f + 0.07f, 1.15f, z),
                                new Vector3(0.07f, 0.11f, 0.02f), boton, piso);

                var texto = new GameObject("Letrero");
                texto.transform.SetParent(t, false);
                texto.transform.localPosition = new Vector3(0f, alto + 0.25f, z + lado * 0.005f);
                // TextMeshPro se lee mirando a lo largo de su +Z.
                texto.transform.localRotation = Quaternion.Euler(0f, lado > 0 ? 180f : 0f, 0f);

                var tmp = texto.AddComponent<TextMeshPro>();
                tmp.text = $"ASCENSOR · Piso {FloorManager.NumeroVisible(piso)}";
                tmp.fontSize = 1.5f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.color = new Color(1f, 0.90f, 0.62f);
                tmp.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
            }
        }
    }

    // ------------------------------------------------------------------
    // Jugador
    // ------------------------------------------------------------------
    /// <summary>
    /// En tercera persona el jugador era una cápsula de 832 triángulos. Se le
    /// pone la figura de los NPC (91), con ropa propia para distinguirlo.
    /// </summary>
    private static void VestirJugador(PlayerController jugador)
    {
        if (jugador == null) return;

        Transform cuerpo = jugador.transform.Find("Body");
        if (cuerpo == null) return;

        var filtro = cuerpo.GetComponent<MeshFilter>();
        var dibujo = cuerpo.GetComponent<MeshRenderer>();
        if (filtro == null || dibujo == null) return;

        filtro.sharedMesh = NpcMeshFactory.Malla;
        cuerpo.localPosition = Vector3.zero;          // la figura tiene los pies en y = 0
        cuerpo.localRotation = Quaternion.identity;
        cuerpo.localScale = Vector3.one;

        bool visible = dibujo.enabled;                // CameraRig lo oculta en primera persona
        var materiales = new Material[3];
        materiales[NpcMeshFactory.SubmallaRopa] = NpcMeshFactory.Material(new Color32(30, 78, 154, 255));     // azul Javeriana
        materiales[NpcMeshFactory.SubmallaPiel] = NpcMeshFactory.Material(new Color32(214, 164, 124, 255));
        materiales[NpcMeshFactory.SubmallaPantalon] = NpcMeshFactory.Material(new Color32(48, 58, 82, 255));
        dibujo.sharedMaterials = materiales;
        dibujo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dibujo.enabled = visible;

        // La cápsula traía su propio collider; el que cuenta es el CharacterController.
        var sobrante = cuerpo.GetComponent<Collider>();
        if (sobrante != null) Object.Destroy(sobrante);
    }
}
