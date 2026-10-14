using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// Piso13Builder.cs — El piso 13, desde su plano de evacuación
// ============================================================================
// La planta sale de Models/Piso13/planta_piso13.json, que genera
// Tools/piso13_planta.py (plano del "treceavo piso", fotos y videos del 14 oct
// 2026). La arma PlantaBuilder con los materiales y texturas del piso 9 (mismo
// edificio, mismos acabados), más los propios del 13: doble altura, perfiles
// dorados, pared gris sobre la madera y baños de pizarra.
//
// En la pila del juego es el piso de arriba (3, 9, 13): se llega en ascensor.
// ============================================================================

public static class Piso13Builder
{
    public const string Planta = "piso13";

    public static readonly PlantaBuilder.Config Config = new PlantaBuilder.Config
    {
        planta = Planta,
        etiqueta = "Piso 13",
        rutaPlanta = "Assets/MovU/Models/Piso13/planta_piso13.json",
        carpetaMallas = "Assets/MovU/Models/Piso13/Mallas",
        carpetaMateriales = "Assets/MovU/Materials/Piso13",
        prefijoMaterial = "P13_",
        carpetasTexturas = new[] { "Assets/MovU/Textures/Piso9", "Assets/MovU/Textures/Piso3" },
        texturas = new Dictionary<string, string>
        {
            { "Concreto", "P3_Concreto.png" }, { "Madera", "P9_Madera.png" }, { "Piso", "P3_Piso.png" },
            { "Granito", "P9_Granito.png" }, { "Terrazo", "P9_Terrazo.png" },
        },
    };

    public static bool HayPlanta => PlantaBuilder.HayPlanta(Config);

    public static Transform Construir(Transform padre, string nombre, Vector3 pivote,
                                      float alturaLosa, PlanoDePlanta plano)
    {
        return PlantaBuilder.Construir(Config, "Piso13_", padre, nombre, pivote, alturaLosa, plano, 0f);
    }
}
