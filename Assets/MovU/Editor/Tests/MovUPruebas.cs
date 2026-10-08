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

    /// <summary>
    /// Triángulos de un piso: planta de Meshy (78.878) + relleno de corona (14.750).
    /// El relleno eran 15.562 hasta que se le abrió el hueco de la escalera.
    /// </summary>
    private const int TriangulosDeUnPiso = 93628;
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

    // ------------------------------------------------------------------
    // Ambientación, inventario y escalera (octubre de 2026)
    // ------------------------------------------------------------------
    [Test]
    public void Inventario_ElJugadorEmpiezaConElCarne()
    {
        ContenidoPiso d = Leer();

        var vistos = new HashSet<string>();
        foreach (ObjetoDef o in d.objetos)
        {
            Assert.IsFalse(string.IsNullOrEmpty(o.id), "Hay un objeto sin id.");
            Assert.IsFalse(string.IsNullOrEmpty(o.nombre), $"El objeto {o.id} no tiene nombre.");
            Assert.IsTrue(vistos.Add(o.id.ToLowerInvariant()), $"El id de objeto '{o.id}' está repetido.");
            Assert.LessOrEqual(o.sigla.Length, 3, $"La sigla de '{o.id}' no cabe en la casilla.");
        }

        ObjetoDef carne = d.objetos.Find(o => o.id == "carne");
        Assert.IsNotNull(carne, "Falta el carné en 'objetos'.");
        Assert.IsTrue(carne.inicial, "El carné tiene que estar desde el principio.");
    }

    [Test]
    public void Inventario_LoQueEntreganLasMisionesEstaEnElCatalogo()
    {
        ContenidoPiso d = Leer();

        foreach (MisionDef m in d.misiones)
        {
            if (string.IsNullOrEmpty(m.entrega)) continue;
            ObjetoDef o = d.objetos.Find(x => x.id == m.entrega);
            Assert.IsNotNull(o, $"La misión {m.id} entrega '{m.entrega}', que no está en 'objetos'.");
            Assert.IsFalse(o.inicial, $"'{o.id}' se entrega en la misión {m.id}: no puede ser inicial.");
        }
    }

    [Test]
    public void Inventario_LaDescripcionDelCarneLlevaAlParticipante()
    {
        var o = new ObjetoDef { id = "x", descripcion = "Carné de {participante}." };
        // Fuera de Play no hay sesión: se pone una raya, nunca el texto crudo.
        StringAssert.DoesNotContain("{participante}", InventoryManager.Descripcion(o));
    }

    [Test]
    public void Puertas_CadaUnaTieneMedidasYPoiValidos()
    {
        ContenidoPiso d = Leer();
        Assert.Greater(d.puertas.Count, 0, "No hay puertas en el JSON.");

        var vistos = new HashSet<string>();
        foreach (PuertaDef p in d.puertas)
        {
            Assert.IsTrue(vistos.Add(p.id.ToLowerInvariant()), $"El id de puerta '{p.id}' está repetido.");
            Assert.That(p.u, Is.InRange(0f, 1f), $"Puerta {p.id}: u fuera de la planta.");
            Assert.That(p.v, Is.InRange(0f, 1f), $"Puerta {p.id}: v fuera de la planta.");
            Assert.That(p.ancho, Is.InRange(0.8f, 4f), $"Puerta {p.id}: ancho raro.");
            if (!string.IsNullOrEmpty(p.poi))
            {
                Assert.IsTrue(d.pois.Exists(x => x.id == p.poi),
                              $"La puerta {p.id} da paso al POI '{p.poi}', que no existe.");
            }
        }
    }

    [Test]
    public void Escalera_SusMedidasSonLasDeUnaEscaleraCaminable()
    {
        ContenidoPiso d = Leer();
        EscaleraDef e = d.escalera;
        Assert.IsTrue(e.usar, "La escalera está apagada en el JSON.");

        Assert.Less(e.tramo.u0, e.tramo.u1);
        Assert.Less(e.tramo.v0, e.tramo.v1, "El tramo sube en +v: v0 es el pie y v1 la cima.");
        Assert.LessOrEqual(e.tramo.v1, e.rellano.v0 + 0.0001f, "El rellano empieza donde acaba el tramo o después.");
        Assert.Less(e.rellano.v0, e.rellano.v1);
        Assert.That(e.puertaU, Is.InRange(e.ocultoU0, e.tramo.u0), "Las puertas van en el tabique del tramo oculto.");

        // Con la planta de 106,62 m de largo que tiene hoy el edificio.
        const float largoDeLaPlanta = 106.62f;
        float largo = (e.tramo.v1 - e.tramo.v0) * largoDeLaPlanta;
        float contrahuella = e.alturaDelRellano / e.peldanos;
        float pendiente = Mathf.Atan2(e.alturaDelRellano, largo) * Mathf.Rad2Deg;

        Assert.That(contrahuella, Is.InRange(0.14f, 0.21f), "Contrahuella fuera de lo cómodo.");
        Assert.Less(pendiente, 40f, "Más de 40° y el CharacterController (límite 45°) no sube con margen.");
    }

    [Test]
    public void Formas_LasMallasCuestanLoQueDicen()
    {
        Assert.AreEqual(FormasMovU.TriangulosDeCaja, FormasMovU.Cubo.triangles.Length / 3);
        Assert.AreEqual(FormasMovU.TriangulosDePlaca, FormasMovU.Placa.triangles.Length / 3);

        Mesh escalones = FormasMovU.Escalones(18, 2.8f, 7.6f, 3.55f, out int triangulos);
        Assert.AreEqual(18 * 2 + 17 * 2, triangulos, "Dos por contrahuella y dos por huella.");
        Assert.AreEqual(3.55f, escalones.bounds.max.y, 0.001f);
        Assert.AreEqual(7.6f, escalones.bounds.max.z, 0.001f);
        Object.DestroyImmediate(escalones);
    }

    [Test]
    public void Presupuesto_ElPisoAmbientadoCabeEnRD4()
    {
        ContenidoPiso d = Leer();

        int gente = d.npcs.Count + d.multitud.estudiantes + d.multitud.visitantes;
        int puertas = d.puertas.Count * DoorManager.TriangulosPorPuerta;
        int escalera = d.escalera.usar ? StairsManager.Triangulos(d.escalera.peldanos) : 0;
        int mostradores = d.mobiliario.Count * 6 * FormasMovU.TriangulosDeCaja;
        // Techo (1 placa) + lámparas: una cada 6 m sobre 39 x 107 m son 126 como mucho.
        int techo = (1 + 126) * FormasMovU.TriangulosDePlaca;
        int ascensor = 8 * FormasMovU.TriangulosDeCaja;
        int jugador = NpcMeshFactory.Triangulos;

        int total = TriangulosDeUnPiso + gente * NpcMeshFactory.Triangulos +
                    puertas + escalera + mostradores + techo + ascensor + jugador;

        Assert.LessOrEqual(total, PresupuestoRD4,
            $"El piso ambientado llega a {total:N0} triángulos; el SRS deja {PresupuestoRD4:N0}.");
    }
}
