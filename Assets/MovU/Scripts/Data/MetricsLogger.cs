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
    private const string FileName = "movu_metrics.csv";

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
                    writer.WriteLine("Timestamp,Seed,MazeWidth,MazeHeight,BraidPercent,TotalTimeSec,TotalDistanceMeters,OptimalDistanceMeters,DetourRatio,SPL,GuidanceMode,ModeChangeCount,UniqueCellsVisited,IsNavMeshValid,Aborted,AvgFPS,TimeOff,TimeDirect,TimeNavMesh,DistOff,DistDirect,DistNavMesh");
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
                    metrics.distInNavMesh.ToString("F2", CultureInfo.InvariantCulture)
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
