using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// Piso9Builder.cs — El piso 9 (y su copia, el 10) sin el modelo de Meshy
// ============================================================================
// La planta sale de Models/Piso9/planta_piso9.json, que genera
// Tools/piso9_planta.py a partir del letrero de la planta, el plano de
// evacuación y las fotos del recorrido (octubre de 2026). La arma PlantaBuilder.
//
// El JSON trae además el 'marco': dónde quedaba el piso 9 con el modelo de
// Meshy (plano, pivote, losa y separación). BuildingSetup lo usa para armar el
// edificio en el mismo sitio, así que el contenido, el NavMesh y las métricas
// siguen siendo comparables con las corridas anteriores.
// ============================================================================

public static class Piso9Builder
{
    public static readonly PlantaBuilder.Config Config = new PlantaBuilder.Config
    {
        planta = FloorManager.PlantaBase,
        etiqueta = "Piso 9",
        rutaPlanta = "Assets/MovU/Models/Piso9/planta_piso9.json",
        carpetaMallas = "Assets/MovU/Models/Piso9/Mallas",
        carpetaMateriales = "Assets/MovU/Materials/Piso9",
        prefijoMaterial = "P9_",
        carpetasTexturas = new[] { "Assets/MovU/Textures/Piso9", "Assets/MovU/Textures/Piso3" },
        texturas = new Dictionary<string, string>
        {
            { "Concreto", "P3_Concreto.png" }, { "Madera", "P9_Madera.png" }, { "Piso", "P3_Piso.png" },
            { "Granito", "P9_Granito.png" }, { "Terrazo", "P9_Terrazo.png" },
        },
    };

    public static bool HayPlanta => PlantaBuilder.HayPlanta(Config);

    /// <summary>El marco del piso 9 guardado en su planta (null si no lo trae).</summary>
    public static PlantaBuilder.Marco Marco => PlantaBuilder.LeerMarco(Config);

    /// <summary>
    /// Arma un piso con la planta del 9. 'numero' (9, 10) solo distingue las
    /// mallas guardadas de cada copia.
    /// </summary>
    public static Transform Construir(Transform padre, string nombre, int numero, Vector3 pivote,
                                      float alturaLosa, PlanoDePlanta plano, float alturaDelTecho)
    {
        return PlantaBuilder.Construir(Config, $"Piso{numero}_", padre, nombre, pivote, alturaLosa, plano, alturaDelTecho);
    }
}
