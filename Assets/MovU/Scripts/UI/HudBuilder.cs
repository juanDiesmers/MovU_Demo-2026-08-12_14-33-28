using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// ============================================================================
// HudBuilder.cs — Arma el HUD de la escena del edificio en tiempo de ejecución
// ============================================================================
// Igual que el panel del ascensor: no hay que construir nada a mano en la
// escena ni arrastrar referencias. Crea el lienzo, los textos y el panel de
// resultados, y se los entrega al HUDController.
//
// Todo el HUD es un solo Canvas en Screen Space - Overlay. Ningún texto recibe
// clics (raycastTarget apagado), así que el EventSystem no tiene nada que
// recorrer.
// ============================================================================

public static class HudBuilder
{
    private static readonly Color Tinta = new Color(1f, 1f, 1f, 0.96f);
    private static readonly Color Tenue = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color Fondo = new Color(0.07f, 0.08f, 0.10f, 0.78f);

    public static HUDController Construir()
    {
        var raiz = new GameObject("HUD");

        var lienzo = raiz.AddComponent<Canvas>();
        lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
        lienzo.sortingOrder = 100;                  // por debajo del panel del ascensor (200)

        var escala = raiz.AddComponent<CanvasScaler>();
        escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        escala.referenceResolution = new Vector2(1920f, 1080f);
        escala.matchWidthOrHeight = 0.5f;

        AsegurarEventSystem();

        // --- Arriba a la izquierda: misión ------------------------------
        GameObject cajaMision = Caja("Mision", raiz.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                     new Vector2(36f, -32f), new Vector2(760f, 128f));
        TextMeshProUGUI txtContador = Texto("Contador", cajaMision.transform, 24f, Tenue,
                                            TextAlignmentOptions.TopLeft);
        Colocar(txtContador.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(22f, -46f), new Vector2(-22f, -10f));

        TextMeshProUGUI txtObjetivo = Texto("Enunciado", cajaMision.transform, 30f, Tinta,
                                            TextAlignmentOptions.TopLeft);
        txtObjetivo.textWrappingMode = TextWrappingModes.Normal;
        Colocar(txtObjetivo.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(22f, 12f), new Vector2(-22f, -46f));

        // --- Arriba a la derecha: medidas -------------------------------
        GameObject cajaDatos = Caja("Datos", raiz.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                                    new Vector2(-36f, -32f), new Vector2(360f, 176f));
        TextMeshProUGUI txtTiempo = Linea("Tiempo", cajaDatos.transform, 0, 40f, Tinta);
        TextMeshProUGUI txtDistancia = Linea("Distancia", cajaDatos.transform, 1, 24f, Tinta);
        TextMeshProUGUI txtGuia = Linea("Guia", cajaDatos.transform, 2, 24f, Tinta);
        TextMeshProUGUI txtPiso = Linea("Piso", cajaDatos.transform, 3, 24f, Tenue);
        txtTiempo.text = "00:00";
        txtDistancia.text = "";

        // --- Abajo: aviso de interacción y subtítulo --------------------
        GameObject cajaAviso = Caja("Aviso", raiz.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                    new Vector2(0f, 218f), new Vector2(560f, 56f));
        TextMeshProUGUI txtAviso = Texto("Texto", cajaAviso.transform, 26f, Tinta,
                                         TextAlignmentOptions.Center);
        Estirar(txtAviso.rectTransform, 12f);

        GameObject cajaSub = Caja("Subtitulo", raiz.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                  new Vector2(0f, 64f), new Vector2(1180f, 116f));
        TextMeshProUGUI txtSub = Texto("Texto", cajaSub.transform, 28f, Tinta,
                                       TextAlignmentOptions.Center);
        txtSub.textWrappingMode = TextWrappingModes.Normal;
        txtSub.richText = true;
        Estirar(txtSub.rectTransform, 20f);

        // --- Centro: resultados -----------------------------------------
        GameObject panel = Caja("Resultados", raiz.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                Vector2.zero, new Vector2(820f, 380f));
        panel.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.10f, 0.93f);

        TextMeshProUGUI resTitulo = Renglon("Titulo", panel.transform, -34f, 40f, new Color(1f, 0.86f, 0.35f));
        TextMeshProUGUI resTiempo = Renglon("Tiempo", panel.transform, -116f, 30f, Tinta);
        TextMeshProUGUI resDistancia = Renglon("Distancia", panel.transform, -166f, 30f, Tinta);
        TextMeshProUGUI resMejor = Renglon("Mejor", panel.transform, -216f, 30f, Tinta);
        TextMeshProUGUI resPista = Renglon("Pista", panel.transform, -300f, 24f, Tenue);

        var hud = raiz.AddComponent<HUDController>();
        hud.Configure(txtObjetivo, txtTiempo, txtDistancia, txtGuia,
                      panel, resTitulo, resTiempo, resDistancia, resMejor, resPista,
                      txtContador, txtPiso,
                      cajaAviso, txtAviso,
                      cajaSub, txtSub);

        panel.SetActive(false);
        cajaAviso.SetActive(false);
        cajaSub.SetActive(false);
        return hud;
    }

    public static void AsegurarEventSystem()
    {
        if (EventSystem.current != null) return;
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        // Input System nuevo: con StandaloneInputModule los botones no reciben nada.
        go.AddComponent<InputSystemUIInputModule>();
    }

    // ------------------------------------------------------------------
    private static GameObject Caja(string nombre, Transform padre, Vector2 ancla, Vector2 pivote,
                                   Vector2 posicion, Vector2 tamano)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = ancla;
        rt.pivot = pivote;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;

        var img = go.AddComponent<Image>();
        img.color = Fondo;
        img.raycastTarget = false;
        return go;
    }

    private static TextMeshProUGUI Texto(string nombre, Transform padre, float tamano, Color color,
                                         TextAlignmentOptions alineacion)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(padre, false);

        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = tamano;
        t.color = color;
        t.alignment = alineacion;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.text = "";
        return t;
    }

    /// <summary>Una línea de la caja de datos, alineada a la derecha.</summary>
    private static TextMeshProUGUI Linea(string nombre, Transform padre, int fila, float tamano, Color color)
    {
        TextMeshProUGUI t = Texto(nombre, padre, tamano, color, TextAlignmentOptions.Right);
        float y = fila == 0 ? -10f : -(60f + (fila - 1) * 36f);
        float alto = fila == 0 ? 50f : 34f;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-40f, alto);
        return t;
    }

    /// <summary>Un renglón centrado del panel de resultados.</summary>
    private static TextMeshProUGUI Renglon(string nombre, Transform padre, float y, float tamano, Color color)
    {
        TextMeshProUGUI t = Texto(nombre, padre, tamano, color, TextAlignmentOptions.Center);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-48f, tamano + 16f);
        return t;
    }

    private static void Colocar(RectTransform rt, Vector2 anclaMin, Vector2 anclaMax,
                                Vector2 margenMin, Vector2 margenMax)
    {
        rt.anchorMin = anclaMin;
        rt.anchorMax = anclaMax;
        rt.offsetMin = margenMin;
        rt.offsetMax = margenMax;
    }

    private static void Estirar(RectTransform rt, float margen)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margen, 0f);
        rt.offsetMax = new Vector2(-margen, 0f);
    }
}
