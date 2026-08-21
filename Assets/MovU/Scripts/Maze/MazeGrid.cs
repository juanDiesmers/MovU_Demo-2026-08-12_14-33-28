using System;
using System.Collections.Generic;

// ============================================================================
// MazeGrid.cs — Estructura de datos del laberinto
// ============================================================================
// Clase pura de C# sin dependencias de MonoBehaviour.
//
// Sistema de coordenadas:
//   - X crece hacia el Este  (derecha)
//   - Y crece hacia el Norte (arriba)
//   - (0,0) es la esquina suroeste del laberinto
// ============================================================================

/// <summary>
/// Flags que representan los muros de una celda.
/// </summary>
[Flags]
public enum WallFlags
{
    None  = 0,
    North = 1 << 0,   // +Y
    South = 1 << 1,   // -Y
    East  = 1 << 2,   // +X
    West  = 1 << 3,   // -X
    All   = North | South | East | West
}

/// <summary>
/// Celda individual del laberinto.
/// </summary>
public struct MazeCell
{
    public WallFlags Walls;
    public bool Visited;
}

public class MazeGrid
{
    private MazeCell[,] cells;

    public int Width  { get; private set; }
    public int Height { get; private set; }
    public int StartX { get; private set; }
    public int StartY { get; private set; }
    public int GoalX { get; private set; }
    public int GoalY { get; private set; }

    public static readonly WallFlags[] AllDirections =
    {
        WallFlags.North, WallFlags.South, WallFlags.East, WallFlags.West
    };

    public MazeGrid(int width, int height)
    {
        Width  = width;
        Height = height;
        cells  = new MazeCell[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                cells[x, y] = new MazeCell
                {
                    Walls   = WallFlags.All,
                    Visited = false
                };
            }
        }
    }

    /// <summary>
    /// Constructor para reconstruir el grid exacto a partir de un arreglo serializado de muros.
    /// Garantiza cero discrepancias entre tiempo de edición y runtime (Hallazgo #15).
    /// </summary>
    public MazeGrid(int width, int height, int[] flatWalls, int startX, int startY, int goalX, int goalY)
    {
        Width  = width;
        Height = height;
        StartX = startX;
        StartY = startY;
        GoalX  = goalX;
        GoalY  = goalY;
        cells  = new MazeCell[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int index = x + y * width;
                cells[x, y] = new MazeCell
                {
                    Walls   = (WallFlags)flatWalls[index],
                    Visited = false
                };
            }
        }
    }

    public bool InBounds(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    /// <summary>
    /// Devuelve la celda en (x, y) con validación defensiva de límites (Hallazgo #23).
    /// </summary>
    public MazeCell GetCell(int x, int y)
    {
        if (!InBounds(x, y))
        {
            throw new IndexOutOfRangeException($"[MazeGrid] Coordenadas ({x}, {y}) fuera de límites ({Width}x{Height}).");
        }
        return cells[x, y];
    }

    /// <summary>
    /// ¿Tiene muro en la dirección especificada? Con validación defensiva (Hallazgo #23).
    /// </summary>
    public bool HasWall(int x, int y, WallFlags direction)
    {
        if (!InBounds(x, y)) return true; // Defensivo: fuera de límites actúa como muro
        return (cells[x, y].Walls & direction) != 0;
    }

    public void SetVisited(int x, int y, bool visited)
    {
        if (!InBounds(x, y)) return;
        MazeCell cell = cells[x, y];
        cell.Visited = visited;
        cells[x, y]  = cell;
    }

    public void RemoveWall(int x, int y, WallFlags direction)
    {
        if (!InBounds(x, y)) return;
        MazeCell cell = cells[x, y];
        cell.Walls &= ~direction;
        cells[x, y] = cell;

        GetNeighborCoords(x, y, direction, out int nx, out int ny);
        if (InBounds(nx, ny))
        {
            MazeCell neighbor = cells[nx, ny];
            neighbor.Walls &= ~GetOpposite(direction);
            cells[nx, ny]   = neighbor;
        }
    }

    public int CountExits(int x, int y)
    {
        if (!InBounds(x, y)) return 0;
        int exits = 0;
        WallFlags walls = cells[x, y].Walls;

        if ((walls & WallFlags.North) == 0 && InBounds(x, y + 1)) exits++;
        if ((walls & WallFlags.South) == 0 && InBounds(x, y - 1)) exits++;
        if ((walls & WallFlags.East)  == 0 && InBounds(x + 1, y)) exits++;
        if ((walls & WallFlags.West)  == 0 && InBounds(x - 1, y)) exits++;

        return exits;
    }

    public void ComputeStartAndGoal()
    {
        var (farthestFromOrigin, _) = BFS(0, 0);
        StartX = farthestFromOrigin.x;
        StartY = farthestFromOrigin.y;

        var (farthestFromStart, _) = BFS(StartX, StartY);
        GoalX = farthestFromStart.x;
        GoalY = farthestFromStart.y;
    }

    public bool IsSolvable()
    {
        var (_, distances) = BFS(StartX, StartY);
        return distances[GoalX, GoalY] >= 0;
    }

    public ((int x, int y) farthest, int[,] distances) BFS(int startX, int startY)
    {
        int[,] dist = new int[Width, Height];

        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                dist[x, y] = -1;

        var queue = new Queue<(int x, int y)>();
        dist[startX, startY] = 0;
        queue.Enqueue((startX, startY));

        (int x, int y) farthest = (startX, startY);
        int maxDist = 0;

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            int currentDist = dist[cx, cy];

            if (currentDist > maxDist)
            {
                maxDist  = currentDist;
                farthest = (cx, cy);
            }

            foreach (WallFlags dir in AllDirections)
            {
                if (HasWall(cx, cy, dir)) continue;

                GetNeighborCoords(cx, cy, dir, out int nx, out int ny);
                if (!InBounds(nx, ny)) continue;
                if (dist[nx, ny] >= 0) continue;

                dist[nx, ny] = currentDist + 1;
                queue.Enqueue((nx, ny));
            }
        }

        return (farthest, dist);
    }

    /// <summary>
    /// Exporta los muros como un arreglo unidimensional para serialización en MonoBehaviour.
    /// </summary>
    public int[] ExportFlatWalls()
    {
        int[] flat = new int[Width * Height];
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                flat[x + y * Width] = (int)cells[x, y].Walls;
            }
        }
        return flat;
    }

    public static WallFlags GetOpposite(WallFlags direction)
    {
        switch (direction)
        {
            case WallFlags.North: return WallFlags.South;
            case WallFlags.South: return WallFlags.North;
            case WallFlags.East:  return WallFlags.West;
            case WallFlags.West:  return WallFlags.East;
            default:              return WallFlags.None;
        }
    }

    public static void GetNeighborCoords(int x, int y, WallFlags direction, out int nx, out int ny)
    {
        nx = x;
        ny = y;
        switch (direction)
        {
            case WallFlags.North: ny = y + 1; break;
            case WallFlags.South: ny = y - 1; break;
            case WallFlags.East:  nx = x + 1; break;
            case WallFlags.West:  nx = x - 1; break;
        }
    }
}
