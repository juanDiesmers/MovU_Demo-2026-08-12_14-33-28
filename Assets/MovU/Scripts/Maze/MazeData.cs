using UnityEngine;

// ============================================================================
// MazeData.cs — Puente de datos del laberinto en runtime
// ============================================================================
// Almacena la estructura del laberinto serializada desde el editor (Hallazgo #15).
// Evita depender de regenerar el grid con System.Random en runtime, garantizando
// que las métricas de celdas visitadas coincidan 100% con la geometría física.
// ============================================================================

public class MazeData : MonoBehaviour
{
    [Header("Parámetros del Laberinto")]
    public int seed = 42;
    public int width = 13;
    public int height = 13;
    public float braidPercent = 0.15f;

    [Header("Geometría (1u = 1m)")]
    public float cellSize = 3.0f;
    public float wallHeight = 3.0f;
    public float wallThickness = 0.2f;
    public Vector3 originPosition = Vector3.zero;

    [Header("Coordenadas S y G")]
    public int startX;
    public int startY;
    public int goalX;
    public int goalY;

    [Header("Serialización de Muros")]
    [HideInInspector] public int[] serializedWalls;

    /// <summary>Grid de C# con los datos del laberinto.</summary>
    public MazeGrid Grid { get; private set; }

    private void Awake()
    {
        InitializeGrid();
    }

    /// <summary>
    /// Reconstruye el MazeGrid a partir del arreglo de muros serializado en el build.
    /// Si el arreglo no existe (ej. test), regenera mediante el seed.
    /// </summary>
    public void InitializeGrid()
    {
        if (Grid != null) return;

        if (serializedWalls != null && serializedWalls.Length == width * height)
        {
            Grid = new MazeGrid(width, height, serializedWalls, startX, startY, goalX, goalY);
        }
        else
        {
            Grid = MazeGenerator.Generate(width, height, seed, braidPercent);
            startX = Grid.StartX;
            startY = Grid.StartY;
            goalX = Grid.GoalX;
            goalY = Grid.GoalY;
            serializedWalls = Grid.ExportFlatWalls();
        }
    }

    /// <summary>
    /// Convierte una posición en espacio de mundo de Unity (Vector3)
    /// a coordenadas discretas de celda (x, y) en el grid.
    /// </summary>
    public bool WorldToCell(Vector3 worldPos, out int cellX, out int cellY)
    {
        Vector3 localPos = worldPos - originPosition;

        cellX = Mathf.FloorToInt(localPos.x / cellSize);
        cellY = Mathf.FloorToInt(localPos.z / cellSize);

        if (Grid == null)
        {
            InitializeGrid();
        }

        return Grid.InBounds(cellX, cellY);
    }

    /// <summary>
    /// Retorna la posición en mundo del centro de la celda (x, y).
    /// </summary>
    public Vector3 CellToWorldCenter(int cellX, int cellY, float heightY = 0f)
    {
        float x = originPosition.x + (cellX + 0.5f) * cellSize;
        float z = originPosition.z + (cellY + 0.5f) * cellSize;
        return new Vector3(x, heightY, z);
    }
}
