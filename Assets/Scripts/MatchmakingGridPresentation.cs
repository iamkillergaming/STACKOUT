using UnityEngine;

/// <summary>The small animated tetromino grid shown while the arena finds players.</summary>
[DisallowMultipleComponent]
public sealed class MatchmakingGridPresentation : MonoBehaviour
{
    [SerializeField] private SpriteRenderer[] cells = new SpriteRenderer[16];
    [SerializeField, Min(.05f)] private float holdDuration = .3f;
    [SerializeField, Min(.05f)] private float blankDuration = .2f;
    [SerializeField] private Color idleColor = new Color(.19f, .20f, .21f, 1f);
    [Tooltip("Piece colors in I, O, T, S, Z, J, L order.")]
    [SerializeField] private Color[] pieceColors =
    {
        new Color(.1f, .85f, .95f), new Color(1f, .85f, .1f),
        new Color(.75f, .2f, .95f), new Color(.35f, .95f, .15f),
        new Color(1f, .15f, .3f), new Color(.15f, .35f, 1f), new Color(1f, .5f, .1f)
    };

    private static readonly int[] PieceOrder = { 4, 3, 0, 5, 6, 2, 1 };
    // Exact 4x4 positions from the supplied loading sheet, in I, O, T, S, Z, J, L order.
    // Bits run left to right, bottom row first; columns follow the sheet's four rotation frames.
    private static readonly int[,] PieceMasks =
    {
        { 0x0f00, 0x4444, 0x00f0, 0x2222 },
        { 0x0660, 0x0660, 0x0660, 0x0660 },
        { 0x0270, 0x0262, 0x0072, 0x0232 },
        { 0x0630, 0x0264, 0x0063, 0x0132 },
        { 0x0360, 0x0462, 0x0036, 0x0231 },
        { 0x0170, 0x0622, 0x0074, 0x0223 },
        { 0x0470, 0x0226, 0x0071, 0x0322 }
    };
    // The photographed matchmaking indicator begins with the Z in its lower position.
    private const int InitialRotation = 2;
    private static readonly int[] Frames = CreateFrames();
    private float elapsed;
    public int FrameIndex { get; private set; }
    public int CurrentPieceIndex => PieceOrder[FrameIndex % PieceOrder.Length];
    public int RotationIndex => (InitialRotation + FrameIndex / PieceOrder.Length) % 4;
    public bool IsPieceVisible { get; private set; } = true;

    private void OnEnable() => ResetAnimation();

    public void ResetAnimation()
    {
        elapsed = 0f;
        FrameIndex = 0;
        IsPieceVisible = true;
        PaintFrame();
    }

    private void Update() => Advance(Time.unscaledDeltaTime);

    /// <summary>Holds each colored piece, leaves the grid empty, then advances to the next piece.</summary>
    public void Advance(float delta)
    {
        if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta)) return;
        float hold = Mathf.Max(.05f, holdDuration);
        float step = hold + Mathf.Max(.05f, blankDuration);
        elapsed = Mathf.Repeat(elapsed + delta, step * Frames.Length);
        int next = Mathf.Min(Frames.Length - 1, Mathf.FloorToInt(elapsed / step));
        bool visible = elapsed - next * step < hold;
        if (next == FrameIndex && visible == IsPieceVisible) return;
        FrameIndex = next;
        IsPieceVisible = visible;
        PaintFrame();
    }

    private void PaintFrame()
    {
        int mask = IsPieceVisible ? Frames[FrameIndex] : 0;
        Color color = pieceColors != null && CurrentPieceIndex < pieceColors.Length
            ? pieceColors[CurrentPieceIndex] : Color.white;
        for (int i = 0; i < cells.Length && i < 16; i++)
            if (cells[i] != null) cells[i].color = (mask & (1 << i)) != 0 ? color : idleColor;
    }

    private static int[] CreateFrames()
    {
        // Visit every color before advancing the rotation, preserving the sheet's cell offsets.
        var frames = new int[28];
        for (int rotation = 0; rotation < 4; rotation++)
        for (int order = 0; order < PieceOrder.Length; order++)
            frames[rotation * PieceOrder.Length + order] =
                PieceMasks[PieceOrder[order], (InitialRotation + rotation) % 4];
        return frames;
    }
}
