using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// ============================================================================
// MovUPruebas.cs — Pruebas automáticas (Unity Test Framework, modo edición)
// ============================================================================
// Se corren en  Window > General > Test Runner > EditMode > Run All.
// No abren ninguna escena ni necesitan dar Play: revisan los DATOS y los
// CÁLCULOS, que es donde un error pasa sin que nadie lo vea.
//
// Cada prueba dice a qué requerimiento del SRS responde, para llenar la columna
// "caso de prueba" de la matriz de trazabilidad.
// ============================================================================

public class MovUPruebas
{
    private const string RutaJson = "Assets/MovU/Resources/MovU/contenido_piso9.json";

    /// <summary>Triángulos de un piso: planta de Meshy (78.878) + relleno de corona (15.562).</summary>
    private const int TriangulosDeUnPiso = 94440;
    private const int PresupuestoRD4 = 100000;

    private static ContenidoPiso Leer()
    {
        var archivo = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaJson);
        Assert.IsNotNull(archivo, "Falta " + RutaJson);
        ContenidoPiso d = ContenidoLoader.Interpretar(archivo.text);
        Assert.IsNotNull(d, "El JSON no se pudo interpretar: revisa comas y llaves.");
        return d;
    }

    // ------------------------------------------------------------------
    // Contenido (SRS: misiones como datos; RF-3, RF-4)
    // ------------------------------------------------------------------
    [Test]
    public void Contenido_TieneAlMenosDosMisionesConPoi()
    {
        ContenidoPiso d = Leer();
        Assert.GreaterOrEqual(d.misiones.Count, 2, "El hito H3 pide al menos dos misiones.");

        foreach (MisionDef m in d.misiones)
        {
            Assert.IsFalse(string.IsNullOrEmpty(m.id), "Hay una misión sin id.");
            Assert.IsFalse(string.IsNullOrEmpty(m.enunciado), $"La misión {m.id} no tiene enunciado.");
            Assert.IsTrue(d.pois.Exists(p => p.id == m.poi),
                          $"La misión {m.id} apunta al POI '{m.poi}', que no está en el JSON.");
        }
    }

    [Test]
    public void Contenido_LosIdsNoSeRepiten()
    {
        ContenidoPiso d = Leer();

        var vistos = new HashSet<string>();
        foreach (PoiDef p in d.pois)
        {
            Assert.IsTrue(vistos.Add(p.id.ToLowerInvariant()), $"El id de POI '{p.id}' está repetido.");
        }

        vistos.Clear();
        foreach (MisionDef m in d.misiones)
        {
            Assert.IsTrue(vistos.Add(m.id.ToLowerInvariant()), $"El id de misión '{m.id}' está repetido.");
        }
    }

    [Test]
    public void Contenido_CategoriasYRolesExisten()
    {
        ContenidoPiso d = Leer();

        foreach (PoiDef p in d.pois)
        {
            Assert.IsTrue(PointOfInterest.CategoriaDesdeTexto(p.categoria, out _),
                          $"POI '{p.id}': la categoría '{p.categoria}' no existe.");
        }
        foreach (NpcDef n in d.npcs)
        {
            Assert.IsTrue(NpcCharacter.RolDesdeTexto(n.rol, out _),
                          $"NPC '{n.id}': el rol '{n.rol}' no existe.");
        }
    }

    [Test]
    public void Contenido_TodoCaeDentroDeLaPlanta()
    {
        ContenidoPiso d = Leer();

        foreach (PoiDef p in d.pois)
        {
            Assert.That(p.u, Is.InRange(0f, 1f), $"POI '{p.id}': u fuera de la planta.");
            Assert.That(p.v, Is.InRange(0f, 1f), $"POI '{p.id}': v fuera de la planta.");
            Assert.Greater(p.radio, 0f, $"POI '{p.id}': el radio de llegada debe ser mayor que cero.");
        }
        foreach (NpcDef n in d.npcs)
        {
            Assert.That(n.u, Is.InRange(0f, 1f), $"NPC '{n.id}': u fuera de la planta.");
            Assert.That(n.v, Is.InRange(0f, 1f), $"NPC '{n.id}': v fuera de la planta.");
        }
        foreach (PuntoUV r in d.recorridos)
        {
            Assert.That(r.u, Is.InRange(0f, 1f));
            Assert.That(r.v, Is.InRange(0f, 1f));
        }
    }

    [Test]
    public void Contenido_LosDosEspaciosClaveSonDestinoDeMision()
    {
        ContenidoPiso d = Leer();

        foreach (PoiDef p in d.pois)
        {
            if (!p.importante) continue;
            Assert.IsTrue(d.misiones.Exists(m => m.poi == p.id),
                          $"'{p.nombre}' está marcado como importante pero ninguna misión lleva a él.");
        }
    }

    // ------------------------------------------------------------------
    // Coordenadas de planta
    // ------------------------------------------------------------------
    [Test]
    public void Plano_IdaYVueltaDevuelveElMismoPunto()
    {
        var plano = new PlanoDePlanta { xMin = -19.65f, xMax = 19.65f, zMin = -53.31f, zMax = 53.31f, valido = true };

        Vector3 mundo = plano.AMundo(0.6959f, 0.5370f, 0.82f);
        Vector2 uv = plano.AUV(mundo);

        Assert.AreEqual(0.6959f, uv.x, 0.0001f);
        Assert.AreEqual(0.5370f, uv.y, 0.0001f);
        Assert.AreEqual(0.82f, mundo.y, 0.0001f);
    }

    [Test]
    public void Plano_LasPosicionesSobrevivenAUnCambioDeEscala()
    {
        // La escala del edificio sigue por decidir: el mismo (u, v) tiene que
        // caer en el mismo sitio RELATIVO de una planta el doble de grande.
        var chica = new PlanoDePlanta { xMin = -10f, xMax = 10f, zMin = -25f, zMax = 25f, valido = true };
        var grande = new PlanoDePlanta { xMin = -20f, xMax = 20f, zMin = -50f, zMax = 50f, valido = true };

        Vector3 a = chica.AMundo(0.25f, 0.75f, 0f);
        Vector3 b = grande.AMundo(0.25f, 0.75f, 0f);

        Assert.AreEqual(a.x * 2f, b.x, 0.0001f);
        Assert.AreEqual(a.z * 2f, b.z, 0.0001f);
    }

    // ------------------------------------------------------------------
    // Métricas (SRS RF-12)
    // ------------------------------------------------------------------
    private static RunMetrics Corrida(float distancia, float optima, bool abortada)
    {
        return new RunMetrics(60f, distancia, optima, "Off", 0, 0, 0, 0, 0, 0f,
                              true, abortada, 60f, 60f, 0f, 0f, distancia, 0f, 0f);
    }

    [Test]
    public void Metricas_DesvioYSplSegunLaFormula()
    {
        RunMetrics r = Corrida(80f, 50f, false);
        Assert.AreEqual(1.6f, r.detourRatio, 0.0001f, "Desvío = recorrida / óptima.");
        Assert.AreEqual(0.625f, r.splMetric, 0.0001f, "SPL = óptima / max(recorrida, óptima).");
    }

    [Test]
    public void Metricas_SplNuncaPasaDeUno()
    {
        // Si el jugador caminó MENOS que la "óptima" (pasa por redondeos del
        // NavMesh), el SPL se queda en 1 y no sube de ahí.
        RunMetrics r = Corrida(40f, 50f, false);
        Assert.AreEqual(1f, r.splMetric, 0.0001f);
    }

    [Test]
    public void Metricas_UnaCorridaAbortadaTieneSplCero()
    {
        RunMetrics r = Corrida(80f, 50f, true);
        Assert.AreEqual(0f, r.splMetric, 0.0001f);
    }

    [Test]
    public void Csv_LaCabeceraTieneLasColumnasDeMisionYParticipante()
    {
        string[] columnas = MetricsLogger.Header.Split(',');

        Assert.AreEqual(36, columnas.Length, "Si cambias columnas, cambia también la fila en LogRun.");
        CollectionAssert.Contains(columnas, "ParticipantId");
        CollectionAssert.Contains(columnas, "MissionId");
        CollectionAssert.Contains(columnas, "FloorChanges");
        CollectionAssert.Contains(columnas, "WrongFloorVisits");
        CollectionAssert.Contains(columnas, "CaptureRadius");
        CollectionAssert.Contains(columnas, "NpcConsults");
    }

    [Test]
    public void Csv_UnTextoConComasNoRompeLasColumnas()
    {
        string limpio = MetricsLogger.Limpiar(" P01, grupo \"A\"\n");
        StringAssert.DoesNotContain(",", limpio);
        StringAssert.DoesNotContain("\n", limpio);
        StringAssert.DoesNotContain("\"", limpio);
    }

    // ------------------------------------------------------------------
    // Rendimiento (SRS RD-4: no más de 100.000 triángulos con un piso cargado)
    // ------------------------------------------------------------------
    [Test]
    public void Npc_LaFiguraEsLiviana()
    {
        Mesh malla = NpcMeshFactory.Malla;
        Assert.AreEqual(3, malla.subMeshCount, "Ropa, piel y pantalón.");
        Assert.LessOrEqual(NpcMeshFactory.Triangulos, 150, "La figura de un NPC debe ser de pocos triángulos.");
        Assert.AreEqual(1.8f, malla.bounds.max.y, 0.01f, "Mide 1,80 m con los pies en y = 0.");
    }

    [Test]
    public void Presupuesto_ElPisoConTodaSuGenteCabeEnRD4()
    {
        ContenidoPiso d = Leer();
        int gente = d.npcs.Count + d.multitud.estudiantes + d.multitud.visitantes;
        int total = TriangulosDeUnPiso + gente * NpcMeshFactory.Triangulos;

        Assert.LessOrEqual(total, PresupuestoRD4,
            $"{gente} NPC llevan el piso a {total:N0} triángulos; el SRS deja {PresupuestoRD4:N0}.");
    }
}
