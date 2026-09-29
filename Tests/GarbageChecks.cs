#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Execute GarbageChecks.Run in an isolated Unity project. Never modifies the authored game scene.
public sealed class GarbageChecks : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GarbageCombat receiver;
    private GarbageCombat senderA;
    private GarbageCombat senderB;
    private Board board;
    private readonly List<int> attacks = new List<int>();
    private readonly List<int> pendingEvents = new List<int>();

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Garbage checks", typeof(GarbageChecks));
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/GarbageChecks.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Check(bool condition, string message)
    {
        if (condition) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new Exception(message);
    }

    private static void Set(object instance, string field, object value)
        => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static T Data<T>(Board source, string field)
        => (T)typeof(Board).GetField(field, Private).GetValue(source);
    private static GarbageCombat MakeCombat(string name)
    {
        var instance = new GameObject(name, typeof(Board), typeof(GarbageCombat));
        Set(instance.GetComponent<Board>(), "fitCameraOnStart", false);
        return instance.GetComponent<GarbageCombat>();
    }
    private static int Blocks(Board source)
    {
        int count = 0;
        foreach (bool occupied in source.CopyOccupancy()) if (occupied) count++;
        return count;
    }
    private static int Hole(Board source, int row)
    {
        int hole = -1;
        for (int x = 0; x < Board.Width; x++)
        {
            if (!source.IsCellFree(new Vector2Int(x, row))) continue;
            Check(hole < 0, $"Garbage row {row} has multiple holes");
            hole = x;
        }
        Check(hole >= 0, $"Garbage row {row} is solid");
        return hole;
    }
    private void Fresh(float delay = 0f)
    {
        receiver.ResetCombat();
        board.ClearBoard();
        attacks.Clear();
        pendingEvents.Clear();
        Set(receiver, "incomingDelay", delay);
        Set(receiver, "maxRisePerPiece", 6);
    }
    private int Attack(int lines, SpinKind spin = SpinKind.None, bool allClear = false)
    {
        int previous = attacks.Count;
        Check(receiver.ResolveLock(lines, spin, allClear), "Attack fixture unexpectedly topped out");
        Check(attacks.Count <= previous + 1, "One lock emitted multiple attack packets");
        return attacks.Count > previous ? attacks[attacks.Count - 1] : 0;
    }
    private void AddBadgeBits(int count)
    {
        // Distinct zero-bit victims exercise the same KO award path as a real round.
        for (int i = 0; i < count; i++)
        {
            GarbageCombat victim = MakeCombat("Badge victim");
            receiver.AwardKnockout(victim);
            Destroy(victim.gameObject);
        }
    }

    private IEnumerator Start()
    {
        receiver = MakeCombat("Receiver");
        senderA = MakeCombat("Sender A");
        senderB = MakeCombat("Sender B");
        board = receiver.GetComponent<Board>();
        receiver.PendingChanged += pendingEvents.Add;
        receiver.AttackReady += (source, lines) =>
        {
            Check(source == receiver && lines > 0 && lines <= 20, "Invalid attack event");
            attacks.Add(lines);
        };

        Fresh();
        receiver.ReceiveGarbage(0, senderA, 4);
        receiver.ReceiveGarbage(-1, senderA, 4);
        Check(pendingEvents.Count == 0, "Nonpositive garbage changed the queue");
        bool rejected = false;
        try { receiver.ReceiveGarbage(1, senderA, Board.Width); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && receiver.PendingLines == 0, "Invalid hole was accepted");
        receiver.ReceiveGarbage(8, senderA, 2);
        receiver.ReceiveGarbage(int.MaxValue, senderB, 7);
        int events = pendingEvents.Count;
        receiver.ReceiveGarbage(1, senderA, 9);
        Check(receiver.PendingLines == 12 && pendingEvents.Count == events && Blocks(board) == 0,
            "Twelve-line pending cap did not discard excess without moving the board");
        Check(receiver.GetPendingStage(-1) == GarbageStage.None && receiver.GetPendingStage(12) == GarbageStage.None,
            "Out-of-range meter slots are visible");
        Check(receiver.ResolveLock(0) && receiver.PendingLines == 6 && Blocks(board) == 54,
            "First packet exceeded the six-line rise cap");
        Check(receiver.ResolveLock(0) && receiver.PendingLines == 4 && Blocks(board) == 72,
            "A partial head packet lost rows or pulled rows from the next packet");
        Check(receiver.ResolveLock(0) && receiver.PendingLines == 0 && Blocks(board) == 108,
            "Accepted rows in the second packet were lost");
        for (int y = 0; y < 4; y++) Check(Hole(board, y) == 7, "Newest packet hole was changed");
        for (int y = 4; y < 12; y++) Check(Hole(board, y) == 2, "Split packet hole was changed");
        Check(receiver.LastAttacker == senderB, "Applied head packet has incorrect attribution");

        Fresh(3f);
        receiver.ReceiveGarbage(2, senderA, 3);
        receiver.ReceiveGarbage(2, senderB, 8);
        Check(receiver.GetPendingStage(0) == GarbageStage.Queued && receiver.GetPendingStage(2) == GarbageStage.Queued,
            "New packets did not start gray");
        Set(receiver, "headStartedAt", Time.time - 1.1f);
        Check(receiver.GetPendingStage(0) == GarbageStage.Warning && receiver.GetPendingStage(2) == GarbageStage.Queued,
            "Only the head packet should turn yellow during its middle third");
        Set(receiver, "headStartedAt", Time.time - 2.1f);
        Check(receiver.GetPendingStage(1) == GarbageStage.Imminent && receiver.GetPendingStage(3) == GarbageStage.Queued,
            "Only the head packet should turn red during its final third");
        Check(receiver.ResolveLock(0) && Blocks(board) == 0, "Red garbage rose before the full delay expired");
        Set(receiver, "headStartedAt", Time.time - 3.1f);
        Check(Attack(1) == 0 && receiver.PendingLines == 4 && Blocks(board) == 0,
            "A zero-attack single failed to postpone mature garbage");
        Check(receiver.ResolveLock(0) && receiver.PendingLines == 2 && Blocks(board) == 18,
            "Mature head packet failed to rise");
        Check(receiver.GetPendingStage(0) == GarbageStage.Queued && receiver.ResolveLock(0) && Blocks(board) == 18,
            "A packet behind the head aged before becoming active");
        Set(receiver, "headStartedAt", Time.time - 1.1f);
        Check(receiver.GetPendingStage(0) == GarbageStage.Warning, "New head's timer did not start");

        // A real game-time delay supplements the controlled timer boundary checks above.
        Fresh(0.06f);
        receiver.ReceiveGarbage(3, senderA);
        Check(receiver.ResolveLock(0) && Blocks(board) == 0, "Fresh garbage rose immediately");
        yield return new WaitForSeconds(0.08f);
        Check(receiver.ResolveLock(0) && Blocks(board) == 27, "Real elapsed game time did not mature the packet");
        int randomHole = Hole(board, 0);
        for (int y = 1; y < 3; y++) Check(Hole(board, y) == randomHole, "Random holes differ inside one packet");

        Fresh(3f);
        receiver.ReceiveGarbage(2, senderA, 1);
        receiver.ReceiveGarbage(3, senderB, 8);
        Set(receiver, "headStartedAt", Time.time - 2.5f);
        Check(Attack(3) == 0 && receiver.PendingLines == 3, "Triple did not cancel the oldest two lines");
        Check(receiver.GetPendingStage(0) == GarbageStage.Queued && receiver.LastAttacker == null,
            "Cancellation did not activate a fresh timer, or credited unapplied garbage");
        Check(Attack(4) == 2 && receiver.PendingLines == 0 && Blocks(board) == 0,
            "Tetris plus consecutive-clear bonus should cancel three lines and send two surplus");

        int[] baseAttacks = { 0, 1, 2, 4 };
        for (int lines = 1; lines <= 4; lines++)
        {
            Fresh();
            Check(Attack(lines) == baseAttacks[lines - 1], "Base attack table mismatch");
        }
        for (int lines = 1; lines <= 3; lines++)
        {
            Fresh();
            Check(Attack(lines, SpinKind.Full) == lines * 2, "Full T-spin attack mismatch");
        }
        Fresh(); Check(Attack(1, SpinKind.Mini) == 0, "Mini single attack mismatch");
        Fresh(); Check(Attack(2, SpinKind.Mini) == 1, "Mini double attack mismatch");
        Fresh(); Check(Attack(1, SpinKind.None, true) == 4, "All-clear bonus mismatch");
        Check(Attack(0, SpinKind.Full, true) == 0, "A no-clear lock emitted bonus garbage");

        Fresh();
        Check(Attack(4) == 4 && Attack(0) == 0 && Attack(4) == 5,
            "Non-clearing locks must preserve back-to-back but reset combos");
        Attack(1); // Ordinary clear breaks back-to-back.
        Attack(0);
        Check(Attack(4) == 4, "Normal clear failed to break back-to-back");
        Fresh();
        Check(Attack(1, SpinKind.Mini) == 0, "First mini should not receive back-to-back");
        Attack(0);
        Check(Attack(1, SpinKind.Full) == 3, "Mini clear did not establish back-to-back");
        Fresh();
        int[] comboAttacks = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 4, 5, 5 };
        foreach (int expected in comboAttacks) Check(Attack(1) == expected, "Combo table mismatch");
        Attack(0);
        Check(Attack(1) == 0, "Empty lock failed to reset the combo");

        int[] targetBonus = { 0, 0, 1, 3, 5, 7, 9, 9 };
        for (int attackers = 0; attackers < targetBonus.Length; attackers++)
        {
            Fresh();
            receiver.SetAttackersCount(attackers);
            Check(Attack(1) == targetBonus[attackers], "Attacker count bonus mismatch");
        }
        int[] thresholds = { 2, 6, 14, 30 };
        for (int tier = 0; tier < thresholds.Length; tier++)
        {
            Fresh();
            AddBadgeBits(thresholds[tier] - 1);
            Check(receiver.BadgeCount == tier, "Badge threshold activated too early");
            AddBadgeBits(1);
            Check(receiver.BadgeBits == thresholds[tier] && receiver.BadgeCount == tier + 1,
                "Badge threshold mismatch");
            Check(Attack(4) == 5 + tier, "Badge damage multiplier mismatch");
        }
        Fresh();
        senderA.AwardKnockout(senderB); // Victim now has one bit of its own.
        receiver.AwardKnockout(senderA);
        Check(receiver.BadgeBits == 2, "KO failed to transfer one bit plus victim bits");
        receiver.SetAttackersCount(2);
        Check(Attack(3) == 3, "Multiplier must floor the full base plus attacker bonus");
        Fresh();
        AddBadgeBits(30);
        receiver.SetAttackersCount(6);
        Check(Attack(3, SpinKind.Full, true) == 20, "Outgoing attack cap is not twenty");
        receiver.ReceiveGarbage(12, senderB, 5);
        Check(Attack(3, SpinKind.Full, true) == 8 && receiver.PendingLines == 0,
            "Outgoing cap must apply before cancellation, not after it");

        // ClearPending is used when a round finishes; ResetCombat prepares a new round.
        receiver.ReceiveGarbage(3, senderB, 1);
        receiver.ClearPending();
        Check(receiver.PendingLines == 0 && receiver.BadgeBits == 30 && receiver.BadgeCount == 4,
            "Clearing the meter erased earned badges");
        Check(Attack(4) == 20, "ClearPending erased combo, back-to-back, or attacker state");
        receiver.ResetCombat();
        Check(receiver.PendingLines == 0 && receiver.BadgeBits == 0 && receiver.LastAttacker == null && Attack(4) == 4,
            "Round reset left attack-chain, target-count, badge, or garbage state");

        // Invalid insertion is atomic. Fatal overflow still inserts rows and preserves surviving art.
        Fresh();
        Sprite art = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        Color color = new Color(0.7f, 0.15f, 0.9f, 1f);
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(4, 5), color, new[] { art }, 3), "Seed failed");
        receiver.ReceiveGarbage(2, senderA, 9);
        Check(receiver.ResolveLock(0) && !board.IsCellFree(new Vector2Int(4, 7)), "Existing block was not shifted");
        Check(Data<Sprite[,]>(board, "cellArt")[4, 7] == art && Data<Color[,]>(board, "colors")[4, 7] == color &&
            Data<int[,]>(board, "cellTurns")[4, 7] == 3, "Garbage lost original artwork, color, or rotation");
        int before = Blocks(board);
        board.SetRowsHidden(new[] { 7 }, true);
        Check(!board.TryAddGarbage(new[] { 2 }, art, color, out bool hiddenOverflow) && !hiddenOverflow && Blocks(board) == before,
            "Garbage modified a board during clear animation");
        board.SetRowsHidden(new[] { 7 }, false);
        Check(!board.TryAddGarbage(new[] { 0, Board.Width }, art, color, out bool invalidOverflow) && !invalidOverflow && Blocks(board) == before,
            "Invalid hole caused a partial insertion");
        Fresh();
        board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(4, 19), color, new[] { art }, 2);
        board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(2, 8), color, new[] { art }, 3);
        receiver.ReceiveGarbage(8, senderB, 6);
        events = pendingEvents.Count;
        Check(!receiver.ResolveLock(0) && receiver.PendingLines == 2 && receiver.LastAttacker == senderB &&
            receiver.LastRiseCount == 6 && Blocks(board) == 55 && pendingEvents.Count == events + 1 &&
            Data<Sprite[,]>(board, "cellArt")[2, 14] == art && Data<Color[,]>(board, "colors")[2, 14] == color &&
            Data<int[,]>(board, "cellTurns")[2, 14] == 3 && board.IsCellFree(new Vector2Int(4, 19)),
            "Fatal rise did not insert and consume its capped rows, preserve surviving art, or retain KO attribution");
        for (int y = 0; y < 6; y++) for (int x = 0; x < Board.Width; x++)
            Check(board.IsCellFree(new Vector2Int(x, y)) == (x == 6), "Fatal garbage lost its shared hole");
        receiver.ResetCombat();
        Check(receiver.LastRiseCount == 0, "Round reset retained fatal-rise state");
        Destroy(art);
        Debug.Log("GARBAGE_CHECKS_PASSED");
        EditorApplication.Exit(0);
    }
}
#endif
