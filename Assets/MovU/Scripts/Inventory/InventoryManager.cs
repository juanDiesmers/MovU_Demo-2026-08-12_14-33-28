using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// InventoryManager.cs — Lo que el jugador lleva encima
// ============================================================================
// Un inventario pequeño, a la medida del juego: el carné, el horario, y lo que
// le van entregando en las misiones (el portátil que presta DTI, el comprobante
// de soporte).
//
// Como todo el contenido, sale de contenido_piso9.json:
//   - "objetos": el catálogo. Los que traen "inicial": true se tienen desde el
//     principio.
//   - "misiones[].entrega": el objeto que se recibe al completar esa misión.
//
// Patrón Manager + Observer: guarda el estado y publica eventos; la interfaz
// (InventoryUI) y el sonido (AudioManager) se suscriben.
//
// No toca las métricas: tener o no un objeto no cambia cuándo se completa una
// misión ni lo que se escribe en el CSV.
// ============================================================================

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    /// <summary>El jugador recibió un objeto durante la partida (no cuenta los iniciales).</summary>
    public event Action<ObjetoDef> OnObjetoObtenido;
    /// <summary>Cambió lo que hay en el inventario.</summary>
    public event Action OnCambio;
    /// <summary>Se abrió (true) o se cerró (false) el panel.</summary>
    public event Action<bool> OnPanelCambiado;

    private readonly List<ObjetoDef> catalogo = new List<ObjetoDef>();
    private readonly List<ObjetoDef> objetos = new List<ObjetoDef>();
    private MissionManager misiones;
    private bool activo = true;

    public IReadOnlyList<ObjetoDef> Objetos => objetos;
    public bool PanelAbierto { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public void Configurar(ContenidoPiso datos)
    {
        catalogo.Clear();
        objetos.Clear();
        activo = datos != null && (datos.ajustes == null || datos.ajustes.inventario);
        if (datos == null || datos.objetos == null) return;

        for (int i = 0; i < datos.objetos.Count; i++)
        {
            ObjetoDef o = datos.objetos[i];
            if (o == null || string.IsNullOrEmpty(o.id)) continue;
            catalogo.Add(o);
            if (o.inicial) objetos.Add(o);
        }
    }

    private void Start()
    {
        misiones = MissionManager.Instance;
        if (misiones != null)
        {
            misiones.OnMissionStarted += AlEmpezarMision;
            misiones.OnMissionCompleted += AlCompletarMision;
        }
    }

    private void OnDestroy()
    {
        if (misiones != null)
        {
            misiones.OnMissionStarted -= AlEmpezarMision;
            misiones.OnMissionCompleted -= AlCompletarMision;
        }
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!activo || TestSession.EsperandoInicio || Keyboard.current == null) return;

        if (Keyboard.current.iKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame)
        {
            AbrirPanel(!PanelAbierto);
        }
    }

    public void AbrirPanel(bool abrir)
    {
        if (PanelAbierto == abrir) return;
        PanelAbierto = abrir;
        OnPanelCambiado?.Invoke(abrir);
    }

    // ------------------------------------------------------------------
    // Consultas y cambios
    // ------------------------------------------------------------------
    public bool Tiene(string id)
    {
        return Indice(objetos, id) >= 0;
    }

    public ObjetoDef Buscar(string id)
    {
        int i = Indice(catalogo, id);
        return i >= 0 ? catalogo[i] : null;
    }

    /// <summary>Entrega un objeto del catálogo. False si no existe o ya lo tenía.</summary>
    public bool Agregar(string id)
    {
        ObjetoDef o = Buscar(id);
        if (o == null)
        {
            Debug.LogWarning($"[Inventario] El objeto '{id}' no está en 'objetos' del JSON.");
            return false;
        }
        if (Tiene(id)) return false;

        objetos.Add(o);
        Debug.Log($"[Inventario] + {o.nombre}");
        OnCambio?.Invoke();
        OnObjetoObtenido?.Invoke(o);
        return true;
    }

    public bool Quitar(string id)
    {
        int i = Indice(objetos, id);
        if (i < 0) return false;
        objetos.RemoveAt(i);
        OnCambio?.Invoke();
        return true;
    }

    private static int Indice(List<ObjetoDef> lista, string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < lista.Count; i++)
        {
            if (string.Equals(lista[i].id, id, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>La descripción con {participante} ya resuelto.</summary>
    public static string Descripcion(ObjetoDef o)
    {
        if (o == null || string.IsNullOrEmpty(o.descripcion)) return "";
        string quien = string.IsNullOrEmpty(TestSession.ParticipantId) ? "—" : TestSession.ParticipantId;
        return o.descripcion.Replace("{participante}", quien);
    }

    // ------------------------------------------------------------------
    // Misiones
    // ------------------------------------------------------------------
    private void AlEmpezarMision(MisionEnCurso mision)
    {
        // Si se repite una misión (tecla R), lo que entregaba se devuelve: se
        // vuelve a recibir al completarla.
        if (mision != null && mision.def != null && !string.IsNullOrEmpty(mision.def.entrega))
        {
            Quitar(mision.def.entrega);
        }
    }

    private void AlCompletarMision(MisionEnCurso mision)
    {
        if (!activo || mision == null || mision.def == null) return;
        if (string.IsNullOrEmpty(mision.def.entrega)) return;

        if (Agregar(mision.def.entrega) && HUDController.Instance != null)
        {
            ObjetoDef o = Buscar(mision.def.entrega);
            string texto = string.IsNullOrEmpty(mision.def.mensajeDeEntrega)
                ? $"Recibiste: <b>{o.nombre}</b>"
                : $"{mision.def.mensajeDeEntrega}  <b>+ {o.nombre}</b>";
            HUDController.Instance.MostrarSubtitulo(texto, 6f);
        }
    }
}
