using System.Collections;
using UnityEngine;

/// <summary>Clears blocks with a bottom-to-top glow, leaving the grid visible. Created by FallingPiece.</summary>
[DisallowMultipleComponent]
public sealed class DeathScreen : MonoBehaviour
{
    [SerializeField] private Color glowColor = new Color(0.35f, 0.85f, 1f, 1f);
    [SerializeField, Min(0.01f)] private float rowInterval = 0.045f;
    [SerializeField, Min(0.05f)] private float rowFlashDuration = 0.24f;
    [Header("Death Sound")]
    [SerializeField] private AudioSource soundSource;
    [SerializeField] private AudioClip deathSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 1f;
    private bool ownsSoundSource;
    private bool playedDeathSound;

    private Board board;
    private Coroutine sequence;
    [SerializeField] private GameObject glowRoot;
    [SerializeField] private Sprite glowSprite;
    [SerializeField] private SpriteRenderer[] rowGlows = new SpriteRenderer[Board.Height];
    private Texture2D glowTexture;
    private Sprite generatedGlowSprite;
    private bool ownsGlowRoot;
    public bool CanRetry { get; private set; }
    public AudioClip DeathSound => deathSound;

    public void Show(Board target, bool showFinalBoardFrame = false)
    {
        Hide();
        board = target;
        if (glowRoot == null) BuildGlow();
        PlayDeathSound();
        sequence = StartCoroutine(Animate(showFinalBoardFrame));
    }

    public void Hide()
    {
        if (sequence != null) StopCoroutine(sequence);
        sequence = null;
        CanRetry = false;
        if (glowRoot != null) glowRoot.SetActive(false);
        if (playedDeathSound && soundSource != null) soundSource.Stop();
        playedDeathSound = false;
    }

    private IEnumerator Animate(bool showFinalBoardFrame)
    {
        // Fatal garbage must reach the screen before the first death row is removed.
        // Game over and its sound already started; this only defers the visual sweep.
        if (showFinalBoardFrame) yield return null;
        glowRoot.SetActive(true);
        float interval = Mathf.Max(0.01f, rowInterval);
        float flash = Mathf.Max(0.05f, rowFlashDuration);
        var deletedFrame = new int[Board.Height];
        var glowStarted = new bool[Board.Height];
        var glowStartTime = new float[Board.Height];
        for (int row = 0; row < Board.Height; row++)
        {
            deletedFrame[row] = -1;
            rowGlows[row].enabled = false;
        }
        float elapsed = 0f;
        bool finished = false;
        while (!finished)
        {
            finished = true;
            for (int row = 0; row < Board.Height; row++)
            {
                if (elapsed < row * interval)
                {
                    finished = false;
                    continue;
                }
                if (deletedFrame[row] < 0)
                {
                    board.ClearDeathRow(row);
                    deletedFrame[row] = Time.frameCount;
                }
                // The row is empty for one frame before its glow becomes visible.
                if (Time.frameCount == deletedFrame[row])
                {
                    finished = false;
                    continue;
                }
                if (!glowStarted[row])
                {
                    glowStarted[row] = true;
                    glowStartTime[row] = elapsed;
                }
                float progress = Mathf.Clamp01((elapsed - glowStartTime[row]) / flash);
                SpriteRenderer glow = rowGlows[row];
                glow.enabled = progress < 1f;
                Color color = Color.Lerp(Color.white, glowColor, progress);
                color.a = 1f - progress;
                glow.color = color;
                glow.transform.localScale = new Vector3(Board.Width * board.CellSize * 1.08f / glowSprite.bounds.size.x,
                    board.CellSize * Mathf.Lerp(1.3f, 2.5f, progress) / glowSprite.bounds.size.y, 1f);
                if (progress < 1f) finished = false;
            }
            if (!finished)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }
        glowRoot.SetActive(false);
        CanRetry = true;
        sequence = null;
    }

    private void BuildGlow()
    {
        // Standalone boards without assigned artwork retain their existing fallback glow.
        if (glowSprite == null)
        {
            glowTexture = new Texture2D(128, 32, TextureFormat.RGBA32, false);
            glowTexture.wrapMode = TextureWrapMode.Clamp;
            glowTexture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < glowTexture.height; y++)
            {
                for (int x = 0; x < glowTexture.width; x++)
                {
                    float vertical = Mathf.Abs((y + 0.5f) / glowTexture.height * 2f - 1f);
                    float horizontal = Mathf.Abs((x + 0.5f) / glowTexture.width * 2f - 1f);
                    float alpha = Mathf.Pow(1f - vertical, 3f) * (1f - Mathf.Pow(horizontal, 12f));
                    glowTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            glowTexture.Apply();
            glowSprite = generatedGlowSprite = Sprite.Create(glowTexture, new Rect(0, 0, 128, 32), new Vector2(0.5f, 0.5f), 128f);
        }
        glowRoot = new GameObject("Death row glow");
        ownsGlowRoot = true;
        glowRoot.transform.SetParent(board.transform, false);
        for (int row = 0; row < Board.Height; row++)
        {
            var stripe = new GameObject($"Row {row} flash");
            stripe.transform.SetParent(glowRoot.transform, false);
            stripe.transform.position = (board.CellToWorld(new Vector2Int(0, row))
                + board.CellToWorld(new Vector2Int(Board.Width - 1, row))) * 0.5f;
            rowGlows[row] = stripe.AddComponent<SpriteRenderer>();
            rowGlows[row].sprite = glowSprite;
            rowGlows[row].sortingOrder = 10;
            rowGlows[row].enabled = false;
        }
        glowRoot.SetActive(false);
    }

    private void PlayDeathSound()
    {
        if (deathSound == null) return;
        if (soundSource == null)
        {
            soundSource = gameObject.AddComponent<AudioSource>();
            soundSource.playOnAwake = false;
            soundSource.loop = false;
            soundSource.spatialBlend = 0f;
            ownsSoundSource = true;
        }
        soundSource.PlayOneShot(deathSound, soundVolume);
        playedDeathSound = true;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        if (ownsSoundSource && soundSource != null) Destroy(soundSource);
        if (ownsGlowRoot && glowRoot != null) Destroy(glowRoot);
        if (generatedGlowSprite != null) Destroy(generatedGlowSprite);
        if (glowTexture != null) Destroy(glowTexture);
    }
}
