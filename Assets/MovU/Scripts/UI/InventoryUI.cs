using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// InventoryUI.cs — El inventario en pantalla
// ============================================================================
// Dos piezas, las dos dentro del lienzo del HUD:
//
//   - La BARRA (abajo a la derecha): una casilla por objeto, siempre visible.
//     No hay iconos: cada casilla es un color y dos o tres letras ("ID", "PC"),
//     que salen del JSON.
//   - El PANEL (tecla I o Tab): la lista con nombre y descripción.
//
// El panel no pausa el juego ni suelta el cursor: es una consulta rápida, el
// cronómetro de la misión sigue corriendo y el jugador se puede seguir
// moviendo. Así abrirlo no altera las medidas más que lo que tarde en leerlo.
//
// Se construye por código, igual que el resto del HUD (HudBuilder).
// ============================================================================

public class InventoryUI : MonoBehaviour
{
    private const int Casillas = 5;
    private const float Lado = 60f;
    private const float Separacion = 8f;

    private static readonly Color Fondo = new Color(0.07f, 0.08f, 0.10f, 0.78f);
    private static readonly Color Vacia = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color Tinta = new Color(1f, 1f, 1f, 0.96f);
    private static readonly Color Tenue = new Color(1f, 1f, 1f, 0.72f);

    private InventoryManager inventario;
    private readonly List<Image> fondos = new List<Image>();
    private readonly List<TextMeshProUGUI> siglas = new List<TextMeshProUGUI>();
    private GameObject panel;
    private TextMeshProUGUI textoPanel;
    private readonly StringBuilder sb = new StringBuilder(512);

    private int casillaDestacada = -1;
    private float destacarHasta = -1f;

    /// <summary>Crea la barra y el panel como hijos del lienzo del HUD.</summary>
    public static InventoryUI Construir(Transform lienzo, InventoryManager gestor)
    {
        var go = new GameObject("Inventario", typeof(RectTransform));
        go.transform.SetParent(lienzo, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var ui = go.AddComponent<InventoryUI>();
        ui.inventario = gestor;
        ui.ConstruirBarra();
        ui.ConstruirPanel();
        ui.Refrescar();
        return ui;
    }

    private void ConstruirBarra()
    {
        float ancho = Casillas * Lado + (Casillas + 1) * Separacion;
        float alto = Lado + Separacion * 2f + 26f;

        RectTransform barra = Caja("Barra", transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                                   new Vector2(-36f, 36f), new Vector2(ancho, alto), Fondo);

        TextMeshProUGUI titulo = Texto("Titulo", barra, 18f, Tenue, TextAlignmentOptions.TopLeft);
        titulo.text = "Inventario  ·  I";
        titulo.rectTransform.anchorMin = new Vector2(0f, 1f);
        titulo.rectTransform.anchorMax = new Vector2(1f, 1f);
        titulo.rectTransform.pivot = new Vector2(0.5f, 1f);
        titulo.rectTransform.anchoredPosition = new Vector2(0f, -5f);
        titulo.rectTransform.sizeDelta = new Vector2(-20f, 24f);

        for (int i = 0; i < Casillas; i++)
        {
            RectTransform casilla = Caja("Casilla_" + i, barra, new Vector2(0f, 0f), new Vector2(0f, 0f),
                                         new Vector2(Separacion + i * (Lado + Separacion), Separacion),
                                         new Vector2(Lado, Lado), Vacia);
            fondos.Add(casilla.GetComponent<Image>());

            TextMeshProUGUI sigla = Texto("Sigla", casilla, 24f, Tinta, TextAlignmentOptions.Center);
            sigla.fontStyle = FontStyles.Bold;
            Estirar(sigla.rectTransform);
            siglas.Add(sigla);
        }
    }

    private void ConstruirPanel()
    {
        RectTransform caja = Caja("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  Vector2.zero, new Vector2(860f, 520f),
                                  new Color(0.07f, 0.08f, 0.10f, 0.94f));
        panel = caja.gameObject;

        TextMeshProUGUI titulo = Texto("Titulo", caja, 36f, new Color(1f, 0.86f, 0.35f), TextAlignmentOptions.TopLeft);
        titulo.text = "Inventario";
        titulo.fontStyle = FontStyles.Bold;
        titulo.rectTransform.anchorMin = new Vector2(0f, 1f);
        titulo.rectTransform.anchorMax = new Vector2(1f, 1f);
        titulo.rectTransform.pivot = new Vector2(0.5f, 1f);
        titulo.rectTransform.anchoredPosition = new Vector2(0f, -24f);
        titulo.rectTransform.sizeDelta = new Vector2(-72f, 48f);

        textoPanel = Texto("Lista", caja, 30f, Tinta, TextAlignmentOptions.TopLeft);
        textoPanel.textWrappingMode = TextWrappingModes.Normal;
        textoPanel.overflowMode = TextOverflowModes.Truncate;
        textoPanel.richText = true;
        textoPanel.rectTransform.anchorMin = Vector2.zero;
        textoPanel.rectTransform.anchorMax = Vector2.one;
        textoPanel.rectTransform.offsetMin = new Vector2(36f, 58f);
        textoPanel.rectTransform.offsetMax = new Vector2(-36f, -86f);

        TextMeshProUGUI pie = Texto("Pie", caja, 20f, Tenue, TextAlignmentOptions.Bottom);
        pie.text = "I o Tab: cerrar";
        pie.rectTransform.anchorMin = new Vector2(0f, 0f);
        pie.rectTransform.anchorMax = new Vector2(1f, 0f);
        pie.rectTransform.pivot = new Vector2(0.5f, 0f);
        pie.rectTransform.anchoredPosition = new Vector2(0f, 18f);
        pie.rectTransform.sizeDelta = new Vector2(-72f, 30f);

        panel.SetActive(false);
    }

    private void OnEnable()
    {
        if (inventario == null) inventario = InventoryManager.Instance;
        Suscribir(true);
    }

    private void Start()
    {
        if (inventario == null)
        {
            inventario = InventoryManager.Instance;
            Suscribir(true);
        }
        Refrescar();
    }

    private void OnDisable()
    {
        Suscribir(false);
    }

    private bool suscrito;

    private void Suscribir(bool activar)
    {
        if (inventario == null || suscrito == activar) return;
        suscrito = activar;

        if (activar)
        {
            inventario.OnCambio += Refrescar;
            inventario.OnObjetoObtenido += AlObtener;
            inventario.OnPanelCambiado += AlAbrir;
        }
        else
        {
            inventario.OnCambio -= Refrescar;
            inventario.OnObjetoObtenido -= AlObtener;
            inventario.OnPanelCambiado -= AlAbrir;
        }
    }

    private void Update()
    {
        // La casilla del objeto recién recibido late un momento.
        if (casillaDestacada < 0) return;

        if (Time.unscaledTime >= destacarHasta)
        {
            fondos[casillaDestacada].rectTransform.localScale = Vector3.one;
            casillaDestacada = -1;
            return;
        }

        float k = 1f + 0.16f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f));
        fondos[casillaDestacada].rectTransform.localScale = new Vector3(k, k, 1f);
    }

    private void AlObtener(ObjetoDef objeto)
    {
        if (inventario == null) return;
        for (int i = 0; i < inventario.Objetos.Count && i < Casillas; i++)
        {
            if (inventario.Objetos[i] != objeto) continue;
            if (casillaDestacada >= 0) fondos[casillaDestacada].rectTransform.localScale = Vector3.one;
            casillaDestacada = i;
            destacarHasta = Time.unscaledTime + 2.2f;
            break;
        }
    }

    private void AlAbrir(bool abierto)
    {
        if (panel == null) return;
        if (abierto) EscribirPanel();
        panel.SetActive(abierto);
    }

    private void Refrescar()
    {
        if (inventario == null) return;
        IReadOnlyList<ObjetoDef> objetos = inventario.Objetos;

        for (int i = 0; i < Casillas; i++)
        {
            if (i < objetos.Count)
            {
                ObjetoDef o = objetos[i];
                Color32 c = FormasMovU.ColorDesdeHex(o.color, new Color32(90, 107, 124, 255));
                fondos[i].color = new Color32(c.r, c.g, c.b, 235);
                siglas[i].text = Sigla(o);
            }
            else
            {
                fondos[i].color = Vacia;
                siglas[i].text = "";
            }
        }

        if (panel != null && panel.activeSelf) EscribirPanel();
    }

    private static string Sigla(ObjetoDef o)
    {
        if (!string.IsNullOrEmpty(o.sigla)) return o.sigla;
        if (string.IsNullOrEmpty(o.nombre)) return "?";
        return o.nombre.Substring(0, Mathf.Min(2, o.nombre.Length)).ToUpperInvariant();
    }

    private void EscribirPanel()
    {
        if (textoPanel == null || inventario == null) return;
        IReadOnlyList<ObjetoDef> objetos = inventario.Objetos;

        sb.Length = 0;
        if (objetos.Count == 0)
        {
            sb.Append("<color=#FFFFFFB0>No llevas nada encima.</color>");
        }
        for (int i = 0; i < objetos.Count; i++)
        {
            ObjetoDef o = objetos[i];
            Color32 c = FormasMovU.ColorDesdeHex(o.color, new Color32(90, 107, 124, 255));
            sb.Append("<mark=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append("FF><b> ")
              .Append(Sigla(o)).Append(" </b></mark>   <b>").Append(o.nombre).Append("</b>\n");

            string descripcion = InventoryManager.Descripcion(o);
            if (descripcion.Length > 0)
            {
                sb.Append("<size=80%><color=#FFFFFFC0>").Append(descripcion).Append("</color></size>\n");
            }
            sb.Append("<size=40%>\n</size>");
        }
        textoPanel.text = sb.ToString();
    }

    // ------------------------------------------------------------------
    private static RectTransform Caja(string nombre, Transform padre, Vector2 ancla, Vector2 pivote,
                                      Vector2 posicion, Vector2 tamano, Color color)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = ancla;
        rt.pivot = pivote;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;

        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
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
        t.overflowMode = TextOverflowModes.Overflow;
        t.text = "";
        return t;
    }

    private static void Estirar(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
