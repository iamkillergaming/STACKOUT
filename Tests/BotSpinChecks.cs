#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Execute BotSpinChecks.Run in an isolated Unity project. Uses actual Board/BotPlayer/SRS/combat code.
public static class BotSpinChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Board board;
    private static BotPlayer bot;
    private static GarbageCombat combat;
    private static readonly List<int> attacks = new List<int>();

    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = new GameObject("Bot spin checks");
            board = instance.AddComponent<Board>();
            combat = instance.AddComponent<GarbageCombat>();
            bot = instance.AddComponent<BotPlayer>();
            bot.enabled = false; // This fixture advances the real bot methods deliberately.
            Set(board, "fitCameraOnStart", false);
            combat.AttackReady += (_, lines) => attacks.Add(lines);

            // Each row is bottom first. The open vertical entry admits a legal final SRS turn.
            TestSpin("full double", new[] { "####.#####", "###...####", "...#......" }, SpinKind.Full, 2, 4);
            TestSpin("full single", new[] { "...#.#....", "###...####", "...#......" }, SpinKind.Full, 1, 2);

            // Snapshot encountered naturally with an honest seven-bag stream (seed 1, piece 246).
            // Keeping it as a fixed board makes the legal mini path reproducible without a long simulation.
            TestSpin("mini single", new[]
            {
                ".#########", ".#########", "########.#", ".#########", "########.#",
                "######...#", "####.#...#", "###..#....", "##........"
            }, SpinKind.Mini, 1, 0);
            Check(combat.ResolveLock(0) && combat.ResolveLock(4) && attacks.Count == 1 && attacks[0] == 5,
                "The bot's zero-attack mini did not establish back-to-back in real combat");

            TestTranslationInvalidation();
            Set(bot, "lockedSpin", SpinKind.Full);
            bot.ResetBot();
            Check((SpinKind)Get(bot, "lockedSpin") == SpinKind.None && board.IsEmpty,
                "ResetBot retained a spin from the previous round");
            attacks.Clear();
            Check(combat.ResolveLock(4) && attacks.Count == 1 && attacks[0] == 4,
                "ResetBot retained the previous spin's combat chain");
            UnityEngine.Object.DestroyImmediate(instance);
            Debug.Log("BOT_SPIN_CHECKS_PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Set(object target, string field, object value)
        => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Get(object target, string field)
        => target.GetType().GetField(field, Private).GetValue(target);
    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static T PlacementField<T>(object placement, string field)
        => (T)placement.GetType().GetField(field).GetValue(placement);
    private static int Blocks(bool[,] snapshot)
    {
        int count = 0;
        foreach (bool occupied in snapshot) if (occupied) count++;
        return count;
    }
    private static void Seed(int x, int y)
    {
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(x, y), Color.white),
            $"Cannot seed fixture cell {x},{y}");
    }
    private static void TestSpin(string label, string[] rows, SpinKind expectedSpin, int expectedLines, int expectedAttack)
    {
        bot.ResetBot();
        attacks.Clear();
        Set(bot, "clearDelay", 100f);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < Board.Width; x++)
                if (rows[y][x] == '#') Seed(x, y);
        Check(board.GetFullRows().Length == 0, label + " starts with an already completed row");

        var planArguments = new object[] { 2, null };
        Check((bool)Call(bot, "TryFindPlacement", planArguments), label + " has no reachable placement");
        object planned = planArguments[1];
        Check(PlacementField<bool>(planned, "LastActionWasRotation"), label + " did not finish with a rotation");
        Check((SpinKind)Call(bot, "ClassifyPlacement", 2, planned) == expectedSpin,
            label + " was incorrectly classified before locking");
        Vector2Int[] cells = PlacementField<Vector2Int[]>(planned, "Cells");
        Vector2Int origin = PlacementField<Vector2Int>(planned, "Origin");
        Check(board.CanPlace(cells, origin) && !board.CanPlace(cells, origin + Vector2Int.down),
            label + " selected an overlapping or floating placement");

        bool[,] before = board.CopyOccupancy();
        ((Queue<int>)Get(bot, "bag")).Enqueue(2);
        Call(bot, "PlaceNextPiece");
        Check(!bot.IsGameOver && bot.PiecesPlaced == 1 && (SpinKind)Get(bot, "lockedSpin") == expectedSpin,
            label + " lost its planned spin while locking");
        bool[,] after = board.CopyOccupancy();
        Check(Blocks(after) == Blocks(before) + 4, label + " did not add exactly four cells");
        for (int y = 0; y < Board.Height; y++)
            for (int x = 0; x < Board.Width; x++)
                Check(!before[x, y] || after[x, y], label + " replaced an existing block");
        Check(board.GetFullRows().Length == expectedLines && bot.LinesCleared == 0 && attacks.Count == 0,
            label + " cleared or sent its attack before the clear pause");
        Call(bot, "Update");
        Check((bool)Get(bot, "clearing") && (SpinKind)Get(bot, "lockedSpin") == expectedSpin && attacks.Count == 0,
            label + " did not retain its spin during the clear pause");
        bot.Paused = true;
        Set(bot, "clearDelay", 0f);
        Call(bot, "Update");
        Check((bool)Get(bot, "clearing") && attacks.Count == 0, label + " resolved while the bot was paused");
        bot.Paused = false;
        Call(bot, "Update");
        Check(!(bool)Get(bot, "clearing") && bot.LinesCleared == expectedLines &&
            Blocks(board.CopyOccupancy()) == Blocks(after) - 10 * expectedLines,
            label + " did not compact the expected rows");
        Check(attacks.Count == (expectedAttack > 0 ? 1 : 0) &&
            (expectedAttack == 0 || attacks[0] == expectedAttack), label + " sent the wrong actual combat attack");
        Debug.Log($"BOT_SPIN {label}: {expectedLines} line(s), {expectedAttack} attack, legal final rotation");
    }

    private static void TestTranslationInvalidation()
    {
        bot.ResetBot();
        attacks.Clear();
        Seed(3, 0);
        Seed(5, 0);
        Seed(5, 2);
        Vector2Int[] start =
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(1, 1)
        };
        Vector2Int origin = new Vector2Int(2, 2);
        Func<Vector2Int[], Vector2Int, bool> fits = (cells, position) => board.CanPlace(cells, position, true);
        Check(fits(start, origin), "Translation fixture starts inside occupied cells");
        Check(SrsRotation.TryRotate(2, start, origin, 0, true, fits, out Vector2Int[] right,
            out Vector2Int rightOrigin, out int rightState, out _), "Cannot set up the first legal rotation");
        Check(SrsRotation.TryRotate(2, right, rightOrigin, rightState, false, fits, out Vector2Int[] flat,
            out Vector2Int flatOrigin, out int flatState, out int kick), "Cannot perform the final legal rotation");
        Type placementType = typeof(BotPlayer).GetNestedType("Placement", BindingFlags.NonPublic);
        object placement = Activator.CreateInstance(placementType, new object[] { flat, flatOrigin, flatState, true, kick });
        Type queueType = typeof(Queue<>).MakeGenericType(placementType);
        object frontier = Activator.CreateInstance(queueType);
        var visited = new HashSet<Vector3Int>();
        foreach (Vector2Int move in new[] { Vector2Int.down, Vector2Int.right })
        {
            Call(bot, "TryMove", placement, move, frontier, visited);
            Check((int)queueType.GetProperty("Count").GetValue(frontier) == 1, "A required legal translation was discarded");
            placement = queueType.GetMethod("Dequeue").Invoke(frontier, null);
            Check(!PlacementField<bool>(placement, "LastActionWasRotation") && PlacementField<int>(placement, "KickIndex") == -1,
                "A translation retained the preceding rotation's spin eligibility");
        }
        Vector2Int finalOrigin = PlacementField<Vector2Int>(placement, "Origin");
        int finalRotation = PlacementField<int>(placement, "Rotation");
        Check(TSpinDetector.Classify(board, finalOrigin, finalRotation, kick) == SpinKind.Mini,
            "Translation fixture does not end in a three-corner mini-shaped cavity");
        Check((SpinKind)Call(bot, "ClassifyPlacement", 2, placement) == SpinKind.None,
            "The bot awarded a spin after translating into the cavity");
        Debug.Log("BOT_SPIN translation: real SRS rotation followed by legal down/right moves receives no spin");
    }
}
#endif
