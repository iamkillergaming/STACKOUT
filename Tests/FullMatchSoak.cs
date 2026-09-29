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

// Copy into Assets of an isolated project; this fixture never saves SampleScene or any prefab.
[DefaultExecutionOrder(1000)]
public sealed class FullMatchSoak : MonoBehaviour
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic;
    private const float WallLimit = 300f;
    private const int PlacementLimit = 100000;
    [SerializeField] private BattleArena arena;
    [SerializeField] private MatchFlow flow;
    [SerializeField] private FallingPiece player;
    [SerializeField] private BotPlayer[] bots;
    private int[] observedPieces;
    private GarbageCombat[] combats;
    private readonly Dictionary<int, int> placementsByFrame = new Dictionary<int, int>();
    private readonly HashSet<int> eliminationRanks = new HashSet<int>();
    private int placements;
    private int deliveredAttacks;
    private int deliveredLines;
    private int participantEliminations;
    private int botEliminations;
    private int maximumPending;
    private int maximumFramePlans;
    private float startedAt;
    private float previousTimeScale;
    private bool changedTimeScale;
    private bool observing;
    private bool forcedPlayerDeath;
    private bool failed;

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var fixture = new GameObject("Full natural match soak").AddComponent<FullMatchSoak>();
            fixture.arena = Object.FindFirstObjectByType<BattleArena>();
            fixture.flow = Object.FindFirstObjectByType<MatchFlow>();
            fixture.player = Object.FindFirstObjectByType<FallingPiece>();
            Check(fixture.arena != null && fixture.flow != null && fixture.player != null,
                "The authored arena, match flow, or player is missing");
            fixture.bots = fixture.arena.GetComponentsInChildren<BotPlayer>(true);
            Check(fixture.bots.Length == 98 && new HashSet<BotPlayer>(fixture.bots).Count == 98,
                "The final scene must contain ninety-eight distinct saved opponents");
            foreach (BotPlayer bot in fixture.bots)
            {
                Check(!bot.gameObject.activeSelf, "A saved opponent starts active instead of waiting to join");
                Check(Mathf.Approximately(Get<float>(bot, "secondsPerPiece"), 1.1f),
                    "The saved opponent interval must retain its authored 1.1-second pace");
            }
            // Admission uses the real API below. Suppress timed joins so it remains deterministic.
            Set(fixture.flow, "minJoinInterval", 1000f);
            Set(fixture.flow, "maxJoinInterval", 1000f);
            Set(fixture.flow, "readyDuration", .05f);
            Set(fixture.flow, "countdownInterval", .05f);
            Set(fixture.flow, "goDuration", .05f);
            EditorSceneManager.SaveScene(fixture.player.gameObject.scene, "Assets/FullMatchSoak.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static T Get<T>(object target, string field)
        => (T)target.GetType().GetField(field, InstanceFields).GetValue(target);
    private static void Set(object target, string field, object value)
        => target.GetType().GetField(field, InstanceFields).SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private IEnumerator Start()
    {
        IEnumerator checks = RunChecks();
        while (!failed)
        {
            bool more;
            object next;
            try { more = checks.MoveNext(); next = more ? checks.Current : null; }
            catch (Exception exception) { Fail(exception); more = false; next = null; }
            if (!more) yield break;
            yield return next;
        }
    }

    private IEnumerator RunChecks()
    {
        yield return null;
        yield return null;
        Check(flow.Phase == MatchFlow.MatchPhase.Matchmaking && !arena.MatchStarted,
            "The saved scene did not begin in matchmaking");
        // Preserve the authored bot cadence relative to garbage/clear delays. Accelerate them together
        // through timeScale after GO, rather than granting extra clear/cancel opportunities per packet.
        foreach (BotPlayer bot in bots)
        {
            Check(!bot.gameObject.activeSelf && bot.PiecesPlaced == 0,
                "An opponent joined before its runtime soak configuration was checked");
            Check(Mathf.Approximately(Get<float>(bot, "secondsPerPiece"), 1.1f),
                "The runtime opponent interval lost its authored 1.1-second pace");
        }
        Set(flow, "minJoinInterval", 1000f);
        Set(flow, "maxJoinInterval", 1000f);
        Set(flow, "joinTimer", 1000f);
        foreach (string field in new[] { "readyDuration", "countdownInterval", "goDuration" })
        {
            Set(flow, field, .05f);
            Check(Mathf.Approximately(Get<float>(flow, field), .05f),
                "A runtime countdown duration was not accelerated: " + field);
        }
        Set(player, "fallInterval", 10000f);
        Check(Mathf.Approximately(Get<float>(player, "fallInterval"), 10000f) &&
            Mathf.Approximately(Get<float>(flow, "joinTimer"), 1000f),
            "The runtime player-gravity or manual-admission guard was not configured");
        Debug.Log("FULL_MATCH_SOAK_CONFIGURATION bots=98 secondsPerPiece=1.1 ready/count/go=0.05 timeScaleAfterGO=16 wallLimit=300 placementLimit=100000");
        observedPieces = new int[bots.Length];
        combats = new GarbageCombat[bots.Length];
        for (int i = 0; i < bots.Length; i++)
        {
            combats[i] = bots[i].GetComponent<GarbageCombat>();
            Check(combats[i] != null, "A saved bot has no real garbage combat component");
        }
        arena.AttackDelivered += AttackDelivered;
        arena.ParticipantEliminated += ParticipantEliminated;
        observing = true;
        startedAt = Time.realtimeSinceStartup;
        while (arena.AdmitNextBot()) { }
        Check(arena.ParticipantCount == 99 && arena.Standings.Count == 99,
            "The real admission API did not create a complete joined roster");

        float startupDeadline = Time.realtimeSinceStartup + 15f;
        while (!arena.MatchStarted && Time.realtimeSinceStartup < startupDeadline) yield return null;
        Check(arena.MatchStarted && flow.Phase == MatchFlow.MatchPhase.Playing &&
            arena.AliveParticipants == 99 && !player.IsGameOver, "The full lobby did not reach GO");
        // This is the only forced elimination. Every bot subsequently uses normal Update/SRS/combat.
        typeof(FallingPiece).GetMethod("EndGame", InstanceFields).Invoke(player, null);
        forcedPlayerDeath = true;
        Check(player.IsGameOver && player.BattlePaused && arena.PlayerRank == 99 && !arena.IsRoundOver,
            "The player's opening elimination did not leave all ninety-eight bots fighting");
        foreach (BotPlayer bot in bots)
            Check(!bot.IsGameOver && !bot.Paused && Mathf.Approximately(Get<float>(bot, "secondsPerPiece"), 1.1f),
                "Player death paused/eliminated an opponent, or GO lost the authored 1.1-second pace");
        previousTimeScale = Time.timeScale;
        changedTimeScale = true;
        Time.timeScale = 16f;
        startedAt = Time.realtimeSinceStartup;
        float deadline = startedAt + WallLimit;
        float nextReport = startedAt + 10f;
        while (!arena.IsRoundOver)
        {
            Check(Time.realtimeSinceStartup < deadline, "Natural match exceeded its wall-clock limit. " + Status());
            Check(placements < PlacementLimit, "Natural match exceeded its placement limit. " + Status());
            if (Time.realtimeSinceStartup >= nextReport)
            {
                Debug.Log("FULL_MATCH_SOAK_PROGRESS " + Status());
                nextReport = Time.realtimeSinceStartup + 10f;
            }
            yield return null;
            Check(arena.PlayerRank == 99, "The spectator's established rank changed while bots fought");
        }
        // Allow MatchFlow's next Update to publish the final standings, and observe the final bot turn.
        yield return null;
        yield return null;
        ObserveFrame();
        Check(flow.Phase == MatchFlow.MatchPhase.Finished && !arena.PlayerWon && arena.PlayerRank == 99,
            "Natural last-survivor completion did not enter the finished spectator results");
        Check(arena.AliveOpponents == 1 && arena.AliveParticipants == 1,
            "The natural match did not finish with exactly one surviving opponent");
        Check(participantEliminations == 98 && botEliminations == 97 && eliminationRanks.Count == 98,
            "The natural elimination event history is incomplete or duplicated");
        Check(deliveredAttacks > 0 && deliveredLines > 0,
            "The bot match ended without any real accepted garbage attacks");
        var finalRanks = new HashSet<int>();
        var finalBots = new HashSet<BotPlayer>();
        int winners = 0;
        int knockouts = 0;
        foreach (MatchStanding standing in arena.Standings)
        {
            Check(finalRanks.Add(standing.Rank), "The completed match contains duplicate ranks");
            knockouts += standing.Knockouts;
            if (standing.IsPlayer) Check(standing.Rank == 99, "The player's final rank is not 99");
            else
            {
                Check(standing.Bot != null && finalBots.Add(standing.Bot) && standing.Rank >= 1 && standing.Rank <= 98,
                    "A bot is missing, duplicated, or has an invalid final rank");
                if (standing.Rank == 1) { winners++; Check(!standing.Bot.IsGameOver, "The winning bot is eliminated"); }
                else Check(standing.Bot.IsGameOver, "A ranked losing bot is still alive");
            }
        }
        Check(finalRanks.Count == 99 && finalBots.SetEquals(bots) && winners == 1 && knockouts > 0,
            "The natural match did not retain all ranks, one winner, and attributed knockouts");
        foreach (BotPlayer bot in bots)
            Check(bot.Paused && bot.GetComponent<GarbageCombat>().PendingLines == 0,
                "A bot retained scheduled play or pending garbage after global completion");
        Check(player.GetComponent<GarbageCombat>().PendingLines == 0,
            "The eliminated player retained pending garbage after completion");
        Check(Get<GameObject>(flow, "resultsRoot").activeInHierarchy &&
            Get<TMP_Text>(flow, "resultsStatus").text == "FINAL STANDINGS" &&
            Get<TMP_Text>(flow, "rankText").text.Contains("99"), "The saved final standings UI is not live");
        Check(new HashSet<BotPlayer>(arena.GetComponentsInChildren<BotPlayer>(true)).SetEquals(bots),
            "The soak replaced or generated an opponent board");
        observing = false;
        RestoreTimeScale();
        Debug.Log("FULL_MATCH_SOAK_PASSED " + Status() + " attributedKOs=" + knockouts);
        EditorApplication.Exit(0);
    }

    private void LateUpdate()
    {
        if (!observing || failed) return;
        try { ObserveFrame(); }
        catch (Exception exception) { Fail(exception); }
    }

    private void ObserveFrame()
    {
        for (int i = 0; i < bots.Length; i++)
        {
            int count = bots[i].PiecesPlaced;
            int added = count - observedPieces[i];
            Check(added >= 0 && added <= 1, "A bot reset or planned twice between consecutive LateUpdate observations");
            if (added == 1)
            {
                int frame = Get<int>(bots[i], "lastPlannedFrame");
                Check(frame >= 0 && frame <= Time.frameCount, "A real bot placement has no valid scheduler frame");
                int framePlans = placementsByFrame.TryGetValue(frame, out int previous) ? previous + 1 : 1;
                placementsByFrame[frame] = framePlans;
                maximumFramePlans = Mathf.Max(maximumFramePlans, framePlans);
                Check(framePlans <= BotPlayer.MaxPlansPerFrame,
                    "Scheduler exceeded its global budget at actual planning frame " + frame);
                placements++;
                observedPieces[i] = count;
            }
            int pending = combats[i].PendingLines;
            Check(pending >= 0 && pending <= GarbageCombat.PendingLimit, "A bot's pending garbage exceeded the twelve-line cap");
            maximumPending = Mathf.Max(maximumPending, pending);
        }
        int scheduledFrame = (int)typeof(BotPlayer).GetField("planningFrame", StaticFields).GetValue(null);
        int scheduledPlans = (int)typeof(BotPlayer).GetField("plansThisFrame", StaticFields).GetValue(null);
        if (scheduledFrame == Time.frameCount)
            Check(scheduledPlans <= BotPlayer.MaxPlansPerFrame, "The scheduler counted more than three searches in one frame");
        if (forcedPlayerDeath) Check(player.IsGameOver && arena.PlayerRank == 99, "The spectator was revived or reranked");
        Check(placements <= PlacementLimit, "Natural match exceeded its placement limit. " + Status());
    }

    private void AttackDelivered(GarbageCombat sender, GarbageCombat recipient, int lines)
    {
        if (failed) return;
        try
        {
            Check(sender != null && recipient != null && sender != recipient && lines > 0 &&
                recipient.PendingLines <= GarbageCombat.PendingLimit, "An actual accepted attack violates recipient or queue limits");
            deliveredAttacks++;
            deliveredLines += lines;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void ParticipantEliminated(MatchStanding standing)
    {
        if (failed) return;
        try
        {
            Check(standing != null && standing.Rank >= 2 && standing.Rank <= 99 && eliminationRanks.Add(standing.Rank),
                "An elimination event has a missing, duplicate, or invalid final rank");
            participantEliminations++;
            if (!standing.IsPlayer) botEliminations++;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private string Status()
        => $"elapsed={Time.realtimeSinceStartup - startedAt:F2}s placements={placements} " +
            $"attacks={deliveredAttacks} acceptedLines={deliveredLines} botEliminations={botEliminations} " +
            $"alive={(arena != null ? arena.AliveOpponents : -1)} phase={(flow != null ? flow.Phase.ToString() : "missing")} " +
            $"playerRank={(arena != null ? arena.PlayerRank : -1)} maxPending={maximumPending} maxFramePlans={maximumFramePlans}";

    private void Fail(Exception exception)
    {
        if (failed) return;
        failed = true;
        observing = false;
        RestoreTimeScale();
        Debug.LogError("FULL_MATCH_SOAK_FAILED " + Status());
        Debug.LogException(exception);
        EditorApplication.Exit(1);
    }

    private void RestoreTimeScale()
    {
        if (!changedTimeScale) return;
        Time.timeScale = previousTimeScale;
        changedTimeScale = false;
    }

    private void OnDisable()
    {
        RestoreTimeScale();
        if (arena == null) return;
        arena.AttackDelivered -= AttackDelivered;
        arena.ParticipantEliminated -= ParticipantEliminated;
    }
}
#endif
