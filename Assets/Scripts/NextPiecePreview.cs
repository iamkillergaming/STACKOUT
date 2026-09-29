using System.Collections.Generic;
using UnityEngine;

/// <summary>Shows upcoming pieces beside the board, with the next spawn at the top.</summary>
[DisallowMultipleComponent]
public sealed class NextPiecePreview : MonoBehaviour
{
    public const int Count = 6;

    [SerializeField, Range(0.2f, 0.63f)] private float previewScale = 0.46f;
    [SerializeField, Min(0.5f)] private float slotSpacing = 1.74f;

    [Header("Scene references")]
    [SerializeField] private Transform root;
    [SerializeField] private SpriteRenderer[] previewBlocks = new SpriteRenderer[Count * 4];

    public void Show(Board board, IReadOnlyList<int> upcoming, Vector2Int[][] shapes,
        Sprite[][] tiles, int[] spriteTurns)
    {
        if (board == null || upcoming == null || !isActiveAndEnabled || !HasVisuals())
        {
            Hide();
            return;
        }
        root.gameObject.SetActive(true);
        GetLayout(board, out float scale, out float spacing, out float centerX,
            out float firstY, out _);

        for (int slot = 0; slot < Count; slot++)
        {
            bool visible = slot < upcoming.Count;
            for (int i = 0; i < 4; i++) previewBlocks[slot * 4 + i].enabled = visible;
            if (!visible) continue;
            int type = upcoming[slot];
            Vector2Int[] cells = shapes[type];
            Vector2 min = cells[0];
            Vector2 max = cells[0];
            for (int i = 1; i < cells.Length; i++)
            {
                min = Vector2.Min(min, cells[i]);
                max = Vector2.Max(max, cells[i]);
            }
            Vector2 center = (min + max) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                SpriteRenderer renderer = previewBlocks[slot * 4 + i];
                renderer.sprite = tiles[type][i];
                renderer.color = Color.white;
                renderer.transform.localPosition = new Vector2(centerX, firstY - slot * spacing)
                    + ((Vector2)cells[i] - center) * board.CellSize * scale;
                renderer.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * spriteTurns[type]);
                Vector3 size = board.GetPieceScale(renderer.sprite);
                renderer.transform.localScale = new Vector3(size.x * scale, size.y * scale, 1f);
            }
        }
    }

    // These bounds include any of the seven shapes and the label, even before Show is called.
    public Rect GetLocalBounds(Board board)
    {
        GetLayout(board, out float scale, out float spacing, out float centerX,
            out float firstY, out float labelY);
        float halfWidth = Mathf.Max(2f * scale, 0.875f) * board.CellSize;
        float bottom = firstY - (Count - 1) * spacing - scale * board.CellSize;
        return Rect.MinMaxRect(centerX - halfWidth, bottom,
            centerX + halfWidth, labelY + 0.15f * board.CellSize);
    }

    public void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    private void GetLayout(Board board, out float scale, out float spacing,
        out float centerX, out float firstY, out float labelY)
    {
        // The frame's right pod has a fixed 2.68-cell interior. Keep even a four-wide I inside it.
        scale = Mathf.Clamp(previewScale, 0.2f, 0.63f);
        float topCenter = Board.Height * 0.5f - 1.88f;
        float interiorBottom = Board.Height * 0.5f - 11.84f;
        float maxSpacing = (topCenter - interiorBottom - scale) / (Count - 1);
        spacing = Mathf.Clamp(slotSpacing, 2f * scale + 0.2f, maxSpacing) * board.CellSize;
        centerX = (Board.Width * 0.5f + 1.5f) * board.CellSize;
        firstY = topCenter * board.CellSize;
        labelY = (Board.Height * 0.5f - 0.15f) * board.CellSize;
    }

    private bool HasVisuals()
    {
        if (root == null || previewBlocks == null || previewBlocks.Length != Count * 4) return false;
        foreach (SpriteRenderer block in previewBlocks)
            if (block == null) return false;
        return true;
    }

    private void OnDisable() => Hide();

}
