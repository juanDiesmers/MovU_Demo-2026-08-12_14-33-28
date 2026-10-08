using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ============================================================================
// NpcCharacter.cs — Una persona del edificio
// ============================================================================
// Estudiantes, profesores, personal de DTI, vigilancia, aseo y visitantes.
// Sirven para dos cosas:
//   - Que el piso se sienta habitado.
//   - Pedir indicaciones (tecla E), que es una estrategia de orientación real y
//     queda medida en el CSV (columna NpcConsults).
//
// No tiene Update ni collider: el NpcManager recorre la lista de NPC activos y
// llama a Tick. Sin collider el jugador los atraviesa; es a propósito, para que
// un NPC parado en una puerta no altere la distancia recorrida que se mide.
// ============================================================================

public enum NpcRole
{
    Estudiante,
    Profesor,
    Administrativo,
    SoporteDTI,
    Vigilante,
    Aseo,
    Visitante
}

public enum NpcConducta
{
    Quieto,
    Deambula
}

[DisallowMultipleComponent]
[AddComponentMenu("MovU/NPC")]
public class NpcCharacter : MonoBehaviour
{
    [SerializeField] private string id = "";
    [SerializeField] private NpcRole rol = NpcRole.Estudiante;
    [SerializeField] private string nombre = "";
    [SerializeField] private NpcConducta conducta = NpcConducta.Quieto;
    [SerializeField] private List<string> frases = new List<string>();

    private NavMeshAgent agente;
    private Transform cuerpo;
    private Renderer dibujo;
    private Vector3[] recorrido;
    private System.Random azar;

    private float esperarHasta = -1f;
    private float hablandoHasta = -1f;
    private bool detenidoPorCharla;
    private float fase;
    private int ultimoDestino = -1;

    public string Id => id;
    public NpcRole Rol => rol;
    public NpcConducta Conducta => conducta;
    public string Nombre => string.IsNullOrEmpty(nombre) ? NombreDelRol(rol) : nombre;

    public void Configurar(string nuevoId, NpcRole nuevoRol, string nuevoNombre,
                           NpcConducta nuevaConducta, List<string> nuevasFrases,
                           Transform figura, Renderer renderizador, NavMeshAgent agent,
                           Vector3[] puntos, int semilla)
    {
        id = nuevoId;
        rol = nuevoRol;
        nombre = nuevoNombre;
        conducta = nuevaConducta;
        frases = nuevasFrases ?? new List<string>();
        cuerpo = figura;
        dibujo = renderizador;
        agente = agent;
        recorrido = puntos;
        azar = new System.Random(semilla);
        fase = (float)azar.NextDouble() * 6.28f;
        esperarHasta = -1f;
    }

    /// <summary>Lo llama el NpcManager una vez por frame, solo si el NPC está activo.</summary>
    public void Tick(float ahora)
    {
        if (conducta != NpcConducta.Deambula || agente == null) return;
        if (!agente.isActiveAndEnabled || !agente.isOnNavMesh) return;

        if (ahora < hablandoHasta) return;                 // está atendiendo al jugador
        if (detenidoPorCharla)
        {
            agente.isStopped = false;
            detenidoPorCharla = false;
        }

        if (!agente.pathPending &&
            (!agente.hasPath || agente.remainingDistance <= agente.stoppingDistance + 0.25f))
        {
            if (esperarHasta < 0f)
            {
                esperarHasta = ahora + 2f + (float)azar.NextDouble() * 7f;
            }
            else if (ahora >= esperarHasta)
            {
                esperarHasta = -1f;
                ElegirDestino();
            }
        }

        // Un balanceo mínimo al caminar, y solo si alguien lo está viendo.
        if (cuerpo != null && dibujo != null && dibujo.isVisible)
        {
            bool camina = agente.velocity.sqrMagnitude > 0.04f;
            float y = camina ? Mathf.Abs(Mathf.Sin(ahora * 7f + fase)) * 0.035f : 0f;
            cuerpo.localPosition = new Vector3(0f, y, 0f);
        }
    }

    private void ElegirDestino()
    {
        if (recorrido == null || recorrido.Length == 0) return;

        int indice = azar.Next(recorrido.Length);
        if (indice == ultimoDestino && recorrido.Length > 1)
        {
            indice = (indice + 1) % recorrido.Length;
        }
        ultimoDestino = indice;
        agente.SetDestination(recorrido[indice]);
    }

    /// <summary>Se detiene unos segundos y gira hacia un punto (hacia donde "señala").</summary>
    public void AtenderYMirar(Vector3 punto, float segundos, float ahora)
    {
        hablandoHasta = ahora + segundos;

        if (agente != null && agente.isActiveAndEnabled && agente.isOnNavMesh)
        {
            agente.isStopped = true;
            detenidoPorCharla = true;
        }

        Vector3 d = punto - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.04f)
        {
            transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }

    /// <summary>Una frase propia. Si el JSON no trae ninguna, una genérica de su rol.</summary>
    public string FrasePropia()
    {
        if (frases != null && frases.Count > 0)
        {
            return frases[azar != null ? azar.Next(frases.Count) : 0];
        }

        string[] genericas = FrasesDelRol(rol);
        return genericas[azar != null ? azar.Next(genericas.Length) : 0];
    }

    public static string NombreDelRol(NpcRole r)
    {
        switch (r)
        {
            case NpcRole.Profesor: return "Profesor";
            case NpcRole.Administrativo: return "Secretaría";
            case NpcRole.SoporteDTI: return "Personal de DTI";
            case NpcRole.Vigilante: return "Vigilante";
            case NpcRole.Aseo: return "Personal de aseo";
            case NpcRole.Visitante: return "Visitante";
            default: return "Estudiante";
        }
    }

    public static bool RolDesdeTexto(string texto, out NpcRole r)
    {
        return System.Enum.TryParse(texto, true, out r);
    }

    private static string[] FrasesDelRol(NpcRole r)
    {
        switch (r)
        {
            case NpcRole.Profesor:
                return new[] { "Buen día. Ya casi empiezo clase.", "Si buscas un salón, fíjate en los rótulos de las puertas." };
            case NpcRole.SoporteDTI:
                return new[] { "Hola, ¿en qué te ayudo?", "Para pedir un equipo necesitas tu carné." };
            case NpcRole.Vigilante:
                return new[] { "Buenas. Los ascensores están en el hall.", "Cualquier cosa, me avisas." };
            case NpcRole.Aseo:
                return new[] { "Cuidado, que el piso está recién trapeado.", "Buen día." };
            case NpcRole.Visitante:
                return new[] { "Yo también ando perdido, es mi primera vez aquí.", "¿Sabes dónde queda la salida?" };
            case NpcRole.Administrativo:
                return new[] { "Buen día, ¿a quién buscas?", "Las oficinas atienden hasta las cinco." };
            default:
                return new[] { "¡Hola!", "Voy tarde a clase.", "¿Ya entregaste el taller?" };
        }
    }
}
