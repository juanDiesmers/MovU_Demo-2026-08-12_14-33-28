using System;
using System.Collections.Generic;

// ============================================================================
// MazeGenerator.cs — Generador de laberintos con DFS iterativo
// ============================================================================
// Clase pura de C# sin dependencias de MonoBehaviour.
//
// Genera un laberinto "perfecto" (un solo camino entre cualquier par de celdas)
// usando DFS iterativo con backtracking, y luego aplica "braiding" opcional
// para eliminar callejones sin salida y crear rutas alternativas.
//
// La semilla fija garantiza reproducibilidad: todos los usuarios de prueba
// recorren exactamente el mismo laberinto.
// ============================================================================

/// <summary>
/// Generador estático de laberintos. No tiene estado propio — toda la
/// configuración se pasa como parámetros al método Generate().
/// </summary>
public static class MazeGenerator
{
    /// <summary>
    /// Genera un laberinto completo y retorna el grid resultante.
    /// </summary>
    /// <param name="width">Ancho en celdas.</param>
    /// <param name="height">Alto en celdas.</param>
    /// <param name="seed">
    /// Semilla para el generador aleatorio.
    /// Misma semilla + mismo tamaño = mismo laberinto siempre.
    /// </param>
    /// <param name="braidPercent">
    /// Fracción de callejones sin salida a eliminar (0.0–1.0).
    ///   0.0 = laberinto perfecto (sin ciclos)
    ///   0.15 = ~15% de dead-ends eliminados (valor por defecto del demo)
    ///   1.0 = todos los dead-ends eliminados
    /// </param>
    /// <returns>MazeGrid listo para usar, con posiciones S y G calculadas.</returns>
    public static MazeGrid Generate(int width, int height, int seed, float braidPercent)
    {
        var rng  = new System.Random(seed);
        var grid = new MazeGrid(width, height);

        // --- Paso 1: Generar laberinto perfecto con DFS iterativo ---
        GenerateDFS(grid, rng);

        // --- Paso 2: Braiding — eliminar callejones sin salida ---
        if (braidPercent > 0f)
        {
            ApplyBraiding(grid, rng, braidPercent);
        }

        // --- Paso 3: Determinar inicio (S) y objetivo (G) ---
        // Se hace después del braiding porque éste cambia las distancias
        grid.ComputeStartAndGoal();

        return grid;
    }

    // -----------------------------------------------------------------------
    // Paso 1: DFS iterativo con backtracking
    // -----------------------------------------------------------------------

    /// <summary>
    /// Genera un laberinto perfecto usando DFS iterativo.
    ///
    /// Usa una pila explícita (Stack) en lugar de recursión para evitar
    /// stack overflow en laberintos grandes. El algoritmo:
    ///
    ///   1. Empezar en (0,0), marcar como visitada, push a la pila
    ///   2. Mientras la pila tenga elementos:
    ///      a. Peek la celda actual
    ///      b. Buscar vecinos no visitados
    ///      c. Si hay: elegir uno al azar, quitar muro, marcar, push
    ///      d. Si no hay: pop (backtrack)
    ///
    /// Al terminar, todas las celdas están conectadas y hay exactamente
    /// un camino entre cualquier par de celdas (laberinto perfecto).
    /// </summary>
    private static void GenerateDFS(MazeGrid grid, System.Random rng)
    {
        var stack = new Stack<(int x, int y)>();

        // Empezar desde la esquina suroeste
        grid.SetVisited(0, 0, true);
        stack.Push((0, 0));

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();

            // Obtener vecinos que aún no hemos visitado
            var unvisited = GetUnvisitedNeighbors(grid, cx, cy);

            if (unvisited.Count > 0)
            {
                // Elegir un vecino al azar (la semilla controla esto)
                int index = rng.Next(unvisited.Count);
                var (nx, ny, dir) = unvisited[index];

                // Quitar el muro entre la celda actual y el vecino elegido
                grid.RemoveWall(cx, cy, dir);

                // Marcar el vecino como visitado y avanzar
                grid.SetVisited(nx, ny, true);
                stack.Push((nx, ny));
            }
            else
            {
                // No hay vecinos sin visitar → retroceder (backtrack)
                stack.Pop();
            }
        }
    }

    /// <summary>
    /// Busca los vecinos de (cx, cy) que aún no han sido visitados.
    /// Retorna una lista con las coordenadas y la dirección del muro a quitar.
    /// </summary>
    private static List<(int x, int y, WallFlags dir)> GetUnvisitedNeighbors(
        MazeGrid grid, int cx, int cy)
    {
        var neighbors = new List<(int, int, WallFlags)>(4);

        foreach (WallFlags dir in MazeGrid.AllDirections)
        {
            MazeGrid.GetNeighborCoords(cx, cy, dir, out int nx, out int ny);

            if (grid.InBounds(nx, ny) && !grid.GetCell(nx, ny).Visited)
            {
                neighbors.Add((nx, ny, dir));
            }
        }

        return neighbors;
    }

    // -----------------------------------------------------------------------
    // Paso 2: Braiding — eliminar callejones sin salida
    // -----------------------------------------------------------------------

    /// <summary>
    /// Elimina muros de callejones sin salida para crear rutas alternativas.
    ///
    /// Un callejón sin salida es una celda con exactamente 1 salida (3 muros).
    /// Para cada uno, con probabilidad braidPercent, se elimina un muro
    /// aleatorio hacia un vecino válido, creando un ciclo.
    ///
    /// Esto es intencional para el estudio de wayfinding: las rutas
    /// alternativas hacen que el laberinto sea más interesante de navegar
    /// y producen métricas más ricas sobre la toma de decisiones del usuario.
    /// </summary>
    private static void ApplyBraiding(MazeGrid grid, System.Random rng, float braidPercent)
    {
        // Recopilar todos los callejones sin salida
        var deadEnds = new List<(int x, int y)>();

        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                if (grid.CountExits(x, y) == 1)
                {
                    deadEnds.Add((x, y));
                }
            }
        }

        // Iterar sobre cada callejón sin salida
        foreach (var (dx, dy) in deadEnds)
        {
            // Re-verificar: un braiding previo pudo haber cambiado esta celda
            if (grid.CountExits(dx, dy) != 1)
                continue;

            // ¿Eliminamos este callejón? Decidir con la probabilidad configurada
            if (rng.NextDouble() >= braidPercent)
                continue;

            // Encontrar muros que podemos eliminar
            // (muros que den a un vecino dentro del grid)
            var removableWalls = new List<WallFlags>(4);

            foreach (WallFlags dir in MazeGrid.AllDirections)
            {
                // Solo considerar muros que existen
                if (!grid.HasWall(dx, dy, dir))
                    continue;

                // Verificar que el vecino está dentro del grid
                MazeGrid.GetNeighborCoords(dx, dy, dir, out int nx, out int ny);
                if (!grid.InBounds(nx, ny))
                    continue;

                removableWalls.Add(dir);
            }

            if (removableWalls.Count == 0)
                continue;

            // Elegir un muro al azar y eliminarlo
            WallFlags wallToRemove = removableWalls[rng.Next(removableWalls.Count)];
            grid.RemoveWall(dx, dy, wallToRemove);
        }
    }
}
