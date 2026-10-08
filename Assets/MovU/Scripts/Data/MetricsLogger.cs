using System.Globalization;
using System.IO;
using UnityEngine;

// ============================================================================
// MetricsLogger.cs — Grabador de métricas en formato CSV
// ============================================================================
// Escribe las métricas experimentales formateadas con CultureInfo.InvariantCulture
// para prevenir corrupción de decimales en locales de habla hispana (Hallazgo #2).
// ============================================================================

public static class MetricsLogger
{
    // v2: se añadieron columnas de misión, participante y pisos AL FINAL de las
    // que ya había. Va a un archivo nuevo para que ningún CSV quede con filas de
    // dos anchos distintos; el movu_metrics.csv anterior no se toca.
    private const string FileName = "movu_metrics_v2.csv";

    public const string Header =
        "Timestamp,Seed,MazeWidth,MazeHeight,BraidPercent,TotalTimeSec,TotalDistanceMeters," +
        "OptimalDistanceMeters,DetourRatio,SPL,GuidanceMode,ModeChangeCount,UniqueCellsVisited," +
        "IsNavMeshValid,Aborted,AvgFPS,TimeOff,TimeDirect,TimeNavMesh,DistOff,DistDirect,DistNavMesh," +
        "Scene,ParticipantId,MissionId,MissionIndex,PoiId,StartFloor,TargetFloor,FloorChanges," +
        "WrongFloorVisits,CaptureRadius,NpcConsults,GuidanceLocked,RouteUsesElevator,MinFPS";

    /// <summary>Un texto libre no puede traer comas ni saltos: romperían las columnas.</summary>
    public static string Limpiar(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return texto.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ').Replace('"', '\'').Trim();
    }

    public static string CSVPath => Path.Combine(Application.persistentDataPath, FileName);

    public static void LogRun(RunMetrics metrics)
    {
        try
        {
            string path = CSVPath;
            bool fileExists = File.Exists(path);

            using (StreamWriter writer = new StreamWriter(path, append: true))
            {
                // Encabezados completos para reproducibilidad experimental (Hallazgo #7)
                if (!fileExists)
                {
                    writer.WriteLine(Header);
                }

                // Hallazgo #2: Formatear TODOS los valores con InvariantCulture (punto decimal)
                string line = string.Join(",", new[]
                {
                    metrics.timestamp,
                    metrics.seed.ToString(CultureInfo.InvariantCulture),
                    metrics.mazeWidth.ToString(CultureInfo.InvariantCulture),
                    metrics.mazeHeight.ToString(CultureInfo.InvariantCulture),
                    metrics.braidPercent.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.totalTime.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.totalDistance.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.optimalDistance.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.detourRatio.ToString("F4", CultureInfo.InvariantCulture),
                    metrics.splMetric.ToString("F4", CultureInfo.InvariantCulture),
                    metrics.finalGuidanceMode,
                    metrics.modeChangeCount.ToString(CultureInfo.InvariantCulture),
                    metrics.uniqueCellsVisited.ToString(CultureInfo.InvariantCulture),
                    metrics.isNavMeshValid.ToString().ToLowerInvariant(),
                    metrics.aborted.ToString().ToLowerInvariant(),
                    metrics.avgFps.ToString("F1", CultureInfo.InvariantCulture),
                    metrics.timeInOff.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.timeInDirect.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.timeInNavMesh.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.distInOff.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.distInDirect.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.distInNavMesh.ToString("F2", CultureInfo.InvariantCulture),
                    Limpiar(metrics.scene),
                    Limpiar(metrics.participantId),
                    Limpiar(metrics.missionId),
                    metrics.missionIndex.ToString(CultureInfo.InvariantCulture),
                    Limpiar(metrics.poiId),
                    metrics.startFloor.ToString(CultureInfo.InvariantCulture),
                    metrics.targetFloor.ToString(CultureInfo.InvariantCulture),
                    metrics.floorChanges.ToString(CultureInfo.InvariantCulture),
                    metrics.wrongFloorVisits.ToString(CultureInfo.InvariantCulture),
                    metrics.captureRadius.ToString("F2", CultureInfo.InvariantCulture),
                    metrics.npcConsults.ToString(CultureInfo.InvariantCulture),
                    metrics.guidanceLocked.ToString().ToLowerInvariant(),
                    metrics.routeUsesElevator.ToString().ToLowerInvariant(),
                    metrics.minFps.ToString("F1", CultureInfo.InvariantCulture)
                });

                writer.WriteLine(line);
            }

            Debug.Log($"[MetricsLogger] Métricas guardadas exitosamente en CSV:\n{path}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MetricsLogger] Error al escribir métricas en CSV: {ex.Message}");
        }
    }
}
