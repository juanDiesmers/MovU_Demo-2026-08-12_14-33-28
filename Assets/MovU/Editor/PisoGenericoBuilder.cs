using UnityEngine;

// ============================================================================
// PisoGenericoBuilder.cs — Los pisos que todavía no tienen planta (4-8, 10-12)
// ============================================================================
// Están para que la escalera de emergencia una el edificio de verdad: del 4 al
// 13 se puede subir y bajar piso por piso. Son el núcleo del piso 9 (mismo
// edificio, mismos acabados) con TODAS las puertas cerradas salvo la de la
// escalera: los salones de esos pisos todavía no tienen nada.
//
// La planta sale de Models/PisoGenerico/planta_generico.json, que genera
// Tools/piso_generico_planta.py a partir de la del 9. Cuando haya plano de uno
// de estos pisos, se le hace su planta como al 13 y deja de ser genérico.
// ============================================================================

public static class PisoGenericoBuilder
{
    public static readonly PlantaBuilder.Config Config = new PlantaBuilder.Config
    {
        planta = FloorManager.PlantaGenerica,
        etiqueta = "Piso genérico",
        rutaPlanta = "Assets/MovU/Models/PisoGenerico/planta_generico.json",
        carpetaMallas = "Assets/MovU/Models/PisoGenerico/Mallas",
        // Los materiales del 9: es el mismo acabado y así no se duplican.
        carpetaMateriales = "Assets/MovU/Materials/Piso9",
        prefijoMaterial = "P9_",
        carpetasTexturas = Piso9Builder.Config.carpetasTexturas,
        texturas = Piso9Builder.Config.texturas,
    };

    public static bool HayPlanta => PlantaBuilder.HayPlanta(Config);

    /// <summary>Arma el piso genérico 'numero' (4, 5... 12) en 'pivote'.</summary>
    public static Transform Construir(Transform padre, int numero, Vector3 pivote, float alturaLosa, PlanoDePlanta plano)
    {
        return PlantaBuilder.Construir(Config, $"Piso{numero}_", padre, $"Piso_{numero}", pivote, alturaLosa,
                                       plano, 0f, numero.ToString("00"));
    }
}
