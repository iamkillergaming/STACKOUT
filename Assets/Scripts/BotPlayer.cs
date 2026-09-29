using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Plays directly on an authored miniature Board using reachable SRS placements.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Board), typeof(GarbageCombat))]
public sealed class BotPlayer : MonoBehaviour
{
    public const int MaxPlansPerFrame = 3;
    private static readonly LinkedList<BotPlayer> PlanningQueue = new LinkedList<BotPlayer>();
    private static int planningFrame = -1;
    private static int plansThisFrame;

    [SerializeField] private Board board;
    [SerializeField] private GarbageCombat combat;
    [SerializeField] private Sprite blockSprite;
    [Tooltip("Original colored cell sprites in I, O, T, S, Z, J, L order.")]
    [SerializeField] private Sprite[] cellSprites = new Sprite[7];
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private SpriteRenderer targetFrame;
    [SerializeField] private Sprite normalFrameSprite;
    [SerializeField] private Sprite selectedFrameSprite;
    [SerializeField] private OpponentEliminationPresentation eliminationPresentation;
    [SerializeField] private OpponentBoardPresentation boardPresentation;
    [SerializeField, Min(0.1f)] private float secondsPerPiece = 1.1f;
    [SerializeField, Min(0f)] private float clearDelay = 0.25f;

    // The same cropped spawn coordinates and shape order used by the player's SRS controller.
    private static readonly Vector2Int[][] Shapes =
    {
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(1, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(2, 1) },
        new[] { new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(2, 1) }
    };
    private static readonly Color[] PieceColors =
    {
        new Color(0.1f, 0.85f, 0.95f), new Color(1f, 0.85f, 0.1f),
        new Color(0.75f, 0.2f, 0.95f), new Color(0.35f, 0.95f, 0.15f),
        new Color(1f, 0.15f, 0.3f), new Color(0.15f, 0.35f, 1f),
        new Color(1f, 0.5f, 0.1f)
    };

    private readonly Queue<int> bag = new Queue<int>();
    // Each bot has its own random stream, so adding bots never changes the player's seven-bag.
    private readonly System.Random random = new System.Random(Guid.NewGuid().GetHashCode());
    private readonly Sprite[] tiles = new Sprite[4];
    private readonly Queue<Placement> frontier = new Queue<Placement>(1024);
    private readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
    private readonly Dictionary<Vector3Int, float> landingScores = new Dictionary<Vector3Int, float>();
    private readonly bool[,] scoreCells = new bool[Board.Width, Board.Height];
    private readonly int[] scoreHeights = new int[Board.Width];
    private Func<Vector2Int[], Vector2Int, bool> canReach;
    private LinkedListNode<BotPlayer> planningNode;
    private int lastPlannedFrame = -1;
    private bool paused;
    private float pieceTimer;
    private float clearTimer;
    private bool clearing;
    private SpinKind lockedSpin;
    private int displayedPending = -1;
    private bool displayedGameOver;
    private BattleArena arena;

    public bool IsGameOver { get; private set; }
    public bool Paused
    {
        get => paused;
        set
        {
            paused = value;
            if (paused) RemovePlanningRequest();
        }
    }
    public string DisplayName => nameLabel != null ? nameLabel.text : gameObject.name;
    public int PiecesPlaced { get; private set; }
    public int LinesCleared { get; private set; }
    public event Action<BotPlayer> Eliminated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPlanningScheduler()
    {
        PlanningQueue.Clear();
        planningFrame = -1;
        plansThisFrame = 0;
    }

    private void Awake() => HideLegacyLabels();

    private void OnEnable()
    {
        HideLegacyLabels();
        arena = GetComponentInParent<BattleArena>();
        if (arena == null) arena = FindFirstObjectByType<BattleArena>();
        if (arena != null) arena.RegisterBot(this);
    }

    private void OnDisable()
    {
        RemovePlanningRequest();
        if (boardPresentation == null) boardPresentation = GetComponent<OpponentBoardPresentation>();
        if (boardPresentation != null)
        {
            boardPresentation.FinishArrival();
            boardPresentation.SetTargeted(false);
        }
        if (arena != null) arena.UnregisterBot(this);
        arena = null;
    }

    public void SetTargeted(bool selected)
    {
        if (boardPresentation == null) boardPresentation = GetComponent<OpponentBoardPresentation>();
        if (boardPresentation != null) boardPresentation.SetTargeted(selected);
        if (targetFrame == null) return;
        Sprite original = selected ? selectedFrameSprite : normalFrameSprite;
        if (original != null)
        {
            targetFrame.sprite = original;
            targetFrame.color = Color.white;
        }
        else targetFrame.color = selected ? new Color(0.95f, 0.8f, 0.25f)
            : new Color(0.67f, 0.68f, 0.83f);
    }

    public void ShowElimination(int rank, bool creditedToPlayer)
    {
        if (rank < 1) return;
        if (boardPresentation == null) boardPresentation = GetComponent<OpponentBoardPresentation>();
        if (boardPresentation != null) boardPresentation.ShowElimination();
        if (eliminationPresentation == null) eliminationPresentation = GetComponent<OpponentEliminationPresentation>();
        if (eliminationPresentation != null) eliminationPresentation.Show(rank, creditedToPlayer);
    }

    private void HideLegacyLabels()
    {
        if (nameLabel != null) nameLabel.gameObject.SetActive(false);
        if (statusLabel != null) statusLabel.gameObject.SetActive(false);
    }

    private void Start()
    {
        ResolveReferences();
        StaggerFirstPiece();
        UpdateStatus();
    }

    private void ResolveReferences()
    {
        if (board == null) board = GetComponent<Board>();
        if (combat == null) combat = GetComponent<GarbageCombat>();
        if (eliminationPresentation == null) eliminationPresentation = GetComponent<OpponentEliminationPresentation>();
        if (boardPresentation == null) boardPresentation = GetComponent<OpponentBoardPresentation>();
        if (canReach == null) canReach = CanReach;
        for (int i = 0; i < tiles.Length; i++) tiles[i] = blockSprite;
    }

    private void Update()
    {
        UpdateStatus();
        if (Paused || IsGameOver || board == null || lastPlannedFrame == Time.frameCount) return;
        if (clearing)
        {
            clearTimer += Time.deltaTime;
            if (clearTimer >= Mathf.Max(0f, clearDelay))
            {
                clearing = false;
                int cleared = board.ClearFullLines();
                LinesCleared += cleared;
                FinishLock(cleared);
            }
            return;
        }

        pieceTimer += Time.deltaTime;
        if (pieceTimer < Mathf.Max(0.1f, secondsPerPiece)) return;
        if (planningNode == null) planningNode = new LinkedListNode<BotPlayer>(this);
        if (planningNode.List == null) PlanningQueue.AddLast(planningNode);
        ServicePlanningQueue();
    }

    private void RemovePlanningRequest()
    {
        if (planningNode != null && planningNode.List != null) PlanningQueue.Remove(planningNode);
    }

    private void StaggerFirstPiece()
    {
        // A full lobby becomes ready together. Spread its first searches without consuming Unity's player RNG.
        pieceTimer = (float)random.NextDouble() * Mathf.Max(0.1f, secondsPerPiece);
    }

    private static void ServicePlanningQueue()
    {
        if (planningFrame != Time.frameCount)
        {
            planningFrame = Time.frameCount;
            plansThisFrame = 0;
        }
        // FIFO order keeps a late-updating bot from starving behind earlier Update callbacks.
        // Waiting bots retain their due timer; only a completed scheduling turn starts the next interval.
        while (plansThisFrame < MaxPlansPerFrame && PlanningQueue.First != null)
        {
            BotPlayer bot = PlanningQueue.First.Value;
            PlanningQueue.RemoveFirst();
            if (bot == null || !bot.isActiveAndEnabled || bot.Paused || bot.IsGameOver || bot.clearing ||
                bot.board == null || bot.pieceTimer < Mathf.Max(0.1f, bot.secondsPerPiece)) continue;
            plansThisFrame++;
            bot.lastPlannedFrame = Time.frameCount;
            bot.pieceTimer = 0f;
            bot.PlaceNextPiece();
        }
    }

    /// <summary>Clears the existing board and combat state without creating any scene objects.</summary>
    public void ResetBot()
    {
        RemovePlanningRequest();
        ResolveReferences();
        if (board != null) board.ClearBoard();
        if (combat != null) combat.ResetCombat();
        if (eliminationPresentation != null) eliminationPresentation.ResetView();
        if (boardPresentation != null) boardPresentation.ResetView();
        HideLegacyLabels();
        bag.Clear();
        StaggerFirstPiece();
        lastPlannedFrame = -1;
        clearTimer = 0f;
        clearing = false;
        lockedSpin = SpinKind.None;
        IsGameOver = false;
        Paused = false;
        PiecesPlaced = 0;
        LinesCleared = 0;
        displayedPending = -1;
        UpdateStatus();
    }

    private int NextShape()
    {
        if (bag.Count == 0)
        {
            int[] next = { 0, 1, 2, 3, 4, 5, 6 };
            for (int i = next.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int value = next[i];
                next[i] = next[j];
                next[j] = value;
            }
            foreach (int shape in next) bag.Enqueue(shape);
        }
        return bag.Dequeue();
    }

    private void PlaceNextPiece()
    {
        int shape = NextShape();
        if (!TryFindPlacement(shape, out Placement placement))
        {
            Die();
            return;
        }
        // Read the corners before the T's own cells are committed, and retain this through the clear pause.
        lockedSpin = ClassifyPlacement(shape, placement);
        Sprite originalCell = cellSprites != null && shape < cellSprites.Length ? cellSprites[shape] : null;
        for (int i = 0; i < tiles.Length; i++) tiles[i] = originalCell != null ? originalCell : blockSprite;
        Color color = originalCell != null ? Color.white : PieceColors[shape];
        if (!board.TryLockPiece(placement.Cells, placement.Origin, color, tiles))
        {
            Die();
            return;
        }
        PiecesPlaced++;
        int[] rows = board.GetFullRows();
        if (rows.Length > 0)
        {
            // The miniature keeps upper rows suspended for the same clear pause as a real board.
            board.SetRowsHidden(rows, true);
            clearing = true;
            clearTimer = 0f;
        }
        else FinishLock(0);
    }

    private void FinishLock(int cleared)
    {
        pieceTimer = 0f;
        if (combat != null && !combat.ResolveLock(cleared, lockedSpin, cleared > 0 && board.IsEmpty)) Die();
        UpdateStatus();
    }

    private void Die()
    {
        if (IsGameOver) return;
        RemovePlanningRequest();
        IsGameOver = true;
        clearing = false;
        UpdateStatus();
        Eliminated?.Invoke(this);
    }

    private void UpdateStatus()
    {
        int pending = combat != null ? combat.PendingLines : 0;
        if (displayedPending == pending && displayedGameOver == IsGameOver) return;
        displayedPending = pending;
        displayedGameOver = IsGameOver;
        if (statusLabel == null) return;
        statusLabel.text = IsGameOver ? "K.O." : pending > 0 ? "+" + pending : "";
        statusLabel.color = IsGameOver ? new Color(1f, 0.3f, 0.3f)
            : new Color(1f, 0.8f, 0.25f);
    }

    private struct Placement
    {
        public Vector2Int[] Cells;
        public Vector2Int Origin;
        public int Rotation;
        public bool LastActionWasRotation;
        public int KickIndex;

        public Placement(Vector2Int[] cells, Vector2Int origin, int rotation,
            bool lastActionWasRotation = false, int kickIndex = -1)
        {
            Cells = cells;
            Origin = origin;
            Rotation = rotation;
            LastActionWasRotation = lastActionWasRotation;
            KickIndex = kickIndex;
        }
    }

    private bool CanReach(Vector2Int[] cells, Vector2Int origin)
    {
        // SRS kicks may move a piece above spawn. Bound that empty region to keep search finite.
        return origin.y <= Board.Height + 2 && board.CanPlace(cells, origin, true);
    }

    private bool TryFindPlacement(int shape, out Placement best)
    {
        best = default;
        Vector2Int spawn = new Vector2Int(shape == 1 ? 4 : 3, shape == 0 ? 19 : 18);
        Placement initial = new Placement(Shapes[shape], spawn, 0);
        if (!CanReach(initial.Cells, initial.Origin)) return false;

        bool[,] snapshot = board.CopyOccupancy();
        frontier.Clear();
        visited.Clear();
        landingScores.Clear();
        if (canReach == null) canReach = CanReach;
        Enqueue(initial, frontier, visited);
        bool found = false;
        float bestScore = float.NegativeInfinity;
        SpinKind bestSpin = SpinKind.None;
        while (frontier.Count > 0)
        {
            Placement current = frontier.Dequeue();
            Vector2Int lower = current.Origin + Vector2Int.down;
            if (!CanReach(current.Cells, lower) && board.CanPlace(current.Cells, current.Origin))
            {
                var landing = new Vector3Int(current.Origin.x, current.Origin.y, current.Rotation);
                if (!landingScores.TryGetValue(landing, out float score))
                {
                    score = Score(snapshot, current);
                    landingScores.Add(landing, score);
                }
                SpinKind spin = ClassifyPlacement(shape, current);
                if (!found || score > bestScore || (score == bestScore && spin > bestSpin))
                {
                    found = true;
                    bestScore = score;
                    bestSpin = spin;
                    best = current;
                }
            }

            TryMove(current, Vector2Int.left, frontier, visited);
            TryMove(current, Vector2Int.right, frontier, visited);
            TryMove(current, Vector2Int.down, frontier, visited);
            for (int direction = 0; direction < 2; direction++)
            {
                if (SrsRotation.TryRotate(shape, current.Cells, current.Origin, current.Rotation,
                    direction == 0, canReach, out Vector2Int[] cells, out Vector2Int origin,
                    out int rotation, out int kickIndex))
                    Enqueue(new Placement(cells, origin, rotation, shape == 2, kickIndex), frontier, visited);
            }
        }
        return found;
    }

    private void TryMove(Placement from, Vector2Int direction, Queue<Placement> frontier,
        HashSet<Vector3Int> visited)
    {
        Vector2Int destination = from.Origin + direction;
        if (CanReach(from.Cells, destination))
            // A translation after a rotation invalidates that rotation's T-spin eligibility.
            Enqueue(new Placement(from.Cells, destination, from.Rotation), frontier, visited);
    }

    private SpinKind ClassifyPlacement(int shape, Placement placement)
    {
        return shape == 2 && placement.LastActionWasRotation
            ? TSpinDetector.Classify(board, placement.Origin, placement.Rotation, placement.KickIndex)
            : SpinKind.None;
    }

    private static void Enqueue(Placement placement, Queue<Placement> frontier,
        HashSet<Vector3Int> visited)
    {
        // The same T position can have different awards depending on its last legal action.
        // Keep ordinary rotations and fifth-kick rotations separate; other shapes use only state zero.
        int action = placement.LastActionWasRotation ? (placement.KickIndex == 4 ? 2 : 1) : 0;
        var key = new Vector3Int(placement.Origin.x, placement.Origin.y, placement.Rotation + action * 4);
        if (visited.Add(key)) frontier.Enqueue(placement);
    }

    private float Score(bool[,] snapshot, Placement placement)
    {
        bool[,] result = scoreCells;
        Array.Copy(snapshot, result, snapshot.Length);
        foreach (Vector2Int offset in placement.Cells)
        {
            Vector2Int cell = placement.Origin + offset;
            result[cell.x, cell.y] = true;
        }

        int destination = 0;
        for (int source = 0; source < Board.Height; source++)
        {
            bool full = true;
            for (int x = 0; x < Board.Width; x++) full &= result[x, source];
            if (full) continue;
            for (int x = 0; x < Board.Width; x++) result[x, destination] = result[x, source];
            destination++;
        }
        int cleared = Board.Height - destination;
        for (int y = destination; y < Board.Height; y++)
            for (int x = 0; x < Board.Width; x++) result[x, y] = false;

        int[] heights = scoreHeights;
        Array.Clear(heights, 0, heights.Length);
        int aggregateHeight = 0;
        int holes = 0;
        int maxHeight = 0;
        for (int x = 0; x < Board.Width; x++)
        {
            bool covered = false;
            for (int y = Board.Height - 1; y >= 0; y--)
            {
                if (result[x, y])
                {
                    if (!covered) heights[x] = y + 1;
                    covered = true;
                }
                else if (covered) holes++;
            }
            aggregateHeight += heights[x];
            maxHeight = Mathf.Max(maxHeight, heights[x]);
        }
        int bumpiness = 0;
        int wells = 0;
        for (int x = 0; x < Board.Width; x++)
        {
            if (x > 0) bumpiness += Mathf.Abs(heights[x] - heights[x - 1]);
            int left = x == 0 ? Board.Height : heights[x - 1];
            int right = x == Board.Width - 1 ? Board.Height : heights[x + 1];
            int depth = Mathf.Max(0, Mathf.Min(left, right) - heights[x]);
            wells += depth * (depth + 1) / 2;
        }
        // When there is room, leave a clean right-hand well for real four-line attacks.
        // Existing garbage in that column or a high stack returns the bot to ordinary survival.
        bool openRightWell = true;
        for (int y = 0; y < Board.Height; y++)
            if (snapshot[Board.Width - 1, y]) openRightWell = false;
        int wellBlocks = 0;
        if (openRightWell && maxHeight < 11 && cleared < 4)
            foreach (Vector2Int offset in placement.Cells)
                if (placement.Origin.x + offset.x == Board.Width - 1) wellBlocks++;
        return 1.2f * cleared + 0.3f * cleared * cleared - 0.5f * aggregateHeight
            - 3f * holes - 0.2f * bumpiness - 0.08f * wells
            - 0.4f * Mathf.Max(0, maxHeight - 12) - 8f * wellBlocks;
    }
}
