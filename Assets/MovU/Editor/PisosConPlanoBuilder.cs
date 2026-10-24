using UnityEngine;

// ============================================================================
// PisosConPlanoBuilder.cs — Los pisos 4 a 8 y 10 a 12, desde sus planos
// ============================================================================
// Hasta el 21 de octubre de 2026 eran genéricos (el núcleo del 9 con todas las
// puertas cerradas). Ahora cada uno tiene su planta, trazada desde la foto de
// su plano de evacuación por Tools/pisos_planta.py: los salones con su nombre
// real, las puertas abiertas y un POI por espacio, para poder escribirles
// misiones. El núcleo (escaleras, baños, cuartos, ascensores, lobby) es el del
// piso 9, igual que en el 13.
//
// Usan los materiales y texturas del 9 (mismo edificio, mismos acabados). Si a
// un piso le falta su planta, BuildingSetup sigue armándolo como genérico.
// ============================================================================

public static class PisosConPlanoBuilder
{
    /// <summary>Los pisos que tienen planta propia en Models/Pisos.</summary>
    public static readonly int[] Numeros = { 4, 5, 6, 7, 8, 10, 11, 12 };

    /// <summary>Nombre de la planta que se le pasa al FloorManager ("piso4", "piso10"...).</summary>
    public static string Planta(int numero) => "piso" + numero;

    public static PlantaBuilder.Config Config(int numero) => new PlantaBuilder.Config
    {
        planta = Planta(numero),
        etiqueta = "Piso " + numero,
        rutaPlanta = $"Assets/MovU/Models/Pisos/planta_piso{numero}.json",
        carpetaMallas = "Assets/MovU/Models/Pisos/Mallas",
        carpetaMateriales = "Assets/MovU/Materials/Piso9",
        prefijoMaterial = "P9_",
        carpetasTexturas = Piso9Builder.Config.carpetasTexturas,
        texturas = Piso9Builder.Config.texturas,
    };

    public static bool HayPlanta(int numero)
        => System.Array.IndexOf(Numeros, numero) >= 0 && PlantaBuilder.HayPlanta(Config(numero));

    /// <summary>Arma el piso 'numero' en 'pivote'; el "{n}" del lobby se vuelve su número.</summary>
    public static Transform Construir(Transform padre, int numero, Vector3 pivote, float alturaLosa, PlanoDePlanta plano)
    {
        return PlantaBuilder.Construir(Config(numero), $"Piso{numero}_", padre, $"Piso_{numero}", pivote, alturaLosa,
                                       plano, 0f, numero.ToString("00"));
    }
}
