using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ============================================================================
// MovURevision.cs — Cámara de revisión de un piso, sin dar Play
// ============================================================================
// MovU > Revision > Planta del piso N: deja encendido solo ese piso (y su
// contenido), apaga techos y luminarias y pone la cámara 'CamaraRevision'
// mirando hacia abajo, con la planta orientada como los planos de evacuación
// (ascensores abajo, el extremo de las oficinas arriba). Sirve para capturarla
// desde el puente de Unity y compararla con la foto del plano.
// MovU > Revision > Terminar: deja todo como estaba y borra la cámara.
// ============================================================================

public static class MovURevision
{
    private const string NombreCamara = "CamaraRevision";
    private static readonly List<(GameObject obj, bool activo)> estados = new List<(GameObject, bool)>();
    private static readonly List<Renderer> apagados = new List<Renderer>();

    [MenuItem("MovU/Revision/Planta del piso 4")] private static void P4() => Planta(4);
    [MenuItem("MovU/Revision/Planta del piso 5")] private static void P5() => Planta(5);
    [MenuItem("MovU/Revision/Planta del piso 6")] private static void P6() => Planta(6);
    [MenuItem("MovU/Revision/Planta del piso 7")] private static void P7() => Planta(7);
    [MenuItem("MovU/Revision/Planta del piso 8")] private static void P8() => Planta(8);
    [MenuItem("MovU/Revision/Planta del piso 9")] private static void P9() => Planta(9);
    [MenuItem("MovU/Revision/Planta del piso 10")] private static void P10() => Planta(10);
    [MenuItem("MovU/Revision/Planta del piso 11")] private static void P11() => Planta(11);
    [MenuItem("MovU/Revision/Planta del piso 12")] private static void P12() => Planta(12);
    [MenuItem("MovU/Revision/Planta del piso 13")] private static void P13() => Planta(13);

    public static void Planta(int numero)
    {
        Terminar();
        var fm = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (fm == null) { Debug.LogWarning("[Revisión] No hay edificio."); return; }
        int indice = -1;
        string planta = numero == 9 ? FloorManager.PlantaBase : "piso" + numero;
        for (int i = 0; i < fm.CantidadDePisos; i++) if (fm.PlantaDe(i) == planta) indice = i;
        if (indice < 0) { Debug.LogWarning($"[Revisión] No hay piso {numero}."); return; }

        for (int i = 0; i < fm.CantidadDePisos; i++)
        {
            Recordar(fm.Piso(i).gameObject, i == indice);
            Transform c = fm.ContenidoDelPisoSiExiste(i);
            if (c != null) Recordar(c.gameObject, i == indice);
        }
        Transform piso = fm.Piso(indice);
        foreach (var r in piso.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name;
            if ((n.StartsWith("Techo") || n == "Luz" || n == "Metal" || n == "TuboOscuro" || n.StartsWith("Caja_")) && r.enabled)
            { r.enabled = false; apagados.Add(r); }
        }

        PlanoDePlanta plano = PlanoDePlanta.Medir(piso);
        float suelo = fm.AlturaDelSuelo(indice);
        var go = new GameObject(NombreCamara);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 60f;
        cam.depth = 50f;
        Vector3 centro = plano.AMundo(0.5f, 0.5f, suelo + 30f);
        // Arriba en la imagen = hacia v pequeño (el extremo de las oficinas), como en los planos.
        Vector3 haciaArriba = (plano.AMundo(0.5f, 0f, suelo) - plano.AMundo(0.5f, 1f, suelo)).normalized;
        go.transform.position = centro;
        go.transform.rotation = Quaternion.LookRotation(Vector3.down, haciaArriba);
        float largo = Vector3.Distance(plano.AMundo(0.5f, 0f, 0f), plano.AMundo(0.5f, 1f, 0f));
        cam.orthographicSize = largo * 0.5f + 1f;
        Debug.Log($"[Revisión] Piso {numero} listo para capturar con '{NombreCamara}'. " +
                  $"u=0 queda a la {(Vector3.Dot(plano.AMundo(0f, 0.5f, 0f) - plano.AMundo(1f, 0.5f, 0f), go.transform.right) > 0 ? "derecha" : "izquierda")}.");
    }

    [MenuItem("MovU/Revision/Terminar")]
    public static void Terminar()
    {
        foreach (var (obj, activo) in estados) if (obj != null) obj.SetActive(activo);
        foreach (var r in apagados) if (r != null) r.enabled = true;
        estados.Clear();
        apagados.Clear();
        var cam = GameObject.Find(NombreCamara);
        if (cam != null) Object.DestroyImmediate(cam);
    }

    private static void Recordar(GameObject obj, bool activo)
    {
        estados.Add((obj, obj.activeSelf));
        obj.SetActive(activo);
    }
}
