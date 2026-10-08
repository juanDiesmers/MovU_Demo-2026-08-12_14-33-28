using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// TestSession.cs — Datos de la sesión de prueba (participante y condición)
// ============================================================================
// Antes de la primera misión muestra un panel para el EVALUADOR:
//
//   - Identificador del participante: queda en cada fila del CSV, que es lo
//     que permite cruzar las métricas con la encuesta SUS de esa persona.
//   - Condición de guía: "Libre" (el jugador cambia de modo con G) o un modo
//     fijo (Sin guía / Directa / Por ruta) con la tecla G desactivada. Resuelve
//     en la práctica la pregunta "¿quién controla el modo de guía en las
//     pruebas?": se decide aquí, sesión por sesión, y queda en la columna
//     GuidanceLocked.
//
// Está hecho con IMGUI a propósito: es una herramienta del evaluador, no parte
// del juego, y así no añade ni un objeto a la interfaz de verdad. Mientras el
// panel está visible el jugador no se puede mover.
//
// Se puede saltar con "pedirParticipante": false en el JSON.
// ============================================================================

public class TestSession : MonoBehaviour
{
    public static string ParticipantId { get; private set; } = "";
    public static bool GuiaBloqueada { get; private set; }

    /// <summary>True mientras el panel está en pantalla y la sesión no ha empezado.</summary>
    public static bool EsperandoInicio { get; private set; }

    private static int ultimaCondicion;          // se recuerda entre participantes

    private static readonly string[] Condiciones =
    {
        "Libre (tecla G)", "Sin guía", "Directa", "Por ruta"
    };

    private bool pedir = true;
    private bool empezar;
    private bool enfocado;
    private string texto = "";
    private int condicion;

    private PlayerController jugador;
    private MouseLook mirada;
    private GUIStyle estiloTitulo, estiloTexto, estiloCampo, estiloBoton, estiloCaja;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        ParticipantId = "";
        GuiaBloqueada = false;
        EsperandoInicio = false;
        ultimaCondicion = 0;
    }

    public void Configurar(bool pedirParticipante)
    {
        pedir = pedirParticipante;
        condicion = ultimaCondicion;
        EsperandoInicio = true;
        empezar = !pedir;      // sin panel: arranca en el primer Update
    }

    /// <summary>
    /// Empieza la sesión sin pasar por el panel: para las pruebas automáticas y
    /// el menú de depuración. 'condicionDeGuia': 0 libre, 1 sin guía, 2 directa,
    /// 3 por ruta.
    /// </summary>
    public void Empezar(string participante, int condicionDeGuia)
    {
        if (!EsperandoInicio) return;
        texto = participante ?? "";
        condicion = Mathf.Clamp(condicionDeGuia, 0, Condiciones.Length - 1);
        empezar = true;
    }

    private void OnDestroy()
    {
        EsperandoInicio = false;
    }

    private void Update()
    {
        if (!EsperandoInicio) return;

        if (jugador == null) jugador = FindFirstObjectByType<PlayerController>();
        if (mirada == null) mirada = FindFirstObjectByType<MouseLook>();

        // Enter también se lee por el Input System, por si el panel de IMGUI no
        // recibiera eventos de teclado en algún equipo.
        if (pedir && Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame ||
             Keyboard.current.numpadEnterKey.wasPressedThisFrame))
        {
            empezar = true;
        }

        if (empezar)
        {
            // Se arranca desde Update y no desde Start: para entonces el HUD, la
            // flecha y el GameManager ya se suscribieron a los eventos de misión.
            Comenzar();
            return;
        }

        // Mientras se llena el panel el personaje no debe moverse ni girar.
        if (jugador != null && jugador.InputEnabled) jugador.SetInputEnabled(false);
        if (mirada != null) mirada.SetLookEnabled(false);
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Comenzar()
    {
        string id = texto != null ? texto.Trim() : "";
        ParticipantId = string.IsNullOrEmpty(id) ? "sin-id" : MetricsLogger.Limpiar(id);
        ultimaCondicion = condicion;

        GuidanceArrow flecha = FindFirstObjectByType<GuidanceArrow>();
        if (flecha != null)
        {
            switch (condicion)
            {
                case 1: flecha.SetMode(GuidanceMode.Off, false); break;
                case 2: flecha.SetMode(GuidanceMode.Direct, false); break;
                case 3: flecha.SetMode(GuidanceMode.NavMesh, false); break;
            }
            flecha.BloquearModo(condicion != 0);
        }
        GuiaBloqueada = condicion != 0;

        if (jugador != null)
        {
            jugador.SetInputEnabled(true);
            jugador.LockCursor();
        }
        if (mirada != null) mirada.SetLookEnabled(true);

        EsperandoInicio = false;
        empezar = false;

        Debug.Log($"[Sesión] Participante '{ParticipantId}', guía: {Condiciones[condicion]}.");

        if (MissionManager.Instance != null) MissionManager.Instance.IniciarSiguiente();
        enabled = false;                  // deja de dibujar y de ejecutar Update
    }

    // ------------------------------------------------------------------
    private void OnGUI()
    {
        if (!EsperandoInicio || !pedir) return;

        PrepararEstilos();

        // Todo se dibuja como si la pantalla midiera 1080 de alto.
        float k = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(k, k, 1f));
        float anchoVirtual = Screen.width / k;

        const float ancho = 780f, alto = 430f;
        var zona = new Rect((anchoVirtual - ancho) * 0.5f, (1080f - alto) * 0.5f, ancho, alto);

        Event e = Event.current;
        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            empezar = true;
            e.Use();
        }

        GUI.Box(zona, GUIContent.none, estiloCaja);
        GUILayout.BeginArea(new Rect(zona.x + 40f, zona.y + 28f, zona.width - 80f, zona.height - 56f));

        GUILayout.Label("MovU · sesión de prueba", estiloTitulo);
        GUILayout.Space(14f);

        GUILayout.Label("Identificador del participante (por ejemplo P01)", estiloTexto);
        GUI.SetNextControlName("participante");
        texto = GUILayout.TextField(texto ?? "", 24, estiloCampo, GUILayout.Height(52f));
        if (!enfocado)
        {
            GUI.FocusControl("participante");
            enfocado = true;
        }

        GUILayout.Space(18f);
        GUILayout.Label("Guía de orientación en esta sesión", estiloTexto);
        condicion = GUILayout.Toolbar(condicion, Condiciones, estiloBoton, GUILayout.Height(50f));

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Empezar  (Enter)", estiloBoton, GUILayout.Height(58f)))
        {
            empezar = true;
        }

        GUILayout.EndArea();
    }

    private void PrepararEstilos()
    {
        if (estiloTitulo != null) return;

        estiloTitulo = new GUIStyle(GUI.skin.label) { fontSize = 36, fontStyle = FontStyle.Bold };
        estiloTitulo.normal.textColor = Color.white;

        estiloTexto = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        estiloTexto.normal.textColor = new Color(1f, 1f, 1f, 0.82f);

        estiloCampo = new GUIStyle(GUI.skin.textField) { fontSize = 30, alignment = TextAnchor.MiddleLeft };
        estiloBoton = new GUIStyle(GUI.skin.button) { fontSize = 24 };

        var fondo = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        fondo.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.10f, 0.96f));
        fondo.Apply();
        estiloCaja = new GUIStyle(GUI.skin.box);
        estiloCaja.normal.background = fondo;
    }
}
