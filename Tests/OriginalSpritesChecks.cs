#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Run only in an isolated copy after OriginalSpriteSetup.BatchSetup.
public sealed class OriginalSpritesChecks : MonoBehaviour
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string OriginalRoot = "Assets/Nintendo Switch - Tetris 99 - Themes - ";
    private const string Theme = OriginalRoot + "Default (Tetris 99)/";
    [SerializeField] private FallingPiece player;
    [SerializeField] private BattleArena arena;
    [SerializeField] private MatchFlow flow;
    [SerializeField] private BotPlayer[] bots;
    [SerializeField] private TMP_Text[] savedLabels;
    [SerializeField] private SpriteRenderer[] savedEliminationRenderers;
    [SerializeField] private SpriteRenderer[] savedBoardPresentationRenderers;
    [SerializeField] private SpriteRenderer[] savedMatchmakingCells;
    private AudioSource knockoutSource;
    private AudioClip probe;
    private UnityEngine.Random.State originalRandomState;
    private Vector3 cameraPosition;
    private float cameraSize;

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var check = new GameObject("Original sprites checks").AddComponent<OriginalSpritesChecks>();
            check.player = Object.FindFirstObjectByType<FallingPiece>();
            check.arena = Object.FindFirstObjectByType<BattleArena>();
            check.flow = Object.FindFirstObjectByType<MatchFlow>();
            Check(check.player != null && check.arena != null && check.flow != null, "The saved match is incomplete");
            check.bots = Get<BotPlayer[]>(check.arena, "bots");
            Check(check.bots.Length == 98 && new HashSet<BotPlayer>(check.bots).Count == 98,
                "Expected 98 distinct saved opponents");
            check.savedLabels = check.player.GetComponentsInChildren<TMP_Text>(true);
            check.savedEliminationRenderers = check.EliminationRenderers();
            check.savedBoardPresentationRenderers = check.BoardPresentationRenderers();
            check.savedMatchmakingCells = Get<SpriteRenderer[]>(
                Get<GameObject>(check.flow, "lobbyRoot").GetComponent<MatchmakingGridPresentation>(), "cells");
            Check(Get<int>(check.flow, "minJoinBatch") == 1 && Get<int>(check.flow, "maxJoinBatch") == 5,
                "Default arrival waves must contain one through five opponents");
            Check(Mathf.Approximately(Get<float>(check.flow, "matchmakingDuration"), 90f), "Lobby deadline changed");
            check.CheckSavedArt();
            foreach (BotPlayer bot in check.bots)
            {
                Check(!bot.gameObject.activeSelf, "An opponent is active before matchmaking");
                CheckNamesHidden(bot);
            }
            AudioClip ko = Get<AudioClip>(check.flow, "knockoutSound");
            Check(ko != null && AssetDatabase.GetAssetPath(ko) == "Assets/General Sounds/me_game_ko_vo.wav",
                "The dedicated K.O. cue is not the supplied original voice");
            AudioSource source = Get<AudioSource>(check.flow, "knockoutSource");
            Check(source != null && source != Get<AudioSource>(check.flow, "cueSource") && !source.playOnAwake
                && !source.loop && source.spatialBlend == 0f, "K.O. voice needs its own saved non-looping 2D source");
            EditorSceneManager.SaveScene(check.player.gameObject.scene, "Assets/OriginalSpritesChecks.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private IEnumerator Start()
    {
        // Reflection before SaveScene does not reliably persist prefab overrides: set controls after load.
        foreach (BotPlayer bot in bots) Set(bot, "secondsPerPiece", 10000f);
        Set(player, "fallInterval", 10000f);
        Set(flow, "minJoinInterval", 10000f); Set(flow, "maxJoinInterval", 10000f); Set(flow, "joinTimer", 10000f);
        Set(flow, "readyDuration", .05f); Set(flow, "countdownInterval", .05f); Set(flow, "goDuration", .05f);
        originalRandomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(90210);
        Set(arena, "lobbyAdmissions", new System.Random(1234));
        knockoutSource = Get<AudioSource>(flow, "knockoutSource");
        probe = AudioClip.Create("Original K.O. playback probe", 441000, 1, 44100, false);
        Set(flow, "knockoutSound", probe); knockoutSource.volume = 0f;
        yield return null;
        IEnumerator sequence = Checks();
        while (true)
        {
            object next;
            try { if (!sequence.MoveNext()) break; next = sequence.Current; }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); yield break; }
            yield return next;
        }
    }

    private IEnumerator Checks()
    {
        Board playerBoard = player.GetComponent<Board>();
        Camera.main.aspect = 16f / 9f; playerBoard.FitCamera();
        cameraPosition = Camera.main.transform.position; cameraSize = Camera.main.orthographicSize;
        Check(arena.ParticipantCount == 1 && !player.HasActivePiece && player.BattlePaused, "Lobby startup was not gated");
        GameObject lobbyRoot = Get<GameObject>(flow, "lobbyRoot");
        MatchmakingGridPresentation loadingGrid = lobbyRoot.GetComponent<MatchmakingGridPresentation>();
        CheckLobbyLabels();
        var waveSizes = new HashSet<int>();
        var joined = new HashSet<BotPlayer>();
        BotPlayer firstArrival = null;
        for (int wave = 0; wave < 8; wave++)
        {
            int before = arena.ParticipantCount;
            Set(flow, "joinTimer", 0f); Call(flow, "Update");
            int added = arena.ParticipantCount - before;
            Check(added >= 1 && added <= 5, "A default arrival wave exceeded its one-to-five bounds");
            waveSizes.Add(added);
            int addedObjects = 0;
            foreach (BotPlayer bot in bots)
            {
                CheckNamesHidden(bot);
                if (bot.gameObject.activeSelf && joined.Add(bot))
                {
                    addedObjects++;
                    if (firstArrival == null) firstArrival = bot;
                    OpponentBoardPresentation arrival = bot.GetComponent<OpponentBoardPresentation>();
                    Check(arrival.IsArriving && Get<GameObject>(arrival, "arrivalRoot").activeInHierarchy,
                        "An admitted opponent did not start its saved spawn animation");
                }
                Check(bot.PiecesPlaced == 0 && (!bot.gameObject.activeSelf || bot.Paused), "An arriving opponent started playing early");
                Check(!bot.GetComponent<OpponentBoardPresentation>().IsTargeted
                    && !Get<GameObject>(bot.GetComponent<OpponentBoardPresentation>(), "cursorRoot").activeSelf,
                    "An opponent displayed a targeting cursor before GO");
            }
            Check(addedObjects == added && joined.Count == arena.ParticipantCount - 1, "A wave reused an occupied slot or lost a participant");
            CheckLobbyLabels();
            CheckCamera();
        }
        Check(waveSizes.Count > 1, "The fixed-seed waves never varied in size");
        bool outsideInitialSlots = false;
        for (int i = joined.Count; i < bots.Length; i++) outsideInitialSlots |= joined.Contains(bots[i]);
        Check(outsideInitialSlots, "The fixed-seed arrivals filled slots sequentially instead of choosing vacancies");
        var arrivalBoard = new BoardPoseSnapshot(firstArrival.GetComponent<Board>());
        OpponentBoardPresentation arrivalView = firstArrival.GetComponent<OpponentBoardPresentation>();
        float arrivalElapsed = Get<float>(arrivalView, "arrivalElapsed");
        arrivalView.BeginArrival();
        Check(Get<float>(arrivalView, "arrivalElapsed") == arrivalElapsed,
            "A repeated admission animation restarted a live spawn");
        // Let normal updates advance the paused lobby's saved visual pool and initialize URP bindings.
        yield return null;
        Check(arrivalView.IsArriving && Get<float>(arrivalView, "arrivalElapsed") > arrivalElapsed,
            "A paused opponent's spawn animation did not advance");
        SpriteRenderer spiral = Get<SpriteRenderer>(arrivalView, "arrivalSpiral");
        Sprite firstSpiralFrame = Get<Sprite[]>(arrivalView, "arrivalFrames")[0];
        Call(arrivalView, "ShowArrivalPose", .45f);
        Check(spiral.sprite != firstSpiralFrame && spiral.color.a > 0f,
            "The spawn animation did not advance through the supplied spiral frames");
        Capture("/tmp/tetris-original-spawn.png");
        arrivalBoard.CheckUnchanged(firstArrival.GetComponent<Board>());
        BotPlayer disabledArrival = null;
        foreach (BotPlayer bot in joined) if (bot != firstArrival) { disabledArrival = bot; break; }
        Check(disabledArrival != null, "Arrival fixture needs a second admitted opponent");
        OpponentBoardPresentation disabledView = disabledArrival.GetComponent<OpponentBoardPresentation>();
        disabledView.enabled = false; disabledView.enabled = true;
        Check(!disabledView.IsArriving && !Get<GameObject>(disabledView, "arrivalRoot").activeSelf
            && !Get<GameObject>(disabledView, "cursorRoot").activeSelf,
            "Re-enabling a presentation replayed its arrival or showed an unselected cursor");
        disabledView.BeginArrival();
        Check(!disabledView.IsArriving, "A completed or disabled arrival replayed without a lobby reset");
        float deadline = Time.realtimeSinceStartup + 3f;
        while (arrivalView.IsArriving && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!arrivalView.IsArriving && !Get<GameObject>(arrivalView, "arrivalRoot").activeSelf,
            "The .6-second spawn animation did not finish during the paused lobby");
        arrivalView.BeginArrival();
        Check(!arrivalView.IsArriving, "A repeated admission replayed a finished spawn animation");
        foreach (BotPlayer bot in joined)
            Check(bot.Paused && bot.PiecesPlaced == 0 && bot.GetComponent<Board>().IsEmpty,
                "A visual arrival changed the paused lobby's board occupancy");
        arrivalBoard.CheckUnchanged(firstArrival.GetComponent<Board>());
        var loadingBoard = new BoardPoseSnapshot(playerBoard);
        int[] loadingBag = Get<Queue<int>>(player, "bag").ToArray();
        UnityEngine.Random.State loadingRandom = UnityEngine.Random.state;
        loadingGrid.ResetAnimation();
        Check(loadingGrid.FrameIndex == 0 && loadingGrid.CurrentPieceIndex == 4
            && loadingGrid.RotationIndex == 2 && loadingGrid.IsPieceVisible,
            "The loading animation did not reset to its initial red Z");
        CheckLoadingColors();
        int initialLoadingMask = LoadingMask();
        Check(initialLoadingMask == 0x0036, "The initial loading shape must match the reference's red Z in the bottom rows");
        CheckLoadingCycle(loadingGrid);
        yield return null;
        loadingGrid.ResetAnimation();
        float oldTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        bool sawLoadingPause = false;
        deadline = Time.realtimeSinceStartup + 3f;
        while (loadingGrid.FrameIndex == 0 && Time.realtimeSinceStartup < deadline)
        {
            if (!loadingGrid.IsPieceVisible)
            {
                sawLoadingPause = true;
                Check(LoadingMask() == 0, "The loading pause retained a colored block");
            }
            yield return null;
        }
        Time.timeScale = oldTimeScale;
        Check(sawLoadingPause && loadingGrid.FrameIndex == 1 && loadingGrid.IsPieceVisible
            && LoadingMask() != initialLoadingMask,
            "Normal unscaled updates did not hold, clear, pause, and advance the loading grid");
        CheckLoadingColors();
        Check(UnityEngine.Random.state.Equals(loadingRandom), "The loading animation consumed gameplay randomness");
        int[] afterLoadingBag = Get<Queue<int>>(player, "bag").ToArray();
        Check(afterLoadingBag.Length == loadingBag.Length, "The loading animation changed the player's seven-bag");
        for (int i = 0; i < loadingBag.Length; i++)
            Check(afterLoadingBag[i] == loadingBag[i], "The loading animation reordered the player's seven-bag");
        loadingBoard.CheckUnchanged(playerBoard);
        Check(!player.HasActivePiece && player.BattlePaused, "The loading animation started gameplay");
        loadingGrid.ResetAnimation();
        Capture("/tmp/tetris-reference-matchmaking.png");
        Capture("/tmp/tetris-original-lobby.png");
        while (arena.AdmitNextBot()) { }
        Check(arena.ParticipantCount == 99 && !arena.AdmitNextBot(), "Admissions did not stop exactly at 99");
        Check(new HashSet<BotPlayer>(arena.Opponents).SetEquals(bots), "The admitted roster contains a duplicate or missing saved board");
        Call(flow, "Update");
        Check(flow.Phase == MatchFlow.MatchPhase.Ready && !lobbyRoot.activeInHierarchy,
            "The replacement matchmaking presentation remained visible over READY");
        deadline = Time.realtimeSinceStartup + 5f;
        bool sawCountdown = false;
        while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline)
        {
            Check(!lobbyRoot.activeInHierarchy, "The matchmaking grid reappeared during the countdown");
            sawCountdown |= flow.Phase == MatchFlow.MatchPhase.Countdown;
            yield return null;
        }
        Check(flow.Phase == MatchFlow.MatchPhase.Playing && arena.MatchStarted, "The full lobby never reached GO");
        Check(sawCountdown && !lobbyRoot.activeInHierarchy,
            "Countdown/GO did not keep the loading grid, status bar, and participant count hidden");
        foreach (BotPlayer bot in bots)
        {
            bot.Paused = true;
            OpponentBoardPresentation view = bot.GetComponent<OpponentBoardPresentation>();
            Check(!view.IsArriving && !Get<GameObject>(view, "arrivalRoot").activeSelf,
                "GO left a recently admitted board covered by its spawn animation");
        }
        player.enabled = false;
        Get<AudioSource>(player, "soundSource").volume = 0f;
        CheckCamera();

        Sprite[] playerSkin = Get<Sprite[]>(player, "cellSprites");
        for (int shape = 0; shape < 7; shape++)
        {
            playerBoard.ClearBoard();
            Call(player, "ActivatePiece", shape); Set(player, "origin", new Vector2Int(3, 0));
            Call(player, "Lock", false);
            CheckSkin(playerBoard, playerSkin[shape], 4);
        }
        playerBoard.ClearBoard();
        for (int i = 0; i < bots.Length; i++)
        {
            Board board = bots[i].GetComponent<Board>(); board.ClearBoard();
            ForceShape(bots[i], i % 7); Call(bots[i], "PlaceNextPiece");
            CheckSkin(board, Get<Sprite[]>(bots[i], "cellSprites")[i % 7], 4);
        }
        GarbageCombat playerCombat = player.GetComponent<GarbageCombat>();
        Sprite gray = Get<Sprite>(playerCombat, "garbageSprite");
        Board grayBoard = bots[4].GetComponent<Board>(); grayBoard.ClearBoard();
        Check(grayBoard.TryAddGarbage(new[] { 3 }, gray, Color.white, out bool overflow) && !overflow, "Could not show original garbage skin");
        CheckSkin(grayBoard, gray, 9);
        yield return null;
        Capture("/tmp/tetris-original-battle.png");
        yield return null;

        float cursorRefresh = Get<float>(arena, "targetRefreshTimer");
        Set(arena, "targetRefreshTimer", -10000f);
        BotPlayer firstTarget = bots[10], secondTarget = bots[11];
        var firstTargetBoard = new BoardPoseSnapshot(firstTarget.GetComponent<Board>());
        var secondTargetBoard = new BoardPoseSnapshot(secondTarget.GetComponent<Board>());
        arena.SetTarget(firstTarget);
        CheckExclusiveTarget(firstTarget);
        OpponentBoardPresentation cursorView = firstTarget.GetComponent<OpponentBoardPresentation>();
        SpriteRenderer cursorRing = Get<SpriteRenderer>(cursorView, "cursorRing");
        Quaternion initialRingRotation = cursorRing.transform.localRotation;
        float cursorElapsed = Get<float>(cursorView, "cursorElapsed");
        deadline = Time.realtimeSinceStartup + 2f;
        while (Get<float>(cursorView, "cursorElapsed") < cursorElapsed + .08f && Time.realtimeSinceStartup < deadline)
            yield return null;
        Check(Get<float>(cursorView, "cursorElapsed") > cursorElapsed
            && Quaternion.Angle(cursorRing.transform.localRotation, initialRingRotation) > .01f,
            "The selected opponent's supplied targeting ring did not animate");
        cursorElapsed = Get<float>(cursorView, "cursorElapsed");
        firstTarget.SetTargeted(true); arena.SetTarget(firstTarget);
        Check(Get<float>(cursorView, "cursorElapsed") == cursorElapsed,
            "Repeated target selection restarted the cursor animation");
        Capture("/tmp/tetris-original-cursor.png");
        cursorView.enabled = false; cursorView.enabled = true;
        Check(!cursorView.IsTargeted && !Get<GameObject>(cursorView, "cursorRoot").activeSelf,
            "Re-enabling a presentation replayed a stale target cursor without selection");
        arena.SetTarget(firstTarget);
        CheckExclusiveTarget(firstTarget);
        arena.SetTarget(secondTarget);
        CheckExclusiveTarget(secondTarget);
        firstTargetBoard.CheckUnchanged(firstTarget.GetComponent<Board>());
        secondTargetBoard.CheckUnchanged(secondTarget.GetComponent<Board>());
        CheckCamera();
        Set(arena, "targetRefreshTimer", cursorRefresh);

        BoardPresentation presentation = player.GetComponent<BoardPresentation>();
        GameObject warning = Get<GameObject>(presentation, "danger");
        SpriteRenderer warningArt = Get<SpriteRenderer>(presentation, "warningFrame");
        float targetRefresh = Get<float>(arena, "targetRefreshTimer");
        Set(arena, "targetRefreshTimer", -10000f);
        Get<AudioSource>(presentation, "targetingSoundSource").volume = 0f;
        presentation.ClearAttackRoutes();
        player.enabled = true;
        presentation.SetAttackers(new[] { bots[6], bots[30], bots[61] });
        yield return null;
        Check(warning.activeInHierarchy && warningArt.enabled && warningArt.color == Color.white,
            "Active attackers did not display the original WATCH OUT artwork at its original tint");
        Check(!warning.transform.Find("Watch out label").gameObject.activeSelf,
            "The supplied WATCH OUT graphic is covered by duplicate game text");
        bool hasTargetingLine = false;
        foreach (SpriteRenderer line in Get<SpriteRenderer[]>(presentation, "targetingLines")) hasTargetingLine |= line.enabled;
        Check(hasTargetingLine, "The textured WATCH OUT warning lost its targeting routes");
        Capture("/tmp/tetris-original-warning.png");
        presentation.SetAttackers(null);
        Check(!warning.activeSelf, "The original WATCH OUT warning remained after the attackers cleared");
        foreach (SpriteRenderer line in Get<SpriteRenderer[]>(presentation, "targetingLines"))
            Check(!line.enabled, "A targeting line remained after the attackers cleared");
        player.enabled = false;
        Set(arena, "targetRefreshTimer", targetRefresh);
        CheckCamera();

        BotPlayer playerVictim = bots[0], botVictim = bots[1], selfVictim = bots[2], winner = bots[3];
        arena.SetTarget(playerVictim);
        CheckExclusiveTarget(playerVictim);
        knockoutSource.Stop();
        KnockOut(playerVictim, playerCombat);
        OpponentBoardPresentation victimCursor = playerVictim.GetComponent<OpponentBoardPresentation>();
        Check(!victimCursor.IsTargeted && !victimCursor.IsArriving
            && !Get<GameObject>(victimCursor, "cursorRoot").activeSelf
            && !Get<GameObject>(victimCursor, "arrivalRoot").activeSelf && arena.Target != playerVictim,
            "A real selected-opponent knockout retained its cursor or spawn effect");
        CheckExclusiveTarget(arena.Target);
        MatchStanding playerKill = Standing(playerVictim);
        OpponentEliminationPresentation pop = playerVictim.GetComponent<OpponentEliminationPresentation>();
        Check(playerKill.KilledByPlayer && playerKill.Rank == 99 && playerCombat.BadgeBits == 1
            && pop.DisplayedRank == 99 && pop.IsAnimating && knockoutSource.isPlaying,
            "Player garbage did not produce a credited original K.O. pop and voice");
        Check(!Get<TMP_Text>(pop, "knockoutText").gameObject.activeInHierarchy
            && Get<SpriteRenderer>(pop, "knockoutSprite").gameObject.activeInHierarchy
            && Get<SpriteRenderer>(pop, "knockoutSprite").enabled
            && !Get<SpriteRenderer>(pop, "eliminatedMedal").gameObject.activeSelf,
            "A credited knockout must show the original gold K.O. graphic before its persistent medal");
        CheckEffectPool(pop, true);
        CheckHiddenFatalBoard(playerVictim);
        var fatalData = new BoardSnapshot(playerVictim.GetComponent<Board>());
        knockoutSource.Stop();
        float elapsedBeforeDuplicate = Get<float>(pop, "elapsed");
        Call(arena, "BotEliminated", playerVictim); Call(flow, "ParticipantEliminated", playerKill);
        playerVictim.ShowElimination(99, true); playerVictim.ShowElimination(99, false);
        Check(!knockoutSource.isPlaying && pop.IsAnimating && Get<float>(pop, "elapsed") == elapsedBeforeDuplicate,
            "A duplicate knockout replayed its voice, restarted its animation, or replaced the current pop");
        KnockOut(botVictim, winner.GetComponent<GarbageCombat>());
        KnockOut(selfVictim, selfVictim.GetComponent<GarbageCombat>());
        foreach (BotPlayer victim in new[] { botVictim, selfVictim })
        {
            OpponentEliminationPresentation view = victim.GetComponent<OpponentEliminationPresentation>();
            Check(!Standing(victim).KilledByPlayer && !view.IsAnimating && view.DisplayedRank == Standing(victim).Rank
                && Get<TMP_Text>(view, "rankText").gameObject.activeInHierarchy
                && !Get<GameObject>(view, "knockoutRoot").activeSelf && !knockoutSource.isPlaying,
                "A non-player knockout showed the player's K.O. pop/voice instead of its silent medal and rank");
            CheckMedal(view, false);
            CheckEffectPool(view, false);
            victim.ShowElimination(Standing(victim).Rank, true);
            victim.gameObject.SetActive(false); victim.gameObject.SetActive(true);
            CheckMedal(view, false);
            Check(!view.IsAnimating && !knockoutSource.isPlaying,
                "A duplicate or re-enabled non-player knockout started player-credit effects");
            CheckHiddenFatalBoard(victim);
        }
        // Give newly visible effect renderers one normal frame to reach the render pipeline.
        yield return null;
        Check(pop.IsAnimating, "The credited knockout finished before its impact could be rendered");
        // Sample the real animation's impact pose for a stable screenshot, independent of capture latency.
        Call(pop, "AnimatePose", 0f);
        Vector3 initialStarPosition = Get<SpriteRenderer[]>(pop, "knockoutEffects")[2].transform.localPosition;
        Call(pop, "AnimatePose", .18f);
        Check((Get<SpriteRenderer[]>(pop, "knockoutEffects")[2].transform.localPosition - initialStarPosition).sqrMagnitude > .0000001f,
            "The credited knockout's original star artwork did not animate on the victim's board");
        foreach (SpriteRenderer letter in Get<SpriteRenderer[]>(pop, "knockoutLetters"))
            Check(letter.color.a > 0f, "A supplied BOOM letter is invisible at the impact pose");
        Capture("/tmp/tetris-original-knockout.png");
        deadline = Time.realtimeSinceStartup + 3f;
        while (pop.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!pop.IsAnimating && Get<TMP_Text>(pop, "rankText").gameObject.activeInHierarchy
            && !Get<GameObject>(pop, "knockoutRoot").activeSelf, "The .85-second K.O. pop did not settle to its persistent medal and rank");
        CheckMedal(pop, true);
        CheckEffectPool(pop, false);
        fatalData.CheckUnchanged(playerVictim.GetComponent<Board>());
        playerVictim.gameObject.SetActive(false); playerVictim.gameObject.SetActive(true);
        Call(flow, "ParticipantEliminated", playerKill);
        Check(pop.DisplayedRank == 99 && !pop.IsAnimating && !knockoutSource.isPlaying && playerVictim.IsGameOver,
            "Re-enabling an eliminated opponent replayed or cleared its K.O. state");
        CheckMedal(pop, true);
        playerVictim.ShowElimination(1, false);
        CheckMedal(pop, true);
        Check(pop.DisplayedRank == 99, "A duplicate elimination changed the persistent rank");
        Check(!arena.AdmitNextBot(), "A dead board was readmitted during the match");
        fatalData.CheckUnchanged(playerVictim.GetComponent<Board>());
        yield return null;
        CheckMedal(pop, true);
        CheckMedal(botVictim.GetComponent<OpponentEliminationPresentation>(), false);
        CheckMedal(selfVictim.GetComponent<OpponentEliminationPresentation>(), false);
        Capture("/tmp/tetris-original-medals.png");

        DeathScreen death = player.GetComponent<DeathScreen>();
        Set(death, "rowInterval", .01f); Set(death, "rowFlashDuration", .05f); Set(death, "deathSound", null);
        Call(player, "EndGame");
        CheckExclusiveTarget(null);
        yield return null;
        flow.BeginMatchmaking();
        Check(arena.ParticipantCount == 99 && player.IsGameOver && !arena.IsRoundOver,
            "Retry abandoned living opponents while spectating");
        foreach (BotPlayer bot in bots) if (bot != winner && !bot.IsGameOver) Call(bot, "Die");
        deadline = Time.realtimeSinceStartup + 5f;
        while ((!death.CanRetry || flow.Phase != MatchFlow.MatchPhase.Finished ||
            !Get<GameObject>(flow, "resultsRoot").activeInHierarchy) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(arena.IsRoundOver && !arena.PlayerWon && Standing(winner).Rank == 1 && death.CanRetry
            && flow.Phase == MatchFlow.MatchPhase.Finished, "Final results never completed");
        var ranks = new HashSet<int>();
        foreach (MatchStanding standing in arena.Standings)
            Check(standing.Rank >= 1 && standing.Rank <= 99 && ranks.Add(standing.Rank), "Final ranks are missing or duplicated");
        Check(ranks.Count == 99 && flow.StandingsPageCount == 9, "Not all 99 standings can be paged");
        Capture("/tmp/tetris-original-results.png");
        for (int page = 0; page < 9; page++) { Check(flow.StandingsPage == page, "A standings page was skipped"); flow.NextStandingsPage(); }
        Check(flow.StandingsPage == 0, "The final standings page does not wrap");
        flow.BeginMatchmaking();
        Check(flow.Phase == MatchFlow.MatchPhase.Matchmaking && arena.ParticipantCount == 1 && !knockoutSource.isPlaying,
            "Restart did not return to a clean lobby and stop K.O. audio");
        CheckLobbyLabels();
        Check(loadingGrid.FrameIndex == 0 && loadingGrid.CurrentPieceIndex == 4
            && loadingGrid.RotationIndex == 2 && loadingGrid.IsPieceVisible && LoadingMask() == initialLoadingMask
            && new HashSet<SpriteRenderer>(Get<SpriteRenderer[]>(loadingGrid, "cells")).SetEquals(savedMatchmakingCells)
            && lobbyRoot.GetComponentsInChildren<SpriteRenderer>(true).Length == savedMatchmakingCells.Length + 1,
            "Restart did not reset and reuse the saved loading cells");
        CheckLoadingColors();
        foreach (BotPlayer bot in bots)
        {
            OpponentEliminationPresentation view = bot.GetComponent<OpponentEliminationPresentation>();
            Check(!bot.gameObject.activeSelf && !bot.IsGameOver && bot.GetComponent<Board>().IsEmpty
                && view.DisplayedRank == 0 && !view.IsAnimating && Get<GameObject>(view, "lockedArtwork").activeSelf
                && !Get<GameObject>(view, "knockoutRoot").activeSelf && !Get<TMP_Text>(view, "rankText").gameObject.activeSelf
                && !Get<SpriteRenderer>(view, "eliminatedMedal").gameObject.activeSelf,
                "Restart retained an eliminated board's art/medal/rank state");
            CheckEffectPool(view, false);
            CheckNamesHidden(bot);
            OpponentBoardPresentation boardView = bot.GetComponent<OpponentBoardPresentation>();
            Check(!boardView.IsArriving && !boardView.IsTargeted
                && !Get<GameObject>(boardView, "arrivalRoot").activeSelf
                && !Get<GameObject>(boardView, "cursorRoot").activeSelf,
                "Restart retained an opponent's spawn effect or targeting cursor");
        }
        Check(new HashSet<BotPlayer>(arena.GetComponentsInChildren<BotPlayer>(true)).SetEquals(bots)
            && new HashSet<TMP_Text>(player.GetComponentsInChildren<TMP_Text>(true)).SetEquals(savedLabels)
            && new HashSet<SpriteRenderer>(EliminationRenderers()).SetEquals(savedEliminationRenderers)
            && new HashSet<SpriteRenderer>(BoardPresentationRenderers()).SetEquals(savedBoardPresentationRenderers)
            && lobbyRoot.GetComponentsInChildren<MatchmakingGridPresentation>(true).Length == 1,
            "Gameplay generated or replaced saved opponent boards, labels, or presentation renderer pools");
        Check(arena.AdmitNextBot(), "A reset lobby could not admit its first opponent");
        foreach (BotPlayer bot in bots) if (bot.gameObject.activeSelf)
        {
            Check(bot.GetComponent<OpponentBoardPresentation>().IsArriving && bot.Paused && bot.GetComponent<Board>().IsEmpty,
                "A reset board did not play a fresh spawn while preserving paused gameplay");
            OpponentBoardPresentation view = bot.GetComponent<OpponentBoardPresentation>();
            bot.enabled = false;
            Check(!view.IsArriving && !Get<GameObject>(view, "arrivalRoot").activeSelf,
                "Disabling only the bot left its sibling spawn presentation active");
            bot.enabled = true;
            Check(!view.IsArriving && !view.IsTargeted,
                "Re-enabling only the bot replayed an arrival or stale cursor");
        }
        arena.BeginLobby();
        Check(arena.AdmitNextBot(), "A second clean lobby could not admit an opponent");
        arena.enabled = false;
        foreach (BotPlayer bot in bots)
        {
            OpponentBoardPresentation view = bot.GetComponent<OpponentBoardPresentation>();
            Check(!view.IsArriving && !view.IsTargeted && !Get<GameObject>(view, "arrivalRoot").activeSelf
                && !Get<GameObject>(view, "cursorRoot").activeSelf,
                "Disabling only the arena left an opponent's spawn or cursor active");
        }
        CheckCamera();
        Debug.Log("ORIGINAL_SPRITES_CHECKS_PASSED");
        EditorApplication.Exit(0);
    }

    private void CheckSavedArt()
    {
        BoardPresentation presentation = player.GetComponent<BoardPresentation>();
        Sprite frame = Get<SpriteRenderer>(presentation, "boardSprite").sprite;
        Check(AssetDatabase.GetAssetPath(frame) == Theme + "game_field00^q.png", "Main frame still uses replacement art");
        SpriteRenderer warning = Get<SpriteRenderer>(presentation, "warningFrame");
        Check(warning != null && AssetDatabase.GetAssetPath(warning.sprite) == Theme + "enUS/caution00.png"
            && warning.sprite.rect == new Rect(29, 5, 398, 52) && warning.color == Color.white
            && Mathf.Approximately(warning.transform.localScale.x, warning.transform.localScale.y)
            && Mathf.Approximately(warning.sprite.bounds.size.x * warning.transform.localScale.x,
                8.4f * player.GetComponent<Board>().CellSize),
            "WATCH OUT must use the supplied original graphic with intact color and aspect ratio");
        Transform warningLabel = Get<GameObject>(presentation, "danger").transform.Find("Watch out label");
        Check(warningLabel != null && !warningLabel.gameObject.activeSelf,
            "The saved warning retained a duplicate visible WATCH OUT label");
        CheckCellAssets(Get<Sprite[]>(player, "cellSprites"));
        Sprite gray = Get<Sprite>(player.GetComponent<GarbageCombat>(), "garbageSprite");
        Check(AssetDatabase.GetAssetPath(gray) == Theme + "minos00.png" && gray.rect == new Rect(4, 124, 48, 48),
            "Garbage does not use the original gray cell");
        Check(Get<Color>(player.GetComponent<GarbageCombat>(), "garbageColor") == Color.white, "Gray artwork is double tinted");
        foreach (BotPlayer bot in bots)
        {
            CheckCellAssets(Get<Sprite[]>(bot, "cellSprites"));
            CheckOriginal(Get<Sprite>(bot, "normalFrameSprite")); CheckOriginal(Get<Sprite>(bot, "selectedFrameSprite"));
            OpponentEliminationPresentation view = bot.GetComponent<OpponentEliminationPresentation>();
            Check(view != null && Mathf.Approximately(Get<float>(view, "knockoutDuration"), .85f), "Saved opponent presentation is missing");
            Check(AssetDatabase.GetAssetPath(Get<SpriteRenderer>(view, "knockoutSprite").sprite)
                == Theme + "enUS/game_enemy.08^q.png", "Player K.O. does not use the original gold sprite");
            Check(Get<SpriteRenderer>(view, "knockoutSprite").enabled
                && Get<SpriteRenderer>(view, "knockoutSprite").gameObject.activeSelf
                && !Get<TMP_Text>(view, "knockoutText").gameObject.activeSelf,
                "Saved knockout presentation must prefer the supplied raster K.O. graphic");
            Check(AssetDatabase.GetAssetPath(Get<Sprite>(view, "playerKnockoutMedal")) == Theme + "enUS/game_enemy.08^q.png"
                && AssetDatabase.GetAssetPath(Get<Sprite>(view, "otherKnockoutMedal")) == Theme + "enUS/game_enemy.07^q.png"
                && Get<SpriteRenderer>(view, "eliminatedMedal") != null
                && !Get<SpriteRenderer>(view, "eliminatedMedal").gameObject.activeSelf,
                "Saved eliminated-board medals need separate gold player-credit and cyan other-credit originals");
            SpriteRenderer[] effects = Get<SpriteRenderer[]>(view, "knockoutEffects");
            SpriteRenderer[] letters = Get<SpriteRenderer[]>(view, "knockoutLetters");
            Check(effects.Length == 6 && letters.Length == 4, "An opponent is missing its saved burst, spark, stars, or BOOM letters");
            string[] effectPaths = { "boom00base.png", "boom01spark0.png", "boom03star0.png",
                "boom03star0.png", "boom03star0.png", "boom03star0.png" };
            for (int i = 0; i < effects.Length; i++)
                Check(effects[i] != null && AssetDatabase.GetAssetPath(effects[i].sprite) == Theme + "_Textures/" + effectPaths[i],
                    "A saved knockout effect does not use its supplied original texture");
            for (int i = 0; i < letters.Length; i++)
                Check(letters[i] != null && AssetDatabase.GetAssetPath(letters[i].sprite) == Theme + "_Textures/boom04logo" + i + ".png",
                    "A saved BOOM letter does not use its supplied original texture");
            var pool = new HashSet<SpriteRenderer>(effects);
            foreach (SpriteRenderer letter in letters) pool.Add(letter);
            pool.Add(Get<SpriteRenderer>(view, "knockoutSprite")); pool.Add(Get<SpriteRenderer>(view, "eliminatedMedal"));
            Check(pool.Count == 12 && !pool.Contains(null), "An opponent's elimination renderer pool has reused or missing objects");
            foreach (SpriteRenderer renderer in pool)
                Check(renderer.transform.IsChildOf(bot.transform), "Elimination artwork is not saved on its own opponent board");
            Check(Get<TMP_Text>(view, "rankText").font == AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/font/ajn-aokane.otf/ajn-aokane SDF.asset"), "Dynamic opponent rank lost the AJN font");
            Check(Get<Sprite>(bot.GetComponent<GarbageCombat>(), "garbageSprite") == gray, "Opponent garbage uses a different skin");
            CheckSavedBoardPresentation(bot);
        }
        Check(Get<Sprite>(flow, "readyArtwork") == null && Get<Sprite>(flow, "goArtwork") == null
            && Get<Sprite[]>(flow, "countdownArtwork").Length == 0,
            "Raster countdown lettering replaced the user's game font");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/font/ajn-aokane.otf/ajn-aokane SDF.asset");
        foreach (TMP_Text label in savedLabels)
            Check(label.font == font, "A game label lost the AJN font: " + label.name);
        Check(Get<TMP_Text>(flow, "countdownText").enabled, "AJN countdown text is hidden");
        foreach (SpriteRenderer medal in Get<SpriteRenderer[]>(flow, "medalIcons")) CheckOriginal(medal.sprite);
        CheckSavedLobby();
    }

    private void CheckSavedLobby()
    {
        GameObject lobby = Get<GameObject>(flow, "lobbyRoot");
        MatchmakingGridPresentation grid = lobby.GetComponent<MatchmakingGridPresentation>();
        Check(grid != null && Mathf.Approximately(Get<float>(grid, "holdDuration"), .3f)
            && Mathf.Approximately(Get<float>(grid, "blankDuration"), .2f)
            && savedMatchmakingCells.Length == 16 && new HashSet<SpriteRenderer>(savedMatchmakingCells).Count == 16,
            "Matchmaking needs one saved 4-by-4 grid with a 0.3-second piece and 0.2-second blank phase");
        float boardCell = player.GetComponent<Board>().CellSize;
        Check(lobby.transform.localPosition == new Vector3(0f, -6.2f * boardCell, 0f),
            "The complete matchmaking indicator must sit at the bottom of the player's board");
        Color[] palette = Get<Color[]>(grid, "pieceColors");
        Color[] expectedPalette =
        {
            new Color(.1f, .85f, .95f), new Color(1f, .85f, .1f), new Color(.75f, .2f, .95f),
            new Color(.35f, .95f, .15f), new Color(1f, .15f, .3f), new Color(.15f, .35f, 1f),
            new Color(1f, .5f, .1f)
        };
        Check(palette != null && palette.Length == expectedPalette.Length,
            "The loading indicator needs all seven piece colors");
        for (int i = 0; i < expectedPalette.Length; i++)
            Check(palette[i] == expectedPalette[i], "A loading piece lost its canonical tetromino color");
        var columns = new HashSet<float>(); var rows = new HashSet<float>();
        SpriteRenderer bar = null;
        foreach (SpriteRenderer candidate in lobby.GetComponentsInChildren<SpriteRenderer>(true))
            if (candidate.sprite != null && AssetDatabase.GetAssetPath(candidate.sprite)
                == OriginalRoot + "Common Elements/_texture/game_battle99_status00^q.png") bar = candidate;
        Check(bar != null && bar.sprite.rect == new Rect(5, 4, 437, 64) && bar.color == Color.white
            && Mathf.Approximately(bar.transform.localScale.x, bar.transform.localScale.y),
            "The matchmaking status bar lost the supplied rounded red artwork or its aspect ratio");
        foreach (SpriteRenderer cell in savedMatchmakingCells)
        {
            Check(cell != null && cell.enabled && cell.gameObject.activeSelf && cell.transform.IsChildOf(lobby.transform)
                && AssetDatabase.GetAssetPath(cell.sprite) == Theme + "_Textures/effect00.png"
                && cell.transform.position.y > bar.transform.position.y,
                "A loading cell is missing, uses replacement art, or is not above the red status bar");
            columns.Add(cell.transform.localPosition.x); rows.Add(cell.transform.localPosition.y);
        }
        Check(columns.Count == 4 && rows.Count == 4, "Loading cells do not form a 4-by-4 grid");
        var sortedColumns = new List<float>(columns); sortedColumns.Sort();
        var sortedRows = new List<float>(rows); sortedRows.Sort();
        for (int i = 0; i < savedMatchmakingCells.Length; i++)
            Check(savedMatchmakingCells[i].transform.localPosition.x == sortedColumns[i % 4]
                && savedMatchmakingCells[i].transform.localPosition.y == sortedRows[i / 4],
                "Loading cell references must follow their physical grid coordinates, bottom row first");
        Check(bar.transform.position.y > Get<TMP_Text>(flow, "participantsText").transform.position.y
            && Get<TMP_Text>(flow, "timerText") == null,
            "The participant count must sit below the search bar without the former visible timer");
        foreach (string obsolete in new[] { "Panel", "Top accent", "Title", "Players heading", "Divider", "Timer", "Timer heading", "Lobby hint" })
            Check(lobby.transform.Find(obsolete) == null, "The old matchmaking layout retained " + obsolete);
        Check(Get<TMP_Text>(flow, "matchmakingText").text == "SEARCHING FOR PLAYERS...",
            "Matchmaking status must use the English wording");
        CheckLoadingColors();
    }

    private void CheckLobbyLabels()
    {
        Check(Get<GameObject>(flow, "lobbyRoot").activeInHierarchy
            && Get<TMP_Text>(flow, "matchmakingText").text == "SEARCHING FOR PLAYERS..."
            && Get<TMP_Text>(flow, "participantsText").text == arena.ParticipantCount.ToString("00"),
            "The English search status or live two-digit participant count does not match the lobby");
    }

    private void CheckLoadingColors()
    {
        MatchmakingGridPresentation grid = Get<GameObject>(flow, "lobbyRoot").GetComponent<MatchmakingGridPresentation>();
        Color gray = Get<Color>(grid, "idleColor");
        Color[] palette = Get<Color[]>(grid, "pieceColors");
        Check(grid.CurrentPieceIndex >= 0 && grid.CurrentPieceIndex < palette.Length,
            "The loading indicator selected an unknown piece");
        Color pieceColor = palette[grid.CurrentPieceIndex];
        int active = 0;
        foreach (SpriteRenderer cell in savedMatchmakingCells)
        {
            Check(cell != null && (cell.color == pieceColor || cell.color == gray),
                "A loading cell does not match the current piece or the idle grid color");
            if (cell.color != gray) active++;
        }
        Check(active == (grid.IsPieceVisible ? 4 : 0),
            "The loading grid must show four correctly colored cells during a piece and sixteen gray cells during its pause");
    }

    private int LoadingMask()
    {
        Color gray = Get<Color>(Get<GameObject>(flow, "lobbyRoot").GetComponent<MatchmakingGridPresentation>(), "idleColor");
        int mask = 0;
        for (int i = 0; i < savedMatchmakingCells.Length; i++) if (savedMatchmakingCells[i].color != gray) mask |= 1 << i;
        return mask;
    }

    // Transcribed from the supplied loading sheet. Each string has four rows, top to bottom.
    // Columns are the sheet's four rotation states; the O footprint stays fixed in every state.
    private static readonly string[,] ReferenceLoadingPoses =
    {
        { "..../####/..../....", "..#./..#./..#./..#.", "..../..../####/....", ".#../.#../.#../.#.." }, // I
        { "..../.##./.##./....", "..../.##./.##./....", "..../.##./.##./....", "..../.##./.##./...." }, // O
        { "..../.#../###./....", "..../.#../.##./.#..", "..../..../###./.#..", "..../.#../##../.#.." }, // T
        { "..../.##./##../....", "..../.#../.##./..#.", "..../..../.##./##..", "..../#.../##../.#.." }, // S
        { "..../##../.##./....", "..../..#./.##./.#..", "..../..../##../.##.", "..../.#../##../#..." }, // Z
        { "..../#.../###./....", "..../.##./.#../.#..", "..../..../###./..#.", "..../.#../.#../##.." }, // J
        { "..../..#./###./....", "..../.#../.#../.##.", "..../..../###./#...", "..../##../.#../.#.." }  // L
    };

    private static int MaskFromTopRows(string pose)
    {
        string[] rows = pose.Split('/');
        int mask = 0;
        for (int row = 0; row < 4; row++)
            for (int x = 0; x < 4; x++)
                if (rows[row][x] == '#') mask |= 1 << ((3 - row) * 4 + x);
        return mask;
    }

    private void CheckLoadingCycle(MatchmakingGridPresentation grid)
    {
        // Probe just past phase boundaries to avoid accumulated floating-point rounding.
        float hold = Get<float>(grid, "holdDuration");
        float blank = Get<float>(grid, "blankDuration");
        float stateDuration = hold + blank;
        grid.enabled = false;
        try
        {
            grid.ResetAnimation();
            grid.Advance(.001f);
            var states = new HashSet<int>();
            var colors = new HashSet<Color>();
            var masks = new HashSet<int>[7];
            int[] expectedPieceOrder = { 4, 3, 0, 5, 6, 2, 1 };
            for (int piece = 0; piece < masks.Length; piece++) masks[piece] = new HashSet<int>();
            int occupiedCoordinates = 0;
            for (int frame = 0; frame < 28; frame++)
            {
                int expectedPiece = expectedPieceOrder[frame % 7];
                int expectedRotation = (frame / 7 + 2) % 4;
                Check(grid.FrameIndex == frame && grid.CurrentPieceIndex == expectedPiece
                    && grid.RotationIndex == expectedRotation && grid.IsPieceVisible,
                    "The loading cycle skipped or duplicated a visible state");
                Check(states.Add(grid.CurrentPieceIndex * 4 + grid.RotationIndex),
                    "The loading cycle repeated a piece rotation before showing all 28 states");
                CheckLoadingColors();
                int piece = grid.CurrentPieceIndex, rotation = grid.RotationIndex, mask = LoadingMask();
                Check(mask == MaskFromTopRows(ReferenceLoadingPoses[expectedPiece, expectedRotation]),
                    "Loading frame " + frame + " does not match the supplied sheet's exact piece position and rotation");
                masks[piece].Add(mask); occupiedCoordinates |= mask;
                colors.Add(Get<Color[]>(grid, "pieceColors")[piece]);
                grid.Advance(hold * .75f);
                Check(grid.FrameIndex == frame && grid.CurrentPieceIndex == piece && grid.RotationIndex == rotation
                    && grid.IsPieceVisible && LoadingMask() == mask, "A loading piece changed before its hold ended");
                grid.Advance(hold * .25f);
                Check(grid.FrameIndex == frame && !grid.IsPieceVisible && LoadingMask() == 0,
                    "A loading piece did not clear when its hold ended");
                CheckLoadingColors();
                grid.Advance(blank * .75f);
                Check(grid.FrameIndex == frame && !grid.IsPieceVisible && LoadingMask() == 0,
                    "The next loading piece appeared before the blank pause ended");
                grid.Advance(blank * .25f);
                Check(grid.FrameIndex == (frame + 1) % 28 && grid.IsPieceVisible,
                    "The next loading piece did not appear when the blank pause ended");
            }
            Check(states.Count == 28 && colors.Count == 7
                && occupiedCoordinates == MaskFromTopRows(".##./####/####/###."),
                "The loading cycle missed a color or rotation, or used grid positions outside the supplied sheet");
            for (int piece = 0; piece < masks.Length; piece++)
                Check(masks[piece].Count == (piece == 1 ? 1 : 4),
                    "A tetromino did not use every grid rotation, or the square moved between rotations");
            Check(grid.FrameIndex == 0 && grid.CurrentPieceIndex == 4 && grid.RotationIndex == 2
                && LoadingMask() == 0x0036, "The complete loading cycle did not return to its initial red Z");

            // Three full cycles, one complete state, then one quarter into its blank phase.
            grid.Advance(stateDuration * 28 * 3 + stateDuration + hold + blank * .25f);
            Check(grid.FrameIndex == 1 && !grid.IsPieceVisible && LoadingMask() == 0,
                "A large loading update lost complete cycles or its blank-phase remainder");
            grid.Advance(blank * .5f);
            Check(grid.FrameIndex == 1 && !grid.IsPieceVisible, "A resumed blank phase ended too early");
            grid.Advance(blank * .25f);
            Check(grid.FrameIndex == 2 && grid.CurrentPieceIndex == 0 && grid.IsPieceVisible,
                "A resumed loading cycle did not preserve its next-piece boundary");
            CheckLoadingColors();
            Capture("/tmp/tetris-loading-colors.png");
        }
        finally
        {
            grid.enabled = true;
            grid.ResetAnimation();
        }
    }

    private static void CheckMedal(OpponentEliminationPresentation view, bool playerCredit)
    {
        SpriteRenderer medal = Get<SpriteRenderer>(view, "eliminatedMedal");
        Sprite expected = Get<Sprite>(view, playerCredit ? "playerKnockoutMedal" : "otherKnockoutMedal");
        Check(medal.gameObject.activeInHierarchy && medal.enabled && medal.sprite == expected && medal.color == Color.white
            && Get<TMP_Text>(view, "rankText").gameObject.activeInHierarchy
            && !Get<GameObject>(view, "knockoutRoot").activeSelf && !view.IsAnimating,
            "An eliminated board lost its persistent " + (playerCredit ? "gold player-credit" : "cyan other-credit") + " medal and rank");
    }

    private static void CheckEffectPool(OpponentEliminationPresentation view, bool visible)
    {
        foreach (string field in new[] { "knockoutEffects", "knockoutLetters" })
            foreach (SpriteRenderer renderer in Get<SpriteRenderer[]>(view, field))
                Check(renderer != null && (renderer.gameObject.activeInHierarchy && renderer.enabled) == visible,
                    "A saved knockout effect is visible outside a credited pop, or missing during one");
    }

    private SpriteRenderer[] EliminationRenderers()
    {
        var renderers = new List<SpriteRenderer>();
        foreach (BotPlayer bot in bots)
        {
            Transform root = bot.transform.Find("Elimination presentation");
            Check(root != null, "An opponent lost its saved elimination presentation");
            renderers.AddRange(root.GetComponentsInChildren<SpriteRenderer>(true));
        }
        return renderers.ToArray();
    }

    private static readonly string[] BoardPresentationSpriteFields =
        { "arrivalSpiral", "arrivalFlash", "cursorCore", "cursorRing", "cursorFlare" };

    private static void CheckSavedBoardPresentation(BotPlayer bot)
    {
        OpponentBoardPresentation view = bot.GetComponent<OpponentBoardPresentation>();
        Check(view != null && Get<OpponentBoardPresentation>(bot, "boardPresentation") == view
            && Mathf.Approximately(Get<float>(view, "arrivalDuration"), .6f),
            "An opponent lost its saved spawn/cursor presentation or bot binding");
        Sprite[] frames = Get<Sprite[]>(view, "arrivalFrames");
        Rect[] expectedFrames = { new Rect(0, 128, 128, 128), new Rect(128, 128, 128, 128),
            new Rect(256, 128, 128, 128), new Rect(384, 128, 128, 128), new Rect(0, 0, 128, 128) };
        Check(frames != null && frames.Length == expectedFrames.Length && new HashSet<Sprite>(frames).Count == frames.Length,
            "The original spawn spiral needs five distinct atlas frames");
        for (int i = 0; i < frames.Length; i++)
            Check(frames[i] != null && AssetDatabase.GetAssetPath(frames[i]) == Theme + "_Textures/eff_spiral0.png"
                && frames[i].rect == expectedFrames[i], "An original spawn spiral frame was cropped or reordered");
        Check(AssetDatabase.GetAssetPath(Get<SpriteRenderer>(view, "arrivalFlash").sprite)
            == OriginalRoot + "Common Elements/_texture/game_enemy.05.png",
            "The spawn flash does not use the supplied green opponent artwork");
        string[] cursorFields = { "cursorCore", "cursorRing", "cursorFlare" };
        string[] cursorPaths = { "target00a.png", "target00b.png", "target00c.png" };
        for (int i = 0; i < cursorFields.Length; i++)
        {
            Sprite sprite = Get<SpriteRenderer>(view, cursorFields[i]).sprite;
            Check(sprite != null && AssetDatabase.GetAssetPath(sprite) == Theme + "_Textures/" + cursorPaths[i]
                && sprite.rect == new Rect(0, 0, sprite.texture.width, sprite.texture.height),
                "The targeting cursor lost an original complete texture layer");
        }
        Check(!Get<GameObject>(view, "arrivalRoot").activeSelf && !Get<GameObject>(view, "cursorRoot").activeSelf,
            "A saved unjoined opponent has a visible spawn effect or target cursor");
        var pool = new HashSet<SpriteRenderer>();
        foreach (string field in BoardPresentationSpriteFields)
        {
            SpriteRenderer renderer = Get<SpriteRenderer>(view, field);
            Check(renderer != null && pool.Add(renderer) && renderer.enabled && renderer.gameObject.activeSelf
                && renderer.transform.IsChildOf(bot.transform),
                "A spawn/cursor sprite is missing, shared, disabled, or saved outside its opponent");
        }
    }

    private SpriteRenderer[] BoardPresentationRenderers()
    {
        var pool = new HashSet<SpriteRenderer>();
        foreach (BotPlayer bot in bots)
        {
            OpponentBoardPresentation view = bot.GetComponent<OpponentBoardPresentation>();
            Check(view != null, "An opponent lost its saved spawn/cursor presentation");
            foreach (string field in BoardPresentationSpriteFields)
            {
                SpriteRenderer renderer = Get<SpriteRenderer>(view, field);
                Check(renderer != null && pool.Add(renderer), "Opponents share or are missing a spawn/cursor renderer");
            }
        }
        var result = new SpriteRenderer[pool.Count]; pool.CopyTo(result); return result;
    }

    private void CheckExclusiveTarget(BotPlayer selected)
    {
        Check(arena.Target == selected, "The arena and requested target disagree");
        foreach (BotPlayer bot in bots)
        {
            OpponentBoardPresentation view = bot.GetComponent<OpponentBoardPresentation>();
            bool expected = bot == selected;
            Check(view.IsTargeted == expected && Get<GameObject>(view, "cursorRoot").activeInHierarchy == expected,
                "Target selection left a missing or extra cursor on an opponent");
            Check(Get<SpriteRenderer>(bot, "targetFrame").sprite == Get<Sprite>(bot, expected ? "selectedFrameSprite" : "normalFrameSprite"),
                "The target cursor and original miniature frame disagree");
        }
    }

    private static void CheckCellAssets(Sprite[] skin)
    {
        int[] indices = { 6, 7, 4, 2, 5, 1, 3 };
        Check(skin != null && skin.Length == 7, "An original seven-piece cell skin is missing");
        for (int i = 0; i < skin.Length; i++)
            Check(skin[i] != null && AssetDatabase.GetAssetPath(skin[i]) == Theme + "minos00.png"
                && skin[i].rect == new Rect(4 + 56 * indices[i], 124, 48, 48), "Original piece color/atlas rect is wrong at shape " + i);
    }

    private static void CheckOriginal(Sprite sprite) => Check(sprite != null && AssetDatabase.GetAssetPath(sprite).StartsWith(OriginalRoot),
        "A saved presentation sprite does not come from the supplied original packs");
    private static void CheckNamesHidden(BotPlayer bot)
    {
        TMP_Text name = Get<TMP_Text>(bot, "nameLabel"), status = Get<TMP_Text>(bot, "statusLabel");
        Check(name != null && !name.gameObject.activeSelf && !string.IsNullOrEmpty(bot.DisplayName)
            && status != null && !status.gameObject.activeSelf, "A mini name/status is visible or its leaderboard name was lost");
    }
    private static void ForceShape(BotPlayer bot, int shape)
    {
        Queue<int> bag = Get<Queue<int>>(bot, "bag"); bag.Clear(); bag.Enqueue(shape);
    }
    private void KnockOut(BotPlayer victim, GarbageCombat attacker)
    {
        Board board = victim.GetComponent<Board>(); GarbageCombat combat = victim.GetComponent<GarbageCombat>();
        board.ClearBoard(); combat.ResetCombat(); Set(combat, "incomingDelay", 0f);
        Sprite tile = Get<Sprite[]>(victim, "cellSprites")[2];
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, 19), Color.white, new[] { tile })
            && board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(2, 8), Color.white, new[] { tile }, 3), "Could not seed fatal board");
        combat.ReceiveGarbage(1, attacker, 5); ForceShape(victim, 1); Call(victim, "PlaceNextPiece");
        Check(victim.IsGameOver && combat.LastAttacker == attacker && combat.LastRiseCount == 1,
            "The real lock did not apply fatal garbage with its sender intact");
    }
    private static void CheckHiddenFatalBoard(BotPlayer victim)
    {
        Board board = victim.GetComponent<Board>();
        OpponentEliminationPresentation view = victim.GetComponent<OpponentEliminationPresentation>();
        Check(!Get<GameObject>(view, "lockedArtwork").activeSelf && CountBlocks(board) == 14
            && !board.IsCellFree(new Vector2Int(2, 9)), "Elimination cleared fatal stack data instead of hiding its artwork");
        for (int x = 0; x < 10; x++) Check(board.IsCellFree(new Vector2Int(x, 0)) == (x == 5), "Fatal gray row disappeared");
        foreach (SpriteRenderer renderer in Get<SpriteRenderer[]>(board, "boardCells"))
            Check(!renderer.gameObject.activeInHierarchy, "An eliminated board still renders locked blocks");
    }
    private static void CheckSkin(Board board, Sprite expected, int count)
    {
        Check(CountBlocks(board) == count, "A real piece/garbage insertion produced the wrong occupancy");
        SpriteRenderer[] cells = Get<SpriteRenderer[]>(board, "boardCells");
        for (int y = 0; y < Board.Height; y++) for (int x = 0; x < Board.Width; x++)
        {
            bool occupied = !board.IsCellFree(new Vector2Int(x, y)); SpriteRenderer renderer = cells[y * Board.Width + x];
            Check(renderer.enabled == occupied, "Sprite pool occupancy differs from actual board cells");
            if (!occupied) continue;
            Check(renderer.sprite == expected && renderer.color == Color.white
                && (renderer.transform.localScale - board.GetPieceScale(expected)).sqrMagnitude < .000001f,
                "Original cell art is missing, double tinted, or does not fill the grid");
        }
    }
    private static int CountBlocks(Board board)
    {
        int count = 0;
        for (int y = 0; y < Board.Height; y++) for (int x = 0; x < Board.Width; x++)
            if (!board.IsCellFree(new Vector2Int(x, y))) count++;
        return count;
    }
    private MatchStanding Standing(BotPlayer bot)
    {
        foreach (MatchStanding standing in arena.Standings) if (standing.Bot == bot) return standing;
        throw new InvalidOperationException("Missing opponent standing");
    }
    private void CheckCamera() => Check((Camera.main.transform.position - cameraPosition).sqrMagnitude < .000001f
        && Mathf.Abs(Camera.main.orthographicSize - cameraSize) < .00001f, "Joining/eliminating opponents changed camera framing");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    private static object Call(object target, string method, params object[] arguments)
        => target.GetType().GetMethod(method, Fields).Invoke(target, arguments);

    private sealed class BoardSnapshot
    {
        private readonly bool[,] occupied;
        private readonly Sprite[,] sprites;
        private readonly Color[,] colors;
        private readonly int[,] turns;
        public BoardSnapshot(Board board)
        {
            occupied = (bool[,])Get<bool[,]>(board, "occupied").Clone();
            sprites = (Sprite[,])Get<Sprite[,]>(board, "cellArt").Clone();
            colors = (Color[,])Get<Color[,]>(board, "colors").Clone();
            turns = (int[,])Get<int[,]>(board, "cellTurns").Clone();
        }
        public void CheckUnchanged(Board board)
        {
            bool[,] actualOccupied = Get<bool[,]>(board, "occupied"); Sprite[,] actualSprites = Get<Sprite[,]>(board, "cellArt");
            Color[,] actualColors = Get<Color[,]>(board, "colors"); int[,] actualTurns = Get<int[,]>(board, "cellTurns");
            for (int y = 0; y < Board.Height; y++) for (int x = 0; x < Board.Width; x++)
                Check(actualOccupied[x, y] == occupied[x, y] && actualSprites[x, y] == sprites[x, y]
                    && actualColors[x, y] == colors[x, y] && actualTurns[x, y] == turns[x, y],
                    "K.O. presentation/re-enable mutated board occupancy or artwork data");
        }
    }

    private sealed class BoardPoseSnapshot
    {
        private readonly BoardSnapshot contents;
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;
        public BoardPoseSnapshot(Board board)
        {
            contents = new BoardSnapshot(board);
            position = board.transform.localPosition;
            rotation = board.transform.localRotation;
            scale = board.transform.localScale;
        }
        public void CheckUnchanged(Board board)
        {
            contents.CheckUnchanged(board);
            Check(board.transform.localPosition == position && board.transform.localRotation == rotation
                && board.transform.localScale == scale, "A spawn/cursor visual moved or scaled the gameplay board");
        }
    }

    private void Capture(string path)
    {
        Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Original art captures require graphics; omit -nographics");
        Camera view = Camera.main; RenderTexture oldTarget = view.targetTexture, oldActive = RenderTexture.active;
        float oldAspect = view.aspect;
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            target.Create(); foreach (TMP_Text label in savedLabels) label.ForceMeshUpdate(true);
            view.targetTexture = target; view.aspect = 16f / 9f;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                // Warm the offscreen camera's sprite bindings after enabling/disabling effect pools.
                // The first manual URP pass can retain bindings from the preceding game-camera render.
                RenderPipeline.SubmitRenderRequest(view, new RenderPipeline.StandardRequest { destination = target });
                RenderPipeline.SubmitRenderRequest(view, new RenderPipeline.StandardRequest { destination = target });
            }
            else view.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); pixels.Apply(false, false);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            view.targetTexture = oldTarget; view.aspect = oldAspect; RenderTexture.active = oldActive;
            target.Release(); Destroy(target); Destroy(pixels);
        }
    }
    private void OnDestroy()
    {
        UnityEngine.Random.state = originalRandomState;
        if (probe != null) Destroy(probe);
    }
}
#endif
