using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// AudioManager.cs — Todo el sonido del juego
// ============================================================================
// Patrón Manager + Observer: nadie le pide sonidos. Se suscribe a los eventos
// que ya publican los demás gestores (misiones, pisos, NPC, puertas, escalera,
// inventario) y decide qué suena.
//
// Los sonidos son archivos WAV en  Assets/MovU/Resources/MovU/Audio/ , generados
// por  Tools/generar_sonidos.py  (síntesis, sin licencias de terceros). Para
// cambiar uno por una grabación de verdad basta con reemplazar el archivo
// conservando el nombre. Si falta alguno, ese sonido simplemente no suena.
//
// Todo es 2D (sin espacialización): la atenuación de las puertas se hace a
// mano con la distancia al jugador. Cuatro AudioSource en total.
//
// Se apaga con "sonido": false en los ajustes del JSON.
// ============================================================================

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const string Carpeta = "MovU/Audio/";
    private const float AlcanceDePuertas = 16f;

    private AudioSource efectos;      // avisos e interfaz
    private AudioSource pasos;        // pasos del jugador
    private AudioSource voz;          // "habla" de los NPC
    private AudioSource ambiente;     // murmullo de fondo, en bucle

    private readonly List<AudioClip> clipsDePaso = new List<AudioClip>();
    private AudioClip clipVoz, clipPuertaAbre, clipPuertaCierra;
    private AudioClip clipMisionInicio, clipMisionCompleta, clipTodasCompletas;
    private AudioClip clipObjeto, clipInventarioAbre, clipInventarioCierra;
    private AudioClip clipAscensor, clipNegado, clipAmbiente;

    private bool activo = true;
    private float volumen = 0.8f;
    private int ultimoPaso = -1;
    private Coroutine hablando;
    private Transform jugador;

    private MissionManager misiones;
    private FloorManager pisos;
    private NpcManager npcs;
    private DoorManager puertas;
    private StairsManager escalera;
    private InventoryManager inventario;

    public bool Activo => activo;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public void Configurar(AjustesDeContenido ajustes)
    {
        activo = ajustes == null || ajustes.sonido;
        volumen = ajustes != null ? Mathf.Clamp01(ajustes.volumen) : 0.8f;
        if (!activo) return;

        efectos = Fuente("Efectos", 1f);
        pasos = Fuente("Pasos", 0.55f);
        voz = Fuente("Voz", 0.5f);
        ambiente = Fuente("Ambiente", 0.32f);
        ambiente.loop = true;

        for (int i = 1; i <= 6; i++)
        {
            AudioClip c = Cargar("paso_" + i);
            if (c != null) clipsDePaso.Add(c);
        }
        clipVoz = Cargar("voz_blip");
        clipPuertaAbre = Cargar("puerta_abrir");
        clipPuertaCierra = Cargar("puerta_cerrar");
        clipMisionInicio = Cargar("mision_inicio");
        clipMisionCompleta = Cargar("mision_completa");
        clipTodasCompletas = Cargar("todas_completas");
        clipObjeto = Cargar("objeto_obtenido");
        clipInventarioAbre = Cargar("inventario_abrir");
        clipInventarioCierra = Cargar("inventario_cerrar");
        clipAscensor = Cargar("ascensor");
        clipNegado = Cargar("negado");
        clipAmbiente = Cargar("ambiente");

        if (clipAmbiente != null)
        {
            ambiente.clip = clipAmbiente;
            ambiente.Play();
        }
    }

    private AudioSource Fuente(string nombre, float nivel)
    {
        var go = new GameObject("Audio_" + nombre);
        go.transform.SetParent(transform, false);
        var f = go.AddComponent<AudioSource>();
        f.playOnAwake = false;
        f.spatialBlend = 0f;
        f.volume = nivel * volumen;
        return f;
    }

    private static AudioClip Cargar(string nombre)
    {
        return Resources.Load<AudioClip>(Carpeta + nombre);
    }

    private void Start()
    {
        if (!activo) return;

        misiones = MissionManager.Instance;
        if (misiones != null)
        {
            misiones.OnMissionStarted += AlEmpezarMision;
            misiones.OnMissionCompleted += AlCompletarMision;
            misiones.OnAllMissionsCompleted += AlTerminarTodas;
        }

        pisos = FloorManager.Instance;
        if (pisos != null) pisos.OnPisoCambiado += AlCambiarDePiso;

        npcs = NpcManager.Instance;
        if (npcs != null) npcs.OnNpcHabla += AlHablarNpc;

        puertas = DoorManager.Instance;
        if (puertas != null)
        {
            puertas.OnPuertaSeAbre += AlAbrirPuerta;
            puertas.OnPuertaSeCierra += AlCerrarPuerta;
        }

        escalera = StairsManager.Instance;
        if (escalera != null)
        {
            escalera.OnTramoRecorrido += AlCruzarEscalera;
            escalera.OnPuertaCerrada += Negar;
        }

        inventario = InventoryManager.Instance;
        if (inventario != null)
        {
            inventario.OnObjetoObtenido += AlObtenerObjeto;
            inventario.OnPanelCambiado += AlAbrirInventario;
        }
    }

    private void OnDestroy()
    {
        if (misiones != null)
        {
            misiones.OnMissionStarted -= AlEmpezarMision;
            misiones.OnMissionCompleted -= AlCompletarMision;
            misiones.OnAllMissionsCompleted -= AlTerminarTodas;
        }
        if (pisos != null) pisos.OnPisoCambiado -= AlCambiarDePiso;
        if (npcs != null) npcs.OnNpcHabla -= AlHablarNpc;
        if (puertas != null)
        {
            puertas.OnPuertaSeAbre -= AlAbrirPuerta;
            puertas.OnPuertaSeCierra -= AlCerrarPuerta;
        }
        if (escalera != null)
        {
            escalera.OnTramoRecorrido -= AlCruzarEscalera;
            escalera.OnPuertaCerrada -= Negar;
        }
        if (inventario != null)
        {
            inventario.OnObjetoObtenido -= AlObtenerObjeto;
            inventario.OnPanelCambiado -= AlAbrirInventario;
        }

        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Pasos (los pide PlayerFootsteps: es el único que sabe cuándo se pisa)
    // ------------------------------------------------------------------
    /// <summary>Un paso del jugador. 'enEscalera' los hace un poco más agudos y secos.</summary>
    public void Paso(bool enEscalera)
    {
        if (!activo || pasos == null || clipsDePaso.Count == 0) return;

        // Nunca el mismo dos veces seguidas: repetido suena a metralleta.
        int i = Random.Range(0, clipsDePaso.Count);
        if (i == ultimoPaso) i = (i + 1) % clipsDePaso.Count;
        ultimoPaso = i;

        pasos.pitch = (enEscalera ? 1.14f : 1f) * Random.Range(0.93f, 1.07f);
        pasos.PlayOneShot(clipsDePaso[i], Random.Range(0.8f, 1f));
    }

    // ------------------------------------------------------------------
    // Eventos
    // ------------------------------------------------------------------
    private void AlEmpezarMision(MisionEnCurso mision)
    {
        Sonar(clipMisionInicio, 0.7f);
    }

    private void AlCompletarMision(MisionEnCurso mision)
    {
        // Si era la última suena la fanfarria (AlTerminarTodas), no las dos.
        if (misiones != null && misiones.Estado == MissionManager.EstadoDeMisiones.Terminado) return;
        Sonar(clipMisionCompleta, 0.85f);
    }

    private void AlTerminarTodas()
    {
        Sonar(clipTodasCompletas, 0.9f);
    }

    private void AlCambiarDePiso(int anterior, int nuevo)
    {
        // Por la escalera ya suenan los pasos del tramo oculto.
        if (StairsManager.EnTransicion) return;
        Sonar(clipAscensor, 0.7f);
    }

    private void AlCruzarEscalera(bool subiendo, int piso)
    {
        if (!activo) return;
        StartCoroutine(PasosDelTramoOculto());
    }

    private IEnumerator PasosDelTramoOculto()
    {
        for (int i = 0; i < 5; i++)
        {
            Paso(true);
            yield return new WaitForSecondsRealtime(0.13f);
        }
    }

    private void Negar()
    {
        Sonar(clipNegado, 0.6f);
    }

    private void AlObtenerObjeto(ObjetoDef objeto)
    {
        Sonar(clipObjeto, 0.8f);
    }

    private void AlAbrirInventario(bool abierto)
    {
        Sonar(abierto ? clipInventarioAbre : clipInventarioCierra, 0.5f);
    }

    private void AlAbrirPuerta(Vector3 donde)
    {
        Sonar(clipPuertaAbre, 0.75f * Cercania(donde));
    }

    private void AlCerrarPuerta(Vector3 donde)
    {
        Sonar(clipPuertaCierra, 0.7f * Cercania(donde));
    }

    /// <summary>1 junto al jugador, 0 más allá del alcance: las puertas lejanas no se oyen.</summary>
    private float Cercania(Vector3 punto)
    {
        if (jugador == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go == null) return 0f;
            jugador = go.transform;
        }
        float d = Vector3.Distance(jugador.position, punto);
        float k = Mathf.Clamp01(1f - d / AlcanceDePuertas);
        return k * k;
    }

    private void Sonar(AudioClip clip, float nivel)
    {
        if (!activo || efectos == null || clip == null || nivel <= 0.01f) return;
        efectos.PlayOneShot(clip, nivel);
    }

    // ------------------------------------------------------------------
    // Voz de los NPC
    // ------------------------------------------------------------------
    // No hay voces grabadas: mientras el subtítulo se va escribiendo suena una
    // sílaba corta por cada tres letras, como en muchos juegos de bajo
    // presupuesto. Cada personaje tiene su tono (sale de su nombre), así que se
    // reconoce quién habla sin mirar.
    private void AlHablarNpc(string quien, string frase)
    {
        if (!activo || voz == null || clipVoz == null || string.IsNullOrEmpty(frase)) return;

        if (hablando != null) StopCoroutine(hablando);
        hablando = StartCoroutine(Hablar(quien ?? "", frase));
    }

    private IEnumerator Hablar(string quien, string frase)
    {
        // Tono propio y estable: del nombre, no del azar.
        int semilla = 17;
        for (int i = 0; i < quien.Length; i++) semilla = semilla * 31 + quien[i];
        float tono = 0.85f + (Mathf.Abs(semilla) % 100) / 100f * 0.55f;

        var azar = new System.Random(semilla + frase.Length);
        float porLetra = 1f / HUDController.LetrasPorSegundo;

        for (int i = 0; i < frase.Length; i += 3)
        {
            char c = frase[i];
            if (char.IsLetterOrDigit(c))
            {
                voz.pitch = tono * (0.94f + (float)azar.NextDouble() * 0.14f);
                voz.PlayOneShot(clipVoz, 0.8f);
            }
            // En las pausas de la frase la voz también calla.
            float espera = porLetra * 3f;
            if (c == ',' || c == '.' || c == ':' || c == ';' || c == '?' || c == '!') espera += 0.12f;
            yield return new WaitForSeconds(espera);
        }
        hablando = null;
    }
}
