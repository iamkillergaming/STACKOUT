using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Hides completed blocks one frame before flashing their rows. Collision data stays intact.</summary>
[DisallowMultipleComponent]
public sealed class LineClearEffect : MonoBehaviour
{
    [SerializeField] private SpriteRenderer[] halos = new SpriteRenderer[Board.Height];
    [SerializeField] private SpriteRenderer[] cores = new SpriteRenderer[Board.Height];
    [SerializeField] private GameObject glowRoot;
    [SerializeField] private Sprite haloSprite;
    [SerializeField] private Sprite coreSprite;
    private Texture2D glowTexture;
    private Sprite generatedHaloSprite;
    private Sprite generatedCoreSprite;
    private bool ownsGlowRoot;
    private int sequenceVersion;
    private Board displayedBoard;
    private int[] displayedRows;

    public IEnumerator Play(Board board, IReadOnlyList<int> rows, float duration, Color glowColor)
    {
        Hide();
        if (board == null || rows == null || rows.Count == 0 || !isActiveAndEnabled)
            yield break;

        displayedBoard = board;
        displayedRows = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++) displayedRows[i] = rows[i];
        board.SetRowsHidden(displayedRows, true);
        int version = sequenceVersion;
        // Let the empty cells render once before starting the light.
        yield return null;
        if (version != sequenceVersion || !isActiveAndEnabled || board == null) yield break;

        BuildGlow();
        glowRoot.transform.SetParent(board.transform, false);
        glowRoot.transform.localPosition = Vector3.zero;
        glowRoot.transform.localRotation = Quaternion.identity;
        glowRoot.transform.localScale = Vector3.one;
        for (int i = 0; i < rows.Count; i++)
        {
            int row = rows[i];
            if (row < 0 || row >= Board.Height) continue;
            Vector3 position = new Vector3(0f, (row - (Board.Height - 1) * 0.5f) * board.CellSize, 0f);
            halos[row].transform.localPosition = position;
            cores[row].transform.localPosition = position;
            halos[row].enabled = true;
            cores[row].enabled = true;
        }

        float elapsed = 0f;
        duration = Mathf.Max(0.05f, duration);
        glowRoot.SetActive(true);
        while (elapsed < duration && version == sequenceVersion && isActiveAndEnabled && board != null)
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            float opacity = 1f - progress * progress;
            Color haloColor = glowColor;
            haloColor.a *= opacity;
            Color coreColor = Color.Lerp(Color.white, glowColor, progress * 0.3f);
            coreColor.a = opacity;
            for (int row = 0; row < Board.Height; row++)
            {
                if (!halos[row].enabled) continue;
                halos[row].color = haloColor;
                halos[row].transform.localScale = new Vector3(Board.Width * board.CellSize * 1.06f / haloSprite.bounds.size.x,
                    board.CellSize * Mathf.Lerp(1.5f, 2.1f, progress) / haloSprite.bounds.size.y, 1f);
                cores[row].color = coreColor;
                cores[row].transform.localScale = new Vector3(Board.Width * board.CellSize / coreSprite.bounds.size.x,
                    board.CellSize * Mathf.Lerp(0.85f, 0.08f, progress) / coreSprite.bounds.size.y, 1f);
            }
            yield return null;
            elapsed += Time.deltaTime;
        }
        // An older coroutine must not hide a newer animation after cancellation.
        if (version == sequenceVersion) HideGlows();
    }

    public void Hide()
    {
        sequenceVersion++;
        HideGlows();
        if (displayedBoard != null) displayedBoard.SetRowsHidden(displayedRows, false);
        displayedBoard = null;
        displayedRows = null;
    }

    private void HideGlows()
    {
        if (glowRoot != null) glowRoot.SetActive(false);
        for (int row = 0; row < Board.Height; row++)
        {
            if (halos[row] != null) halos[row].enabled = false;
            if (cores[row] != null) cores[row].enabled = false;
        }
    }

    private void BuildGlow()
    {
        if (glowRoot != null) return;
        if (haloSprite == null || coreSprite == null)
        {
            glowTexture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            glowTexture.wrapMode = TextureWrapMode.Clamp;
            glowTexture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < glowTexture.height; y++)
            {
                for (int x = 0; x < glowTexture.width; x++)
                {
                    float vertical = Mathf.Abs((y + 0.5f) / glowTexture.height * 2f - 1f);
                    float horizontal = Mathf.Abs((x + 0.5f) / glowTexture.width * 2f - 1f);
                    float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1f, vertical));
                    float alpha = edge * (1f - Mathf.Pow(horizontal, 24f));
                    glowTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            glowTexture.Apply();
            if (haloSprite == null)
                haloSprite = generatedHaloSprite = Sprite.Create(glowTexture, new Rect(0, 0, 64, 32),
                    new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect);
            if (coreSprite == null)
                coreSprite = generatedCoreSprite = Sprite.Create(glowTexture, new Rect(32, 15, 1, 1),
                    new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        }
        glowRoot = new GameObject("Line clear glow");
        ownsGlowRoot = true;
        glowRoot.SetActive(false);
        for (int row = 0; row < Board.Height; row++)
        {
            halos[row] = CreateStripe($"Row {row} halo", haloSprite, 8);
            cores[row] = CreateStripe($"Row {row} core", coreSprite, 9);
        }
    }

    private SpriteRenderer CreateStripe(string stripeName, Sprite sprite, int order)
    {
        var stripe = new GameObject(stripeName);
        stripe.transform.SetParent(glowRoot.transform, false);
        var renderer = stripe.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        renderer.enabled = false;
        return renderer;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        Hide();
        if (ownsGlowRoot && glowRoot != null) Destroy(glowRoot);
        if (generatedHaloSprite != null) Destroy(generatedHaloSprite);
        if (generatedCoreSprite != null) Destroy(generatedCoreSprite);
        if (glowTexture != null) Destroy(glowTexture);
    }
}
