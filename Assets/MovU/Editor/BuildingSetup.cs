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
// Es idempotente: vuelve a construir el edificio desde cero cada vez.
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
    private const float PasoDeRejilla = 0.5f;

    // ------------------------------------------------------------------
    [MenuItem("MovU/Edificio/Construir edificio de 3 pisos", priority = 10)]
    public static void Construir()
    {
        GameObject modeloAsset = CargarModeloMeshy();
        GameObject rellenoAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RutaRelleno);

        if (modeloAsset == null)
        {
            Dialogo("No encuentro el modelo de Meshy en " + CarpetaModelos + ".");
            return;
        }
        if (rellenoAsset == null)
        {
            Dialogo("Falta " + RutaRelleno + ".\n\nGeneralo con:\n" +
                    "    python3 Tools/tapar_corona.py\n\n" +
                    "Sin el relleno, al apilar los pisos se ve el de arriba por " +
                    "encima de los muros que quedaron cortos.");
            return;
        }

        Transform jugador = BuscarJugador();
        if (jugador == null)
        {
            Dialogo("No hay un Player en la escena.\n\n" +
                    "Corre primero 'MovU > Preparar plano Meshy jugable' para que " +
                    "lo construya, y vuelve a este menu.");
            return;
        }

        LimpiarEscena();

        var edificio = new GameObject(NombreEdificio);
        Undo.RegisterCreatedObjectUndo(edificio, "Construir edificio");

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
            Dialogo("No encontre ninguna zona abierta donde quepa el personaje.");
            return;
        }

        Vector3 sitioAscensor = abiertos[0];
        Vector3 aparicion = sitioAscensor;
        foreach (Vector3 p in abiertos)
        {
            float d = Vector3.Distance(p, sitioAscensor);
            if (d > 2.5f && d < 9f) { aparicion = p; break; }
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
                          i, matAscensor);
        }

        // --- Jugador ----------------------------------------------------
        ColocarJugador(jugador, new Vector3(aparicion.x,
                                            piso1.position.y + alturaLosa,
                                            aparicion.z));

        // --- Gestor de pisos --------------------------------------------
        var gestor = edificio.AddComponent<FloorManager>();
        gestor.Configurar(pisos, alturaLosa, jugador, 0);
        EditorUtility.SetDirty(gestor);

        // --- Material: la altura se mide dentro de cada piso -------------
        if (material != null && material.HasProperty("_FloorSpacing"))
        {
            material.SetFloat("_FloorLevel", piso1.position.y + alturaLosa);
            material.SetFloat("_FloorSpacing", separacion);

            // Con la losa de techo puesta, el sol ya no entra: el interior se
            // apagaria. El techo emite un poco y hace de luminaria, que es
            // mucho mas barato que sembrar luces reales por todo el piso.
            if (material.HasProperty("_CeilingEmission"))
            {
                material.SetColor("_CeilingEmission", new Color(0.40f, 0.40f, 0.38f));
            }

            // El degradado del muro tiene que abarcar el muro entero. Con un
            // valor fijo de 3 m y el techo a 5,6 m, los dos metros de arriba
            // quedaban de un solo color plano.
            if (material.HasProperty("_WallGradientHeight"))
            {
                material.SetFloat("_WallGradientHeight", separacion - alturaLosa);
            }
            EditorUtility.SetDirty(material);
        }

        AjustarLuzInterior();

        MarcarEstatico(edificio);
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(edificio.scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Edificio] Listo: {CantidadDePisos} pisos, ascensor en " +
                  $"{sitioAscensor}, aparicion en {aparicion}. " +
                  "Falta hornear el Occlusion Culling: " +
                  "Window > Rendering > Occlusion Culling > Bake.");
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
    private static void CrearAscensor(Transform piso, Vector3 posicion, int indice,
                                      Material material)
    {
        var raiz = new GameObject($"Ascensor_Piso{indice + 1}");
        raiz.transform.SetParent(piso, true);
        raiz.transform.position = posicion;

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

    private static void ColocarJugador(Transform jugador, Vector3 suelo)
    {
        var cc = jugador.GetComponent<CharacterController>();
        float alto = cc != null ? cc.height : 1.8f;

        if (cc != null) cc.enabled = false;
        jugador.position = suelo + Vector3.up * (alto * 0.5f + 0.1f);
        jugador.rotation = Quaternion.identity;
        if (cc != null) cc.enabled = true;
    }

    private static void LimpiarEscena()
    {
        var previo = GameObject.Find(NombreEdificio);
        if (previo != null) Undo.DestroyObjectImmediate(previo);

        // Modelos sueltos de intentos anteriores: cualquier malla grande que no
        // sea hijo del edificio.
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // El bucle destruye objetos mientras recorre una copia de la lista:
            // hay que descartar los que ya no existen antes de tocarlos.
            if (mf == null) continue;
            if (mf.sharedMesh == null || mf.sharedMesh.vertexCount < 5000) continue;
            Transform raiz = mf.transform;
            while (raiz.parent != null) raiz = raiz.parent;
            if (raiz.name == NombreEdificio) continue;
            Undo.DestroyObjectImmediate(raiz.gameObject);
        }

        var cubo = GameObject.Find("Meter_Cube");
        if (cubo != null) Undo.DestroyObjectImmediate(cubo);
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
    private static void AjustarLuzInterior()
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
}
