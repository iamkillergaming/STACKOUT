using UnityEngine;

/// <summary>Three occupied corners around the T pivot, checked before its cells are committed.</summary>
public static class TSpinDetector
{
    // Cropped T spawn coordinates put its pivot at (1, 0).
    public static SpinKind Classify(Board board, Vector2Int origin, int turns, int kickIndex)
    {
        Vector2Int pivot = origin + Vector2Int.right;
        bool upperLeft = !board.IsCellFree(pivot + new Vector2Int(-1, 1));
        bool upperRight = !board.IsCellFree(pivot + new Vector2Int(1, 1));
        bool lowerLeft = !board.IsCellFree(pivot + new Vector2Int(-1, -1));
        bool lowerRight = !board.IsCellFree(pivot + new Vector2Int(1, -1));
        int corners = (upperLeft ? 1 : 0) + (upperRight ? 1 : 0)
            + (lowerLeft ? 1 : 0) + (lowerRight ? 1 : 0);
        if (corners < 3) return SpinKind.None;
        bool bothFront;
        switch ((turns % 4 + 4) % 4)
        {
            case 1: bothFront = upperRight && lowerRight; break;
            case 2: bothFront = lowerLeft && lowerRight; break;
            case 3: bothFront = upperLeft && lowerLeft; break;
            default: bothFront = upperLeft && upperRight; break;
        }
        // The fifth SRS kick upgrades a mini to a full T-spin.
        return bothFront || kickIndex == 4 ? SpinKind.Full : SpinKind.Mini;
    }
}
