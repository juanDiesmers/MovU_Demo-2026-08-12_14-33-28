using System;
using UnityEngine;

// ============================================================================
// Objective.cs — Estructura de datos serializable de un objetivo
// ============================================================================

[Serializable]
public class Objective
{
    public string id = "server_room";
    public string displayName = "Encuentra la sala de servidores";
    public Transform target;

    /// <summary>Constructor sin parámetros para la serialización de Unity (Hallazgo #12).</summary>
    public Objective() { }

    public Objective(string id, string displayName, Transform target)
    {
        this.id = id;
        this.displayName = displayName;
        this.target = target;
    }
}
