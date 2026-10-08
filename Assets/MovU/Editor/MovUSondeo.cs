using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// ============================================================================
// MovUSondeo.cs — Mide la planta por trazado de rayos y lo escribe en un texto
// ============================================================================
// Herramienta de diagnóstico: no cambia nada de la escena. Deja en
// Temp/MovUSondeo/ un mapa de ocupación del piso 1 (muro / libre) y la
// jerarquía del edificio. Sirve para colocar puertas, escaleras y mobiliario
// con números y no a ojo.
// ============================================================================

public static class MovUSondeo
{
    private const float Celda = 0.5f;

    [MenuItem("MovU/Contenido/Sondear la planta (informe en Temp)")]
    public static void Sondear()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        FloorManager pisos = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (pisos == null) { Debug.LogError("[Sondeo] No hay FloorManager."); return; }

        Physics.SyncTransforms();

        sb.AppendLine("# Jerarquia");
        foreach (GameObject raiz in pisos.gameObject.scene.GetRootGameObjects())
            Volcar(raiz.transform, 0, sb, 3);

        sb.AppendLine();
        sb.AppendLine("# Pisos");
        for (int i = 0; i < pisos.CantidadDePisos; i++)
        {
            Transform p = pisos.Piso(i);
            PlanoDePlanta pl = PlanoDePlanta.Medir(p);
            sb.AppendLine(string.Format(ci, "piso {0}: activo={1} base={2:F3} suelo={3:F3} x[{4:F2},{5:F2}] z[{6:F2},{7:F2}]",
                i, p.gameObject.activeInHierarchy, p.position.y, pisos.AlturaDelSuelo(i), pl.xMin, pl.xMax, pl.zMin, pl.zMax));
        }
        sb.AppendLine(string.Format(ci, "separacion={0:F3} alturaLosa={1:F3}", pisos.SeparacionEntrePisos, pisos.AlturaDeLaLosa));

        PlanoDePlanta plano = PlanoDePlanta.Medir(pisos.Piso(0));
        float suelo = pisos.AlturaDelSuelo(0);
        float desde = suelo + Mathf.Max(3f, pisos.SeparacionEntrePisos - 0.6f);

        // Altura del techo en el punto de aparicion.
        int nx = Mathf.CeilToInt((plano.xMax - plano.xMin) / Celda);
        int nz = Mathf.CeilToInt((plano.zMax - plano.zMin) / Celda);
        sb.AppendLine(string.Format(ci, "mapa: {0} x {1} celdas de {2} m; origen x={3:F2} z={4:F2}; rayo desde y={5:F2}",
            nx, nz, Celda, plano.xMin, plano.zMin, desde));

        var alturas = new float[nx, nz];
        var hist = new int[12];
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            Vector3 o = new Vector3(plano.xMin + (ix + 0.5f) * Celda, desde, plano.zMin + (iz + 0.5f) * Celda);
            float h = -9f;
            if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, desde - suelo + 3f, ~0, QueryTriggerInteraction.Ignore))
                h = hit.point.y - suelo;
            alturas[ix, iz] = h;
            if (h > 0.3f) hist[Mathf.Clamp(Mathf.FloorToInt(h / 0.5f), 0, 11)]++;
        }
        sb.Append("histograma de alturas de muro (pasos de 0,5 m): ");
        for (int i = 0; i < 12; i++) sb.Append(hist[i]).Append(' ');
        sb.AppendLine();

        // Mapa: '.' libre, '#' muro, ' ' vacio (sin suelo). Fila de arriba = z maximo.
        var mapa = new char[nx, nz];
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            float h = alturas[ix, iz];
            mapa[ix, iz] = h < -5f ? ' ' : (h > 0.3f ? '#' : (h < -0.3f ? 'v' : '.'));
        }

        ContenidoPiso datos = ContenidoLoader.Leer();
        if (datos != null)
        {
            for (int i = 0; i < datos.pois.Count; i++)
            {
                PoiDef d = datos.pois[i];
                Marcar(mapa, nx, nz, d.u, d.v, (char)('A' + i));
                if (Mathf.Abs(d.rotuloU) > 0.0001f || Mathf.Abs(d.rotuloV) > 0.0001f)
                    Marcar(mapa, nx, nz, d.rotuloU, d.rotuloV, (char)('a' + i));
                sb.AppendLine(string.Format(ci, "POI {0}/{1} = {2}", (char)('A' + i), (char)('a' + i), d.id));
            }
            Marcar(mapa, nx, nz, datos.aparicion.u, datos.aparicion.v, '@');
            Marcar(mapa, nx, nz, datos.ascensor.u, datos.ascensor.v, '$');
            for (int i = 0; i < datos.npcs.Count; i++) Marcar(mapa, nx, nz, datos.npcs[i].u, datos.npcs[i].v, 'n');
            for (int i = 0; i < datos.recorridos.Count; i++) Marcar(mapa, nx, nz, datos.recorridos[i].u, datos.recorridos[i].v, '+');
        }

        sb.AppendLine();
        sb.AppendLine("# Mapa (columna = x creciente, fila superior = z maximo)");
        for (int iz = nz - 1; iz >= 0; iz--)
        {
            sb.Append(iz.ToString("000")).Append(' ');
            for (int ix = 0; ix < nx; ix++) sb.Append(mapa[ix, iz]);
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("# Alturas (digito = altura / 0,5 m; '.' = suelo)");
        for (int iz = nz - 1; iz >= 0; iz--)
        {
            sb.Append(iz.ToString("000")).Append(' ');
            for (int ix = 0; ix < nx; ix++)
            {
                float h = alturas[ix, iz];
                char c = h < -5f ? ' ' : (h <= 0.12f ? '.' : "0123456789ABCDEF"[Mathf.Clamp(Mathf.FloorToInt(h / 0.5f), 0, 15)]);
                sb.Append(c);
            }
            sb.AppendLine();
        }

        // Altura libre (suelo -> techo con collider) en la aparicion.
        if (datos != null && ContenidoLoader.AMundo(pisos, 1, datos.aparicion.u, datos.aparicion.v, out Vector3 ap))
        {
            bool techo = Physics.Raycast(ap + Vector3.up * 0.2f, Vector3.up, out RaycastHit arriba, 30f, ~0, QueryTriggerInteraction.Ignore);
            sb.AppendLine(string.Format(ci, "aparicion {0}: techo con collider = {1} a {2:F2} m", ap, techo, techo ? arriba.distance + 0.2f : -1f));
        }

        string carpeta = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "MovUSondeo");
        Directory.CreateDirectory(carpeta);
        string ruta = Path.Combine(carpeta, "sondeo.txt");
        File.WriteAllText(ruta, sb.ToString());
        Debug.Log("[Sondeo] Informe escrito en " + ruta);
    }

    /// <summary>Alturas en decimetros sobre el suelo, en una ventana fina. Para escaleras y puertas.</summary>
    public static void Ventana(StringBuilder sb, float suelo, float desde, float x0, float x1, float z0, float z1, float paso)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine(string.Format(ci, "# Ventana x[{0:F2},{1:F2}] z[{2:F2},{3:F2}] paso {4:F2} (dm sobre el suelo; fila superior = z maximo)", x0, x1, z0, z1, paso));
        sb.Append("       ");
        for (float x = x0; x <= x1 + 0.001f; x += paso) sb.Append(string.Format(ci, "{0,3:F0}", Mathf.Abs(x * 10f) % 1000 / 10));
        sb.AppendLine();
        for (float z = z1; z >= z0 - 0.001f; z -= paso)
        {
            sb.Append(string.Format(ci, "{0,6:F1} ", z));
            for (float x = x0; x <= x1 + 0.001f; x += paso)
            {
                float h = -99f;
                if (Physics.Raycast(new Vector3(x, desde, z), Vector3.down, out RaycastHit hit, desde - suelo + 3f, ~0, QueryTriggerInteraction.Ignore))
                    h = hit.point.y - suelo;
                if (h < -9f) sb.Append("  ~");
                else if (h < 0.05f) sb.Append("  .");
                else sb.Append(string.Format(ci, "{0,3:F0}", Mathf.Min(99f, h * 10f)));
            }
            sb.AppendLine();
        }
    }

    [MenuItem("MovU/Contenido/Sondear la escalera (informe en Temp)")]
    public static void SondearEscalera()
    {
        FloorManager pisos = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        if (pisos == null) return;
        Physics.SyncTransforms();
        float suelo = pisos.AlturaDelSuelo(0);
        float desde = suelo + Mathf.Max(3f, pisos.SeparacionEntrePisos - 0.6f);
        var sb = new StringBuilder();
        Ventana(sb, suelo, desde, 11.6f, 18.6f, -31.6f, -16.8f, 0.2f);
        string carpeta = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "MovUSondeo");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "escalera.txt"), sb.ToString());
        Debug.Log("[Sondeo] escalera.txt escrito.");
    }

    [MenuItem("MovU/Contenido/Sondear las puertas (informe en Temp)")]
    public static void SondearPuertas()
    {
        var ci = CultureInfo.InvariantCulture;
        FloorManager pisos = Object.FindFirstObjectByType<FloorManager>(FindObjectsInactive.Include);
        ContenidoPiso datos = ContenidoLoader.Leer();
        if (pisos == null || datos == null) return;
        Physics.SyncTransforms();
        float suelo = pisos.AlturaDelSuelo(0);
        float desde = suelo + Mathf.Max(3f, pisos.SeparacionEntrePisos - 0.6f);
        const float paso = 0.2f, medio = 4.2f;
        var sb = new StringBuilder();
        foreach (PoiDef d in datos.pois)
        {
            bool tienePuerta = Mathf.Abs(d.rotuloU) > 0.0001f || Mathf.Abs(d.rotuloV) > 0.0001f;
            if (!tienePuerta) continue;
            ContenidoLoader.AMundo(pisos, 1, d.rotuloU, d.rotuloV, out Vector3 r);
            ContenidoLoader.AMundo(pisos, 1, d.u, d.v, out Vector3 c);
            sb.AppendLine(string.Format(ci, "## {0}: rotulo=({1:F2},{2:F2}) poi=({3:F2},{4:F2}); ventana x0={5:F2} zTop={6:F2} paso={7}",
                d.id, r.x, r.z, c.x, c.z, r.x - medio, r.z + medio, paso));
            int n = Mathf.RoundToInt(2f * medio / paso);
            for (int iz = n; iz >= 0; iz--)
            {
                float z = r.z - medio + iz * paso;
                sb.Append(string.Format(ci, "{0,7:F1} ", z));
                for (int ix = 0; ix <= n; ix++)
                {
                    float x = r.x - medio + ix * paso;
                    float h = -99f;
                    if (Physics.Raycast(new Vector3(x, desde, z), Vector3.down, out RaycastHit hit, desde - suelo + 3f, ~0, QueryTriggerInteraction.Ignore))
                        h = hit.point.y - suelo;
                    char ch = h < -9f ? '~' : (h > 2.2f ? '#' : (h > 0.25f ? '+' : '.'));
                    if (Mathf.Abs(x - r.x) < paso * 0.5f && Mathf.Abs(z - r.z) < paso * 0.5f) ch = 'o';
                    if (Mathf.Abs(x - c.x) < paso * 0.5f && Mathf.Abs(z - c.z) < paso * 0.5f) ch = 'P';
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
        }
        string carpeta = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "MovUSondeo");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "puertas.txt"), sb.ToString());
        Debug.Log("[Sondeo] puertas.txt escrito.");
    }

    private static void Marcar(char[,] mapa, int nx, int nz, float u, float v, char c)
    {
        int ix = Mathf.Clamp(Mathf.FloorToInt(u * nx), 0, nx - 1);
        int iz = Mathf.Clamp(Mathf.FloorToInt(v * nz), 0, nz - 1);
        mapa[ix, iz] = c;
    }

    private static void Volcar(Transform t, int nivel, StringBuilder sb, int maximo)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.Append(new string(' ', nivel * 2)).Append(t.name);
        sb.Append(t.gameObject.activeSelf ? "" : " [apagado]");
        var mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            sb.Append(string.Format(ci, " malla={0} tris={1}", mf.sharedMesh.name, mf.sharedMesh.triangles.Length / 3));
        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform || c is MeshFilter) continue;
            sb.Append(" <").Append(c.GetType().Name).Append('>');
        }
        sb.Append(string.Format(ci, " pos=({0:F2},{1:F2},{2:F2}) rotY={3:F0} esc=({4:F2},{5:F2},{6:F2})",
            t.position.x, t.position.y, t.position.z, t.eulerAngles.y, t.lossyScale.x, t.lossyScale.y, t.lossyScale.z));
        sb.AppendLine();
        if (nivel >= maximo) return;
        for (int i = 0; i < t.childCount; i++) Volcar(t.GetChild(i), nivel + 1, sb, maximo);
    }
}
