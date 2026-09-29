#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Run only in an isolated copy of the final project. The user's SampleScene is never saved by this check.
public sealed class BattleSmoke : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [SerializeField] private FallingPiece player;
    [SerializeField] private Board playerBoard;
    [SerializeField] private BattleArena arena;
    [SerializeField] private BotPlayer first;
    [SerializeField] private BotPlayer second;
    [SerializeField] private SpriteRenderer[] authoredCells;
    [SerializeField] private SpriteRenderer[] authoredFrames;
    [SerializeField] private TMP_Text[] authoredLabels;
    [SerializeField] private Color originalBackground;
    [SerializeField] private CameraClearFlags originalClearFlags;
    private bool observeFatalRise;
    private bool sawFatalRiseSequence;
    private int fatalRiseFrame;

    private void LateUpdate()
    {
        if (!observeFatalRise) return;
        SpriteRenderer glow = playerBoard.transform.Find("Death row glow/Row 0 flash").GetComponent<SpriteRenderer>();
        if (Time.frameCount == fatalRiseFrame)
        {
            GarbageCombat combat = player.GetComponent<GarbageCombat>();
            SpriteRenderer[] cells = Get<SpriteRenderer[]>(playerBoard, "boardCells");
            for (int y = 0; y < 2; y++) for (int x = 0; x < Board.Width; x++)
            {
                Check(playerBoard.IsCellFree(new Vector2Int(x, y)) == (x == 1),
                    "Fatal garbage was not present on its rendered insertion frame");
                if (x == 1) continue;
                SpriteRenderer cell = cells[y * Board.Width + x];
                Check(cell.enabled && cell.sprite == Get<Sprite>(combat, "garbageSprite") &&
                    cell.color == Get<Color>(combat, "garbageColor"), "Fatal garbage did not render as gray blocks");
            }
            Check(!glow.enabled, "Death glow hid the fatal garbage insertion frame");
        }
        else
        {
            for (int x = 0; x < Board.Width; x++)
                Check(playerBoard.IsCellFree(new Vector2Int(x, 0)), "The first death row was not removed after the final board frame");
            if (Time.frameCount == fatalRiseFrame + 1)
                Check(!glow.enabled, "Death glow started on the row deletion frame");
            else
            {
                Check(Time.frameCount == fatalRiseFrame + 2 && glow.enabled,
                    "Death glow did not start one frame after deletion");
                observeFatalRise = false;
                sawFatalRiseSequence = true;
            }
        }
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        FallingPiece player = Object.FindFirstObjectByType<FallingPiece>();
        BattleArena arena = Object.FindFirstObjectByType<BattleArena>();
        Check(player != null && arena != null, "Final SampleScene has no authored player/battle setup");
        Check(arena.transform.Find("Incoming garbage") == null && arena.transform.Find("Opponents label") == null,
            "Removed garbage numbers or the top opponent indicator remain in the saved scene");
        Transform slots = Get<Transform>(arena, "opponentGridRoot");
        Check(slots != null && slots.childCount == 98, "Expected 98 authored slots for a 99-player layout");
        BotPlayer[] existing = arena.GetComponentsInChildren<BotPlayer>(true);
        Check(existing.Length == 1, "This fixture expects the supplied one-opponent SampleScene");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BotOpponent.prefab");
        Check(prefab != null, "Saved BotOpponent prefab is missing");
        var opponent = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slots.GetChild(1));
        opponent.name = "Bot 02 (smoke fixture)";
        opponent.transform.localPosition = Vector3.zero;
        opponent.transform.localRotation = Quaternion.identity;
        opponent.transform.localScale = new Vector3(0.13f, 0.13f, 1f);

        var check = new GameObject("Battle smoke").AddComponent<BattleSmoke>();
        check.player = player;
        check.playerBoard = player.GetComponent<Board>();
        check.arena = arena;
        check.first = existing[0];
        check.second = opponent.GetComponent<BotPlayer>();
        Get<TMP_Text>(check.second, "nameLabel").text = "BOT 02";
        // Keep automatic turns away from timed assertions. Actual placement methods are still exercised below.
        Set(check.first, "secondsPerPiece", 10000f);
        Set(check.second, "secondsPerPiece", 10000f);
        var savedCells = new List<SpriteRenderer>();
        foreach (Board board in new[] { check.playerBoard, check.first.GetComponent<Board>(), check.second.GetComponent<Board>() })
            savedCells.AddRange(Get<SpriteRenderer[]>(board, "boardCells"));
        check.authoredCells = savedCells.ToArray();
        check.authoredFrames = new[]
        {
            Get<SpriteRenderer>(player.GetComponent<BoardPresentation>(), "boardSprite"),
            Get<SpriteRenderer>(check.first, "targetFrame"),
            Get<SpriteRenderer>(check.second, "targetFrame")
        };
        check.authoredLabels = check.playerBoard.GetComponentsInChildren<TMP_Text>(true);
        check.originalBackground = Camera.main.backgroundColor;
        check.originalClearFlags = Camera.main.clearFlags;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/BattleSmoke.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Check(bool condition, string message)
    {
        if (condition) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new Exception(message);
    }

    private static T Get<T>(object instance, string field)
        => (T)instance.GetType().GetField(field, Private).GetValue(instance);
    private static void Set(object instance, string field, object value)
        => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static object Call(object instance, string method, params object[] values)
        => instance.GetType().GetMethod(method, Private).Invoke(instance, values);

    private static int Blocks(Board board)
    {
        int count = 0;
        for (int y = 0; y < Board.Height; y++) for (int x = 0; x < Board.Width; x++)
            if (!board.IsCellFree(new Vector2Int(x, y))) count++;
        return count;
    }

    private static void ForceNext(BotPlayer bot, int shape)
    {
        Queue<int> bag = Get<Queue<int>>(bot, "bag");
        bag.Clear();
        bag.Enqueue(shape);
    }

    private static void CheckGarbageRows(Board board, int count)
    {
        for (int y = 0; y < count; y++)
        {
            int blocks = 0;
            for (int x = 0; x < Board.Width; x++)
                if (!board.IsCellFree(new Vector2Int(x, y))) blocks++;
            Check(blocks == Board.Width - 1, $"Garbage row {y} on {board.name} has {blocks} blocks, expected nine");
        }
    }

    private static void CheckMini(Board board)
    {
        SpriteRenderer[] cells = Get<SpriteRenderer[]>(board, "boardCells");
        Sprite[,] art = Get<Sprite[,]>(board, "cellArt");
        Color[,] colors = Get<Color[,]>(board, "colors");
        Check(cells.Length == Board.Width * Board.Height && new HashSet<SpriteRenderer>(cells).Count == cells.Length,
            "Mini board does not have one saved renderer per cell");
        for (int y = 0; y < Board.Height; y++) for (int x = 0; x < Board.Width; x++)
        {
            SpriteRenderer cell = cells[y * Board.Width + x];
            bool occupied = !board.IsCellFree(new Vector2Int(x, y));
            Check(cell != null && cell.transform.IsChildOf(board.transform) && cell.enabled == occupied,
                $"Mini board renderer {x}, {y} does not match live occupancy");
            if (occupied)
                Check(cell.sprite == art[x, y] && cell.color == colors[x, y],
                    $"Mini board renderer {x}, {y} does not match the locked block's art and color");
        }
    }

    private void CheckAuthoredObjects()
    {
        Check(Object.FindObjectsByType<Board>(FindObjectsSortMode.None).Length == 3,
            "Play mode generated or replaced a board");
        Check(Get<Transform>(arena, "opponentGridRoot").childCount == 98,
            "Runtime changed the saved 98-slot grid");
        var currentCells = new List<SpriteRenderer>();
        foreach (Board board in new[] { playerBoard, first.GetComponent<Board>(), second.GetComponent<Board>() })
            currentCells.AddRange(Get<SpriteRenderer[]>(board, "boardCells"));
        Check(currentCells.Count == authoredCells.Length, "The saved board cell pool changed size");
        for (int i = 0; i < authoredCells.Length; i++)
            Check(authoredCells[i] != null && currentCells[i] == authoredCells[i], "A saved cell was regenerated at launch or restart");
        SpriteRenderer[] currentFrames =
        {
            Get<SpriteRenderer>(player.GetComponent<BoardPresentation>(), "boardSprite"),
            Get<SpriteRenderer>(first, "targetFrame"), Get<SpriteRenderer>(second, "targetFrame")
        };
        for (int i = 0; i < currentFrames.Length; i++)
            Check(currentFrames[i] != null && currentFrames[i] == authoredFrames[i] && currentFrames[i].sprite != null,
                "An authored board frame was replaced");
        TMP_Text[] labels = playerBoard.GetComponentsInChildren<TMP_Text>(true);
        Check(labels.Length == authoredLabels.Length && new HashSet<TMP_Text>(labels).SetEquals(authoredLabels),
            "Editable TMP labels were regenerated at launch or restart");
    }

    private void CheckCamera()
    {
        Camera camera = Camera.main;
        foreach (float aspect in new[] { 9f / 16f, 16f / 9f })
        {
            camera.aspect = aspect;
            playerBoard.FitCamera();
            Check(camera.backgroundColor == originalBackground && camera.clearFlags == originalClearFlags,
                "Battle framing changed the user's background");
            foreach (SpriteRenderer renderer in Get<Transform>(arena, "opponentGridRoot").GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite == null) continue;
                Bounds bounds = renderer.localBounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
                    CheckVisible(camera, renderer.transform.TransformPoint(bounds.center +
                        new Vector3(bounds.extents.x * x, bounds.extents.y * y, 0f)), renderer.name);
            }
            foreach (BotPlayer bot in new[] { first, second })
            {
                foreach (SpriteRenderer renderer in bot.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sprite == null) continue;
                    Bounds bounds = renderer.localBounds;
                    for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
                        CheckVisible(camera, renderer.transform.TransformPoint(bounds.center +
                            new Vector3(bounds.extents.x * x, bounds.extents.y * y, 0f)), renderer.name);
                }
                foreach (TMP_Text label in bot.GetComponentsInChildren<TMP_Text>(true))
                {
                    Rect rect = label.rectTransform.rect;
                    for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
                        CheckVisible(camera, label.transform.TransformPoint(new Vector3(x == 0 ? rect.xMin : rect.xMax,
                            y == 0 ? rect.yMin : rect.yMax, 0f)), label.name);
                }
            }
        }
    }

    private static void CheckVisible(Camera camera, Vector3 point, string name)
    {
        Vector3 viewport = camera.WorldToViewportPoint(point);
        Check(viewport.z > 0f && viewport.x >= -0.001f && viewport.x <= 1.001f &&
            viewport.y >= -0.001f && viewport.y <= 1.001f, "Camera clips miniature " + name);
    }

    private void CheckReset()
    {
        Check(!arena.IsRoundOver && !arena.PlayerWon && !player.IsGameOver && !player.BattlePaused &&
            player.HeldShape == -1 && player.CanHold && Blocks(playerBoard) == 0,
            "Battle restart did not restore the player");
        Check(arena.AliveOpponents == 2 && arena.Target != null && !arena.Target.IsGameOver,
            "Battle restart did not restore two opponents and a live target");
        foreach (BotPlayer bot in new[] { first, second })
        {
            Check(!bot.IsGameOver && !bot.Paused && bot.PiecesPlaced == 0 && bot.LinesCleared == 0 &&
                Blocks(bot.GetComponent<Board>()) == 0 && bot.GetComponent<GarbageCombat>().PendingLines == 0,
                "Battle restart left a bot's previous round state");
            CheckMini(bot.GetComponent<Board>());
            bot.Paused = true;
        }
        Check(player.GetComponent<GarbageCombat>().PendingLines == 0, "Battle restart left player garbage queued");
        Check(Get<TMP_Text>(player.GetComponent<BoardPresentation>(), "knockoutsText").text == "00",
            "Battle restart did not clear the knockout counter");
        CheckAuthoredObjects();
    }

    private void KnockOutWithPlayerGarbage(BotPlayer bot, GarbageCombat playerCombat)
    {
        Board board = bot.GetComponent<Board>();
        GarbageCombat combat = bot.GetComponent<GarbageCombat>();
        board.ClearBoard();
        combat.ResetCombat();
        Sprite art = Get<Sprite>(bot, "blockSprite");
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, Board.Height - 1), Color.white,
            new[] { art }), "Could not seed the garbage overflow fixture");
        combat.ReceiveGarbage(1, playerCombat, 5);
        ForceNext(bot, 1); // A reachable O locks low; the incoming row then overflows the pre-existing top cell.
        Call(bot, "PlaceNextPiece");
        Check(bot.IsGameOver, "Bot placement did not report the ensuing garbage top out");
        for (int x = 0; x < Board.Width; x++)
            Check(board.IsCellFree(new Vector2Int(x, 0)) == (x == 5), "Fatal garbage was absent from the knocked-out miniature");
        CheckMini(board);
    }

    private IEnumerator Start()
    {
        yield return null;
        yield return null;
        Check(player != null && player.enabled && !player.IsGameOver, "Saved player sprites or references failed at startup");
        Check(Get<AudioClip>(player, "victorySound") != null, "The supplied victory clip is not assigned");
        AudioClip victoryProbe = AudioClip.Create("Victory audio probe", 44100 * 5, 1, 44100, false);
        Set(player, "victorySound", victoryProbe);
        AudioSource victorySource = Get<AudioSource>(player, "soundSource");
        BackgroundMusic music = Get<BackgroundMusic>(player, "backgroundMusic");
        Check(victorySource != null && music != null, "Victory audio needs the existing effects source and music controller");
        player.enabled = false; // Coroutines still run, while keyboard/gravity cannot change deterministic fixtures.
        first.Paused = true;
        second.Paused = true;
        Check(arena.Opponents.Count == 2 && arena.AliveOpponents == 2,
            "An editor-added prefab did not register as a second live opponent");
        CheckAuthoredObjects();
        CheckCamera();
        Board firstBoard = first.GetComponent<Board>();
        Board secondBoard = second.GetComponent<Board>();
        GarbageCombat playerCombat = player.GetComponent<GarbageCombat>();
        GarbageCombat firstCombat = first.GetComponent<GarbageCombat>();
        GarbageCombat secondCombat = second.GetComponent<GarbageCombat>();
        foreach (GarbageCombat combat in new[] { playerCombat, firstCombat, secondCombat }) Set(combat, "incomingDelay", 0f);
        Set(player, "lineClearDuration", 0.05f);
        arena.SetTarget(second);
        foreach (GarbageCombat combat in new[] { playerCombat, firstCombat, secondCombat }) combat.SetAttackersCount(0);
        Check(arena.Target == second, "Target selection did not select the second saved board");

        // Fill four rows except their rightmost cells and complete them with the real rotated I controller.
        playerBoard.ClearBoard();
        Sprite whiteCell = Get<Sprite>(second, "blockSprite");
        var foundation = new List<Vector2Int>();
        var foundationArt = new List<Sprite>();
        for (int y = 0; y < 4; y++) for (int x = 0; x < Board.Width - 1; x++)
        {
            foundation.Add(new Vector2Int(x, y));
            foundationArt.Add(whiteCell);
        }
        Check(playerBoard.TryLockPiece(foundation, Vector2Int.zero, Color.white, foundationArt.ToArray()),
            "Could not seed a four-line clear");
        playerBoard.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, 5), Color.magenta, new[] { whiteCell });
        Call(player, "ActivatePiece", 0);
        Call(player, "Rotate", true);
        Vector2Int[] cells = Get<Vector2Int[]>(player, "cells");
        int minY = cells[0].y;
        foreach (Vector2Int cell in cells)
        {
            Check(cell.x == cells[0].x, "The actual I rotation did not become vertical");
            minY = Mathf.Min(minY, cell.y);
        }
        Set(player, "origin", new Vector2Int(9 - cells[0].x, -minY));
        Call(player, "Draw");
        Call(player, "Lock", true);
        Check(player.IsClearingLines && firstCombat.PendingLines == 0 && secondCombat.PendingLines == 0,
            "Attack was sent before the player's clear sequence completed");
        float deadline = Time.realtimeSinceStartup + 3f;
        while (player.IsClearingLines && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!player.IsClearingLines && player.LinesCleared == 4 && !player.IsGameOver,
            "Real four-line clear did not finish and spawn safely");
        Check(firstCombat.PendingLines == 0 && secondCombat.PendingLines == 4,
            "Arena did not route four garbage lines exclusively to the selected opponent");
        Check(Blocks(firstBoard) == 0 && Blocks(secondBoard) == 0,
            "An opponent board changed immediately on attack receipt");

        Color markerColor = new Color(0.8f, 0.2f, 0.65f);
        secondBoard.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, 5), markerColor, new[] { whiteCell }, 2);
        Check(secondCombat.ResolveLock(0) && secondCombat.PendingLines == 0 && Blocks(secondBoard) == 37,
            "Ready targeted garbage did not rise between opponent pieces");
        CheckGarbageRows(secondBoard, 4);
        Check(!secondBoard.IsCellFree(new Vector2Int(0, 9)) &&
            Get<Color[,]>(secondBoard, "colors")[0, 9] == markerColor, "Opponent marker did not move with its artwork");
        CheckMini(secondBoard);

        ForceNext(first, 1);
        Call(first, "PlaceNextPiece");
        Check(first.PiecesPlaced == 1 && Blocks(firstBoard) == 4 && !first.IsGameOver,
            "Actual bot search and placement did not place a piece");
        CheckMini(firstBoard);

        // Receipt during a falling piece must wait for that exact piece's lock before raising the stack.
        playerBoard.ClearBoard();
        Call(player, "ActivatePiece", 1);
        Vector2Int fallingOrigin = Get<Vector2Int>(player, "origin");
        playerCombat.ReceiveGarbage(2, firstCombat, 9);
        Check(Blocks(playerBoard) == 0 && Get<Vector2Int>(player, "origin") == fallingOrigin,
            "Incoming garbage moved the active player piece before it locked");
        Set(player, "origin", (Vector2Int)Call(player, "GetLandingOrigin"));
        Vector2Int lockedOrigin = Get<Vector2Int>(player, "origin");
        Vector2Int[] lockedCells = (Vector2Int[])Get<Vector2Int[]>(player, "cells").Clone();
        Sprite[] lockedArt = Get<Sprite[][]>(player, "tiles")[1];
        Call(player, "Lock", true);
        Check(!player.IsGameOver && !player.IsClearingLines && playerCombat.PendingLines == 0 && Blocks(playerBoard) == 22,
            "Player hard drop did not resolve ready garbage before spawning");
        CheckGarbageRows(playerBoard, 2);
        for (int i = 0; i < lockedCells.Length; i++)
        {
            Vector2Int position = lockedOrigin + lockedCells[i] + Vector2Int.up * 2;
            Check(!playerBoard.IsCellFree(position) && Get<Sprite[,]>(playerBoard, "cellArt")[position.x, position.y] == lockedArt[i],
                "Player garbage rise lost the just-locked piece's sprite");
        }
        Check(playerBoard.CanPlace(Get<Vector2Int[]>(player, "cells"), Get<Vector2Int>(player, "origin"), true),
            "Player spawned inside the raised stack");

        // A real last-action rotation into a three-corner T slot must earn a T-spin single.
        playerBoard.ClearBoard();
        playerCombat.ResetCombat();
        secondCombat.ClearPending();
        var spinFoundation = new List<Vector2Int>
        {
            new Vector2Int(3, 2), new Vector2Int(5, 2), new Vector2Int(3, 0)
        };
        for (int x = 0; x < 10; x++) if (x < 3 || x > 5) spinFoundation.Add(new Vector2Int(x, 1));
        var spinArt = new Sprite[spinFoundation.Count];
        for (int i = 0; i < spinArt.Length; i++) spinArt[i] = whiteCell;
        Check(playerBoard.TryLockPiece(spinFoundation, Vector2Int.zero, Color.white, spinArt), "Could not seed T-spin slot");
        Call(player, "ActivatePiece", 2);
        Call(player, "Rotate", false);
        Set(player, "origin", new Vector2Int(3, 1));
        Call(player, "Rotate", true);
        Check(Get<int>(player, "turns") == 0 && Get<bool>(player, "lastActionWasRotation"), "T-spin entry rotation failed");
        Check(TSpinDetector.Classify(playerBoard, new Vector2Int(3, 1), 1, 0) == SpinKind.Mini &&
            TSpinDetector.Classify(playerBoard, new Vector2Int(3, 1), 1, 4) == SpinKind.Full,
            "Facing corners and fifth kick did not distinguish mini/full spins");
        Call(player, "Lock", true);
        Check(Get<SpinKind>(player, "lockedSpin") == SpinKind.Full, "Three-corner T lock was not classified as full spin");
        deadline = Time.realtimeSinceStartup + 3f;
        while (player.IsClearingLines && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!player.IsClearingLines && secondCombat.PendingLines == 2, "T-spin single did not send two lines through the arena");
        playerBoard.ClearBoard();
        Call(player, "ActivatePiece", 2);
        Call(player, "Rotate", true);
        Check(Get<bool>(player, "lastActionWasRotation"), "Successful rotation did not record spin eligibility");
        Check(!(bool)Call(player, "Move", new Vector2Int(-20, 0)) && Get<bool>(player, "lastActionWasRotation"),
            "A blocked move removed spin eligibility");
        Check((bool)Call(player, "Move", Vector2Int.left) && !Get<bool>(player, "lastActionWasRotation"),
            "A successful translation retained spin eligibility");

        // The authored meter follows packet time, with later packets staying gray.
        GarbageMeter meter = player.GetComponentInChildren<GarbageMeter>();
        Check(meter != null, "Missing saved garbage meter under Hold");
        SpriteRenderer[] segments = Get<SpriteRenderer[]>(meter, "segments");
        Check(segments.Length == 12 && new HashSet<SpriteRenderer>(segments).Count == 12, "Expected twelve unique meter segments");
        playerCombat.ResetCombat();
        Set(playerCombat, "incomingDelay", .3f);
        playerCombat.ReceiveGarbage(2, firstCombat, 4);
        playerCombat.ReceiveGarbage(20, secondCombat, 7);
        meter.Refresh();
        Check(playerCombat.PendingLines == 12 && playerCombat.GetPendingStage(0) == GarbageStage.Queued &&
            segments[0].enabled && segments[11].enabled, "Capped queued meter did not render all twelve lines");
        yield return new WaitForSeconds(.12f);
        meter.Refresh();
        Check(playerCombat.GetPendingStage(0) == GarbageStage.Warning && segments[0].color == Get<Color>(meter, "warningColor") &&
            playerCombat.GetPendingStage(2) == GarbageStage.Queued, "Active packet was not yellow while later packets stayed gray");
        yield return new WaitForSeconds(.2f);
        meter.Refresh();
        Check(playerCombat.GetPendingStage(0) == GarbageStage.Imminent && segments[0].color == Get<Color>(meter, "imminentColor"),
            "Ready garbage was not shown in red");
        playerCombat.ClearPending();
        meter.Refresh();
        foreach (SpriteRenderer segment in segments) Check(!segment.enabled, "Empty meter retained an attack segment");
        Set(playerCombat, "incomingDelay", 0f);

        KnockOutWithPlayerGarbage(second, playerCombat);
        Check(!arena.IsRoundOver && arena.AliveOpponents == 1 && arena.Target == first,
            "A knocked-out target was not replaced by the remaining opponent");
        TMP_Text kos = Get<TMP_Text>(player.GetComponent<BoardPresentation>(), "knockoutsText");
        Check(kos.text == "01", "Player-attributed garbage knockout did not update the existing TMP counter");
        Check(!Get<bool>(player, "playedVictorySound"), "A non-final knockout played the victory sound");
        music.Play();
        yield return null;
        Check(music.IsPlaying, "Music fixture did not start before victory");
        // The last opponent can die while the player is still displaying a line-clear glow.
        playerBoard.ClearBoard();
        for (int x = 4; x < Board.Width; x++)
            playerBoard.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(x, 0), Color.white, new[] { whiteCell });
        Call(player, "ActivatePiece", 0);
        Set(player, "origin", Vector2Int.zero);
        int[] beforeVictoryQueue = Get<Queue<int>>(player, "bag").ToArray();
        Call(player, "Lock", false);
        Check(player.IsClearingLines, "Victory-during-clear fixture did not start its glow");
        KnockOutWithPlayerGarbage(first, playerCombat);
        Check(arena.IsRoundOver && arena.PlayerWon && player.BattlePaused && first.Paused && second.Paused &&
            arena.AliveOpponents == 0 && arena.Target == null && kos.text == "02",
            "Eliminating both bots did not finish the round, clear the target, and record both KOs");
        Check(playerCombat.BadgeBits == 2, "Round completion discarded earned badge bits");
        Check(Get<bool>(player, "playedVictorySound") && victorySource.isPlaying && !music.IsPlaying,
            "Victory did not play its sound and stop background music");
        deadline = Time.realtimeSinceStartup + 3f;
        while (player.IsClearingLines && Time.realtimeSinceStartup < deadline) yield return null;
        int[] afterVictoryQueue = Get<Queue<int>>(player, "bag").ToArray();
        Check(!player.IsClearingLines && !player.IsGameOver && arena.PlayerWon &&
            beforeVictoryQueue.Length == afterVictoryQueue.Length, "An in-flight clear spawned or killed the player after victory");
        for (int i = 0; i < beforeVictoryQueue.Length; i++)
            Check(beforeVictoryQueue[i] == afterVictoryQueue[i], "The next queue advanced after the battle ended");
        // A duplicate result must not replay a completed victory clip.
        victorySource.Stop();
        Call(arena, "FinishRound", true);
        player.PlayVictorySound();
        Check(!victorySource.isPlaying, "Duplicate victory notification replayed its sound");
        player.ResetForBattle();
        CheckReset();
        Check(!Get<bool>(player, "playedVictorySound") && !victorySource.isPlaying && music.IsPlaying,
            "Restart retained victory playback or failed to resume music");

        // Fatal garbage rises before loss; its rendered stack precedes the death sweep.
        DeathScreen death = player.GetComponent<DeathScreen>();
        Set(death, "rowInterval", 0.01f);
        Set(death, "rowFlashDuration", 0.02f);
        playerBoard.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, Board.Height - 1),
            Color.white, new[] { whiteCell });
        playerBoard.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(2, 8), Color.magenta, new[] { whiteCell });
        Set(playerCombat, "incomingDelay", 0f);
        playerCombat.ReceiveGarbage(2, firstCombat, 1);
        secondCombat.ReceiveGarbage(3, playerCombat, 2);
        Call(player, "ActivatePiece", 1);
        Set(player, "origin", new Vector2Int(4, 0));
        int[] queueBeforeFatalRise = Get<Queue<int>>(player, "bag").ToArray();
        fatalRiseFrame = Time.frameCount;
        observeFatalRise = true;
        Call(player, "Lock", false);
        Check(player.IsGameOver && playerCombat.LastRiseCount == 2 && playerCombat.LastAttacker == firstCombat &&
            firstCombat.BadgeBits == 1 && !playerBoard.IsCellFree(new Vector2Int(2, 10)),
            "Fatal garbage did not shift the stack before awarding the attacking opponent its KO");
        Check(string.Join(",", queueBeforeFatalRise) == string.Join(",", Get<Queue<int>>(player, "bag").ToArray()),
            "A new piece spawned after fatal garbage");
        Check(arena.IsRoundOver && !arena.PlayerWon && player.IsGameOver && player.BattlePaused && first.Paused && second.Paused,
            "Player death did not stop the whole battle");
        Check(!Get<bool>(player, "playedVictorySound"), "Defeat played the victory sound");
        Check(playerCombat.PendingLines == 0 && secondCombat.PendingLines == 0,
            "Round completion retained pending attacks");
        deadline = Time.realtimeSinceStartup + 3f;
        while (!death.CanRetry && Time.realtimeSinceStartup < deadline) yield return null;
        Check(sawFatalRiseSequence, "Fatal rise/deletion/glow frame ordering was not observed");
        Check(death.CanRetry, "Existing defeat sweep never permitted retry");
        player.RestartGame();
        CheckReset();
        CheckCamera();
        Object.Destroy(victoryProbe);
        Debug.Log("BATTLE_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }
}
#endif
