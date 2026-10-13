using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// ============================================================================
// MovUPruebasPiso3.cs — Pruebas de modo edición del piso 3 y la pila de pisos
// ============================================================================
// Window > General > Test Runner > EditMode > Run All
// ============================================================================

public class MovUPruebasPiso3
{
    private const string RutaPlanta = "Assets/MovU/Models/Piso3/planta_piso3.json";
    private const string CarpetaMallas = "Assets/MovU/Models/Piso3/Mallas";

    private static ContenidoPiso Contenido()
    {
        var texto = Resources.Load<TextAsset>(ContenidoLoader.Recurso);
        Assert.IsNotNull(texto, "No encuentro el JSON de contenido.");
        return ContenidoLoader.Interpretar(texto.text);
    }

    [Test]
    public void Pisos_LosNumerosVisiblesSonTresNueveYDiez()
    {
        ContenidoPiso d = Contenido();
        CollectionAssert.AreEqual(new List<int> { 3, 9, 10 }, d.ajustes.numerosDePiso,
            "La pila es piso 3 (abajo), piso 9 y la copia del 9.");
    }

    [Test]
    public void Pisos_TodoElContenidoApuntaAUnPisoDeLaPila()
    {
        ContenidoPiso d = Contenido();
        int n = d.ajustes.numerosDePiso.Count;
        foreach (PoiDef p in d.pois)
            Assert.That(p.piso, Is.InRange(1, n), $"POI '{p.id}' en un piso que no existe.");
        foreach (NpcDef npc in d.npcs)
            Assert.That(npc.piso, Is.InRange(1, n), $"NPC '{npc.id}' en un piso que no existe.");
        foreach (PuertaDef pu in d.puertas)
            Assert.That(pu.piso, Is.InRange(0, n), $"Puerta '{pu.id}' en un piso que no existe.");
        Assert.AreEqual(2, d.aparicion.piso, "El jugador aparece en el piso 9, el segundo de la pila.");
    }

    [Test]
    public void Piso3_LosPoiDelPisoTresTienenSuPuertaORotulo()
    {
        ContenidoPiso d = Contenido();
        int delTres = 0;
        foreach (PoiDef p in d.pois)
        {
            if (p.piso != 1) continue;
            delTres++;
            Assert.IsTrue(p.rotuloU > 0f && p.rotuloV > 0f, $"POI '{p.id}' del piso 3 sin rótulo.");
        }
        Assert.GreaterOrEqual(delTres, 10, "El plano del piso 3 nombra más de diez espacios.");
    }

    [Test]
    public void Piso3_LaPlantaExisteYCaeDentroDelEdificio()
    {
        var texto = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaPlanta);
        Assert.IsNotNull(texto, "Falta " + RutaPlanta + ". Genérala con: python3 Tools/piso3_planta.py");
        Assert.IsTrue(Piso3Builder.HayPlanta);

        // Lectura mínima: las medidas del edificio y que haya muros.
        StringAssert.Contains("\"muros\"", texto.text);
        StringAssert.Contains("\"ascensores\"", texto.text);
        StringAssert.Contains("\"ancho\": 39.3", texto.text);
        StringAssert.Contains("\"largo\": 106.6", texto.text);
    }

    [Test]
    public void Presupuesto_ElPisoTresCabeEnRD4()
    {
        string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { CarpetaMallas });
        if (guids.Length == 0) Assert.Ignore("El piso 3 todavía no se ha construido en esta copia del proyecto.");

        long triangulos = 0;
        foreach (string g in guids)
        {
            string ruta = AssetDatabase.GUIDToAssetPath(g);
            if (ruta.Contains("Colision") || ruta.Contains("Huella")) continue;
            var malla = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
            if (malla != null) triangulos += malla.triangles.Length / 3;
        }
        Assert.Less(triangulos, 20000, $"El piso 3 dibuja {triangulos:N0} triángulos; debería ser liviano.");
        Assert.Less(triangulos, 100000, "SRS RD-4: no más de 100.000 triángulos con un piso cargado.");
    }
}
