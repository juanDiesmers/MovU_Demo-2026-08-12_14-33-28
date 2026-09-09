using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// ============================================================================
// ElevatorPanel.cs — Panel de seleccion de piso
// ============================================================================
// Se construye solo en tiempo de ejecucion: no hay que armar nada a mano en la
// escena ni arrastrar referencias. Basta con que exista un FloorManager.
//
// Dos formas de elegir piso, a proposito: los botones con el raton y las teclas
// 1..9. Los botones dependen del EventSystem y del raycaster; las teclas no.
// Si algo falla en la interfaz, el panel sigue siendo usable — que en una
// demostracion delante del director vale mas que la elegancia.
//
// Mientras esta abierto se apagan el movimiento y la mirada del jugador, y se
// libera el cursor. Al cerrar se restaura todo.
// ============================================================================

public class ElevatorPanel : MonoBehaviour
{
    private static ElevatorPanel instancia;

    private Canvas lienzo;
    private GameObject panel;
    private GameObject aviso;
    private TextMeshProUGUI textoTitulo;
    private readonly List<Button> botones = new List<Button>();

    private FloorManager pisos;
    private PlayerController jugador;
    private MouseLook mirada;

    public bool Abierto { get; private set; }

    // ------------------------------------------------------------------
    public static ElevatorPanel Obtener()
    {
        if (instancia != null) return instancia;

        var go = new GameObject("ElevatorPanel");
        instancia = go.AddComponent<ElevatorPanel>();
        instancia.Construir();
        return instancia;
    }

    private void Awake()
    {
        if (instancia == null) instancia = this;
    }

    // ------------------------------------------------------------------
    // Construccion de la interfaz
    // ------------------------------------------------------------------
    private void Construir()
    {
        AsegurarEventSystem();

        lienzo = gameObject.AddComponent<Canvas>();
        lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
        lienzo.sortingOrder = 200;

        var escala = gameObject.AddComponent<CanvasScaler>();
        escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        escala.referenceResolution = new Vector2(1920f, 1080f);
        escala.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        ConstruirAviso();

        panel = NuevoRect("Panel", transform);
        var fondo = panel.AddComponent<Image>();
        fondo.color = new Color(0.08f, 0.09f, 0.11f, 0.94f);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(460f, 320f);

        textoTitulo = NuevoTexto("Titulo", panel.transform, 34f, Color.white,
                                 TextAlignmentOptions.Center);
        var trt = textoTitulo.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -22f);
        trt.sizeDelta = new Vector2(-40f, 50f);

        panel.SetActive(false);
    }

    private void ConstruirAviso()
    {
        aviso = NuevoRect("Aviso", transform);
        var fondo = aviso.AddComponent<Image>();
        fondo.color = new Color(0f, 0f, 0f, 0.55f);
        var rt = aviso.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 150f);
        rt.sizeDelta = new Vector2(420f, 58f);

        var t = NuevoTexto("Texto", aviso.transform, 26f, Color.white,
                           TextAlignmentOptions.Center);
        t.text = "E  —  Ascensor";
        Estirar(t.rectTransform);

        aviso.SetActive(false);
    }

    private static void AsegurarEventSystem()
    {
        if (EventSystem.current != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        // El proyecto usa el Input System nuevo: con StandaloneInputModule los
        // botones no reciben nada y ademas salta una excepcion en consola.
        go.AddComponent<InputSystemUIInputModule>();
    }

    // ------------------------------------------------------------------
    private void ReconstruirBotones()
    {
        foreach (var b in botones)
        {
            if (b != null) Destroy(b.gameObject);
        }
        botones.Clear();

        int n = pisos.CantidadDePisos;
        const float alto = 58f;
        const float sep = 12f;

        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(460f, 110f + n * (alto + sep) + 40f);

        for (int i = 0; i < n; i++)
        {
            int destino = i;                     // copia para la clausura
            var go = NuevoRect($"Piso_{i + 1}", panel.transform);

            var img = go.AddComponent<Image>();
            bool actual = i == pisos.PisoActual;
            img.color = actual ? new Color(0.20f, 0.42f, 0.62f, 1f)
                               : new Color(0.18f, 0.19f, 0.22f, 1f);

            var brt = go.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.sizeDelta = new Vector2(-60f, alto);
            // Se listan de arriba hacia abajo, pero el piso 1 es el de abajo:
            // se invierte el orden para que el panel se lea como un ascensor.
            int fila = n - 1 - i;
            brt.anchoredPosition = new Vector2(0f, -(84f + fila * (alto + sep)));

            var texto = NuevoTexto("Texto", go.transform, 26f, Color.white,
                                   TextAlignmentOptions.Center);
            texto.text = actual ? $"Piso {i + 1}   (aqui estas)" : $"Piso {i + 1}";
            Estirar(texto.rectTransform);

            var boton = go.AddComponent<Button>();
            boton.targetGraphic = img;
            boton.onClick.AddListener(() => Elegir(destino));
            botones.Add(boton);
        }
    }

    // ------------------------------------------------------------------
    public void MostrarAviso(bool visible)
    {
        if (aviso == null) return;
        aviso.SetActive(visible && !Abierto);
    }

    public void Abrir(FloorManager gestor)
    {
        pisos = gestor;
        if (pisos == null || panel == null) return;

        textoTitulo.text = "¿A qué piso?";
        ReconstruirBotones();

        panel.SetActive(true);
        aviso.SetActive(false);
        Abierto = true;

        jugador = FindFirstObjectByType<PlayerController>();
        mirada = FindFirstObjectByType<MouseLook>();
        if (jugador != null)
        {
            jugador.SetInputEnabled(false);
            jugador.UnlockCursor();
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (mirada != null) mirada.SetLookEnabled(false);
    }

    public void Cerrar()
    {
        if (panel != null) panel.SetActive(false);
        Abierto = false;

        if (jugador != null)
        {
            jugador.SetInputEnabled(true);
            jugador.LockCursor();
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (mirada != null) mirada.SetLookEnabled(true);
    }

    private void Elegir(int destino)
    {
        if (pisos != null) pisos.LlevarAlPiso(destino);
        Cerrar();
    }

    // ------------------------------------------------------------------
    private void Update()
    {
        if (!Abierto || Keyboard.current == null || pisos == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cerrar();
            return;
        }

        // Atajos 1..9: no dependen del EventSystem ni del raton.
        var teclas = new[]
        {
            Keyboard.current.digit1Key, Keyboard.current.digit2Key,
            Keyboard.current.digit3Key, Keyboard.current.digit4Key,
            Keyboard.current.digit5Key, Keyboard.current.digit6Key,
            Keyboard.current.digit7Key, Keyboard.current.digit8Key,
            Keyboard.current.digit9Key,
        };
        int tope = Mathf.Min(pisos.CantidadDePisos, teclas.Length);
        for (int i = 0; i < tope; i++)
        {
            if (teclas[i].wasPressedThisFrame)
            {
                Elegir(i);
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------
    private static GameObject NuevoRect(string nombre, Transform padre)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        return go;
    }

    private static TextMeshProUGUI NuevoTexto(string nombre, Transform padre,
                                              float tamano, Color color,
                                              TextAlignmentOptions alineacion)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(padre, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = tamano;
        t.color = color;
        t.alignment = alineacion;
        t.raycastTarget = false;
        return t;
    }

    private static void Estirar(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
