using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Assign tightly cropped, whole-shape sprites in their documented starting orientation.</summary>
[RequireComponent(typeof(Board))]
[DisallowMultipleComponent]
public sealed class FallingPiece : MonoBehaviour
{
    [Header("Whole-shape sprites (see Assets/Scripts/Pieces.md)")]
    [SerializeField] private Sprite iSprite;
    [SerializeField] private Sprite oSprite;
    [SerializeField] private Sprite tSprite;
    [SerializeField] private Sprite sSprite;
    [SerializeField] private Sprite zSprite;
    [SerializeField] private Sprite jSprite;
    [SerializeField] private Sprite lSprite;
    [Header("Optional original cell atlas (I, O, T, S, Z, J, L)")]
    [SerializeField] private Sprite[] cellSprites = new Sprite[7];
    [SerializeField, Min(0.05f)] private float fallInterval = 0.8f;
    [SerializeField, Min(0.01f)] private float softDropInterval = 0.05f;
    [SerializeField, Min(0.01f)] private float moveRepeatInterval = 0.09f;
    [SerializeField, Min(0f)] private float moveRepeatDelay = 0.18f;
    [Header("Held Movement Acceleration")]
    [SerializeField, Min(0.01f)] private float fastestMoveInterval = 0.025f;
    [SerializeField, Min(0.01f)] private float fastestSoftDropInterval = 0.015f;
    [SerializeField, Min(0f)] private float accelerationTime = 0.6f;
    [Header("Landing")]
    [SerializeField, Min(0f)] private float lockDelay = 0.5f;
    [SerializeField, Min(0)] private int maxLockResets = 15;
    [SerializeField, Range(0f, 1f)] private float ghostOpacity = 0.25f;
    [Header("Sounds")]
    [SerializeField] private AudioSource soundSource;
    [SerializeField] private AudioClip moveSound;
    [SerializeField] private AudioClip rotateSound;
    [SerializeField] private AudioClip softDropSound;
    [SerializeField] private AudioClip dropSound;
    [SerializeField] private AudioClip hardDropSound;
    [SerializeField] private AudioClip holdSound;
    [SerializeField] private AudioClip victorySound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 1f;
    [SerializeField] private BackgroundMusic backgroundMusic;
    private bool resumeMusicOnRestart;
    private bool playedVictorySound;
    [Header("Line Clears")]
    [SerializeField] private AudioClip singleClearSound;
    [SerializeField] private AudioClip doubleClearSound;
    [SerializeField] private AudioClip tripleClearSound;
    [SerializeField] private AudioClip tetrisClearSound;
    [SerializeField, Min(0.05f)] private float lineClearDuration = 0.25f;
    [SerializeField] private Color lineClearColor = new Color(0.35f, 0.85f, 1f, 1f);

    // Coordinates are measured from the bottom-left of each sprite rectangle.
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

    private Board board;
    private DeathScreen deathScreen;
    private LineClearEffect lineClearEffect;
    private NextPiecePreview nextPreview;
    private HoldPiecePreview holdPreview;
    private GarbageCombat combat;
    private bool holdUsed;
    private Coroutine clearSequence;
    private int[] pendingClearRows;
    private readonly List<Sprite> generatedSprites = new List<Sprite>();
    private readonly Queue<int> bag = new Queue<int>();
    private readonly Sprite[][] tiles = new Sprite[7][];
    private readonly int[] spriteTurns = new int[7];
    private readonly SpriteRenderer[] renderers = new SpriteRenderer[4];
    private readonly SpriteRenderer[] ghostRenderers = new SpriteRenderer[4];
    private readonly PieceLockDelay lockTimer = new PieceLockDelay();
    private Vector2Int[] cells;
    private Vector2Int origin;
    private int shape;
    private int turns;
    private bool lastActionWasRotation;
    private int lastKickIndex;
    private SpinKind lockedSpin;
    private int heldDirection;
    private float fallTimer;
    private float repeatTimer;
    private float horizontalHoldTime;
    private float softDropHoldTime;
    private bool wasSoftDropping;
    private bool waitingForMatch;
    private bool initialized;
    public bool HasActivePiece => initialized && !waitingForMatch && cells != null && !IsGameOver;
    public bool IsGameOver { get; private set; }
    public int LinesCleared { get; private set; }
    public bool IsClearingLines { get; private set; }
    public int HeldShape { get; private set; } = -1;
    public bool BattlePaused { get; set; }
    public event System.Action<FallingPiece> Eliminated;
    public event System.Action<FallingPiece> Restarted;
    public bool CanHold => cells != null && !IsGameOver && !IsClearingLines && !holdUsed && !BattlePaused;

    private void Start()
    {
        board = GetComponent<Board>();
        combat = GetComponent<GarbageCombat>();
        if (backgroundMusic == null) backgroundMusic = FindFirstObjectByType<BackgroundMusic>();
        deathScreen = GetComponent<DeathScreen>();
        if (deathScreen == null) deathScreen = gameObject.AddComponent<DeathScreen>();
        lineClearEffect = GetComponent<LineClearEffect>();
        if (lineClearEffect == null) lineClearEffect = gameObject.AddComponent<LineClearEffect>();
        if (soundSource == null)
        {
            // A dedicated source avoids changing settings on existing music/audio components.
            soundSource = gameObject.AddComponent<AudioSource>();
            soundSource.playOnAwake = false;
            soundSource.loop = false;
            soundSource.spatialBlend = 0f;
            ownsSoundSource = true;
        }
        Sprite[] sources = { iSprite, oSprite, tSprite, sSprite, zSprite, jSprite, lSprite };
        bool useCells = cellSprites != null && cellSprites.Length == 7;
        if (useCells) foreach (Sprite tile in cellSprites) if (tile == null) useCells = false;
        foreach (Sprite source in sources)
        {
            if (!useCells && (source == null || source.packed))
            {
                Debug.LogError("Assign all seven original, unpacked shape sprites on FallingPiece.", this);
                enabled = false;
                return;
            }
        }

        // Split whole shapes into cells so individual rows can clear without losing their artwork.
        for (int type = 0; type < sources.Length; type++)
        {
            if (useCells)
            {
                tiles[type] = new[] { cellSprites[type], cellSprites[type], cellSprites[type], cellSprites[type] };
                spriteTurns[type] = 0;
                continue;
            }
            int width = type == 0 ? 4 : type == 1 ? 2 : 3;
            int height = type == 0 ? 1 : 2;
            Sprite source = sources[type];
            Rect bounds = source.rect;
            spriteTurns[type] = FindSpriteRotation(source, type, width, height);
            tiles[type] = new Sprite[4];
            for (int i = 0; i < 4; i++)
            {
                Rect rect = SourceCellRect(bounds, Shapes[type][i], width, height, spriteTurns[type]);
                tiles[type][i] = Sprite.Create(source.texture, rect, new Vector2(0.5f, 0.5f),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                generatedSprites.Add(tiles[type][i]);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            var block = new GameObject($"Active block {i}");
            block.transform.SetParent(board.transform, false);
            renderers[i] = block.AddComponent<SpriteRenderer>();
            renderers[i].sortingOrder = 2;
            renderers[i].enabled = false;
            var ghost = new GameObject($"Ghost block {i}");
            ghost.transform.SetParent(board.transform, false);
            ghostRenderers[i] = ghost.AddComponent<SpriteRenderer>();
            ghostRenderers[i].sortingOrder = 1;
            ghostRenderers[i].enabled = false;
        }
        initialized = true;
        if (!waitingForMatch) Spawn();
        board.RefitCameraIfEnabled();
    }

    // Map a logical cell back into the unrotated source image.
    private static Rect SourceCellRect(Rect bounds, Vector2Int cell, int width, int height, int rotation)
    {
        for (int turn = 0; turn < rotation; turn++)
        {
            cell = new Vector2Int(height - 1 - cell.y, cell.x);
            int previousWidth = width;
            width = height;
            height = previousWidth;
        }
        return new Rect(bounds.x + cell.x * bounds.width / width,
            bounds.y + cell.y * bounds.height / height, bounds.width / width, bounds.height / height);
    }

    private static int FindSpriteRotation(Sprite source, int type, int width, int height)
    {
        // Read a temporary GPU copy so imported textures do not need Read/Write enabled.
        Texture2D texture = source.texture;
        RenderTexture previous = RenderTexture.active;
        RenderTexture copy = RenderTexture.GetTemporary(texture.width, texture.height, 0,
            RenderTextureFormat.ARGB32);
        Texture2D readable = null;
        try
        {
            Graphics.Blit(texture, copy);
            RenderTexture.active = copy;
            readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readable.Apply();
            int bestRotation = 0;
            float bestScore = float.NegativeInfinity;
            for (int rotation = 0; rotation < 4; rotation++)
            {
                float expectedAspect = rotation % 2 == 0 ? (float)width / height : (float)height / width;
                float score = -10f * Mathf.Abs(Mathf.Log(source.rect.width / source.rect.height / expectedAspect));
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        bool filled = System.Array.IndexOf(Shapes[type], cell) >= 0;
                        Rect rect = SourceCellRect(source.rect, cell, width, height, rotation);
                        // Sample inside each block, avoiding decorative borders.
                        for (int sy = 1; sy <= 3; sy++)
                        {
                            for (int sx = 1; sx <= 3; sx++)
                            {
                                int px = Mathf.Clamp(Mathf.FloorToInt(rect.x + rect.width * sx / 4f), 0, texture.width - 1);
                                int py = Mathf.Clamp(Mathf.FloorToInt(rect.y + rect.height * sy / 4f), 0, texture.height - 1);
                                float alpha = readable.GetPixel(px, py).a;
                                score += (filled ? alpha : -alpha) / 9f;
                            }
                        }
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRotation = rotation;
                }
            }
            return bestRotation;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(copy);
            if (readable != null) Destroy(readable);
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (IsGameOver)
        {
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                RestartGame();
            }
            return;
        }
        if (IsClearingLines || BattlePaused) return;
        if (keyboard != null)
        {
            if ((keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame)
                && TryHold()) return;
            int direction = (keyboard.rightArrowKey.isPressed ? 1 : 0)
                - (keyboard.leftArrowKey.isPressed ? 1 : 0);
            if (direction != heldDirection)
            {
                heldDirection = direction;
                horizontalHoldTime = 0f;
                repeatTimer = moveRepeatDelay;
                if (direction != 0) Move(new Vector2Int(direction, 0));
            }
            else if (direction != 0)
            {
                horizontalHoldTime += Time.deltaTime;
                repeatTimer -= Time.deltaTime;
                float moveInterval = AcceleratedInterval(moveRepeatInterval, fastestMoveInterval, horizontalHoldTime);
                for (int step = 0; repeatTimer <= 0f && step < Board.Width; step++)
                {
                    if (!Move(new Vector2Int(direction, 0)))
                    {
                        repeatTimer = moveInterval;
                        break;
                    }
                    repeatTimer += moveInterval;
                }
            }
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.xKey.wasPressedThisFrame) Rotate(true);
            else if (keyboard.zKey.wasPressedThisFrame) Rotate(false);
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                Vector2Int landing = GetLandingOrigin();
                if (landing != origin) lastActionWasRotation = false;
                origin = landing;
                Lock(true);
                return;
            }
        }
        else
        {
            heldDirection = 0;
            horizontalHoldTime = 0f;
            repeatTimer = 0f;
        }
        bool softDropping = keyboard != null && keyboard.downArrowKey.isPressed;
        if (softDropping != wasSoftDropping)
        {
            // Don't turn stored gravity time into a burst of soft-drop steps (or vice versa).
            fallTimer = 0f;
            softDropHoldTime = 0f;
        }
        else if (softDropping) softDropHoldTime += Time.deltaTime;
        wasSoftDropping = softDropping;
        fallTimer += Time.deltaTime;
        float interval = softDropping
            ? Mathf.Min(fallInterval, AcceleratedInterval(softDropInterval, fastestSoftDropInterval, softDropHoldTime))
            : fallInterval;
        interval = Mathf.Max(0.01f, interval);
        for (int step = 0; fallTimer >= interval && step < Board.Height; step++)
        {
            if (!Move(Vector2Int.down))
            {
                fallTimer = 0f;
                break;
            }
            fallTimer -= interval;
            if (softDropping) PlaySound(softDropSound);
        }
        if (lockTimer.Tick(IsGrounded(), Time.deltaTime, lockDelay)) Lock();
    }

    private float AcceleratedInterval(float startingInterval, float fastestInterval, float heldTime)
    {
        float start = Mathf.Max(0.01f, startingInterval);
        float finish = Mathf.Clamp(fastestInterval, 0.01f, start);
        float progress = accelerationTime <= 0f ? 1f : Mathf.Clamp01(heldTime / accelerationTime);
        return Mathf.Lerp(start, finish, progress);
    }

    private void ResetHeldMovement()
    {
        heldDirection = 0;
        horizontalHoldTime = 0f;
        softDropHoldTime = 0f;
        wasSoftDropping = false;
        repeatTimer = 0f;
        fallTimer = 0f;
    }

    private void EnsureUpcomingPieces(int minimumCount)
    {
        // Append whole seven-piece bags; never replace already-previewed pieces.
        while (bag.Count < minimumCount)
        {
            int[] order = { 0, 1, 2, 3, 4, 5, 6 };
            for (int i = order.Length - 1; i > 0; i--)
            {
                int other = Random.Range(0, i + 1);
                int value = order[i];
                order[i] = order[other];
                order[other] = value;
            }
            foreach (int type in order) bag.Enqueue(type);
        }
    }

    private void Spawn()
    {
        EnsureUpcomingPieces(NextPiecePreview.Count + 1);
        int nextShape = bag.Dequeue();
        if (nextPreview == null)
        {
            nextPreview = GetComponent<NextPiecePreview>();
        }
        if (nextPreview != null)
            nextPreview.Show(board, bag.ToArray(), Shapes, tiles, spriteTurns);
        holdUsed = false;
        ActivatePiece(nextShape);
        UpdateHoldPreview();
    }

    private void ActivatePiece(int type)
    {
        shape = type;
        cells = (Vector2Int[])Shapes[shape].Clone();
        origin = new Vector2Int(shape == 1 ? 4 : 3, shape == 0 ? Board.Height - 1 : Board.Height - 2);
        turns = 0;
        lastActionWasRotation = false;
        lastKickIndex = -1;
        lockedSpin = SpinKind.None;
        ResetHeldMovement();
        lockTimer.Reset();
        if (!board.CanPlace(cells, origin))
        {
            EndGame();
            return;
        }
        Draw();
    }

    public bool TryHold()
    {
        if (!CanHold) return false;
        int incoming = HeldShape;
        HeldShape = shape;
        if (incoming < 0) Spawn();
        else ActivatePiece(incoming);
        holdUsed = true;
        UpdateHoldPreview();
        if (!IsGameOver) PlaySound(holdSound);
        return true;
    }

    private void UpdateHoldPreview()
    {
        if (holdPreview == null)
        {
            holdPreview = GetComponent<HoldPiecePreview>();
        }
        if (holdPreview != null)
            holdPreview.Show(board, HeldShape, Shapes, tiles, spriteTurns, CanHold);
    }

    private bool Move(Vector2Int offset)
    {
        if (!CanPlaceActive(cells, origin + offset)) return false;
        bool wasGrounded = IsGrounded();
        origin += offset;
        if (offset != Vector2Int.zero) lastActionWasRotation = false;
        if (offset.x != 0)
        {
            lockTimer.OnSuccessfulAction(wasGrounded, maxLockResets);
            PlaySound(moveSound);
        }
        Draw();
        return true;
    }

    private bool CanPlaceActive(Vector2Int[] candidateCells, Vector2Int candidateOrigin)
    {
        return board.CanPlace(candidateCells, candidateOrigin, true);
    }

    private void Rotate(bool clockwise)
    {
        bool wasGrounded = IsGrounded();
        if (SrsRotation.TryRotate(shape, cells, origin, turns, clockwise, CanPlaceActive,
            out Vector2Int[] rotated, out Vector2Int kickedOrigin, out int nextState, out int kickIndex))
        {
            cells = rotated;
            origin = kickedOrigin;
            turns = nextState;
            lastActionWasRotation = true;
            lastKickIndex = kickIndex;
            lockTimer.OnSuccessfulAction(wasGrounded, maxLockResets);
            PlaySound(rotateSound);
            Draw();
        }
    }

    private void Lock(bool hardDrop = false)
    {
        if (IsGameOver || IsClearingLines || BattlePaused) return;
        lockedSpin = shape == 2 && lastActionWasRotation
            ? TSpinDetector.Classify(board, origin, turns, lastKickIndex) : SpinKind.None;
        if (!board.TryLockPiece(cells, origin, Color.white, tiles[shape], turns + spriteTurns[shape]))
        {
            EndGame();
            return;
        }
        PlaySound(hardDrop ? hardDropSound : dropSound);
        int[] rows = board.GetFullRows();
        if (rows.Length == 0)
        {
            FinishPlacement(0);
            return;
        }

        board.SetActivePieceCells(null, Vector2Int.zero);
        foreach (SpriteRenderer renderer in renderers) renderer.enabled = false;
        foreach (SpriteRenderer renderer in ghostRenderers) renderer.enabled = false;
        IsClearingLines = true;
        UpdateHoldPreview();
        pendingClearRows = rows;
        PlaySound(GetLineClearSound(rows.Length));
        clearSequence = StartCoroutine(ClearLines(rows));
    }

    private AudioClip GetLineClearSound(int count)
    {
        switch (count)
        {
            case 1: return singleClearSound;
            case 2: return doubleClearSound;
            case 3: return tripleClearSound;
            case 4: return tetrisClearSound;
            default: return null;
        }
    }

    private IEnumerator ClearLines(int[] rows)
    {
        yield return lineClearEffect.Play(board, rows, lineClearDuration, lineClearColor);
        int cleared = board.ClearFullLines();
        LinesCleared += cleared;
        pendingClearRows = null;
        IsClearingLines = false;
        clearSequence = null;
        FinishPlacement(cleared);
    }

    // Incoming rows can only move the stack after a lock/clear, before the next spawn.
    private void FinishPlacement(int cleared)
    {
        if (IsGameOver || BattlePaused) return;
        if (combat == null) combat = GetComponent<GarbageCombat>();
        if (combat != null && !combat.ResolveLock(cleared, lockedSpin, cleared > 0 && board.IsEmpty))
        {
            EndGame();
            return;
        }
        if (!IsGameOver && !BattlePaused) Spawn();
    }

    private void CancelLineClear()
    {
        if (clearSequence != null) StopCoroutine(clearSequence);
        clearSequence = null;
        pendingClearRows = null;
        IsClearingLines = false;
        if (lineClearEffect != null) lineClearEffect.Hide();
    }

    private bool ownsSoundSource;

    private void PlaySound(AudioClip clip)
    {
        if (soundSource != null && clip != null) soundSource.PlayOneShot(clip, soundVolume);
    }

    public AudioClip VictorySound => victorySound;

    public void PlayVictorySound()
    {
        if (!BattlePaused || IsGameOver || playedVictorySound) return;
        playedVictorySound = true;
        resumeMusicOnRestart = backgroundMusic != null && backgroundMusic.IsPlaying;
        if (backgroundMusic != null) backgroundMusic.Stop();
        if (soundSource != null) soundSource.Stop();
        PlaySound(victorySound);
    }

    private void EndGame()
    {
        if (IsGameOver) return;
        CancelLineClear();
        board.SetActivePieceCells(null, Vector2Int.zero);
        IsGameOver = true;
        UpdateHoldPreview();
        resumeMusicOnRestart = backgroundMusic != null && backgroundMusic.IsPlaying;
        if (backgroundMusic != null) backgroundMusic.Stop();
        foreach (SpriteRenderer renderer in renderers) renderer.enabled = false;
        foreach (SpriteRenderer renderer in ghostRenderers) renderer.enabled = false;
        deathScreen.Show(board, combat != null && combat.LastRiseCount > 0);
        Eliminated?.Invoke(this);
    }

    public void RestartGame()
    {
        if (!IsGameOver || !deathScreen.CanRetry) return;
        ResetForBattle();
    }

    // Also permits the arena to restart after winning, without creating a new board.
    public void ResetForBattle()
    {
        MatchFlow flow = GetComponentInChildren<MatchFlow>(true);
        if (flow != null && flow.isActiveAndEnabled)
        {
            if (flow.Phase == MatchFlow.MatchPhase.Finished) flow.BeginMatchmaking();
            return;
        }
        if (board == null || (IsGameOver && deathScreen != null && !deathScreen.CanRetry)) return;
        if (playedVictorySound && soundSource != null) soundSource.Stop();
        playedVictorySound = false;
        CancelLineClear();
        if (deathScreen != null) deathScreen.Hide();
        if (resumeMusicOnRestart && backgroundMusic != null && backgroundMusic.isActiveAndEnabled)
            backgroundMusic.Play();
        resumeMusicOnRestart = false;
        board.ClearBoard();
        bag.Clear();
        HeldShape = -1;
        holdUsed = false;
        LinesCleared = 0;
        IsGameOver = false;
        BattlePaused = false;
        if (combat != null) combat.ResetCombat();
        Restarted?.Invoke(this);
        Spawn();
    }

    /// <summary>Resets the saved board while keeping the first piece hidden until GO.</summary>
    public void PrepareForMatch()
    {
        waitingForMatch = true;
        if (board == null) board = GetComponent<Board>();
        if (combat == null) combat = GetComponent<GarbageCombat>();
        if (deathScreen == null) deathScreen = GetComponent<DeathScreen>();
        if (nextPreview == null) nextPreview = GetComponent<NextPiecePreview>();
        if (holdPreview == null) holdPreview = GetComponent<HoldPiecePreview>();
        CancelLineClear();
        if (deathScreen != null) deathScreen.Hide();
        if (soundSource != null) soundSource.Stop();
        playedVictorySound = false;
        resumeMusicOnRestart = false;
        if (board != null) board.ClearBoard();
        if (combat != null) combat.ResetCombat();
        bag.Clear();
        cells = null;
        HeldShape = -1;
        holdUsed = false;
        LinesCleared = 0;
        IsGameOver = false;
        BattlePaused = true;
        foreach (SpriteRenderer renderer in renderers) if (renderer != null) renderer.enabled = false;
        foreach (SpriteRenderer renderer in ghostRenderers) if (renderer != null) renderer.enabled = false;
        if (nextPreview != null) nextPreview.Hide();
        if (holdPreview != null) holdPreview.Hide();
        Restarted?.Invoke(this);
    }

    public void BeginBattle()
    {
        if (!waitingForMatch || IsGameOver) return;
        waitingForMatch = false;
        BattlePaused = false;
        if (initialized) Spawn();
    }

    [ContextMenu("Test Game Over (Play Mode)")]
    private void TestGameOver()
    {
        if (Application.isPlaying && enabled && cells != null) EndGame();
    }

    private bool IsGrounded()
    {
        return !CanPlaceActive(cells, origin + Vector2Int.down);
    }

    private Vector2Int GetLandingOrigin()
    {
        Vector2Int landing = origin;
        while (CanPlaceActive(cells, landing + Vector2Int.down)) landing += Vector2Int.down;
        return landing;
    }

    private void Draw()
    {
        board.SetActivePieceCells(cells, origin);
        Vector2Int landing = GetLandingOrigin();
        for (int i = 0; i < 4; i++)
        {
            SpriteRenderer renderer = renderers[i];
            renderer.enabled = true;
            renderer.sprite = tiles[shape][i];
            renderer.transform.position = board.CellToWorld(origin + cells[i]);
            renderer.transform.localRotation = Quaternion.Euler(0, 0, -90f * (turns + spriteTurns[shape]));
            renderer.transform.localScale = board.GetPieceScale(renderer.sprite);
            SpriteRenderer ghost = ghostRenderers[i];
            ghost.enabled = landing != origin && ghostOpacity > 0f;
            ghost.sprite = renderer.sprite;
            ghost.color = new Color(1f, 1f, 1f, ghostOpacity);
            ghost.transform.position = board.CellToWorld(landing + cells[i]);
            ghost.transform.localRotation = renderer.transform.localRotation;
            ghost.transform.localScale = renderer.transform.localScale;
        }
    }

    private void OnDestroy()
    {
        CancelLineClear();
        if (board != null) board.SetActivePieceCells(null, Vector2Int.zero);
        if (ownsSoundSource && soundSource != null) Destroy(soundSource);
        foreach (SpriteRenderer renderer in renderers)
            if (renderer != null) Destroy(renderer.gameObject);
        foreach (SpriteRenderer renderer in ghostRenderers)
            if (renderer != null) Destroy(renderer.gameObject);
        foreach (Sprite sprite in generatedSprites)
            if (sprite != null) Destroy(sprite);
        if (nextPreview != null) nextPreview.Hide();
        if (holdPreview != null) holdPreview.Hide();
    }

    private void OnDisable()
    {
        ResetHeldMovement();
        // Keep the locked board intact and resume its pending clear when re-enabled.
        if (clearSequence != null) StopCoroutine(clearSequence);
        clearSequence = null;
        if (lineClearEffect != null) lineClearEffect.Hide();
    }

    private void OnEnable()
    {
        if (IsClearingLines && pendingClearRows != null)
            clearSequence = StartCoroutine(ClearLines(pendingClearRows));
    }
}
