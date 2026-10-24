using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// ============================================================================
// MovUPruebasPiso9.cs — Pruebas de la planta del piso 9 (sin Meshy)
// ============================================================================
// Revisan que la planta trazada (Models/Piso9/planta_piso9.json) y el contenido
// (contenido_piso9.json) encajen: que nada del contenido caiga dentro de un
// muro, un bloque o una mesa, y que cada puerta quede en un vano.
// ============================================================================

public class MovUPruebasPiso9
{
    private const string RutaJson = "Assets/MovU/Resources/MovU/contenido_piso9.json";
    private const float Ancho = 39.3f, Largo = 106.6f;

    [Serializable] private class Muro { public float[] a; public float[] b; public float g = 0.3f; public string nombre; }
    [Serializable] private class Caja { public float v0, v1, u0, u1; public string nombre; public string tipo; public bool solido = true; }
    [Serializable] private class Tramo { public float v0, v1, u0, u1; }
    [Serializable] private class Escalera { public List<Tramo> tramos; }
    [Serializable] private class Planta { public List<Muro> muros; public List<Caja> bloques; public List<Caja> muebles; public List<Escalera> escaleras; }

    private static Planta LeerPlanta() => LeerPlanta(Piso9Builder.Config.rutaPlanta);

    private static Planta LeerPlanta(string ruta)
    {
        var t = AssetDatabase.LoadAssetAtPath<TextAsset>(ruta);
        Assert.IsNotNull(t, "Falta " + ruta + ": corre el script de Tools/ que la genera.");
        return JsonUtility.FromJson<Planta>(t.text);
    }

    private static ContenidoPiso LeerContenido()
    {
        var t = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaJson);
        Assert.IsNotNull(t);
        return ContenidoLoader.Interpretar(t.text);
    }

    private static float DistanciaASegmento(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float t = d.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + d * t);
    }

    /// <summary>Lo sólido en el punto (v, u) en metros, a 'radio' de distancia; null si está libre.</summary>
    private static string Choca(Planta pl, float v, float u, float radio)
    {
        var p = new Vector2(v, u);
        foreach (Muro m in pl.muros)
            if (DistanciaASegmento(p, new Vector2(m.a[0], m.a[1]), new Vector2(m.b[0], m.b[1])) < m.g * 0.5f + radio)
                return "muro " + m.nombre;
        var cajas = new List<Caja>(pl.bloques);
        foreach (Caja c in pl.muebles) if (c.solido) cajas.Add(c);
        foreach (Escalera e in pl.escaleras)
            foreach (Tramo t in e.tramos) cajas.Add(new Caja { v0 = t.v0, v1 = t.v1, u0 = t.u0, u1 = t.u1, nombre = "escalera" });
        foreach (Caja c in cajas)
            if (v > c.v0 - radio && v < c.v1 + radio && u > c.u0 - radio && u < c.u1 + radio)
                return (c.nombre ?? c.tipo) + "";
        return null;
    }

    [Test]
    public void Piso9_LaPlantaExisteYTraeSuMarco()
    {
        Assert.IsTrue(Piso9Builder.HayPlanta, "Falta la planta del piso 9.");
        PlantaBuilder.Marco m = Piso9Builder.Marco;
        Assert.IsNotNull(m, "La planta del piso 9 no trae 'marco': sin él no se puede armar el edificio sin Meshy.");
        Assert.AreEqual(Ancho, m.xMax - m.xMin, 0.05f, "El marco no mide 39,3 m de ancho.");
        Assert.AreEqual(Largo, m.zMax - m.zMin, 0.05f, "El marco no mide 106,6 m de largo.");
    }

    [Test]
    public void Piso9_NadaDelContenidoCaeDentroDeAlgoSolido() => NadaCaeDentro(LeerPlanta(), 7, true);

    [Test]
    public void Piso13_NadaDelContenidoCaeDentroDeAlgoSolido()
        => NadaCaeDentro(LeerPlanta(Piso13Builder.Config.rutaPlanta), 11, false);

    [Test]
    public void Piso13_CadaPuertaQuedaEnUnVano()
        => PuertasEnVano(LeerPlanta(Piso13Builder.Config.rutaPlanta), 11);

    // --- Pisos 4 a 8 y 10 a 12, trazados desde sus planos (Tools/pisos_planta.py) ---

    [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(10)] [TestCase(11)] [TestCase(12)]
    public void PisoConPlano_NadaDelContenidoCaeDentroDeAlgoSolido(int numero)
    {
        Assert.IsTrue(PisosConPlanoBuilder.HayPlanta(numero), $"Falta la planta del piso {numero}: corre Tools/pisos_planta.py.");
        NadaCaeDentro(LeerPlanta(PisosConPlanoBuilder.Config(numero).rutaPlanta), numero - 2, false);
    }

    [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(10)] [TestCase(11)] [TestCase(12)]
    public void PisoConPlano_CadaPuertaQuedaEnUnVano(int numero)
        => PuertasEnVano(LeerPlanta(PisosConPlanoBuilder.Config(numero).rutaPlanta), numero - 2);

    [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(10)] [TestCase(11)] [TestCase(12)]
    public void PisoConPlano_TieneSusEspaciosEscalerasYAscensores(int numero)
    {
        ContenidoPiso d = LeerContenido();
        List<PoiDef> pois = d.pois.FindAll(p => p.piso == numero - 2);
        foreach (string id in new[] { "banos", "escalera_emergencia", "escalera_principal", "ascensores" })
            Assert.IsTrue(pois.Exists(p => p.id == $"p{numero}_{id}"), $"Falta el POI 'p{numero}_{id}'.");
        int espacios = pois.FindAll(p => p.categoria != "Bano" && p.categoria != "Escalera" && p.categoria != "Ascensor").Count;
        Assert.GreaterOrEqual(espacios, 3, $"El piso {numero} debería tener al menos tres espacios con POI.");
        // Cada POI de un espacio con puerta tiene la suya.
        foreach (PoiDef p in pois)
        {
            if (p.categoria == "Ascensor" || p.id.EndsWith("free_plaza")) continue;
            if (p.id.EndsWith("calidad_de_agua")) continue;      // se entra por los laboratorios vecinos
            Assert.IsTrue(d.puertas.Exists(q => q.poi == p.id && q.piso == numero - 2), $"El POI '{p.id}' no tiene puerta.");
        }
    }

    [Test]
    public void Piso13_TieneSusLaboratoriosConNombreReal()
    {
        ContenidoPiso d = LeerContenido();
        foreach (string id in new[] { "p13_virtualizacion", "p13_bioingenieria", "p13_tejidos",
                                      "p13_robotica_autonoma", "p13_robotica_educacion", "p13_nanolab" })
            Assert.IsTrue(d.pois.Exists(p => p.id == id && p.piso == 11), $"Falta el POI '{id}' en el piso 13.");
    }

    private static void NadaCaeDentro(Planta pl, int piso, bool conAparicion)
    {
        ContenidoPiso d = LeerContenido();
        var fallos = new List<string>();
        void Revisar(string que, float u, float v)
        {
            string c = Choca(pl, v * Largo, u * Ancho, 0.35f);
            if (c != null) fallos.Add($"{que} ({u * Ancho:F1}, {v * Largo:F1}) choca con {c}");
        }
        foreach (PoiDef p in d.pois) if (p.piso == piso) Revisar("POI " + p.id, p.u, p.v);
        foreach (NpcDef n in d.npcs) if (n.piso == piso) Revisar("NPC " + n.id, n.u, n.v);
        if (conAparicion)
        {
            foreach (PuntoUV r in d.recorridos) Revisar("recorrido", r.u, r.v);
            Revisar("aparición", d.aparicion.u, d.aparicion.v);
        }
        Revisar("ascensor", d.ascensor.u, d.ascensor.v);
        Assert.IsEmpty(fallos, string.Join("\n", fallos));
    }

    [Test]
    public void PisosGenericos_LaPlantaExisteYCierraTodoMenosLasEscaleras()
    {
        Assert.IsTrue(PisoGenericoBuilder.HayPlanta, "Falta la planta genérica: corre Tools/piso_generico_planta.py.");
        var t = AssetDatabase.LoadAssetAtPath<TextAsset>(PisoGenericoBuilder.Config.rutaPlanta);
        var g = JsonUtility.FromJson<Generica>(t.text);
        ContenidoPiso d = LeerContenido();
        int delNueve = d.puertas.FindAll(p => p.piso == 0).Count;
        Assert.AreEqual(delNueve - 2, g.puertasCerradas.Count,
            "Los genéricos cierran todas las puertas del 9 menos las de las dos escaleras.");
        Assert.IsEmpty(g.muebles, "Los salones de los pisos genéricos van vacíos.");
    }

    [Serializable] private class Cerrada { public float v, u, ancho; }
    [Serializable] private class Generica { public List<Cerrada> puertasCerradas; public List<Caja> muebles; }

    [Test]
    public void Piso9_CadaPuertaQuedaEnUnVano() => PuertasEnVano(LeerPlanta(), 0);

    private static void PuertasEnVano(Planta pl, int piso)
    {
        ContenidoPiso d = LeerContenido();
        var fallos = new List<string>();
        foreach (PuertaDef p in d.puertas)
        {
            if (p.piso != piso) continue;
            var c = new Vector2(p.v * Largo, p.u * Ancho);
            foreach (Muro m in pl.muros)
                if (DistanciaASegmento(c, new Vector2(m.a[0], m.a[1]), new Vector2(m.b[0], m.b[1])) < 0.2f)
                    fallos.Add($"La puerta {p.id} cae sobre el muro '{m.nombre}'.");
        }
        Assert.IsEmpty(fallos, string.Join("\n", fallos));
    }
}
