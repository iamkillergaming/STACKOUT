using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpinKind { None, Mini, Full }
public enum GarbageStage { None, Queued, Warning, Imminent }

/// <summary>The resolved clear and its attack after cancelling this player's incoming garbage.</summary>
public readonly struct ClearResult
{
    public int ClearedLines { get; }
    public SpinKind Spin { get; }
    public int Combo { get; }
    public bool BackToBack { get; }
    public bool AllClear { get; }
    public int GeneratedLines { get; }
    public int CancelledLines { get; }
    public int OutgoingLines { get; }

    public ClearResult(int clearedLines, SpinKind spin, int combo, bool backToBack, bool allClear,
        int generatedLines, int cancelledLines, int outgoingLines)
    {
        ClearedLines = clearedLines;
        Spin = spin;
        Combo = combo;
        BackToBack = backToBack;
        AllClear = allClear;
        GeneratedLines = generatedLines;
        CancelledLines = cancelledLines;
        OutgoingLines = outgoingLines;
    }
}

/// <summary>Queues incoming attacks and resolves them between pieces, after line clearing finishes.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Board))]
public sealed class GarbageCombat : MonoBehaviour
{
    [SerializeField] private Board board;
    [SerializeField] private Sprite garbageSprite;
    [SerializeField] private Color garbageColor = new Color(0.72f, 0.74f, 0.78f, 1f);
    [SerializeField, Min(0f)] private float incomingDelay = 3f;
    [SerializeField, Range(1, 6)] private int maxRisePerPiece = 6;
    [Tooltip("Garbage sent for a single, double, triple, and four-line clear. Clears cancel pending garbage first.")]
    [SerializeField] private int[] attackLines = { 0, 1, 2, 4 };

    private sealed class Packet
    {
        public int remaining;
        public int hole;
        public GarbageCombat sender;
    }

    private readonly Queue<Packet> incoming = new Queue<Packet>();
    private readonly System.Random holeRandom = new System.Random(Guid.NewGuid().GetHashCode());
    private readonly List<int> risingHoles = new List<int>(Board.Height);
    private int pendingLines;
    private float headStartedAt;
    private int combo = -1;
    private bool backToBack;
    private int attackersCount;

    public const int PendingLimit = 12;
    public const int AttackLimit = 20;
    public int PendingLines => pendingLines;
    public int LastRiseCount { get; private set; }
    public int BadgeBits { get; private set; }
    public int BadgeCount => BadgeBits >= 30 ? 4 : BadgeBits >= 14 ? 3 : BadgeBits >= 6 ? 2
        : BadgeBits >= 2 ? 1 : 0;
    public GarbageCombat LastAttacker { get; private set; }
    public event Action<GarbageCombat, int> AttackReady;
    public event Action<GarbageCombat, ClearResult> LockResolved;
    public event Action<int> PendingChanged;

    private void Awake()
    {
        if (board == null) board = GetComponent<Board>();
    }

    /// <summary>Adds one shared-hole packet. Lines beyond the twelve-line pending limit are discarded.</summary>
    public void ReceiveGarbage(int lines, GarbageCombat sender, int hole = -1)
    {
        if (lines <= 0) return;
        if (hole < -1 || hole >= Board.Width)
            throw new ArgumentOutOfRangeException(nameof(hole), "A garbage hole must be -1 (random), or a board column.");
        lines = Mathf.Min(lines, PendingLimit - pendingLines);
        if (lines == 0) return;
        if (hole == -1) hole = holeRandom.Next(Board.Width);

        if (incoming.Count == 0) headStartedAt = Time.time;
        incoming.Enqueue(new Packet
        {
            remaining = lines,
            hole = hole,
            sender = sender
        });
        pendingLines += lines;
        PendingChanged?.Invoke(PendingLines);
    }

    /// <summary>FIFO line index: only the head packet ages; all packets behind it remain gray.</summary>
    public GarbageStage GetPendingStage(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= pendingLines || incoming.Count == 0)
            return GarbageStage.None;
        if (zeroBasedIndex >= incoming.Peek().remaining) return GarbageStage.Queued;
        float duration = Mathf.Max(0f, incomingDelay);
        float elapsed = Mathf.Max(0f, Time.time - headStartedAt);
        if (duration == 0f || elapsed >= duration * (2f / 3f)) return GarbageStage.Imminent;
        return elapsed >= duration / 3f ? GarbageStage.Warning : GarbageStage.Queued;
    }

    public void SetAttackersCount(int count)
    {
        attackersCount = Mathf.Clamp(count, 0, 6);
    }

    /// <summary>A knockout awards one bit plus the defeated opponent's accumulated bits.</summary>
    public void AwardKnockout(GarbageCombat victim)
    {
        if (victim == null || victim == this) return;
        BadgeBits = (int)Math.Min(int.MaxValue, (long)BadgeBits + 1L + victim.BadgeBits);
    }

    /// <summary>
    /// Cancels incoming lines before sending any surplus attack. Clearing a line postpones rising garbage.
    /// Call exactly once per locked piece, after any clear animation and board compaction. False means top out.
    /// </summary>
    public bool ResolveLock(int clearedLines, SpinKind spin = SpinKind.None, bool allClear = false)
    {
        LastRiseCount = 0;
        if (clearedLines < 0 || clearedLines > 4)
            throw new ArgumentOutOfRangeException(nameof(clearedLines));
        if (board == null) board = GetComponent<Board>();
        if (board == null) return false;

        bool earnedBackToBack = clearedLines > 0 && backToBack
            && (clearedLines == 4 || spin != SpinKind.None);
        int attack = CalculateAttack(clearedLines, spin, allClear);
        int generated = attack;
        int cancelled = 0;
        if (attack > 0 && pendingLines > 0)
        {
            cancelled = Mathf.Min(pendingLines, attack);
            Consume(cancelled);
            attack -= cancelled;
            PendingChanged?.Invoke(PendingLines);
        }
        if (attack > 0) AttackReady?.Invoke(this, attack);
        LockResolved?.Invoke(this, new ClearResult(clearedLines, spin, combo, earnedBackToBack,
            clearedLines > 0 && allClear, generated, cancelled, attack));
        if (clearedLines > 0 || incoming.Count == 0) return true;

        // A packet waiting behind the head does not receive a countdown until it becomes active.
        // Even with a zero test delay, each non-clearing lock applies only the current head packet.
        if (Time.time - headStartedAt < Mathf.Max(0f, incomingDelay)) return true;
        Packet head = incoming.Peek();
        risingHoles.Clear();
        int limit = Mathf.Clamp(maxRisePerPiece, 1, 6);
        int count = Mathf.Min(head.remaining, limit);
        for (int i = 0; i < count; i++) risingHoles.Add(head.hole);

        // Apply even a fatal packet before reporting top out, retaining its sender for KO attribution.
        if (head.sender != null) LastAttacker = head.sender;
        if (!board.TryAddGarbage(risingHoles, garbageSprite, garbageColor, out bool overflow)) return false;

        LastRiseCount = risingHoles.Count;
        Consume(risingHoles.Count);
        PendingChanged?.Invoke(PendingLines);
        return !overflow;
    }

    private int CalculateAttack(int clearedLines, SpinKind spin, bool allClear)
    {
        if (clearedLines == 0)
        {
            combo = -1;
            return 0; // Empty locks preserve back-to-back, but break the consecutive-clear combo.
        }

        combo = Mathf.Min(combo + 1, 10);
        int attack = attackLines != null && clearedLines <= attackLines.Length
            ? Mathf.Max(0, attackLines[clearedLines - 1]) : 0;
        if (spin == SpinKind.Full && clearedLines <= 3) attack = clearedLines * 2;
        else if (spin == SpinKind.Mini && clearedLines <= 2) attack = clearedLines - 1;

        bool difficultClear = clearedLines == 4 || spin != SpinKind.None;
        if (difficultClear && backToBack) attack++;
        backToBack = difficultClear; // A normal line clear ends the difficult-clear chain.
        if (allClear) attack += 4;
        attack += combo >= 10 ? 5 : combo >= 7 ? 4 : (combo + 1) / 2;
        if (attackersCount >= 2) attack += attackersCount * 2 - 3;

        // Apply the badge multiplier to the whole attack, round down, then cap before cancellation.
        return Mathf.Clamp(Mathf.FloorToInt(attack * (1f + BadgeCount * 0.25f)), 0, AttackLimit);
    }

    /// <summary>Empties the pending meter while retaining the round's badges and attack chains.</summary>
    public void ClearPending()
    {
        bool hadPending = pendingLines > 0;
        incoming.Clear();
        risingHoles.Clear();
        pendingLines = 0;
        headStartedAt = 0f;
        if (hadPending) PendingChanged?.Invoke(0);
    }

    public void ResetCombat()
    {
        LastRiseCount = 0;
        LastAttacker = null;
        combo = -1;
        backToBack = false;
        attackersCount = 0;
        BadgeBits = 0;
        ClearPending();
    }

    private void Consume(int lines)
    {
        pendingLines -= lines;
        bool advanced = false;
        while (lines > 0)
        {
            Packet packet = incoming.Peek();
            int count = Mathf.Min(lines, packet.remaining);
            packet.remaining -= count;
            lines -= count;
            if (packet.remaining == 0)
            {
                incoming.Dequeue();
                advanced = true;
            }
        }
        if (advanced) headStartedAt = incoming.Count > 0 ? Time.time : 0f;
    }
}
