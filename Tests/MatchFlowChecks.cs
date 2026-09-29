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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Run in an isolated copy of the final project; never save SampleScene from this fixture.
public sealed class MatchFlowChecks : MonoBehaviour
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [SerializeField] private FallingPiece player;
    [SerializeField] private BattleArena arena;
    [SerializeField] private MatchFlow flow;
    [SerializeField] private BotPlayer[] bots;
    [SerializeField] private TMP_Text[] savedLabels;
    [SerializeField] private TMP_FontAsset font;
    private BackgroundMusic music;
    private AudioSource musicSource;
    private AudioSource cueSource;
    private Keyboard keyboard;
    private InputSettings originalInputSettings;
    private InputSettings fixtureInputSettings;
    private bool originalRunInBackground;
    private int eliminatedEvents;
    private int standingsEvents;

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var check = new GameObject("Match flow checks").AddComponent<MatchFlowChecks>();
            check.player = Object.FindFirstObjectByType<FallingPiece>();
            check.arena = Object.FindFirstObjectByType<BattleArena>();
            check.flow = Object.FindFirstObjectByType<MatchFlow>();
            Check(check.player != null && check.arena != null && check.flow != null, "Authored match flow is missing");
            check.bots = check.arena.GetComponentsInChildren<BotPlayer>(true);
            Check(check.bots.Length == 98 && new HashSet<BotPlayer>(check.bots).Count == 98,
                "Final scene must contain ninety-eight distinct saved opponents");
            foreach (BotPlayer bot in check.bots)
            {
                Check(!bot.gameObject.activeSelf, "A saved opponent starts active instead of joining the lobby");
                Set(bot, "secondsPerPiece", 10000f);
            }
            Transform grid = Get<Transform>(check.arena, "opponentGridRoot");
            Check(grid != null && grid.childCount == 98, "The saved ninety-eight opponent positions are missing");
            foreach (SpriteRenderer sprite in grid.GetComponentsInChildren<SpriteRenderer>(true))
                Check(sprite.GetComponentInParent<BotPlayer>() != null || !sprite.enabled || !sprite.gameObject.activeInHierarchy,
                    "An empty opponent slot still contains a visible placeholder object: " + sprite.name);
            Check(Mathf.Approximately(Get<float>(check.flow, "matchmakingDuration"), 90f),
                "The authored lobby timeout must be ninety seconds");
            Check(Get<float>(check.flow, "minJoinInterval") > 0f &&
                Get<float>(check.flow, "maxJoinInterval") >= Get<float>(check.flow, "minJoinInterval"),
                "Saved joins do not have a positive gradual interval");
            Check(Get<int>(check.flow, "minJoinBatch") == 1 && Get<int>(check.flow, "maxJoinBatch") == 5,
                "Saved matchmaking must admit batches of one through five opponents");
            Set(check.flow, "minJoinInterval", .04f); Set(check.flow, "maxJoinInterval", .04f);
            Set(check.flow, "readyDuration", .3f); Set(check.flow, "countdownInterval", .3f); Set(check.flow, "goDuration", .3f);
            Set(check.player, "fallInterval", 10000f);
            check.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/font/ajn-aokane.otf/ajn-aokane SDF.asset");
            check.savedLabels = check.player.GetComponent<Board>().GetComponentsInChildren<TMP_Text>(true);
            check.CheckFonts();
            Check(Get<TMP_Text[]>(check.flow, "standingRows").Length == 12 &&
                Get<TMP_Text[]>(check.flow, "modeLabels").Length == 4 &&
                Get<SpriteRenderer[]>(check.flow, "medalIcons").Length == 4,
                "The authored standings, attack selector, or four medal icons are missing");
            check.CheckOriginalArtwork();
            string[] fields = { "menuMusic", "battleMusic", "joinSound", "readySound", "countSound", "goSound" };
            string[] names = { "01. Menu Theme.mp3", "02. Battle Theme [Tetris Type A GB Remix] [Default Theme].mp3",
                "se_game_match.wav", "me_game_start1.wav", "se_game_count.wav", "me_game_start2_vo.wav" };
            for (int i = 0; i < fields.Length; i++)
            {
                AudioClip clip = Get<AudioClip>(check.flow, fields[i]);
                Check(clip != null && AssetDatabase.GetAssetPath(clip) == "Assets/General Sounds/" + names[i],
                    fields[i] + " is not assigned to the supplied original sound");
            }
            EditorSceneManager.SaveScene(check.player.gameObject.scene, "Assets/MatchFlowChecks.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static T Get<T>(object source, string name) => (T)source.GetType().GetField(name, Fields).GetValue(source);
    private static void Set(object source, string name, object value) => source.GetType().GetField(name, Fields).SetValue(source, value);
    private static object Call(object source, string name, params object[] values)
        => source.GetType().GetMethod(name, Fields).Invoke(source, values);
    private bool Phase(string name) => flow.Phase.ToString() == name;
    private bool RootVisible(string name) => Get<GameObject>(flow, name).activeInHierarchy;
    private GarbageCombat Combat(BotPlayer bot) => bot.GetComponent<GarbageCombat>();
    private MatchStanding Standing(BotPlayer bot)
    {
        foreach (MatchStanding standing in arena.Standings) if (standing.Bot == bot && !standing.IsPlayer) return standing;
        throw new InvalidOperationException("Opponent is missing from match standings");
    }
    private MatchStanding PlayerStanding()
    {
        foreach (MatchStanding standing in arena.Standings) if (standing.IsPlayer) return standing;
        throw new InvalidOperationException("Player is missing from match standings");
    }
    private List<BotPlayer> AdmittedRoster()
    {
        var roster = new List<BotPlayer>(arena.Opponents);
        var authored = new HashSet<BotPlayer>(bots);
        Check(roster.Count == arena.ParticipantCount - 1 && new HashSet<BotPlayer>(roster).Count == roster.Count,
            "Lobby admissions contain a duplicate or disagree with the participant count");
        foreach (BotPlayer bot in roster)
            Check(authored.Contains(bot) && bot.gameObject.activeInHierarchy && !bot.IsGameOver && Standing(bot).Rank == 0,
                "An admitted opponent is not a live instance from a saved slot");
        return roster;
    }
    private void CheckOriginalArtwork()
    {
        const string theme = "Assets/Nintendo Switch - Tetris 99 - Themes - Default (Tetris 99)/";
        const string common = "Assets/Nintendo Switch - Tetris 99 - Themes - Common Elements/";
        CheckOriginalSprite(Get<Sprite>(flow, "readyArtwork"), theme + "enUS/logo_ready00.png");
        CheckOriginalSprite(Get<Sprite>(flow, "goArtwork"), theme + "enUS/logo_go00.png");
        Sprite[] numbers = Get<Sprite[]>(flow, "countdownArtwork");
        Check(numbers.Length == 3 && Get<SpriteRenderer>(flow, "phaseArtwork") != null,
            "The saved countdown is missing its original sprite renderer or three number sprites");
        for (int i = 0; i < numbers.Length; i++)
            CheckOriginalSprite(numbers[i], theme + "_Textures/logo_ready0" + (i + 1) + ".png");
        CheckOriginalSprite(Get<Sprite>(flow, "modeNormalArtwork"), common + "_texture/game_tactics00^t.png");
        CheckOriginalSprite(Get<Sprite>(flow, "modeSelectedArtwork"), common + "_texture/game_tactics01^q.png");
        SpriteRenderer[] panels = Get<SpriteRenderer[]>(flow, "modePanels");
        Check(panels.Length == 4, "The original attack selector is missing one of its four panels");
        foreach (SpriteRenderer panel in panels) Check(panel != null, "An attack selector panel is unassigned");
    }
    private static void CheckOriginalSprite(Sprite sprite, string path)
        => Check(sprite != null && AssetDatabase.GetAssetPath(sprite) == path,
            "The saved presentation is missing supplied original artwork: " + path);
    private void CheckPhaseArtwork(string shown)
    {
        Sprite expected = shown == "READY" ? Get<Sprite>(flow, "readyArtwork") :
            shown == "GO" ? Get<Sprite>(flow, "goArtwork") :
            Get<Sprite[]>(flow, "countdownArtwork")[int.Parse(shown) - 1];
        SpriteRenderer artwork = Get<SpriteRenderer>(flow, "phaseArtwork");
        Check(artwork.enabled && artwork.gameObject.activeInHierarchy && artwork.sprite == expected &&
            !Get<TMP_Text>(flow, "countdownText").enabled,
            "The countdown did not display the matching original sprite for " + shown);
    }
    private void CheckFonts()
    {
        Check(font != null && TMP_Settings.defaultFontAsset == font, "The supplied AJN font is not the TMP default");
        foreach (TMP_Text label in savedLabels) Check(label.font == font, label.name + " does not use ajn-aokane");
    }
    private void CheckSavedObjects()
    {
        Check(new HashSet<BotPlayer>(arena.GetComponentsInChildren<BotPlayer>(true)).SetEquals(bots),
            "A match transition replaced or generated an opponent");
        Check(new HashSet<TMP_Text>(player.GetComponent<Board>().GetComponentsInChildren<TMP_Text>(true)).SetEquals(savedLabels),
            "A match transition recreated editable labels");
        CheckFonts();
    }
    private void CheckLobbyGates()
    {
        Check(!arena.MatchStarted && player.BattlePaused && !arena.IsRoundOver,
            "Lobby/countdown did not gate player gameplay");
        Check(!player.CanHold && player.LinesCleared == 0 && player.GetComponent<Board>().IsEmpty,
            "The gated player retained an active hold action or locked board state");
        foreach (BotPlayer bot in bots)
            Check(bot.PiecesPlaced == 0 && (!bot.gameObject.activeInHierarchy || bot.Paused),
                "An opponent played a piece before GO");
    }
    private void Press(params Key[] keys)
    {
        keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
        keyboard.MakeCurrent();
        var keyStates = new List<string>();
        foreach (Key key in new[] { Key.W, Key.A, Key.S, Key.D, Key.R })
            keyStates.Add($"{key}:pressed={keyboard[key].isPressed},down={keyboard[key].wasPressedThisFrame}");
        Debug.Log($"MATCH_FLOW_INPUT: frame={Time.frameCount}, update={InputState.currentUpdateType}, expectedKeyboard={keyboard.deviceId}, currentKeyboard={(Keyboard.current != null ? Keyboard.current.deviceId : -1)}, deviceEnabled={keyboard.enabled}, appFocus={Application.isFocused}, phase={flow.Phase}, playerDead={player.IsGameOver}, modeBefore={arena.CurrentAttackMode}, keys=[{string.Join("; ", keyStates)}]");
        Check(Keyboard.current == keyboard && InputState.currentUpdateType == InputUpdateType.Dynamic,
            "The synthetic keyboard event did not reach a Dynamic gameplay input update");
        foreach (Key key in keys)
            Check(keyboard[key].isPressed && keyboard[key].wasPressedThisFrame,
                "The queued key did not arrive as a fresh gameplay press: " + key);
        Call(arena, "Update");
        Call(flow, "Update");
        Call(player, "Update");
        Debug.Log($"MATCH_FLOW_INPUT_RESULT: frame={Time.frameCount}, phase={flow.Phase}, playerDead={player.IsGameOver}, modeAfter={arena.CurrentAttackMode}");
    }
    private void KnockOut(BotPlayer victim, GarbageCombat attacker)
    {
        Board board = victim.GetComponent<Board>();
        GarbageCombat combat = Combat(victim);
        board.ClearBoard(); combat.ClearPending();
        Set(combat, "incomingDelay", 0f);
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, Board.Height - 1), Color.white,
            new[] { Get<Sprite>(victim, "blockSprite") }), "Could not seed the attributed garbage topout");
        combat.ReceiveGarbage(1, attacker, 5);
        Queue<int> bag = Get<Queue<int>>(victim, "bag"); bag.Clear(); bag.Enqueue(1);
        Call(victim, "PlaceNextPiece");
        Check(victim.IsGameOver && combat.LastAttacker == attacker,
            "Real opponent placement did not produce its attributed garbage knockout");
    }
    private int RecordFirstTurns(HashSet<BotPlayer> served, Dictionary<int, int> plansByFrame)
    {
        int observed = 0;
        foreach (BotPlayer bot in bots)
        {
            if (bot.PiecesPlaced == 0 || served.Contains(bot)) continue;
            Check(bot.PiecesPlaced == 1 && !bot.IsGameOver && !bot.GetComponent<Board>().IsEmpty,
                "An opponent was served repeatedly while another due opponent waited");
            int plannedFrame = Get<int>(bot, "lastPlannedFrame");
            int count = plansByFrame.TryGetValue(plannedFrame, out int previous) ? previous + 1 : 1;
            plansByFrame[plannedFrame] = count;
            Check(plannedFrame >= 0 && count <= BotPlayer.MaxPlansPerFrame,
                $"Planning budget exceeded: plan frame={plannedFrame}, plans={count}, observation frame={Time.frameCount}, previously served={served.Count}");
            served.Add(bot); bot.Paused = true; observed++;
        }
        return observed;
    }
    private void ApplyRuntimeFixtureTiming()
    {
        // Reflection before SaveScene does not reliably serialize prefab-instance overrides.
        // Reapply the fixture controls in the live scene before its first coroutine yield.
        float loadedBotPace = Get<float>(bots[0], "secondsPerPiece");
        float loadedGravity = Get<float>(player, "fallInterval");
        foreach (BotPlayer bot in bots) Set(bot, "secondsPerPiece", 10000f);
        Set(player, "fallInterval", 10000f);
        Set(flow, "minJoinInterval", .04f); Set(flow, "maxJoinInterval", .04f);
        Set(flow, "readyDuration", .3f); Set(flow, "countdownInterval", .3f); Set(flow, "goDuration", .3f);
        Set(flow, "joinTimer", .04f); // Awake already initialized this countdown from the loaded settings.
        CheckRuntimeFixtureTiming();
        Debug.Log($"MATCH_FLOW_FIXTURE_TIMING: loaded bot pace={loadedBotPace}, gravity={loadedGravity}; runtime bot pace/gravity=10000, joins=.04, READY/count/GO=.3");
    }
    private void CheckRuntimeFixtureTiming()
    {
        foreach (BotPlayer bot in bots)
            Check(Mathf.Approximately(Get<float>(bot, "secondsPerPiece"), 10000f),
                "A saved bot did not receive the live fixture's held pace");
        Check(Mathf.Approximately(Get<float>(player, "fallInterval"), 10000f) &&
            Mathf.Approximately(Get<float>(flow, "minJoinInterval"), .04f) &&
            Mathf.Approximately(Get<float>(flow, "maxJoinInterval"), .04f) &&
            Mathf.Approximately(Get<float>(flow, "readyDuration"), .3f) &&
            Mathf.Approximately(Get<float>(flow, "countdownInterval"), .3f) &&
            Mathf.Approximately(Get<float>(flow, "goDuration"), .3f) &&
            Mathf.Approximately(Get<float>(flow, "matchmakingDuration"), 90f) &&
            Get<int>(flow, "minJoinBatch") == 1 && Get<int>(flow, "maxJoinBatch") == 5,
            "Live fixture gravity/timing controls differ from the intended values");
    }

    private IEnumerator Start()
    {
        IEnumerator checks = RunChecks();
        while (true)
        {
            bool more; object next;
            try { more = checks.MoveNext(); next = more ? checks.Current : null; }
            catch (Exception exception)
            {
                Debug.LogException(exception); EditorApplication.Exit(1); more = false; next = null;
            }
            if (!more) yield break;
            yield return next;
        }
    }

    private IEnumerator RunChecks()
    {
        ApplyRuntimeFixtureTiming();
        yield return null;
        yield return null;
        CheckRuntimeFixtureTiming();
        music = Get<BackgroundMusic>(flow, "music");
        musicSource = Get<AudioSource>(music, "musicSource");
        cueSource = Get<AudioSource>(flow, "cueSource");
        Check(music != null && musicSource != null && cueSource != null && Phase("Matchmaking"),
            "The saved scene did not enter matchmaking with its saved audio sources");
        // A batch editor has no focused Game View. Keep fixture events in the Dynamic player buffer.
        originalInputSettings = InputSystem.settings;
        fixtureInputSettings = Object.Instantiate(originalInputSettings);
        fixtureInputSettings.hideFlags = HideFlags.HideAndDontSave;
        fixtureInputSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        fixtureInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        fixtureInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        originalRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        InputSystem.settings = fixtureInputSettings;
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        arena.ParticipantEliminated += _ => eliminatedEvents++;
        arena.StandingsChanged += () => standingsEvents++;
        AudioClip probe = AudioClip.Create("Match cue playback probe", 44100, 1, 44100, false);
        foreach (string field in new[] { "joinSound", "readySound", "countSound", "goSound" }) Set(flow, field, probe);
        cueSource.volume = 0f;
        Check(music.IsPlaying && musicSource.clip == Get<AudioClip>(flow, "menuMusic"), "Lobby menu music did not start");
        CheckLobbyGates();
        int initialParticipants = arena.ParticipantCount;
        var initiallyAdmitted = new HashSet<BotPlayer>(AdmittedRoster());
        cueSource.Stop();
        float deadline = Time.realtimeSinceStartup + 5f;
        while (arena.ParticipantCount <= initialParticipants && Time.realtimeSinceStartup < deadline) yield return null;
        Check(arena.ParticipantCount > initialParticipants && arena.ParticipantCount < 99 && cueSource.isPlaying,
            "The lobby did not gradually admit an opponent and play its join cue");
        List<BotPlayer> joinedRoster = AdmittedRoster();
        int joinedCount = arena.ParticipantCount - initialParticipants;
        Check(joinedCount >= Get<int>(flow, "minJoinBatch") && joinedCount <= Get<int>(flow, "maxJoinBatch") &&
            initiallyAdmitted.IsSubsetOf(joinedRoster),
            "A timed join exceeded its batch bounds or replaced an existing admitted opponent");
        Debug.Log($"MATCH_FLOW_JOIN_BATCH: admitted={joinedCount}, participants={arena.ParticipantCount}");
        Check(RootVisible("lobbyRoot") && !RootVisible("resultsRoot"), "Lobby/results visibility is wrong during joins");
        CheckLobbyGates();
        Camera.main.aspect = 16f / 9f; player.GetComponent<Board>().FitCamera();
        Capture("/tmp/tetris-match-lobby.png");

        while (arena.AdmitNextBot()) { }
        Check(arena.ParticipantCount == 99 && flow.RemainingSeconds > 0f,
            "The full lobby was not populated before its timeout");
        cueSource.Stop();
        var countdown = new List<string>();
        bool capturedCountdown = false;
        deadline = Time.realtimeSinceStartup + 8f;
        while (!arena.MatchStarted && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            string shown = Get<TMP_Text>(flow, "countdownText").text.ToUpperInvariant().TrimEnd('!');
            if (shown.Length > 0 && (countdown.Count == 0 || countdown[countdown.Count - 1] != shown))
            {
                countdown.Add(shown);
                Check(cueSource.isPlaying, "A visible READY/count/GO transition did not play its cue");
                CheckPhaseArtwork(shown);
            }
            if (!arena.MatchStarted) CheckLobbyGates();
            if (!arena.MatchStarted && !Phase("Matchmaking")) Check(!music.IsPlaying, "Menu music continued under the READY/countdown cues");
            if (shown == "3" && !capturedCountdown)
            {
                Check(cueSource.isPlaying, "The visible countdown number did not play its original count cue");
                Capture("/tmp/tetris-match-countdown.png"); capturedCountdown = true;
            }
        }
        Check(arena.MatchStarted && Phase("Playing") && string.Join(",", countdown) == "READY,3,2,1,GO",
            "A full lobby did not run READY, 3, 2, 1, GO before live play");
        Check(arena.AliveParticipants == 99 && arena.PlayerRank == 0 && !player.IsGameOver && !player.BattlePaused &&
            music.IsPlaying && musicSource.clip == Get<AudioClip>(flow, "battleMusic"),
            "GO did not release ninety-nine participants and switch to battle music");
        Check(arena.Standings.Count == 99, "Full match has an incomplete standings table");
        foreach (MatchStanding standing in arena.Standings) Check(standing.Rank == 0, "A live participant received an elimination rank");
        List<BotPlayer> fullRoster = AdmittedRoster();

        // Preserve any GO-frame first turns, then make every remaining bot due together.
        // Coroutine observations may straddle Update frames: the scheduler's recorded frame owns each plan.
        var served = new HashSet<BotPlayer>();
        var plansByFrame = new Dictionary<int, int>();
        int baseline = RecordFirstTurns(served, plansByFrame);
        Debug.Log($"MATCH_FLOW_BOT_BASELINE: observation frame={Time.frameCount}, first placements={baseline}");
        foreach (BotPlayer bot in bots)
        {
            if (served.Contains(bot)) continue;
            Set(bot, "secondsPerPiece", .1f); Set(bot, "pieceTimer", .1f); bot.Paused = false;
        }
        int completed = served.Count, largestFrame = 0;
        float planningStarted = Time.realtimeSinceStartup;
        deadline = planningStarted + 30f;
        while (completed < 98 && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            int newPlacements = RecordFirstTurns(served, plansByFrame);
            completed = served.Count;
            Debug.Log($"MATCH_FLOW_BOT_BATCH: observation frame={Time.frameCount}, new={newPlacements}, baseline={baseline}, total={completed}");
        }
        foreach (int count in plansByFrame.Values) largestFrame = Mathf.Max(largestFrame, count);
        Check(completed == 98 && BotPlayer.MaxPlansPerFrame == 3, "The fair three-plan budget starved an opponent");
        Debug.Log($"MATCH_FLOW_BOT_FAIRNESS: 98 first placements in {Time.realtimeSinceStartup - planningStarted:F2}s; largest frame={largestFrame}");
        Capture("/tmp/tetris-match-battle.png");
        Check(RootVisible("attackOverlayRoot"), "Attack modes are hidden during live play");
        Key[] keys = { Key.W, Key.A, Key.S, Key.D };
        AttackMode[] modes = { AttackMode.Knockouts, AttackMode.Randoms, AttackMode.Attackers, AttackMode.Badges };
        for (int i = 0; i < keys.Length; i++)
        {
            Press();
            yield return null;
            Press(keys[i]);
            Check(arena.CurrentAttackMode == modes[i], keys[i] + " did not select its attack mode");
            SpriteRenderer[] panels = Get<SpriteRenderer[]>(flow, "modePanels");
            for (int panel = 0; panel < panels.Length; panel++)
                Check(panels[panel].sprite == Get<Sprite>(flow,
                    panel == (int)modes[i] ? "modeSelectedArtwork" : "modeNormalArtwork") &&
                    panels[panel].color == Color.white,
                    "Attack-mode original artwork does not match the keyboard selection");
        }
        Press();
        foreach (AttackMode mode in Enum.GetValues(typeof(AttackMode)))
        {
            arena.SetAttackMode(mode); Check(arena.CurrentAttackMode == mode, "Attack mode API failed");
        }

        BotPlayer attacker = fullRoster[0], victim = fullRoster[1], spectatorOpponent = fullRoster[2];
        GarbageCombat attackCombat = Combat(attacker), victimCombat = Combat(victim);
        Board highStack = victim.GetComponent<Board>();
        Check(highStack.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, Board.Height - 2), Color.white,
            new[] { Get<Sprite>(victim, "blockSprite") }), "Could not seed a uniquely high knockout target");
        arena.SetAttackMode(AttackMode.Knockouts);
        Check(arena.Target == victim, "Knockouts did not choose the uniquely highest stack");
        Combat(spectatorOpponent).AwardKnockout(Combat(fullRoster[3]));
        arena.SetAttackMode(AttackMode.Badges);
        Check(arena.Target == spectatorOpponent, "Badges did not choose the opponent with the most medal bits");
        Combat(spectatorOpponent).ResetCombat();
        var targets = Get<Dictionary<GarbageCombat, GarbageCombat>>(arena, "botTargets");
        for (int i = 0; i < fullRoster.Count; i++) targets[Combat(fullRoster[i])] = Combat(fullRoster[(i + 1) % fullRoster.Count]);
        GarbageCombat playerCombat = player.GetComponent<GarbageCombat>();
        targets[attackCombat] = targets[victimCombat] = playerCombat;
        playerCombat.ResetCombat();
        foreach (BotPlayer bot in bots) Combat(bot).ClearPending();
        arena.SetAttackMode(AttackMode.Attackers);
        var multicast = new HashSet<GarbageCombat>();
        Action<GarbageCombat, GarbageCombat, int> observe = (source, recipient, lines) =>
        {
            if (source != playerCombat) return;
            Check(lines == 1 && multicast.Add(recipient), "Attackers emitted duplicate or incorrectly sized accepted packets");
        };
        arena.AttackDelivered += observe;
        playerCombat.SetAttackersCount(0); // Isolate the one-line double from the independently tested attacker bonus.
        Check(playerCombat.ResolveLock(2), "Multicast fixture unexpectedly topped out");
        arena.AttackDelivered -= observe;
        Check(multicast.SetEquals(new[] { attackCombat, victimCombat }),
            "Attackers mode did not deliver exactly once to both actual incoming attackers");
        foreach (BotPlayer bot in bots)
        {
            Check(Combat(bot).PendingLines == (bot == attacker || bot == victim ? 1 : 0),
                "Attackers mode changed an unrelated recipient queue");
            Combat(bot).ClearPending();
        }
        playerCombat.ResetCombat();
        ClearAttackFeedback feedback = player.GetComponent<ClearAttackFeedback>();
        if (feedback != null) feedback.ClearFeedback();
        arena.SetAttackMode(AttackMode.Randoms);
        attackCombat.ResetCombat(); victimCombat.ResetCombat();
        Get<Dictionary<GarbageCombat, GarbageCombat>>(arena, "botTargets")[attackCombat] = victimCombat;
        Check(attackCombat.ResolveLock(4) && victimCombat.PendingLines == 4,
            "A bot's actual clear did not route accepted garbage to another bot");
        victimCombat.AwardKnockout(Combat(spectatorOpponent));
        KnockOut(victim, attackCombat);
        Check(Standing(victim).Rank == 99 && arena.AliveParticipants == 98 && Standing(attacker).Knockouts == 1 &&
            Standing(attacker).Medals == 2 && attackCombat.BadgeBits == 2 && attackCombat.BadgeCount == 1,
            "Bot knockout rank, KO count, or transferred medal bits are incorrect");
        Check(eliminatedEvents == 1 && standingsEvents > 0, "Standings changes were not published after a bot knockout");
        victim.enabled = false;
        victim.enabled = true;
        Check(victim.IsGameOver && victim.Paused && Standing(victim).Rank == 99 && arena.AliveParticipants == 98 &&
            eliminatedEvents == 1 && !targets.ContainsKey(victimCombat),
            "Re-enabling an eliminated opponent revived it, changed its rank, or restored its attack target");
        foreach (BotPlayer participant in arena.Opponents)
            Check(participant != victim, "An eliminated opponent re-registered as a live combatant");

        DeathScreen death = player.GetComponent<DeathScreen>();
        Set(death, "rowInterval", .01f); Set(death, "rowFlashDuration", .05f); Set(death, "deathSound", probe);
        Get<AudioSource>(death, "soundSource").volume = 0f;
        spectatorOpponent.Paused = false; Set(spectatorOpponent, "pieceTimer", .1f);
        int beforeSpectating = spectatorOpponent.PiecesPlaced;
        Call(player, "EndGame");
        Check(!music.IsPlaying, "Menu music interrupted the player's death cue");
        deadline = Time.realtimeSinceStartup + 5f;
        while ((!Phase("Spectating") || spectatorOpponent.PiecesPlaced == beforeSpectating) && Time.realtimeSinceStartup < deadline) yield return null;
        spectatorOpponent.Paused = true;
        Check(player.IsGameOver && !arena.IsRoundOver && arena.PlayerRank == 98 && PlayerStanding().Rank == 98 &&
            spectatorOpponent.PiecesPlaced > beforeSpectating && !spectatorOpponent.IsGameOver,
            "Player death ended the match or stopped surviving opponents");
        deadline = Time.realtimeSinceStartup + 5f;
        while ((!death.CanRetry || !music.IsPlaying) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(death.CanRetry && music.IsPlaying && musicSource.clip == Get<AudioClip>(flow, "lossResultsMusic"),
            "The spectator results did not start the eliminated-results track after death audio completed");
        Check(RootVisible("resultsRoot") && Get<TMP_Text>(flow, "rankText").text.Contains("98"),
            "The live spectator leaderboard is missing the player's frozen elimination rank after the death sweep");
        flow.BeginMatchmaking(); Press(Key.R); Press();
        Check(Phase("Spectating") && player.IsGameOver && arena.ParticipantCount == 99 && !arena.IsRoundOver,
            "Retry reset the lobby while surviving opponents were still fighting");
        flow.NextStandingsPage(); Check(flow.StandingsPage == 1, "The live leaderboard did not page forward");
        flow.PreviousStandingsPage(); Check(flow.StandingsPage == 0, "The live leaderboard did not page backward");
        Capture("/tmp/tetris-match-spectating.png");

        foreach (BotPlayer bot in fullRoster) if (bot != attacker && !bot.IsGameOver) Call(bot, "Die");
        yield return null;
        Check(arena.IsRoundOver && !arena.PlayerWon && Phase("Finished") && Standing(attacker).Rank == 1 &&
            PlayerStanding().Rank == 98 && Standing(victim).Rank == 99,
            "The last surviving opponent did not win or an established rank changed");
        var ranks = new HashSet<int>();
        foreach (MatchStanding standing in arena.Standings)
            Check(standing.Rank >= 1 && standing.Rank <= 99 && ranks.Add(standing.Rank),
                "Final standings have a missing, duplicate, or out-of-range rank");
        Check(ranks.Count == 99 && eliminatedEvents == 98, "The full match did not retain ninety-nine unique placements");
        Capture("/tmp/tetris-match-results.png");
        CheckSavedObjects();
        Set(flow, "minJoinInterval", 1000f); Set(flow, "maxJoinInterval", 1000f);
        Press(Key.R); Press();
        yield return null;
        Check(Phase("Matchmaking") && arena.ParticipantCount == 1 && !player.IsGameOver && arena.PlayerRank == 0,
            "A completed match did not restart into a clean one-player lobby");
        Check(music.IsPlaying && musicSource.clip == Get<AudioClip>(flow, "menuMusic"),
            "Retry did not resume lobby menu music");
        CheckLobbyGates();
        foreach (BotPlayer bot in bots) Check(!bot.gameObject.activeSelf && bot.PiecesPlaced == 0, "Retry retained an admitted or played opponent");

        // Preserve the authored ninety-second timeout and advance only its remaining clock in this fixture.
        Check(arena.AdmitNextBot() && arena.AdmitNextBot() && arena.ParticipantCount == 3,
            "The smaller timeout fixture could not admit two opponents");
        List<BotPlayer> smallerRoster = AdmittedRoster();
        Set(flow, "<RemainingSeconds>k__BackingField", .02f);
        deadline = Time.realtimeSinceStartup + 8f;
        while (!arena.MatchStarted && Time.realtimeSinceStartup < deadline) yield return null;
        Check(arena.MatchStarted && arena.ParticipantCount == 3 && Phase("Playing"),
            "A nonfull lobby failed to start after its ninety-second deadline");
        Check(new HashSet<BotPlayer>(AdmittedRoster()).SetEquals(smallerRoster),
            "The timeout replaced one of the two randomly admitted opponents");
        AudioSource victorySource = Get<AudioSource>(flow, "victorySoundSource");
        Check(victorySource != null && victorySource == Get<AudioSource>(player, "soundSource"),
            "Match flow does not observe the player's actual victory audio source");
        Set(player, "victorySound", probe); victorySource.volume = 0f;
        foreach (BotPlayer bot in smallerRoster) if (!bot.IsGameOver) Call(bot, "Die");
        Check(arena.IsRoundOver && arena.PlayerWon && arena.PlayerRank == 1 && PlayerStanding().Rank == 1 &&
            !player.IsGameOver && player.BattlePaused && victorySource.isPlaying && !music.IsPlaying,
            "The player did not win the smaller match with rank one, victory audio, and paused battle music");
        yield return null;
        Check(Phase("Finished") && RootVisible("resultsRoot") &&
            Get<TMP_Text>(flow, "resultTitle").text.Contains("VICTORY"),
            "Player victory did not show the finished results panel");
        var victoryRanks = new HashSet<int>();
        foreach (MatchStanding standing in arena.Standings)
            Check(standing.Rank >= 1 && standing.Rank <= 3 && victoryRanks.Add(standing.Rank),
                "The smaller player-win match has duplicate or invalid final ranks");
        Check(victoryRanks.Count == 3, "The player-win match lost one of its three standings");
        flow.BeginMatchmaking();
        Check(Phase("Matchmaking") && arena.ParticipantCount == 1 && arena.PlayerRank == 0 &&
            !arena.IsRoundOver && !arena.PlayerWon && !player.IsGameOver && !victorySource.isPlaying &&
            music.IsPlaying && musicSource.clip == Get<AudioClip>(flow, "menuMusic"),
            "Player-victory retry did not stop victory audio and restore a clean lobby");
        CheckLobbyGates();
        foreach (BotPlayer bot in bots) Check(!bot.gameObject.activeSelf && bot.PiecesPlaced == 0,
            "Player-victory retry retained an admitted or played opponent");
        CheckSavedObjects();
        InputSystem.RemoveDevice(keyboard);
        InputSystem.settings = originalInputSettings;
        Application.runInBackground = originalRunInBackground;
        Destroy(fixtureInputSettings); Destroy(probe);
        Debug.Log("MATCH_FLOW_CHECKS_PASSED screenshots=/tmp/tetris-match-{lobby,countdown,battle,spectating,results}.png");
        EditorApplication.Exit(0);
    }

    private void Capture(string path)
    {
        Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Match screenshots require graphics; omit -nographics");
        Camera view = Camera.main;
        RenderTexture oldTarget = view.targetTexture, oldActive = RenderTexture.active;
        float oldAspect = view.aspect;
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            target.Create();
            foreach (TMP_Text label in savedLabels) label.ForceMeshUpdate(true);
            view.targetTexture = target; view.aspect = 16f / 9f;
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(view, new RenderPipeline.StandardRequest { destination = target });
            else view.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); pixels.Apply(false, false);
            byte[] png = pixels.EncodeToPNG(); Check(png != null && png.Length > 1024, "Could not encode match screenshot");
            File.WriteAllBytes(path, png);
        }
        finally
        {
            view.targetTexture = oldTarget; view.aspect = oldAspect; RenderTexture.active = oldActive;
            target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}
#endif
