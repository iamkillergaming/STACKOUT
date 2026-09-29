using System.Collections.Generic;
using UnityEngine;

/// <summary>A board with bottom-left grid coordinates and separate cell data and visuals.</summary>
[DisallowMultipleComponent]
public sealed class Board : MonoBehaviour
{
    public const int Width = 10;
    public const int Height = 20;

    [SerializeField, Min(0.1f)] private float cellSize = 1f;
    [SerializeField] private Sprite emptyCellSprite;
    [Tooltip("Existing locked-block renderers, ordered left to right, bottom row first.")]
    [SerializeField] private SpriteRenderer[] boardCells = new SpriteRenderer[Width * Height];
    [SerializeField] private bool fitCameraOnStart = true;
    [SerializeField, Min(0f)] private float cameraPadding = 0.6f;
    [Tooltip("Overall size on screen. Lower values zoom out while keeping all boards visible.")]
    [SerializeField, Range(0.25f, 1f)] private float cameraZoom = 0.85f;

    private readonly bool[,] occupied = new bool[Width, Height];
    private readonly bool[] hiddenRows = new bool[Height];
    private readonly Color[,] colors = new Color[Width, Height];
    private readonly Sprite[,] cellArt = new Sprite[Width, Height];
    private readonly int[,] cellTurns = new int[Width, Height];
    public float CellSize => cellSize;
    public bool IsEmpty
    {
        get
        {
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                if (occupied[x, y]) return false;
            return true;
        }
    }

    public Vector3 GetPieceScale(Sprite sprite)
    {
        Vector2 size = sprite.bounds.size;
        // Match the grid cell exactly on both axes.
        return new Vector3(cellSize / size.x, cellSize / size.y, 1f);
    }

    public void SetActivePieceCells(IReadOnlyList<Vector2Int> cells, Vector2Int origin)
    {
        // Kept for the piece controller API. The grid is baked into the frame sprite;
        // full-cell piece sprites cover it without an additional visibility mask.
    }
    private readonly SpriteRenderer[,] visuals = new SpriteRenderer[Width, Height];

    private void Awake()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = y * Width + x;
                visuals[x, y] = boardCells != null && index < boardCells.Length
                    ? boardCells[index] : null;
            }
        }
        RefreshVisuals();
    }

    private void Start()
    {
        RefitCameraIfEnabled();
    }

    public void RefitCameraIfEnabled()
    {
        if (fitCameraOnStart)
            FitCamera();
    }

    [ContextMenu("Fit Camera to Board")]
    public void FitCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("Board needs a camera tagged MainCamera to frame it automatically.", this);
            return;
        }

        // Include both side previews and measure all corners in camera space.
        Rect layout = new Rect(-Width * cellSize * 0.5f, -Height * cellSize * 0.5f,
            Width * cellSize, Height * cellSize);
        BoardPresentation presentation = GetComponent<BoardPresentation>();
        if (presentation != null && presentation.isActiveAndEnabled)
        {
            Rect frameBounds = presentation.GetLocalBounds(this);
            layout = Rect.MinMaxRect(Mathf.Min(layout.xMin, frameBounds.xMin),
                Mathf.Min(layout.yMin, frameBounds.yMin), Mathf.Max(layout.xMax, frameBounds.xMax),
                Mathf.Max(layout.yMax, frameBounds.yMax));
        }
        NextPiecePreview preview = GetComponent<NextPiecePreview>();
        if (preview != null && preview.isActiveAndEnabled)
        {
            Rect previewBounds = preview.GetLocalBounds(this);
            layout = Rect.MinMaxRect(Mathf.Min(layout.xMin, previewBounds.xMin),
                Mathf.Min(layout.yMin, previewBounds.yMin), Mathf.Max(layout.xMax, previewBounds.xMax),
                Mathf.Max(layout.yMax, previewBounds.yMax));
        }
        HoldPiecePreview hold = GetComponent<HoldPiecePreview>();
        if (hold != null && hold.isActiveAndEnabled)
        {
            Rect holdBounds = hold.GetLocalBounds(this);
            layout = Rect.MinMaxRect(Mathf.Min(layout.xMin, holdBounds.xMin),
                Mathf.Min(layout.yMin, holdBounds.yMin), Mathf.Max(layout.xMax, holdBounds.xMax),
                Mathf.Max(layout.yMax, holdBounds.yMax));
        }
        BattleArena arena = GetComponentInChildren<BattleArena>();
        if (arena != null && arena.isActiveAndEnabled)
        {
            Rect arenaBounds = arena.GetLocalBounds(this);
            layout = Rect.MinMaxRect(Mathf.Min(layout.xMin, arenaBounds.xMin),
                Mathf.Min(layout.yMin, arenaBounds.yMin), Mathf.Max(layout.xMax, arenaBounds.xMax),
                Mathf.Max(layout.yMax, arenaBounds.yMax));
        }
        Vector3 center = transform.TransformPoint(new Vector3(layout.center.x, layout.center.y, 0f));
        float extentX = 0f;
        float extentY = 0f;
        float extentZ = 0f;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                Vector3 offset = transform.TransformVector(new Vector3(x * layout.width * 0.5f,
                    y * layout.height * 0.5f, 0f));
                extentX = Mathf.Max(extentX, Mathf.Abs(Vector3.Dot(offset, camera.transform.right)));
                extentY = Mathf.Max(extentY, Mathf.Abs(Vector3.Dot(offset, camera.transform.up)));
                extentZ = Mathf.Max(extentZ, Mathf.Abs(Vector3.Dot(offset, camera.transform.forward)));
            }
        }

        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(extentY + cameraPadding,
            (extentX + cameraPadding) / Mathf.Max(camera.aspect, 0.01f))
            / Mathf.Clamp(cameraZoom, 0.25f, 1f);
        float distance = Mathf.Max(10f, extentZ + camera.nearClipPlane + 1f);
        camera.transform.position = center - camera.transform.forward * distance;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance + extentZ + 1f);
    }

    public bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
    }

    // Out-of-bounds coordinates are blocked, including above the board.
    public bool IsCellFree(Vector2Int cell)
    {
        return IsInside(cell) && !occupied[cell.x, cell.y];
    }

    public bool CanPlace(IReadOnlyList<Vector2Int> cells, Vector2Int origin, bool allowAboveBoard = false)
    {
        if (cells == null || cells.Count == 0)
            return false;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = origin + cells[i];
            if (allowAboveBoard && cell.x >= 0 && cell.x < Width && cell.y >= Height)
                continue;
            if (!IsCellFree(cell))
                return false;
        }
        return true;
    }

    /// <summary>Stores a piece only if all its cells fit. Call ClearFullLines afterward.</summary>
    public bool TryLockPiece(IReadOnlyList<Vector2Int> cells, Vector2Int origin, Color color,
        Sprite[] sprites = null, int clockwiseTurns = 0)
    {
        if (!CanPlace(cells, origin) || (sprites != null && sprites.Length != cells.Count))
            return false;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = origin + cells[i];
            occupied[cell.x, cell.y] = true;
            colors[cell.x, cell.y] = color;
            cellArt[cell.x, cell.y] = sprites == null ? null : sprites[i];
            cellTurns[cell.x, cell.y] = clockwiseTurns;
        }
        RefreshVisuals();
        return true;
    }

    /// <summary>A detached collision snapshot, suitable for planning without changing the board.</summary>
    public bool[,] CopyOccupancy()
    {
        return (bool[,])occupied.Clone();
    }

    /// <summary>
    /// Inserts garbage from the bottom, preserving the artwork of displaced blocks.
    /// Holes are ordered bottom row first. Invalid input or an active clear changes nothing.
    /// Valid rows are inserted even when blocks leave the top; overflow reports that fatal rise.
    /// </summary>
    public bool TryAddGarbage(IReadOnlyList<int> holes, Sprite sprite, Color color, out bool overflow)
    {
        overflow = false;
        if (holes == null || holes.Count > Height) return false;
        int count = holes.Count;
        if (count == 0) return true;
        for (int row = 0; row < count; row++)
            if (holes[row] < 0 || holes[row] >= Width) return false;
        for (int y = 0; y < Height; y++)
            if (hiddenRows[y]) return false;
        for (int y = Height - count; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                if (occupied[x, y]) overflow = true;
        }

        // Visit top rows first so each source row survives until it is read.
        for (int y = Height - 1; y >= count; y--)
        {
            for (int x = 0; x < Width; x++)
            {
                occupied[x, y] = occupied[x, y - count];
                colors[x, y] = colors[x, y - count];
                cellArt[x, y] = cellArt[x, y - count];
                cellTurns[x, y] = cellTurns[x, y - count];
            }
        }
        for (int y = 0; y < count; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool filled = x != holes[y];
                occupied[x, y] = filled;
                colors[x, y] = filled ? color : default;
                cellArt[x, y] = filled ? sprite : null;
                cellTurns[x, y] = 0;
            }
        }
        RefreshVisuals();
        return true;
    }

    /// <summary>Returns completed rows without changing them, so they can animate before removal.</summary>
    public int[] GetFullRows()
    {
        var rows = new List<int>(4);
        for (int y = 0; y < Height; y++)
            if (IsRowFull(y)) rows.Add(y);
        return rows.ToArray();
    }

    // Keep collision data and upper rows in place until the clear animation finishes.
    public void SetRowsHidden(IReadOnlyList<int> rows, bool hidden)
    {
        if (rows == null) return;
        foreach (int row in rows)
            if (row >= 0 && row < Height) hiddenRows[row] = hidden;
        RefreshVisuals();
    }

    private bool IsRowFull(int row)
    {
        for (int x = 0; x < Width; x++)
            if (!occupied[x, row]) return false;
        return true;
    }

    /// <summary>Removes full rows, shifts remaining rows down, and returns the cleared count.</summary>
    public int ClearFullLines()
    {
        int destination = 0;
        for (int source = 0; source < Height; source++)
        {
            if (IsRowFull(source))
                continue;

            for (int x = 0; x < Width; x++)
            {
                occupied[x, destination] = occupied[x, source];
                colors[x, destination] = colors[x, source];
                cellArt[x, destination] = cellArt[x, source];
                cellTurns[x, destination] = cellTurns[x, source];
            }
            destination++;
        }

        int cleared = Height - destination;
        System.Array.Clear(hiddenRows, 0, hiddenRows.Length);
        for (int y = destination; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                occupied[x, y] = false;
                colors[x, y] = default;
                cellArt[x, y] = null;
                cellTurns[x, y] = 0;
            }
        }
        RefreshVisuals();
        return cleared;
    }

    public void ClearBoard()
    {
        System.Array.Clear(hiddenRows, 0, hiddenRows.Length);
        System.Array.Clear(occupied, 0, occupied.Length);
        System.Array.Clear(colors, 0, colors.Length);
        System.Array.Clear(cellArt, 0, cellArt.Length);
        System.Array.Clear(cellTurns, 0, cellTurns.Length);
        RefreshVisuals();
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return transform.TransformPoint(CellLocalPosition(cell));
    }

    // Remove blocks without collapsing other rows or awarding gameplay lines.
    public void ClearDeathRow(int row)
    {
        if (row < 0 || row >= Height) return;
        hiddenRows[row] = false;
        for (int x = 0; x < Width; x++)
        {
            occupied[x, row] = false;
            colors[x, row] = default;
            cellArt[x, row] = null;
            cellTurns[x, row] = 0;
        }
        RefreshVisuals();
    }

    private Vector3 CellLocalPosition(Vector2Int cell)
    {
        return new Vector3((cell.x - (Width - 1) / 2f) * cellSize,
            (cell.y - (Height - 1) / 2f) * cellSize, 0f);
    }

    private void RefreshVisuals()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (visuals[x, y] != null)
                {
                    bool showBlock = occupied[x, y] && !hiddenRows[y];
                    visuals[x, y].enabled = showBlock;
                    visuals[x, y].color = showBlock ? colors[x, y] : Color.white;
                    Sprite art = showBlock && cellArt[x, y] != null ? cellArt[x, y] : emptyCellSprite;
                    visuals[x, y].sprite = art;
                    visuals[x, y].transform.localRotation = Quaternion.Euler(0, 0,
                        showBlock ? -90f * cellTurns[x, y] : 0f);
                    visuals[x, y].transform.localScale = art != null
                        ? GetPieceScale(art)
                        : Vector3.one * cellSize;
                }
            }
        }
    }

}
