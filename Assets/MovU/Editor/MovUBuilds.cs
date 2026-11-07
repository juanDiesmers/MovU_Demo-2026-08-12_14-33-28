using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// ============================================================================
// MovUBuilds.cs — Compilados de Windows, Linux y Mac con un solo comando
// ============================================================================
// Menú: MovU > Compilar > Windows / Linux / Mac / Los tres.
// Línea de comandos (con el editor cerrado):
//
//   Unity.exe -batchmode -nographics -quit -projectPath <proyecto> ^
//             -executeMethod MovUBuilds.Todos -logFile build.log
//
// Compila SOLO Edificio.unity (el juego): así el ejecutable arranca en el
// edificio y no en el laberinto del demo, que aparece primero en Build
// Settings. Los compilados quedan en Builds/Windows, Builds/Linux y Builds/Mac
// (esa carpeta está en .gitignore).
// ============================================================================

public static class MovUBuilds
{
    private const string Escena = "Assets/MovU/Scenes/Edificio.unity";

    [MenuItem("MovU/Compilar/Windows")] public static void Windows() { Construir(BuildTarget.StandaloneWindows64, "Windows/MovU.exe"); }
    [MenuItem("MovU/Compilar/Linux")] public static void Linux() { Construir(BuildTarget.StandaloneLinux64, "Linux/MovU.x86_64"); }
    [MenuItem("MovU/Compilar/Mac")] public static void Mac() { Construir(BuildTarget.StandaloneOSX, "Mac/MovU.app"); }

    [MenuItem("MovU/Compilar/Los tres")]
    public static void Todos()
    {
        Windows();
        Linux();
        Mac();
    }

    private static void Construir(BuildTarget destino, string ruta)
    {
        string raiz = Path.GetDirectoryName(Application.dataPath);
        string salida = Path.Combine(raiz, "Builds", ruta);
        Directory.CreateDirectory(Path.GetDirectoryName(salida));

        var opciones = new BuildPlayerOptions
        {
            scenes = new[] { Escena },
            locationPathName = salida,
            target = destino,
            options = BuildOptions.None,
        };

        Debug.Log($"[MovUBuilds] Compilando {destino} en {salida}");
        BuildReport informe = BuildPipeline.BuildPlayer(opciones);
        BuildSummary resumen = informe.summary;

        if (resumen.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[MovUBuilds] {destino}: {resumen.result} ({resumen.totalErrors} errores).");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[MovUBuilds] {destino} listo: {resumen.totalSize / (1024 * 1024)} MB en {resumen.totalTime.TotalSeconds:F0} s.");
    }
}
