using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ============================================================================
// MovUCapturas.cs — Fotos de revisión de un piso, sin dar Play
// ============================================================================
// MovU > Contenido > Capturar planta y vistas del piso 9 (Temp/MovUCapturas)
//   - planta_piso9.png: vista cenital ortográfica, sin techos ni el piso de arriba.
//   - vista_*.png: vistas a la altura de los ojos en sitios clave del piso 9.
// Sirve para comparar la planta con el plano y las vistas con las fotos.
// ============================================================================

public static class MovUCapturas
{
    private static readonly (string nombre, float u, float v, float yaw)[] Vistas =
    {
        ("lobby_ascensores", 0.78f, 0.935f, 180f),
        ("pasillo_desde_lobby", 0.55f, 0.90f, 180f),
        ("pasillo_centro", 0.54f, 0.55f, 180f),
        ("vestibulo_p9_03", 0.56f, 0.20f, 180f),
        ("paso_escalera", 0.62f, 0.13f, 0f),
        ("nicho_dti_prestamo", 0.53f, 0.50f, 30f),
    };

    private static readonly (string nombre, float u, float v, float yaw)[] Vistas13 =
    {
        ("lobby_ascensores", 0.78f, 0.935f, 180f),
        ("pasillo_desde_lobby", 0.55f, 0.90f, 180f),
        ("pasillo_centro", 0.54f, 0.55f, 180f),
        ("vestibulo_p13_3", 0.56f, 0.20f, 180f),
        ("puertas_robotica", 0.53f, 0.66f, 270f),
        ("lab_robotica", 0.30f, 0.60f, 250f),
        ("lab_bioingenieria", 0.28f, 0.42f, 200f),
    };

    [MenuItem("MovU/Contenido/Capturar planta y vistas del piso 9 (Temp)")]
    public static void Capturar() => Capturar(FloorManager.PlantaBase, "piso9", Vistas);

    private static readonly (string nombre, float u, float v, float yaw)[] VistasGenerico =
    {
        ("lobby_ascensores", 0.78f, 0.935f, 180f),
        ("nicho_cerrado", 0.53f, 0.50f, 30f),
        ("puertas_aulas", 0.52f, 0.62f, 300f),
        ("puerta_escalera", 0.66f, 0.195f, 90f),
    };

    [MenuItem("MovU/Contenido/Capturar planta y vistas de un piso genérico (Temp)")]
    public static void CapturarGenerico() => Capturar(FloorManager.PlantaGenerica, "generico", VistasGenerico);

    [MenuItem("MovU/Contenido/Capturar planta y vistas del piso 13 (Temp)")]
    public static void Capturar13() => Capturar("piso13", "piso13", Vistas13);

    private static void Capturar(string planta, string sufijo, (string nombre, float u, float v, float yaw)[] vistas)
    {
        var fm = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (fm == null) { Debug.LogWarning("[Capturas] No hay edificio."); return; }
        int indice = -1;
        for (int i = 0; i < fm.CantidadDePisos; i++) if (fm.PlantaDe(i) == planta) { indice = i; break; }
        if (indice < 0) { Debug.LogWarning($"[Capturas] No hay piso con la planta '{planta}'."); return; }
        Transform piso = fm.Piso(indice);
        PlanoDePlanta plano = PlanoDePlanta.Medir(piso);
        float suelo = fm.AlturaDelSuelo(indice);

        string carpeta = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "MovUCapturas");
        Directory.CreateDirectory(carpeta);

        // Solo el piso 9 encendido (y su contenido).
        var estados = new List<(GameObject, bool)>();
        for (int i = 0; i < fm.CantidadDePisos; i++)
        {
            bool este = i == indice;
            Recordar(estados, fm.Piso(i).gameObject, este);
            Transform c = fm.ContenidoDelPisoSiExiste(i);
            if (c != null) Recordar(estados, c.gameObject, este);
        }

        var go = new GameObject("CamaraDeCaptura");
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        try
        {
            // --- Planta: sin techos, bandejas ni luces del techo -----------
            var apagados = new List<Renderer>();
            foreach (var r in piso.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n.StartsWith("Techo") || n == "Luz" || n == "Metal" || n == "TuboOscuro" || n.StartsWith("Caja_"))
                {
                    if (r.enabled) { r.enabled = false; apagados.Add(r); }
                }
            }
            Transform cont = fm.ContenidoDelPisoSiExiste(indice);
            if (cont != null)
                foreach (var r in cont.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled && (r.gameObject.name.StartsWith("Caja_") || r.gameObject.name == "Cielo raso"))
                    { r.enabled = false; apagados.Add(r); }

            cam.orthographic = true;
            float ancho = plano.xMax - plano.xMin, largo = plano.zMax - plano.zMin;
            cam.orthographicSize = ancho * 0.5f + 0.5f;
            cam.aspect = (largo + 1f) / (ancho + 1f);
            go.transform.position = new Vector3((plano.xMin + plano.xMax) * 0.5f, suelo + 30f, (plano.zMin + plano.zMax) * 0.5f);
            // Mirando hacia abajo, con v (z) hacia la derecha de la imagen y u (x) hacia abajo, como el diagrama.
            go.transform.rotation = Quaternion.Euler(90f, -90f, 0f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 60f;
            Guardar(cam, Path.Combine(carpeta, $"planta_{sufijo}.png"), 3200, Mathf.RoundToInt(3200 / cam.aspect));
            foreach (var r in apagados) r.enabled = true;

            // --- Vistas a la altura de los ojos ------------------------------
            cam.orthographic = false;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.Skybox;
            foreach (var v in vistas)
            {
                go.transform.position = plano.AMundo(v.u, v.v, suelo + 1.65f);
                go.transform.rotation = Quaternion.Euler(4f, v.yaw, 0f);
                Guardar(cam, Path.Combine(carpeta, $"{sufijo}_vista_{v.nombre}.png"), 1600, 900);
            }
        }
        finally
        {
            Object.DestroyImmediate(go);
            foreach (var (obj, antes) in estados) obj.SetActive(antes);
        }
        Debug.Log("[Capturas] Listo en " + carpeta);
    }

    private static void Recordar(List<(GameObject, bool)> estados, GameObject obj, bool activo)
    {
        estados.Add((obj, obj.activeSelf));
        obj.SetActive(activo);
    }

    private static void Guardar(Camera cam, string ruta, int ancho, int alto)
    {
        var rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
        tex.Apply();
        File.WriteAllBytes(ruta, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
