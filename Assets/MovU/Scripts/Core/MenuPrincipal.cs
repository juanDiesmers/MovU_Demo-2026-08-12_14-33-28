using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// MenuPrincipal.cs — Pantalla de carga, menú principal, opciones y pausa (RF-13)
// ============================================================================
// Secuencia al dar Play / abrir el ejecutable:
//
//   carga  ->  menú principal (Comenzar · Opciones · Salir)
//          ->  panel de sesión del evaluador (TestSession)  ->  misiones
//
// Durante el juego, la tecla P abre la pausa (Continuar · Opciones · Volver al
// menú · Salir). Pausar congela el reloj de la misión (Time.timeScale = 0).
//
// Opciones: sensibilidad del ratón y volumen, guardadas con SaveSystem.
//
// La "pantalla de carga" cubre el montaje del edificio (MovUBootstrap ya corrió
// cuando esto aparece): tapa la escena hasta que han pasado unos cuadros y un
// mínimo de tiempo, para que el jugador no vea el edificio armándose ni el
// primer tirón de cuadros. No es una carga asíncrona de escena.
//
// IMGUI, igual que TestSession: no añade objetos a la interfaz del juego.
// Se apaga con "menuPrincipal": false en el JSON.
// ============================================================================

public class MenuPrincipal : MonoBehaviour
{
    /// <summary>True mientras la carga o el menú principal están en pantalla (antes de la sesión).</summary>
    public static bool Activo { get; private set; }
    public static bool EnPausa { get; private set; }

    private static MenuPrincipal instancia;

    private enum Pantalla { Carga, Principal, Opciones, Pausa, PausaOpciones }
    private Pantalla pantalla;

    private const float TiempoMinimoDeCarga = 0.8f;
    private const int CuadrosMinimosDeCarga = 3;

    private float inicioReal;
    private int cuadroInicial;

    private PlayerController jugador;
    private MouseLook mirada;
    private GUIStyle estiloTitulo, estiloSubtitulo, estiloTexto, estiloBoton, estiloPequeno;
    private Texture2D fondoOpaco, fondoPausa, barra, barraRelleno;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstaticos()
    {
        Activo = false;
        EnPausa = false;
        instancia = null;
    }

    public void Configurar()
    {
        instancia = this;
        Activo = true;
        EnPausa = false;
        pantalla = Pantalla.Carga;
        inicioReal = Time.realtimeSinceStartup;
        cuadroInicial = Time.frameCount;

        AplicarOpciones();
    }

    /// <summary>Se salta el menú: para las pruebas automáticas y el menú de depuración.</summary>
    public static void Saltar()
    {
        Activo = false;
    }

    private void OnDestroy()
    {
        if (instancia == this) instancia = null;
        Activo = false;
        EnPausa = false;
        Time.timeScale = 1f;
    }

    private void BuscarJugador()
    {
        if (jugador == null) jugador = FindFirstObjectByType<PlayerController>();
        if (mirada == null) mirada = FindFirstObjectByType<MouseLook>();
    }

    private void AplicarOpciones()
    {
        AudioListener.volume = SaveSystem.Volumen;
        BuscarJugador();
        if (mirada != null) mirada.SetSensitivity(SaveSystem.Sensibilidad);
    }

    // ------------------------------------------------------------------
    private void Update()
    {
        BuscarJugador();

        if (Activo)
        {
            if (pantalla == Pantalla.Carga &&
                Time.frameCount - cuadroInicial >= CuadrosMinimosDeCarga &&
                Time.realtimeSinceStartup - inicioReal >= TiempoMinimoDeCarga)
            {
                pantalla = Pantalla.Principal;
            }
            return;       // TestSession mantiene quieto al personaje mientras tanto
        }

        if (Keyboard.current == null) return;

        if (EnPausa)
        {
            // Esc lo usa PlayerController para soltar/atrapar el cursor: aquí se
            // mantiene suelto mientras dure la pausa.
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Keyboard.current.pKey.wasPressedThisFrame) Reanudar();
            return;
        }

        if (TestSession.EsperandoInicio) return;

        // Solo se pausa si el jugador tiene el control y el cursor atrapado:
        // si hay otro panel abierto (ascensor, inventario) P no hace nada.
        if (Keyboard.current.pKey.wasPressedThisFrame &&
            jugador != null && jugador.InputEnabled && Cursor.lockState == CursorLockMode.Locked)
        {
            Pausar();
        }
    }

    private void Pausar()
    {
        EnPausa = true;
        pantalla = Pantalla.Pausa;
        Time.timeScale = 0f;
        if (jugador != null) jugador.SetInputEnabled(false);
        if (mirada != null) mirada.SetLookEnabled(false);
    }

    private void Reanudar()
    {
        EnPausa = false;
        Time.timeScale = 1f;
        if (jugador != null)
        {
            jugador.SetInputEnabled(true);
            jugador.LockCursor();
        }
        if (mirada != null) mirada.SetLookEnabled(true);
    }

    private void Salir()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    private void OnGUI()
    {
        if (!Activo && !EnPausa) return;

        PrepararEstilos();

        float k = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(k, k, 1f));
        float ancho = Screen.width / k;

        // Fondo: opaco en carga y menú, velado en la pausa.
        var pantallaCompleta = new Rect(0f, 0f, ancho, 1080f);
        GUI.DrawTexture(pantallaCompleta, EnPausa ? fondoPausa : fondoOpaco);

        switch (pantalla)
        {
            case Pantalla.Carga: DibujarCarga(ancho); break;
            case Pantalla.Principal: DibujarPrincipal(ancho); break;
            case Pantalla.Opciones:
            case Pantalla.PausaOpciones: DibujarOpciones(ancho); break;
            case Pantalla.Pausa: DibujarPausa(ancho); break;
        }
    }

    private void DibujarCarga(float ancho)
    {
        GUI.Label(new Rect(0f, 400f, ancho, 90f), "MovU", estiloTitulo);
        GUI.Label(new Rect(0f, 500f, ancho, 40f), "Preparando el edificio…", estiloSubtitulo);

        float t = Mathf.Clamp01((Time.realtimeSinceStartup - inicioReal) / TiempoMinimoDeCarga);
        var caja = new Rect(ancho * 0.5f - 300f, 570f, 600f, 14f);
        GUI.DrawTexture(caja, barra);
        GUI.DrawTexture(new Rect(caja.x, caja.y, caja.width * t, caja.height), barraRelleno);
    }

    private void DibujarPrincipal(float ancho)
    {
        GUI.Label(new Rect(0f, 220f, ancho, 100f), "MovU", estiloTitulo);
        GUI.Label(new Rect(0f, 320f, ancho, 40f), "Tu primer día en el edificio de Ingeniería", estiloSubtitulo);

        float x = ancho * 0.5f - 200f;
        if (GUI.Button(new Rect(x, 440f, 400f, 70f), "Comenzar", estiloBoton)) Activo = false;
        if (GUI.Button(new Rect(x, 530f, 400f, 70f), "Opciones", estiloBoton)) pantalla = Pantalla.Opciones;
        if (GUI.Button(new Rect(x, 620f, 400f, 70f), "Salir", estiloBoton)) Salir();

        GUI.Label(new Rect(0f, 1000f, ancho, 30f),
                  "WASD mover · ratón mirar · G cambiar la guía · I inventario · R reiniciar misión · P pausa",
                  estiloPequeno);
        GUI.Label(new Rect(0f, 1035f, ancho, 30f), "Versión " + Application.version, estiloPequeno);
    }

    private void DibujarPausa(float ancho)
    {
        GUI.Label(new Rect(0f, 270f, ancho, 90f), "Pausa", estiloTitulo);

        float x = ancho * 0.5f - 220f;
        if (GUI.Button(new Rect(x, 400f, 440f, 70f), "Continuar  (P)", estiloBoton)) Reanudar();
        if (GUI.Button(new Rect(x, 490f, 440f, 70f), "Opciones", estiloBoton)) pantalla = Pantalla.PausaOpciones;
        if (GUI.Button(new Rect(x, 580f, 440f, 70f), "Volver al menú", estiloBoton))
        {
            if (GameManager.Instance != null) GameManager.Instance.AbandonarYVolverAlMenu();
            else Salir();
        }
        if (GUI.Button(new Rect(x, 670f, 440f, 70f), "Salir", estiloBoton)) Salir();

        MissionManager mm = MissionManager.Instance;
        if (mm != null && mm.Estado == MissionManager.EstadoDeMisiones.EnCurso)
        {
            GUI.Label(new Rect(0f, 780f, ancho, 30f),
                      "Si vuelves al menú, la misión en curso queda registrada como abandonada.",
                      estiloPequeno);
        }
    }

    private void DibujarOpciones(float ancho)
    {
        GUI.Label(new Rect(0f, 230f, ancho, 90f), "Opciones", estiloTitulo);

        float x = ancho * 0.5f - 330f;

        GUI.Label(new Rect(x, 380f, 660f, 36f), "Sensibilidad del ratón", estiloTexto);
        float sens = GUI.HorizontalSlider(new Rect(x, 428f, 660f, 24f), SaveSystem.Sensibilidad,
                                          SaveSystem.SensibilidadMin, SaveSystem.SensibilidadMax);
        if (!Mathf.Approximately(sens, SaveSystem.Sensibilidad))
        {
            SaveSystem.Sensibilidad = sens;
            if (mirada != null) mirada.SetSensitivity(sens);
        }

        GUI.Label(new Rect(x, 500f, 660f, 36f), "Volumen", estiloTexto);
        float vol = GUI.HorizontalSlider(new Rect(x, 548f, 660f, 24f), SaveSystem.Volumen, 0f, 1f);
        if (!Mathf.Approximately(vol, SaveSystem.Volumen))
        {
            SaveSystem.Volumen = vol;
            AudioListener.volume = vol;
        }

        if (GUI.Button(new Rect(x, 650f, 320f, 64f), "Valores por defecto", estiloBoton))
        {
            SaveSystem.Sensibilidad = SaveSystem.SensibilidadPorDefecto;
            SaveSystem.Volumen = 1f;
            AplicarOpciones();
        }
        if (GUI.Button(new Rect(x + 340f, 650f, 320f, 64f), "Volver", estiloBoton))
        {
            SaveSystem.GuardarOpciones();
            pantalla = EnPausa ? Pantalla.Pausa : Pantalla.Principal;
        }
    }

    // ------------------------------------------------------------------
    private void PrepararEstilos()
    {
        if (estiloTitulo != null) return;

        estiloTitulo = new GUIStyle(GUI.skin.label) { fontSize = 84, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        estiloTitulo.normal.textColor = Color.white;

        estiloSubtitulo = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
        estiloSubtitulo.normal.textColor = new Color(1f, 1f, 1f, 0.78f);

        estiloTexto = new GUIStyle(GUI.skin.label) { fontSize = 26 };
        estiloTexto.normal.textColor = Color.white;

        estiloPequeno = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        estiloPequeno.normal.textColor = new Color(1f, 1f, 1f, 0.55f);

        estiloBoton = new GUIStyle(GUI.skin.button) { fontSize = 28 };

        fondoOpaco = Color1x1(new Color(0.07f, 0.08f, 0.10f, 1f));
        fondoPausa = Color1x1(new Color(0.05f, 0.06f, 0.08f, 0.88f));
        barra = Color1x1(new Color(1f, 1f, 1f, 0.12f));
        barraRelleno = Color1x1(new Color(0.35f, 0.65f, 1f, 1f));
    }

    private static Texture2D Color1x1(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
