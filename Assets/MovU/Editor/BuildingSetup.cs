using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// ============================================================================
// BuildingSetup.cs — Arma el edificio de 3 pisos
// ============================================================================
// Menu:  MovU > Edificio > Construir edificio de 3 pisos
//
// Se conserva el modelo original de Meshy. Como su extrusion deja las coronas
// de los muros a alturas distintas (de 1,4 a 3,4 m), cada piso lleva ademas la
// malla RellenoCorona, que sube los muros hasta una altura pareja. Sin ella se
// veria el piso de arriba por encima de los muros cortos.
//
// La escala es DELIBERADAMENTE no uniforme: 56x en horizontal y 28x en
// vertical. Escalar todo a 56x duplicaria tambien la altura de los muros
// (pasarian de ~3 m a ~6 m) y los pasillos quedarian como catedrales. Asi la
// planta mide el doble y las alturas siguen siendo humanas, que es lo que un
// juego de orientacion necesita. Ambas escalas estan abajo como constantes.
//
// Es idempotente: vuelve a construir el edificio desde cero cada vez, pero
// SOLO borra lo que lleva el marcador GeneradoPorMovU. Lo que el equipo ponga
// a mano en la raiz 'Contenido' (puertas, POIs, objetivos de mision) sobrevive
// a cualquier reconstruccion: Contenido/Piso_N se enciende y se apaga junto
// con su piso, sin ser hijo de la geometria que se regenera.
// ============================================================================

public static class BuildingSetup
{
    // El modelo local tiene x,y horizontales y z hacia arriba; la rotacion de
    // -90 en X lo acuesta, asi que la escala local z acaba siendo la vertical.
    private const float EscalaHorizontal = 56f;   // 2x respecto a los 28x previos
    private const float EscalaVertical = 45f;     // muros mas altos: techo a ~5,6 m

    private const int CantidadDePisos = 3;
    private const float MargenEntrePisos = 0.06f;

    private const string CarpetaModelos = "Assets/MovU/Models";
    private const string RutaRelleno = CarpetaModelos + "/RellenoCorona.obj";
    private const string RutaMaterial = "Assets/MovU/Materials/Mat_EntornoEstilizado.mat";
    private const string RutaMatAscensor = "Assets/MovU/Materials/Mat_Ascensor.mat";

    private const string NombreEdificio = "Edificio";
    private const string NombreContenido = "Contenido";
    private const string Herramienta = "BuildingSetup";
    private const float PasoDeRejilla = 0.5f;

    private static readonly Color EmisionTecho = new Color(0.40f, 0.40f, 0.38f);

    /// <summary>Vertices a partir de los cuales una malla suelta se considera
    /// geometria de entorno y no un objeto de contenido.</summary>
    private const int VerticesMallaGrande = 5000;

    // ------------------------------------------------------------------
    [MenuItem("MovU/Edificio/Construir edificio de 3 pisos", priority = 10)]
    public static void Construir()
    {
        ConstruirInterno(true);
    }

    /// <summary>
    /// El trabajo de verdad. Devuelve false si no se pudo construir, para que
    /// 'MovU > Preparar todo' sepa que no tiene sentido seguir con el paso
    /// siguiente. Con interactivo = false no abre dialogos: escribe en consola.
    /// </summary>
    internal static bool ConstruirInterno(bool interactivo)
    {
        GameObject modeloAsset = CargarModeloMeshy();
        GameObject rellenoAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RutaRelleno);

        if (modeloAsset == null)
        {
            Aviso(interactivo, "No encuentro el modelo de Meshy en " + CarpetaModelos + ".");
            return false;
        }
        if (rellenoAsset == null)
        {
            Aviso(interactivo, "Falta " + RutaRelleno + ".\n\nGeneralo con:\n" +
                  "    python3 Tools/tapar_corona.py\n\n" +
                  "Sin el relleno, al apilar los pisos se ve el de arriba por " +
                  "encima de los muros que quedaron cortos.");
            return false;
        }

        Transform jugador = BuscarJugador();
        if (jugador == null)
        {
            Aviso(interactivo, "No hay un Player en la escena.\n\n" +
                  "Corre primero 'MovU > Preparar plano Meshy jugable' para que " +
                  "lo construya, y vuelve a este menu.");
            return false;
        }

        if (!LimpiarEscena(interactivo)) return false;

        var edificio = new GameObject(NombreEdificio);
        Undo.RegisterCreatedObjectUndo(edificio, "Construir edificio");
        GeneradoPorMovU.Marcar(edificio, Herramienta);

        Material material = CargarMaterialEntorno();

        // --- Piso 1: se arma primero para poder medir -------------------
        Transform piso1 = ConstruirPiso(edificio.transform, 0, modeloAsset,
                                        rellenoAsset, material);
        Physics.SyncTransforms();

        Bounds caja = LimitesDe(piso1);
        float separacion = caja.size.y + MargenEntrePisos;
        float alturaLosa = DetectarAlturaDeLosa(caja) - piso1.position.y;

        Debug.Log($"[Edificio] Planta de {caja.size.x:F1} x {caja.size.z:F1} m. " +
                  $"Alto de un piso: {caja.size.y:F2} m -> separacion {separacion:F2} m. " +
                  $"Losa a {alturaLosa:F2} m de la base.");

        // --- Punto de aparicion y sitio del ascensor --------------------
        List<Vector3> abiertos = PuntosMasAbiertos(caja, 8);
        if (abiertos.Count == 0)
        {
            Aviso(interactivo, "No encontre ninguna zona abierta donde quepa el personaje.");
            return false;
        }

        Vector3 sitioAscensor = abiertos[0];
        Vector3 aparicion = sitioAscensor;
        foreach (Vector3 p in abiertos)
        {
            float d = Vector3.Distance(p, sitioAscensor);
            if (d > 2.5f && d < 9f) { aparicion = p; break; }
        }

        // Lo anterior es el respaldo: "la zona más despejada", que fue lo que
        // dejó el ascensor dentro de un salón. Si contenido_piso9.json dice
        // dónde van de verdad el ascensor y la aparición, mandan esos puntos.
        float yawAscensor = 0f;
        float yawAparicion = 0f;
        float sueloPiso1 = piso1.position.y + alturaLosa;
        ContenidoPiso contenidoJson = ContenidoLoader.Leer();
        PlanoDePlanta plano = PlanoDePlanta.Medir(piso1);
        if (contenidoJson != null && plano.valido)
        {
            if (contenidoJson.ascensor != null && contenidoJson.ascensor.usar)
            {
                Vector3 p = plano.AMundo(contenidoJson.ascensor.u, contenidoJson.ascensor.v, sueloPiso1);
                if (CabeUnaPersona(p))
                {
                    sitioAscensor = p;
                    yawAscensor = contenidoJson.ascensor.yaw;
                }
                else
                {
                    Debug.LogWarning("[Edificio] El sitio del ascensor del JSON cae dentro de un muro; " +
                                     "uso la zona más despejada. Revisa 'ascensor' en contenido_piso9.json.");
                }
            }

            if (contenidoJson.aparicion != null && contenidoJson.aparicion.usar)
            {
                Vector3 p = plano.AMundo(contenidoJson.aparicion.u, contenidoJson.aparicion.v, sueloPiso1);
                if (CabeUnaPersona(p))
                {
                    aparicion = p;
                    yawAparicion = contenidoJson.aparicion.yaw;
                }
                else
                {
                    Debug.LogWarning("[Edificio] El punto de aparición del JSON cae dentro de un muro; " +
                                     "uso una zona despejada. Revisa 'aparicion' en contenido_piso9.json.");
                }
            }
        }

        // --- Pisos restantes -------------------------------------------
        var pisos = new List<Transform> { piso1 };
        for (int i = 1; i < CantidadDePisos; i++)
        {
            Transform piso = ConstruirPiso(edificio.transform, i, modeloAsset,
                                           rellenoAsset, material);
            // Hereda el centrado en XZ del piso 1: si no, cada piso queda
            // desplazado respecto al de abajo y el ascensor no coincide.
            piso.localPosition = piso1.localPosition
                                 + new Vector3(0f, i * separacion, 0f);
            pisos.Add(piso);
        }

        // --- Ascensores, uno por piso, en el mismo XZ -------------------
        Material matAscensor = CrearMaterialAscensor();
        for (int i = 0; i < pisos.Count; i++)
        {
            float y = pisos[i].position.y + alturaLosa;
            CrearAscensor(pisos[i], new Vector3(sitioAscensor.x, y, sitioAscensor.z),
                          i, matAscensor, yawAscensor);
        }

        // --- Jugador ----------------------------------------------------
        ColocarJugador(jugador, new Vector3(aparicion.x,
                                            piso1.position.y + alturaLosa,
                                            aparicion.z), yawAparicion);

        // --- Contenido puesto a mano ------------------------------------
        // Vive FUERA del edificio a proposito: asi sobrevive a la proxima
        // reconstruccion. El FloorManager lo enciende junto con su piso.
        List<Transform> contenido = AsegurarContenido(pisos.Count);

        // --- Gestor de pisos --------------------------------------------
        var gestor = edificio.AddComponent<FloorManager>();
        gestor.Configurar(pisos, alturaLosa, jugador, 0, contenido);
        EditorUtility.SetDirty(gestor);

        // --- Material: la altura se mide dentro de cada piso -------------
        // OJO: 'alturaLosa' va medida desde el pivote del piso, que no es su base
        // (el pivote queda donde estaba el origen del modelo, unos metros más
        // arriba). El degradado del muro necesita la altura real de la losa
        // sobre la base; con el otro valor salía un degradado de más de 8 m.
        AjustarMaterial(material, piso1.position.y + alturaLosa, separacion,
                        (piso1.position.y + alturaLosa) - caja.min.y);

        AjustarLuzInterior();

        MarcarEstatico(edificio);
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(edificio.scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Edificio] Listo: {CantidadDePisos} pisos, ascensor en " +
                  $"{sitioAscensor}, aparicion en {aparicion}. " +
                  "Falta hornear el Occlusion Culling: " +
                  "Window > Rendering > Occlusion Culling > Bake.");
        return true;
    }

    // ------------------------------------------------------------------
    [MenuItem("MovU/Edificio/Cargar tambien los pisos vecinos", priority = 11)]
    public static void AlternarVecinos()
    {
        var gestor = Object.FindFirstObjectByType<FloorManager>();
        if (gestor == null)
        {
            Dialogo("No hay ningun edificio construido en esta escena.");
            return;
        }

        var so = new SerializedObject(gestor);
        var prop = so.FindProperty("pisosVecinosCargados");
        prop.intValue = prop.intValue == 0 ? 1 : 0;
        so.ApplyModifiedProperties();

        Debug.Log(prop.intValue == 0
            ? "[Edificio] Solo se carga el piso actual (lo mas liviano)."
            : "[Edificio] Se cargan tambien los pisos de arriba y abajo.");
    }

    // ------------------------------------------------------------------
    // Construccion de un piso
    // ------------------------------------------------------------------
    private static Transform ConstruirPiso(Transform padre, int indice,
                                           GameObject modelo, GameObject relleno,
                                           Material material)
    {
        var piso = new GameObject($"Piso_{indice + 1}");
        piso.transform.SetParent(padre, false);

        GameObject geo = Instanciar(modelo, piso.transform, "Planta");
        GameObject cor = Instanciar(relleno, piso.transform, "RellenoCorona");

        Normalizar(geo.transform);
        Normalizar(cor.transform);

        // El relleno vive por encima de la cabeza del jugador: no necesita
        // collider. Ahorrarselo es lo mas barato que se puede hacer aqui.
        foreach (var mf in geo.GetComponentsInChildren<MeshFilter>(true))
        {
            var mc = mf.gameObject.GetComponent<MeshCollider>();
            if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }

        if (material != null)
        {
            foreach (var r in piso.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        // El piso 1 se apoya en y = 0; los demas los coloca el llamador.
        if (indice == 0)
        {
            Physics.SyncTransforms();
            Bounds b = LimitesDe(piso.transform);
            piso.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
        }

        return piso.transform;
    }

    private static GameObject Instanciar(GameObject asset, Transform padre, string nombre)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        go.name = nombre;
        go.transform.SetParent(padre, false);
        return go;
    }

    private static void Normalizar(Transform t)
    {
        // Exactamente -90 en X: el modelo viene de pie, con el relieve en +Z.
        t.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        t.localScale = new Vector3(EscalaHorizontal, EscalaHorizontal, EscalaVertical);
        t.localPosition = Vector3.zero;
    }

    // ------------------------------------------------------------------
    // Medidas
    // ------------------------------------------------------------------
    private static Bounds LimitesDe(Transform raiz)
    {
        var rs = raiz.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(raiz.position, Vector3.one);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    /// <summary>Altura de la cara pisable, por la mediana de una rejilla de rayos.</summary>
    private static float DetectarAlturaDeLosa(Bounds b)
    {
        var impactos = new List<float>();
        float arriba = b.max.y + 5f;
        float largo = b.size.y + 10f;

        for (int ix = 0; ix < 40; ix++)
        {
            float x = Mathf.Lerp(b.min.x, b.max.x, (ix + 0.5f) / 40f);
            for (int iz = 0; iz < 40; iz++)
            {
                float z = Mathf.Lerp(b.min.z, b.max.z, (iz + 0.5f) / 40f);
                if (Physics.Raycast(new Vector3(x, arriba, z), Vector3.down,
                                    out RaycastHit hit, largo))
                {
                    impactos.Add(hit.point.y);
                }
            }
        }

        if (impactos.Count == 0) return b.min.y;
        impactos.Sort();
        return impactos[impactos.Count / 2];
    }

    /// <summary>Los puntos con mas despeje alrededor, de mayor a menor.</summary>
    private static List<Vector3> PuntosMasAbiertos(Bounds b, int cuantos)
    {
        float suelo = DetectarAlturaDeLosa(b);
        int nx = Mathf.Max(2, Mathf.CeilToInt(b.size.x / PasoDeRejilla));
        int nz = Mathf.Max(2, Mathf.CeilToInt(b.size.z / PasoDeRejilla));

        var libre = new bool[nx, nz];
        float arriba = b.max.y + 5f;
        float largo = b.size.y + 10f;

        for (int ix = 0; ix < nx; ix++)
        {
            float x = b.min.x + (ix + 0.5f) * PasoDeRejilla;
            for (int iz = 0; iz < nz; iz++)
            {
                float z = b.min.z + (iz + 0.5f) * PasoDeRejilla;
                if (Physics.Raycast(new Vector3(x, arriba, z), Vector3.down,
                                    out RaycastHit hit, largo))
                {
                    libre[ix, iz] = Mathf.Abs(hit.point.y - suelo) < 0.5f;
                }
            }
        }

        // Transformada de distancia por dos pasadas: cuantas celdas hay hasta
        // el obstaculo mas cercano. El maximo es la zona mas despejada.
        var dist = new float[nx, nz];
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                dist[ix, iz] = libre[ix, iz] ? 1e9f : 0f;

        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                if (libre[ix, iz])
                {
                    float m = dist[ix, iz];
                    if (ix > 0) m = Mathf.Min(m, dist[ix - 1, iz] + 1f);
                    if (iz > 0) m = Mathf.Min(m, dist[ix, iz - 1] + 1f);
                    dist[ix, iz] = m;
                }
        for (int ix = nx - 1; ix >= 0; ix--)
            for (int iz = nz - 1; iz >= 0; iz--)
                if (libre[ix, iz])
                {
                    float m = dist[ix, iz];
                    if (ix < nx - 1) m = Mathf.Min(m, dist[ix + 1, iz] + 1f);
                    if (iz < nz - 1) m = Mathf.Min(m, dist[ix, iz + 1] + 1f);
                    dist[ix, iz] = m;
                }

        var candidatos = new List<(float d, Vector3 p)>();
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                if (libre[ix, iz] && dist[ix, iz] > 1.5f)
                {
                    candidatos.Add((dist[ix, iz], new Vector3(
                        b.min.x + (ix + 0.5f) * PasoDeRejilla,
                        suelo,
                        b.min.z + (iz + 0.5f) * PasoDeRejilla)));
                }

        candidatos.Sort((a, c) => c.d.CompareTo(a.d));

        var salida = new List<Vector3>();
        foreach (var (d, p) in candidatos)
        {
            Vector3 pies = p + Vector3.up * 0.35f;
            Vector3 cabeza = p + Vector3.up * 1.65f;
            if (Physics.CheckCapsule(pies, cabeza, 0.32f)) continue;

            bool muyCerca = false;
            foreach (Vector3 q in salida)
                if (Vector3.Distance(q, p) < 3f) { muyCerca = true; break; }
            if (muyCerca) continue;

            salida.Add(p);
            if (salida.Count >= cuantos) break;
        }
        return salida;
    }

    // ------------------------------------------------------------------
    // Ascensor
    // ------------------------------------------------------------------
    /// <summary>True si en ese punto del suelo cabe la cápsula del jugador.</summary>
    private static bool CabeUnaPersona(Vector3 suelo)
    {
        Physics.SyncTransforms();
        return !Physics.CheckCapsule(suelo + Vector3.up * 0.40f, suelo + Vector3.up * 1.65f,
                                     0.32f, ~0, QueryTriggerInteraction.Ignore);
    }

    private static void CrearAscensor(Transform piso, Vector3 posicion, int indice,
                                      Material material, float yaw)
    {
        var raiz = new GameObject($"Ascensor_Piso{indice + 1}");
        raiz.transform.SetParent(piso, true);
        raiz.transform.SetPositionAndRotation(posicion, Quaternion.Euler(0f, yaw, 0f));

        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuerpo.name = "Cubo";
        cuerpo.transform.SetParent(raiz.transform, false);
        cuerpo.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        cuerpo.transform.localScale = new Vector3(1.2f, 2.2f, 0.35f);
        if (material != null) cuerpo.GetComponent<Renderer>().sharedMaterial = material;

        var zona = new GameObject("Zona");
        zona.transform.SetParent(raiz.transform, false);
        zona.transform.localPosition = new Vector3(0f, 1.0f, 0f);

        var caja = zona.AddComponent<BoxCollider>();
        caja.isTrigger = true;
        caja.size = new Vector3(3.5f, 2.4f, 3.5f);

        zona.AddComponent<ElevatorTrigger>();
    }

    private static Material CrearMaterialAscensor()
    {
        var existente = AssetDatabase.LoadAssetAtPath<Material>(RutaMatAscensor);
        if (existente != null) return existente;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) return null;

        var m = new Material(sh) { name = "Mat_Ascensor" };
        m.SetColor("_BaseColor", new Color(0.95f, 0.72f, 0.15f));
        m.SetFloat("_Smoothness", 0.45f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(0.55f, 0.38f, 0.05f));

        AssetDatabase.CreateAsset(m, RutaMatAscensor);
        return m;
    }

    // ------------------------------------------------------------------
    // Escena
    // ------------------------------------------------------------------
    private static GameObject CargarModeloMeshy()
    {
        // Por GUID y no por ruta literal: el nombre del archivo lleva tilde y
        // una ruta escrita a mano es una fuente de fallos tonta.
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CarpetaModelos }))
        {
            string ruta = AssetDatabase.GUIDToAssetPath(guid);
            if (ruta.Contains("Meshy") && ruta.EndsWith(".obj"))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            }
        }
        return null;
    }

    private static Material CargarMaterialEntorno()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(RutaMaterial);
        if (m == null)
        {
            Debug.LogWarning("[Edificio] No encontre " + RutaMaterial +
                             ". Corre 'MovU > Estilizar entorno > Limpio (low-poly)' " +
                             "para crearlo y vuelve a construir.");
        }
        return m;
    }

    private static Transform BuscarJugador()
    {
        var pc = Object.FindFirstObjectByType<PlayerController>();
        return pc != null ? pc.transform : null;
    }

    private static void ColocarJugador(Transform jugador, Vector3 suelo, float yaw)
    {
        var cc = jugador.GetComponent<CharacterController>();
        float alto = cc != null ? cc.height : 1.8f;

        if (cc != null) cc.enabled = false;
        jugador.position = suelo + Vector3.up * (alto * 0.5f + 0.1f);
        jugador.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;
    }

    /// <summary>
    /// Borra lo que generaron los menus de MovU, y NADA mas.
    ///
    /// La version anterior borraba "toda malla de mas de 5.000 vertices que no
    /// fuera del edificio", y eso se llevaba por delante cualquier cosa que el
    /// equipo hubiera puesto a mano. Ahora la regla es explicita: se borra lo
    /// que lleva GeneradoPorMovU. Lo que no lo lleva se reporta y se pregunta,
    /// nunca se destruye en silencio.
    ///
    /// Devuelve false si el usuario cancela la operacion.
    /// </summary>
    private static bool LimpiarEscena(bool interactivo)
    {
        // 1. Lo marcado: es nuestro, se borra sin preguntar.
        foreach (var marca in Object.FindObjectsByType<GeneradoPorMovU>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Al destruir un padre marcado, sus hijos marcados quedan nulos.
            if (marca == null) continue;
            Undo.DestroyObjectImmediate(marca.gameObject);
        }

        var cubo = GameObject.Find("Meter_Cube");
        if (cubo != null) Undo.DestroyObjectImmediate(cubo);

        // 2. Geometria grande sin marcar: escenas armadas antes de que
        //    existiera el marcador. Puede ser basura de un intento anterior o
        //    puede ser trabajo de alguien. No se adivina: se pregunta.
        List<GameObject> sueltos = GeometriaSueltaSinMarcar();
        if (sueltos.Count == 0) return true;

        var nombres = new List<string>();
        foreach (GameObject go in sueltos) nombres.Add(go.name);
        string lista = string.Join("\n  - ", nombres);

        if (!interactivo)
        {
            Debug.LogWarning("[Edificio] Hay geometria grande sin marcar en la escena; " +
                             "la dejo como esta para no borrar trabajo de nadie:\n  - " +
                             lista + "\n\nSi es basura de un intento anterior, borrala a " +
                             "mano o corre el menu del edificio por separado.");
            return true;
        }

        int respuesta = EditorUtility.DisplayDialogComplex(
            "MovU",
            "Encontre geometria grande en la escena que NO la generaron los menus " +
            "de MovU:\n\n  - " + lista + "\n\n" +
            "Si son sobras de un intento anterior, lo limpio. Si es trabajo de " +
            "ustedes, lo dejo donde esta (y entonces convendria moverlo a la raiz " +
            "'Contenido', que nunca se toca).",
            "Borrarlos",
            "Cancelar",
            "Conservarlos");

        if (respuesta == 1) return false;                 // Cancelar
        if (respuesta == 2)                               // Conservarlos
        {
            Debug.LogWarning("[Edificio] Conservo la geometria sin marcar. Si queda " +
                             "encima del edificio nuevo, muevela o borrala a mano.");
            return true;
        }

        foreach (GameObject go in sueltos)
        {
            if (go != null) Undo.DestroyObjectImmediate(go);
        }
        return true;
    }

    /// <summary>
    /// Raices con mallas grandes que ni llevan el marcador ni cuelgan de
    /// 'Contenido'. Es decir: candidatas a ser sobras, pero sin certeza.
    /// </summary>
    private static List<GameObject> GeometriaSueltaSinMarcar()
    {
        var salida = new List<GameObject>();

        foreach (var mf in Object.FindObjectsByType<MeshFilter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mf == null || mf.sharedMesh == null) continue;
            if (mf.sharedMesh.vertexCount < VerticesMallaGrande) continue;

            Transform raiz = mf.transform;
            while (raiz.parent != null) raiz = raiz.parent;

            if (raiz.name == NombreContenido) continue;
            if (GeneradoPorMovU.EstaMarcado(raiz.gameObject)) continue;
            if (salida.Contains(raiz.gameObject)) continue;

            salida.Add(raiz.gameObject);
        }
        return salida;
    }

    /// <summary>
    /// La raiz 'Contenido' y un hijo por piso. Es la zona segura: BuildingSetup
    /// no la borra nunca, y el FloorManager enciende Contenido/Piso_N junto con
    /// su piso, asi que lo que se ponga ahi respeta la carga por piso.
    /// </summary>
    private static List<Transform> AsegurarContenido(int cantidadPisos)
    {
        GameObject raiz = GameObject.Find(NombreContenido);
        if (raiz == null)
        {
            raiz = new GameObject(NombreContenido);
            Undo.RegisterCreatedObjectUndo(raiz, "Crear contenido");
        }

        // Por si alguien la marco por error: el marcador la condenaria a
        // borrarse en la siguiente reconstruccion.
        var marcaSobrante = raiz.GetComponent<GeneradoPorMovU>();
        if (marcaSobrante != null) Undo.DestroyObjectImmediate(marcaSobrante);

        var salida = new List<Transform>();
        for (int i = 0; i < cantidadPisos; i++)
        {
            string nombre = $"Piso_{i + 1}";
            Transform hijo = raiz.transform.Find(nombre);
            if (hijo == null)
            {
                var go = new GameObject(nombre);
                go.transform.SetParent(raiz.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Crear contenido");
                hijo = go.transform;
            }
            hijo.gameObject.SetActive(true);
            salida.Add(hijo);
        }
        return salida;
    }

    /// <summary>
    /// Los valores del material que dependen de como quedo el edificio. Estan
    /// aparte porque EnvironmentStyler tiene que poder reaplicarlos: si se
    /// estiliza despues de construir, el preset pisa _FloorLevel y el zocalo
    /// queda bien en el piso 1 y mal en el 2 y el 3.
    /// </summary>
    internal static void AjustarMaterial(Material material, float nivelDelPiso,
                                         float separacion, float alturaLosa)
    {
        if (material == null) return;

        bool apilado = separacion > 0.01f;

        if (material.HasProperty("_FloorLevel"))
            material.SetFloat("_FloorLevel", nivelDelPiso);

        if (material.HasProperty("_FloorSpacing"))
            material.SetFloat("_FloorSpacing", apilado ? separacion : 0f);

        // Con la losa de techo puesta, el sol ya no entra: el interior se
        // apagaria. El techo emite un poco y hace de luminaria, que es mucho
        // mas barato que sembrar luces reales por todo el piso.
        if (material.HasProperty("_CeilingEmission"))
            material.SetColor("_CeilingEmission", apilado ? EmisionTecho : Color.black);

        // El degradado del muro tiene que abarcar el muro entero. Con un valor
        // fijo de 3 m y el techo a 5,6 m, los dos metros de arriba quedaban de
        // un solo color plano.
        if (apilado && material.HasProperty("_WallGradientHeight"))
            material.SetFloat("_WallGradientHeight", Mathf.Max(0.5f, separacion - alturaLosa));

        EditorUtility.SetDirty(material);
    }

    /// <summary>
    /// Reaplica lo anterior leyendo el edificio que ya esta en la escena.
    /// Devuelve false si no hay edificio (y entonces no hay nada que ajustar).
    /// </summary>
    internal static bool AjustarMaterialSegunEscena(Material material)
    {
        if (material == null) return false;

        var gestor = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (gestor == null || gestor.CantidadDePisos == 0) return false;

        // Altura de la losa sobre la BASE del piso (ver la nota en ConstruirInterno).
        float sueloPiso1 = gestor.AlturaDelSuelo(0);
        Transform piso1 = gestor.Piso(0);
        float baseDelPiso = piso1 != null ? LimitesDe(piso1).min.y : sueloPiso1;

        AjustarMaterial(material,
                        sueloPiso1,
                        gestor.SeparacionEntrePisos,
                        Mathf.Max(0f, sueloPiso1 - baseDelPiso));
        return true;
    }

    private static void MarcarEstatico(GameObject edificio)
    {
        // Occluder + Occludee habilita el Occlusion Culling horneado; Batching
        // junta los dibujados. No se marca ContributeGI: el modelo no tiene UVs
        // de lightmap y solo produciria avisos.
        var banderas = StaticEditorFlags.OccluderStatic
                       | StaticEditorFlags.OccludeeStatic
                       | StaticEditorFlags.BatchingStatic;

        foreach (var r in edificio.GetComponentsInChildren<Renderer>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, banderas);
        }
    }

    /// <summary>
    /// Sube la luz ambiental. Con un piso a cielo abierto la direccional hacia
    /// casi todo el trabajo; encerrado, el ambiente es lo unico que queda.
    /// </summary>
    internal static void AjustarLuzInterior()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.46f, 0.48f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.30f, 0.32f);
        RenderSettings.ambientIntensity = 1f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.58f, 0.60f, 0.64f);
        RenderSettings.fogStartDistance = 18f;
        RenderSettings.fogEndDistance = 70f;
    }

    private static void Dialogo(string mensaje)
    {
        EditorUtility.DisplayDialog("MovU", mensaje, "Entendido");
    }

    /// <summary>Dialogo cuando lo corre una persona; consola cuando lo corre
    /// otro menu encadenado (los dialogos en cadena son insoportables).</summary>
    private static void Aviso(bool interactivo, string mensaje)
    {
        if (interactivo) Dialogo(mensaje);
        else Debug.LogError("[Edificio] " + mensaje);
    }
}
