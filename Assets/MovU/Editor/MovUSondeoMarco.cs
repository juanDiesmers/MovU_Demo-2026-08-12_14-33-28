using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Sondeo: el marco de referencia del edificio (plano y alturas) en Temp.
public static class MovUSondeoMarco
{
    [MenuItem("MovU/Contenido/Sondear el marco del edificio (informe en Temp)")]
    public static void Sondear()
    {
        var sb = new StringBuilder();
        sb.AppendLine("escena " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        var fm = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (fm != null)
        {
            for (int i = 0; i < fm.CantidadDePisos; i++)
            {
                Transform p = fm.Piso(i);
                PlanoDePlanta pl = PlanoDePlanta.Medir(p);
                sb.AppendLine($"piso {i} {p.name} pos={p.position.ToString("F4")} planta={fm.PlantaDe(i)} suelo={fm.AlturaDelSuelo(i):F4} " +
                              $"plano x[{pl.xMin:F4},{pl.xMax:F4}] z[{pl.zMin:F4},{pl.zMax:F4}]");
                foreach (Transform c in p) sb.AppendLine("   - " + c.name + " " + c.position.ToString("F3") + " act=" + c.gameObject.activeSelf);
            }
            sb.AppendLine("losa " + fm.AlturaDeLaLosa.ToString("F4") + " sep " + fm.SeparacionEntrePisos.ToString("F4"));
        }
        foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            sb.AppendLine("raiz " + r.name);
            foreach (Transform c in r.transform) sb.AppendLine("   . " + c.name);
        }
        var pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null) sb.AppendLine("jugador " + pc.transform.position.ToString("F3"));
        string carpeta = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "MovUSondeo");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "marco.txt"), sb.ToString());
        Debug.Log("[Sondeo] marco.txt escrito");
    }
}
