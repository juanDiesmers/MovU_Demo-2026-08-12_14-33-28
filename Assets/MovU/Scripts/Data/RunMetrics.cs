using System;
using UnityEngine;

// ============================================================================
// RunMetrics.cs — Contenedor de datos de rendimiento por partida
// ============================================================================
// Incluye métricas científicas completas para wayfinding:
//   - detourRatio (totalDistance / optimalDistance) (Hallazgo #5)
//   - splMetric (SPL: Success weighted by Path Length, ∈ [0,1]) (Hallazgo #5)
//   - Desglose por modo de guía (Hallazgo #8)
//   - Contexto completo del experimento (seed, dimensiones, fps, aborted) (Hallazgo #7)
// ============================================================================

[Serializable]
public class RunMetrics
{
    public float totalTime;            // Tiempo total hasta completar (segundos)
    public float totalDistance;        // Distancia total recorrida en metros (horizontal)
    public float optimalDistance;      // Longitud de la ruta óptima en metros
    public float detourRatio;          // Ratio de desvío (totalDistance / optimalDistance) (Hallazgo #5)
    public float splMetric;            // SPL (Success weighted by Path Length) (Hallazgo #5)
    public string finalGuidanceMode;   // Último modo de guía activo
    public int modeChangeCount;        // Cantidad de veces que cambió el modo con 'G'
    public int uniqueCellsVisited;     // Cantidad de celdas únicas exploradas del laberinto

    // Contexto experimental (Hallazgo #7)
    public int seed;
    public int mazeWidth;
    public int mazeHeight;
    public float braidPercent;
    public bool isNavMeshValid;
    public bool aborted;
    public float avgFps;

    // Desglose por modo de guía (Hallazgo #8)
    public float timeInOff;
    public float timeInDirect;
    public float timeInNavMesh;
    public float distInOff;
    public float distInDirect;
    public float distInNavMesh;

    public string timestamp;

    // ------------------------------------------------------------------
    // Contexto de misión y de sesión (modo catálogo, escena Edificio).
    // No van en el constructor para no romper las llamadas del demo: se
    // rellenan con un inicializador de objeto. En el demo quedan en sus
    // valores por defecto.
    // ------------------------------------------------------------------
    public string scene = "";           // escena en la que se jugó
    public string participantId = "";   // para cruzar el CSV con las encuestas SUS
    public string missionId = "";
    public int missionIndex = 0;        // contando desde 1; 0 = demo
    public string poiId = "";
    public int startFloor = 0;          // contando desde 1; 0 = no aplica
    public int targetFloor = 0;
    public int floorChanges = 0;        // veces que cambió de piso
    public int wrongFloorVisits = 0;    // llegadas a un piso que no era el del destino
    public float captureRadius = 0f;    // radio de la zona de llegada, en metros
    public int npcConsults = 0;         // veces que pidió indicaciones a un NPC
    public bool guidanceLocked = false; // true = el evaluador fijó el modo de guía
    public bool routeUsesElevator = false;
    public float minFps = 0f;           // peor segundo de la misión (SRS RD-1)

    public RunMetrics(float totalTime, float totalDistance, float optimalDistance,
                      string finalGuidanceMode, int modeChangeCount, int uniqueCellsVisited,
                      int seed, int mazeWidth, int mazeHeight, float braidPercent,
                      bool isNavMeshValid, bool aborted, float avgFps,
                      float timeOff, float timeDirect, float timeNavMesh,
                      float distOff, float distDirect, float distNavMesh)
    {
        this.totalTime = totalTime;
        this.totalDistance = totalDistance;
        this.optimalDistance = Mathf.Max(0.001f, optimalDistance);

        // Hallazgo #5: detourRatio y SPL
        this.detourRatio = this.totalDistance / this.optimalDistance;
        float success = aborted ? 0f : 1f;
        this.splMetric = success * (this.optimalDistance / Mathf.Max(this.totalDistance, this.optimalDistance));

        this.finalGuidanceMode = finalGuidanceMode;
        this.modeChangeCount = modeChangeCount;
        this.uniqueCellsVisited = uniqueCellsVisited;

        this.seed = seed;
        this.mazeWidth = mazeWidth;
        this.mazeHeight = mazeHeight;
        this.braidPercent = braidPercent;
        this.isNavMeshValid = isNavMeshValid;
        this.aborted = aborted;
        this.avgFps = avgFps;

        this.timeInOff = timeOff;
        this.timeInDirect = timeDirect;
        this.timeInNavMesh = timeNavMesh;

        this.distInOff = distOff;
        this.distInDirect = distDirect;
        this.distInNavMesh = distNavMesh;

        // Hallazgo #2: ISO 8601 Timestamp sin ambigüedad
        this.timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    }
}
