using UnityEngine;

/// <summary>Shows the saved piece beside the left edge of the board.</summary>
[DisallowMultipleComponent]
public sealed class HoldPiecePreview : MonoBehaviour
{
    [SerializeField, Range(0.2f, 0.63f)] private float previewScale = 0.46f;

    [Header("Scene references")]
    [SerializeField] private Transform root;
    [SerializeField] private SpriteRenderer[] previewBlocks = new SpriteRenderer[4];

    public void Show(Board board, int heldShape, Vector2Int[][] shapes,
        Sprite[][] tiles, int[] spriteTurns, bool available)
    {
        if (board == null || !isActiveAndEnabled || !HasVisuals()) { Hide(); return; }
        root.gameObject.SetActive(true);
        GetLayout(board, out float scale, out float centerX, out float pieceY, out _);

        bool visible = heldShape >= 0;
        for (int i = 0; i < previewBlocks.Length; i++) previewBlocks[i].enabled = visible;
        if (!visible) return;

        Vector2Int[] cells = shapes[heldShape];
        Vector2 min = cells[0];
        Vector2 max = cells[0];
        for (int i = 1; i < cells.Length; i++)
        {
            min = Vector2.Min(min, cells[i]);
            max = Vector2.Max(max, cells[i]);
        }
        Vector2 center = (min + max) * 0.5f;
        for (int i = 0; i < previewBlocks.Length; i++)
        {
            SpriteRenderer renderer = previewBlocks[i];
            renderer.sprite = tiles[heldShape][i];
            renderer.color = available ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1f);
            renderer.transform.localPosition = new Vector2(centerX, pieceY)
                + ((Vector2)cells[i] - center) * board.CellSize * scale;
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * spriteTurns[heldShape]);
            Vector3 size = board.GetPieceScale(renderer.sprite);
            renderer.transform.localScale = new Vector3(size.x * scale, size.y * scale, 1f);
        }
    }

    // Reserve enough room for every shape and the heading, including while the slot is empty.
    public Rect GetLocalBounds(Board board)
    {
        GetLayout(board, out float scale, out float centerX, out float pieceY, out float labelY);
        float halfWidth = Mathf.Max(2f * scale, 0.875f) * board.CellSize;
        return Rect.MinMaxRect(centerX - halfWidth, pieceY - scale * board.CellSize,
            centerX + halfWidth, labelY + 0.15f * board.CellSize);
    }

    public void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    private void GetLayout(Board board, out float scale, out float centerX,
        out float pieceY, out float labelY)
    {
        scale = Mathf.Clamp(previewScale, 0.2f, 0.63f);
        centerX = -(Board.Width * 0.5f + 1.5f) * board.CellSize;
        pieceY = (Board.Height * 0.5f - 1.95f) * board.CellSize;
        labelY = (Board.Height * 0.5f - 0.15f) * board.CellSize;
    }

    private bool HasVisuals()
    {
        if (root == null || previewBlocks == null || previewBlocks.Length != 4) return false;
        foreach (SpriteRenderer block in previewBlocks)
            if (block == null) return false;
        return true;
    }

    private void OnDisable() => Hide();

}
