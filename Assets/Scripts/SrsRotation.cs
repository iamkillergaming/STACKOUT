using System;
using UnityEngine;

/// <summary>SRS quarter turns in board coordinates (+Y is up). States: 0, R, 2, L.</summary>
public static class SrsRotation
{
    // Tables 2 and 3: https://erikdemaine.org/papers/TetrisPieces_FUN2024/paper.pdf
    // Rows: 0>R, R>0, R>2, 2>R, 2>L, L>2, L>0, 0>L.
    private static readonly Vector2Int[][] Standard =
    {
        Row(-1, 0, -1, 1, 0, -2, -1, -2),
        Row(1, 0, 1, -1, 0, 2, 1, 2),
        Row(1, 0, 1, -1, 0, 2, 1, 2),
        Row(-1, 0, -1, 1, 0, -2, -1, -2),
        Row(1, 0, 1, 1, 0, -2, 1, -2),
        Row(-1, 0, -1, -1, 0, 2, -1, 2),
        Row(-1, 0, -1, -1, 0, 2, -1, 2),
        Row(1, 0, 1, 1, 0, -2, 1, -2)
    };
    private static readonly Vector2Int[][] Line =
    {
        Row(-2, 0, 1, 0, -2, -1, 1, 2),
        Row(2, 0, -1, 0, 2, 1, -1, -2),
        Row(-1, 0, 2, 0, -1, 2, 2, -1),
        Row(1, 0, -2, 0, 1, -2, -2, 1),
        Row(2, 0, -1, 0, 2, 1, -1, -2),
        Row(-2, 0, 1, 0, -2, -1, 1, 2),
        Row(1, 0, -2, 0, 1, -2, -2, 1),
        Row(-1, 0, 2, 0, -1, 2, 2, -1)
    };

    private static Vector2Int[] Row(params int[] values)
    {
        var row = new Vector2Int[5];
        for (int i = 0; i < 4; i++) row[i + 1] = new Vector2Int(values[i * 2], values[i * 2 + 1]);
        return row;
    }

    // Shape indices match FallingPiece: I, O, T, S, Z, J, L.
    // Outputs equal inputs on failure; callers commit state only on success.
    public static bool TryRotate(int shape, Vector2Int[] cells, Vector2Int origin, int state,
        bool clockwise, Func<Vector2Int[], Vector2Int, bool> canPlace,
        out Vector2Int[] resultCells, out Vector2Int resultOrigin, out int resultState)
    {
        return TryRotate(shape, cells, origin, state, clockwise, canPlace,
            out resultCells, out resultOrigin, out resultState, out _);
    }

    public static bool TryRotate(int shape, Vector2Int[] cells, Vector2Int origin, int state,
        bool clockwise, Func<Vector2Int[], Vector2Int, bool> canPlace,
        out Vector2Int[] resultCells, out Vector2Int resultOrigin, out int resultState, out int kickIndex)
    {
        resultCells = cells;
        resultOrigin = origin;
        resultState = state;
        kickIndex = -1;
        if (shape == 1) return false; // O occupies the same cells and does not kick.

        int next = (state + (clockwise ? 1 : 3)) % 4;
        // Doubled pivots avoid floating point. The cropped I spawn row is above its pivot.
        Vector2Int pivot = shape == 0 ? new Vector2Int(3, -1) : new Vector2Int(2, 0);
        var rotated = new Vector2Int[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            Vector2Int relative = cells[i] * 2 - pivot;
            Vector2Int turned = clockwise ? new Vector2Int(relative.y, -relative.x)
                : new Vector2Int(-relative.y, relative.x);
            rotated[i] = (pivot + turned) / 2;
        }

        int row = clockwise ? state * 2 : (next * 2 + 1) % 8;
        Vector2Int[] kicks = (shape == 0 ? Line : Standard)[row];
        for (int i = 0; i < kicks.Length; i++)
        {
            Vector2Int candidate = origin + kicks[i];
            if (!canPlace(rotated, candidate)) continue;
            resultCells = rotated;
            resultOrigin = candidate;
            resultState = next;
            kickIndex = i;
            return true;
        }
        return false;
    }
}
