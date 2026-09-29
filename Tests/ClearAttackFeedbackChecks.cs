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

// Copy into Assets of an isolated project after authoring the final scene and prefabs.
public sealed class ClearAttackFeedbackChecks : MonoBehaviour
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string FontPath = "Assets/font/ajn-aokane.otf/ajn-aokane SDF.asset";
    private const string ScreenshotPath = "/tmp/tetris-feedback.png";
    [SerializeField] private FallingPiece player;
    [SerializeField] private Board board;
    [SerializeField] private BattleArena arena;
    [SerializeField] private ClearAttackFeedback feedback;
    [SerializeField] private BotPlayer first;
    [SerializeField] private BotPlayer second;
    [SerializeField] private SpriteRenderer[] savedStars;
    [SerializeField] private TMP_Text[] savedLabels;
    [SerializeField] private TMP_FontAsset font;
    private GarbageCombat combat;
    private AudioSource calloutSource;
    private AudioSource attackSource;
    private readonly List<ClearResult> reports = new List<ClearResult>();
    private readonly List<string> eventOrder = new List<string>();
    private readonly List<int> attacks = new List<int>();
    private readonly List<int> deliveries = new List<int>();
    private readonly List<GarbageCombat> recipients = new List<GarbageCombat>();

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var check = new GameObject("Clear and attack feedback checks").AddComponent<ClearAttackFeedbackChecks>();
            check.player = Object.FindFirstObjectByType<FallingPiece>();
            check.arena = Object.FindFirstObjectByType<BattleArena>();
            Check(check.player != null && check.arena != null, "Authored player or arena missing");
            check.board = check.player.GetComponent<Board>();
            check.feedback = check.player.GetComponent<ClearAttackFeedback>();
            Check(check.feedback != null, "Player board has no saved ClearAttackFeedback component");
            BotPlayer[] existing = check.arena.GetComponentsInChildren<BotPlayer>(true);
            Check(existing.Length == 1, "Fixture expects the saved one-opponent SampleScene");
            check.first = existing[0];
            Transform slots = Get<Transform>(check.arena, "opponentGridRoot");
            Check(slots != null && slots.childCount == 98, "Authored opponent slots missing");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BotOpponent.prefab");
            Check(prefab != null, "Authored BotOpponent prefab missing");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slots.GetChild(49));
            instance.name = "BOT 02 (feedback fixture)";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = new Vector3(.13f, .13f, 1f);
            check.second = instance.GetComponent<BotPlayer>();
            Get<TMP_Text>(check.second, "nameLabel").text = "BOT 02";
            foreach (BotPlayer bot in new[] { check.first, check.second }) Set(bot, "secondsPerPiece", 10000f);
            Set(check.player, "fallInterval", 10000f);
            check.savedStars = Get<SpriteRenderer[]>(check.feedback, "stars");
            check.savedLabels = check.board.GetComponentsInChildren<TMP_Text>(true);
            check.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Check(check.font != null && TMP_Settings.defaultFontAsset == check.font,
                "The supplied ajn-aokane asset is not the global TMP default");
            check.CheckSavedFeedback(check.feedback, requireBattleContext: true);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                CheckFonts(saved.GetComponentsInChildren<TMP_Text>(true), check.font, path);
                ClearAttackFeedback savedFeedback = saved.GetComponent<ClearAttackFeedback>();
                if (savedFeedback != null) check.CheckSavedFeedback(savedFeedback, requireBattleContext: false);
            }
            CheckFonts(Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                check.font, "authored scene");
            // Save only the temporary fixture in the isolated copy, never SampleScene or a prefab.
            EditorSceneManager.SaveScene(check.player.gameObject.scene, "Assets/ClearAttackFeedbackChecks.unity");
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
    private GarbageCombat Combat(BotPlayer bot) => bot.GetComponent<GarbageCombat>();
    private TMP_Text Label(string name) => Get<TMP_Text>(feedback, name);
    private IList Waves => Get<IList>(feedback, "waves");
    private Array Particles => Get<Array>(feedback, "particles");
    private string Words => (Label("clearText").text + " " + Label("comboText").text + " " + Label("attackText").text).ToUpperInvariant();
    private static bool Visible(TMP_Text label) => label.enabled && label.gameObject.activeInHierarchy && label.color.a > .01f && !string.IsNullOrEmpty(label.text);

    private static void CheckFonts(IEnumerable<TMP_Text> labels, TMP_FontAsset expected, string context)
    {
        foreach (TMP_Text label in labels)
            Check(label.font == expected, context + ": " + label.name + " does not use the supplied ajn-aokane font");
    }
    private void CheckSavedFeedback(ClearAttackFeedback saved, bool requireBattleContext)
    {
        Check(Get<FallingPiece>(saved, "player") != null, "Feedback lacks its saved player reference");
        // The reusable player prefab gets its battle context from the authored scene overrides.
        if (requireBattleContext)
            Check(Get<GarbageCombat>(saved, "combat") != null && Get<BattleArena>(saved, "arena") != null,
                "Scene feedback lacks saved combat or arena references");
        SpriteRenderer[] stars = Get<SpriteRenderer[]>(saved, "stars");
        Check(stars != null && stars.Length == 192 && new HashSet<SpriteRenderer>(stars).Count == 192,
            "Expected 192 distinct authored star renderers");
        foreach (SpriteRenderer star in stars)
            Check(star != null && star.sprite != null && star.transform.IsChildOf(saved.transform),
                "A pooled star is missing its art or is outside the authored board hierarchy");
        foreach (string field in new[] { "clearText", "comboText", "attackText" })
        {
            TMP_Text label = Get<TMP_Text>(saved, field);
            Check(label != null && label.font == font && label.transform.IsChildOf(saved.transform),
                "A callout is missing, uses the wrong font, or is outside the board");
        }
        AudioSource callout = Get<AudioSource>(saved, "calloutSource");
        AudioSource attack = Get<AudioSource>(saved, "attackSource");
        Check(callout != null && attack != null && callout != attack && !callout.playOnAwake && !attack.playOnAwake,
            "Feedback needs two distinct saved audio sources without play-on-awake");
        string[] clips = { "calloutSound", "comboSound", "specialClearSound", "allClearSound", "attackLaunchSound", "attackHitSound" };
        string[] names = { "msg", "count", "special", "perfect", "attack1", "attack2" };
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = Get<AudioClip>(saved, clips[i]);
            string path = clip != null ? AssetDatabase.GetAssetPath(clip) : "";
            Check(path.StartsWith("Assets/General Sounds/", StringComparison.Ordinal) && path.EndsWith(names[i] + ".wav", StringComparison.Ordinal),
                clips[i] + " is not wired to the supplied " + names[i] + " sound");
        }
    }
    private void ClearRecordings()
    {
        reports.Clear(); eventOrder.Clear(); attacks.Clear(); deliveries.Clear(); recipients.Clear();
    }
    private void Fresh(BotPlayer target = null)
    {
        combat.ResetCombat();
        Combat(first).ClearPending();
        Combat(second).ClearPending();
        feedback.ClearFeedback();
        arena.SetTarget(target != null ? target : first);
        combat.SetAttackersCount(0);
        ClearRecordings();
    }
    private ClearResult Resolve(int lines, SpinKind spin = SpinKind.None, bool allClear = false)
    {
        int before = reports.Count;
        combat.SetAttackersCount(0);
        Check(combat.ResolveLock(lines, spin, allClear), "Report fixture unexpectedly topped out");
        Check(reports.Count == before + 1 && eventOrder[eventOrder.Count - 1] == "resolved",
            "A resolved lock must publish exactly one report after attack and delivery events");
        return reports[reports.Count - 1];
    }
    private static void Report(ClearResult actual, int lines, SpinKind spin, int combo, bool b2b,
        bool allClear, int generated, int cancelled, int outgoing)
    {
        Check(actual.ClearedLines == lines && actual.Spin == spin && actual.Combo == combo &&
            actual.BackToBack == b2b && actual.AllClear == allClear && actual.GeneratedLines == generated &&
            actual.CancelledLines == cancelled && actual.OutgoingLines == outgoing,
            $"Unexpected report: clear={actual.ClearedLines}, spin={actual.Spin}, combo={actual.Combo}, B2B={actual.BackToBack}, allClear={actual.AllClear}, generated={actual.GeneratedLines}, cancelled={actual.CancelledLines}, outgoing={actual.OutgoingLines}");
    }
    private void CheckClean(string context)
    {
        Check(feedback.ActiveStarCount == 0 && feedback.ActiveWaveCount == 0, context + " left active attack stars");
        Check(Get<IList>(board.GetComponent<BoardPresentation>(), "outgoingRoutes").Count == 0,
            context + " left a temporary outgoing connection");
        foreach (SpriteRenderer star in savedStars) Check(!star.enabled, context + " left a visible pooled star");
        foreach (string field in new[] { "clearText", "comboText", "attackText" })
            Check(!Visible(Label(field)), context + " left visible callout text");
        Check(!calloutSource.isPlaying && !attackSource.isPlaying, context + " left feedback audio playing");
    }
    private void CheckAuthoredObjects()
    {
        SpriteRenderer[] current = Get<SpriteRenderer[]>(feedback, "stars");
        Check(current.Length == savedStars.Length, "Runtime changed the authored pool size");
        for (int i = 0; i < current.Length; i++) Check(current[i] == savedStars[i], "Runtime recreated a saved star renderer");
        Check(new HashSet<TMP_Text>(board.GetComponentsInChildren<TMP_Text>(true)).SetEquals(savedLabels),
            "Runtime recreated an editable TMP label");
        CheckFonts(savedLabels, font, "runtime board");
    }
    private void CheckWaveGeometry(object wave, BotPlayer recipient, int expectedStars)
    {
        int count = 0;
        Vector3[] expected = new Vector3[5];
        int expectedCount = board.GetComponent<BoardPresentation>().GetTargetingPath(recipient.GetComponent<Board>(), expected);
        Vector3[] path = Get<Vector3[]>(wave, "Path");
        int pathCount = Get<int>(wave, "PathCount");
        Check(pathCount >= 2 && pathCount == expectedCount, "Attack wave has no matching yellow connection path");
        for (int i = 0; i < pathCount; i++)
            Check(Vector3.Distance(path[i], expected[i]) < .001f, "Attack path diverged from the yellow connection");
        foreach (object particle in Particles)
        {
            if (particle == null || !Get<bool>(particle, "Active") || Get<object>(particle, "Wave") != wave) continue;
            count++;
            Check(Vector3.Distance(Get<Vector3>(particle, "Start"), path[0]) < .001f &&
                Vector3.Distance(Get<Vector3>(particle, "End"), path[pathCount - 1]) < .001f,
                "An attack star does not join the warning outline to its recipient along the yellow path");
        }
        Check(count == expectedStars, "Wave has the wrong number of reserved star particles");
        Vector3 endpoint = recipient.transform.InverseTransformPoint(Get<Vector3>(wave, "TargetPosition"));
        float halfWidth = Board.Width * recipient.GetComponent<Board>().CellSize * .5f;
        float halfHeight = Board.Height * recipient.GetComponent<Board>().CellSize * .5f;
        Check(Mathf.Abs(endpoint.x) <= halfWidth + .01f && Mathf.Abs(endpoint.y) <= halfHeight + .01f &&
            (Mathf.Abs(Mathf.Abs(endpoint.x) - halfWidth) < .01f || Mathf.Abs(Mathf.Abs(endpoint.y) - halfHeight) < .01f),
            "A wave's destination is not on its actual recipient's board edge");
    }

    private void CheckFlyingStarsOnLines()
    {
        int checkedStars = 0;
        SpriteRenderer[] lines = Get<SpriteRenderer[]>(board.GetComponent<BoardPresentation>(), "targetingLines");
        foreach (object particle in Particles)
        {
            if (particle == null || !Get<bool>(particle, "Active")) continue;
            object wave = Get<object>(particle, "Wave");
            float age = Get<float>(wave, "Elapsed") - Get<float>(particle, "Delay");
            SpriteRenderer star = Get<SpriteRenderer>(particle, "Renderer");
            if (age < 0f || age >= Get<float>(wave, "FlightDuration") || !star.enabled) continue;
            float nearest = float.PositiveInfinity;
            foreach (SpriteRenderer line in lines)
            {
                if (line == null || !line.enabled) continue;
                Bounds bounds = line.sprite.bounds;
                Vector3 a = line.transform.TransformPoint(bounds.center - Vector3.right * bounds.extents.x);
                Vector3 b = line.transform.TransformPoint(bounds.center + Vector3.right * bounds.extents.x);
                Vector3 position = star.transform.position;
                a.z = b.z = position.z;
                Vector3 delta = b - a;
                float t = delta.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(position - a, delta) / delta.sqrMagnitude) : 0f;
                nearest = Mathf.Min(nearest, Vector3.Distance(position, a + delta * t));
            }
            Check(nearest < .003f, "A flying star is not centered on a visible yellow line");
            checkedStars++;
        }
        Check(checkedStars > 0, "The line-following check had no stars in flight");
    }

    private IEnumerator Start()
    {
        IEnumerator checks = RunChecks();
        while (true)
        {
            bool more;
            object next;
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
        yield return null;
        yield return null;
        Check(player.enabled && !player.IsGameOver && !arena.IsRoundOver && arena.AliveOpponents == 2,
            "The authored player and two fixture opponents did not start");
        first.Paused = second.Paused = true;
        combat = player.GetComponent<GarbageCombat>();
        calloutSource = Get<AudioSource>(feedback, "calloutSource");
        attackSource = Get<AudioSource>(feedback, "attackSource");
        Set(combat, "incomingDelay", 10000f);
        combat.AttackReady += (source, lines) => { Check(source == combat && lines > 0, "Invalid outgoing attack"); attacks.Add(lines); eventOrder.Add("attack"); };
        combat.LockResolved += (source, result) => { Check(source == combat, "Wrong clear-report sender"); reports.Add(result); eventOrder.Add("resolved"); };
        arena.AttackDelivered += (source, recipient, accepted) =>
        {
            if (source != combat) return;
            Check(accepted > 0 && recipient != null && recipient.PendingLines >= accepted,
                "Delivery event must follow actual positive recipient queue acceptance");
            recipients.Add(recipient); deliveries.Add(accepted); eventOrder.Add("delivered");
        };
        CheckClean("Startup");

        // Combat's existing checks cover the full damage formula; this table verifies the new report contract.
        int[] basic = { 0, 1, 2, 4 };
        for (int lines = 1; lines <= 4; lines++)
        {
            Fresh(); Report(Resolve(lines), lines, SpinKind.None, 0, false, false, basic[lines - 1], 0, basic[lines - 1]);
        }
        for (int lines = 1; lines <= 3; lines++)
        {
            Fresh(); Report(Resolve(lines, SpinKind.Full), lines, SpinKind.Full, 0, false, false, lines * 2, 0, lines * 2);
            Check(Words.Contains("T-SPIN"), "Full spin did not produce a T-SPIN callout");
        }
        for (int lines = 1; lines <= 2; lines++)
        {
            Fresh(); Report(Resolve(lines, SpinKind.Mini), lines, SpinKind.Mini, 0, false, false, lines - 1, 0, lines - 1);
            Check(Words.Contains("MINI"), "Mini spin did not produce a MINI callout");
        }
        Fresh(); Report(Resolve(2, SpinKind.None, true), 2, SpinKind.None, 0, false, true, 5, 0, 5);
        Check(Words.Contains("ALL CLEAR"), "All clear did not produce its callout");
        Fresh(); Resolve(4);
        Report(Resolve(0), 0, SpinKind.None, -1, false, false, 0, 0, 0);
        Report(Resolve(4), 4, SpinKind.None, 0, true, false, 5, 0, 5);
        Check(Words.Contains("BACK-TO-BACK"), "Earned back-to-back bonus is missing from the callout");
        Report(Resolve(1), 1, SpinKind.None, 1, false, false, 1, 0, 1);
        Resolve(0);
        Report(Resolve(4), 4, SpinKind.None, 0, false, false, 4, 0, 4);

        Fresh(); Resolve(1);
        Check(feedback.ActiveStarCount == 0 && deliveries.Count == 0 && !Visible(Label("attackText")),
            "A first single with no attack launched stars or outgoing text");
        Report(Resolve(1), 1, SpinKind.None, 1, false, false, 1, 0, 1);
        Check(Words.Contains("COMBO") && Label("comboText").text.Contains("1") && feedback.ActiveStarCount == 4,
            "The second consecutive clear did not show combo 1 and four stars for its one accepted line");

        Fresh();
        Get<Dictionary<GarbageCombat, GarbageCombat>>(arena, "botTargets")[Combat(second)] = combat;
        Combat(second).ResetCombat();
        Check(Combat(second).ResolveLock(4) && combat.PendingLines == 4 && feedback.ActiveStarCount == 0 &&
            reports.Count == 0 && !Visible(Label("clearText")),
            "An opponent's delivered attack produced player clear callouts or outbound stars");

        Fresh(); combat.ReceiveGarbage(3, Combat(second), 2);
        Report(Resolve(4), 4, SpinKind.None, 0, false, false, 4, 3, 1);
        Check(combat.PendingLines == 0 && deliveries.Count == 1 && deliveries[0] == 1 &&
            feedback.ActiveStarCount == 4 && Label("attackText").text.Contains("+1"),
            "Partial cancellation did not report/show only its outgoing surplus");
        Fresh(); combat.ReceiveGarbage(4, Combat(second), 2);
        Report(Resolve(4), 4, SpinKind.None, 0, false, false, 4, 4, 0);
        Check(deliveries.Count == 0 && attacks.Count == 0 && feedback.ActiveStarCount == 0 && !Label("attackText").text.Contains("+"),
            "Fully cancelled attack launched feedback stars or outgoing text");
        Fresh(); Combat(first).ReceiveGarbage(12, Combat(second), 3);
        Resolve(4);
        Check(deliveries.Count == 0 && feedback.ActiveStarCount == 0 && Combat(first).PendingLines == 12,
            "A full recipient queue produced a delivery or attack stars");
        Fresh(); Combat(first).ReceiveGarbage(11, Combat(second), 3);
        Resolve(4);
        Check(deliveries.Count == 1 && deliveries[0] == 1 && feedback.ActiveStarCount == 4 &&
            Combat(first).PendingLines == 12 && Label("attackText").text.Contains("+4"),
            "Partial recipient acceptance must use one line of stars while the callout retains four outgoing lines");

        // A silent one-second clip proves playback and stopping even on hosts without an audible output device.
        AudioClip probe = AudioClip.Create("Feedback playback probe", 44100, 1, 44100, false);
        foreach (string field in new[] { "calloutSound", "comboSound", "specialClearSound", "allClearSound", "attackLaunchSound", "attackHitSound" }) Set(feedback, field, probe);
        calloutSource.volume = attackSource.volume = 0f;
        Fresh(); Resolve(4);
        Check(feedback.ActiveStarCount == 16 && feedback.ActiveWaveCount == 1 && deliveries[0] == 4 &&
            calloutSource.isPlaying && attackSource.isPlaying, "Four delivered lines need sixteen stars and both launch/callout audio");
        object firstWave = Waves[0];
        Check(Get<GarbageCombat>(firstWave, "Target") == Combat(first), "First wave did not snapshot its actual recipient");
        CheckWaveGeometry(firstWave, first, 16);
        Vector3 firstPosition = savedStars[0].transform.position;
        Vector3 firstDestination = Get<Vector3>(firstWave, "TargetPosition");
        arena.SetTarget(second);
        combat.ResetCombat(); combat.SetAttackersCount(0);
        Resolve(2);
        Check(feedback.ActiveStarCount == 20 && feedback.ActiveWaveCount == 2 && recipients[1] == Combat(second),
            "A second recipient's one-line wave replaced or duplicated the first four-line wave");
        Check(Get<GarbageCombat>(firstWave, "Target") == Combat(first) &&
            Get<Vector3>(firstWave, "TargetPosition") == firstDestination &&
            Get<GarbageCombat>(Waves[1], "Target") == Combat(second), "Changing selection redirected an in-flight attack");
        CheckWaveGeometry(Waves[1], second, 4);
        Vector3 originalOpponentPosition = first.transform.position;
        first.transform.position += Vector3.down * 1.5f;
        attackSource.Stop();
        yield return new WaitForSeconds(.25f);
        Check(savedStars[0].enabled && Vector3.Distance(savedStars[0].transform.position, firstPosition) > .05f,
            "An active star did not fly away from its launch point");
        CheckWaveGeometry(firstWave, first, 16);
        Check(Get<GarbageCombat>(firstWave, "Target") == Combat(first) &&
            Vector3.Distance(Get<Vector3>(firstWave, "TargetPosition"), firstDestination) > .1f,
            "An in-flight route did not follow its original recipient when the miniature moved");
        CheckFlyingStarsOnLines();
        first.transform.position = originalOpponentPosition;
        yield return new WaitForSeconds(.36f);
        Check(Get<bool>(firstWave, "HitPlayed") && attackSource.isPlaying, "The actual recipient did not receive its timed hit sound");
        attackSource.Stop();
        yield return new WaitForSeconds(.05f);
        Check(!attackSource.isPlaying, "A completed wave replayed its hit sound on a later frame");
        yield return new WaitForSeconds(1.05f);
        CheckClean("Natural fade");

        Fresh(); Resolve(4);
        object removedRecipientWave = Waves[0];
        arena.SetTarget(second); combat.ResetCombat(); Resolve(2);
        arena.UnregisterBot(first);
        yield return null;
        Check(feedback.ActiveWaveCount == 1 && feedback.ActiveStarCount == 4 &&
            Get<GarbageCombat>(Waves[0], "Target") == Combat(second) && !Get<bool>(removedRecipientWave, "HitPlayed"),
            "Removing a recipient did not cancel only its in-flight wave before impact");
        arena.RegisterBot(first); first.Paused = true;

        // A fully accepted twelve-line packet uses at most forty-eight stars; overlapping packets share the saved pool.
        Fresh();
        for (int wave = 0; wave < 5; wave++)
        {
            combat.ResetCombat(); combat.SetAttackersCount(6); Combat(first).ClearPending();
            Check(combat.ResolveLock(4, SpinKind.None, true), "Pool pressure fixture unexpectedly topped out");
            Check(deliveries[deliveries.Count - 1] == 12 && feedback.ActiveStarCount == Mathf.Min((wave + 1) * 48, 192),
                "Large accepted waves exceeded the static pool or lost proportional particles before the pool filled");
            if (wave == 0) CheckWaveGeometry(Waves[0], first, 48);
        }
        feedback.ClearFeedback(); CheckClean("Pool reset");

        // Outgoing stars need a visible path even when their recipient is not targeting the player.
        Fresh();
        Dictionary<GarbageCombat, GarbageCombat> targets = Get<Dictionary<GarbageCombat, GarbageCombat>>(arena, "botTargets");
        targets[Combat(first)] = Combat(second);
        targets[Combat(second)] = Combat(first);
        Call(arena, "RefreshTargeting");
        yield return null;
        BoardPresentation presentation = board.GetComponent<BoardPresentation>();
        Resolve(2);
        yield return new WaitForSeconds(.2f);
        Check(!Get<GameObject>(presentation, "danger").activeSelf,
            "An outgoing-only attack incorrectly activated WATCH OUT");
        CheckFlyingStarsOnLines();
        feedback.ClearFeedback();
        foreach (SpriteRenderer line in Get<SpriteRenderer[]>(presentation, "targetingLines"))
            Check(!line.enabled, "Clearing an outgoing-only wave left a yellow connection");
        targets[Combat(first)] = combat;
        targets[Combat(second)] = combat;
        Call(arena, "RefreshTargeting");

        // Real locking and glow completion must publish one clear, then launch its feedback.
        Fresh(); Resolve(1); feedback.ClearFeedback(); ClearRecordings();
        foreach (BotPlayer bot in new[] { first, second })
        {
            Set(bot, "clearDelay", 0f);
            Queue<int> bag = Get<Queue<int>>(bot, "bag"); bag.Clear();
            for (int i = 0; i < 4; i++) bag.Enqueue(1); // Four reachable O placements cannot complete a ten-cell row.
            for (int i = 0; i < 4; i++)
            {
                Call(bot, "PlaceNextPiece");
                if (Get<bool>(bot, "clearing")) { bot.Paused = false; Call(bot, "Update"); bot.Paused = true; }
            }
            Check(!bot.IsGameOver && bot.PiecesPlaced == 4 && !bot.GetComponent<Board>().IsEmpty,
                "Screenshot opponent does not contain four real AI placements");
        }
        arena.SetTarget(second); combat.SetAttackersCount(0);
        board.ClearBoard();
        Sprite art = Get<Sprite>(first, "blockSprite");
        for (int y = 0; y < 4; y++) for (int x = 0; x < Board.Width; x++)
            if (x != 4) Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(x, y),
                new Color(.25f, .7f, 1f), new[] { art }), "Unable to seed real four-line clear");
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, 6), Color.cyan, new[] { art }),
            "Unable to retain a block above the real clear");
        Call(player, "ActivatePiece", 0); Call(player, "Rotate", true);
        Vector2Int[] cells = Get<Vector2Int[]>(player, "cells");
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (Vector2Int cell in cells) { minX = Mathf.Min(minX, cell.x); minY = Mathf.Min(minY, cell.y); }
        Set(player, "origin", new Vector2Int(4 - minX, -minY));
        Set(player, "lineClearDuration", .2f);
        Call(player, "Lock", false);
        Check(player.IsClearingLines && reports.Count == 0 && feedback.ActiveStarCount == 0,
            "A real lock published feedback before the clear glow finished");
        float deadline = Time.realtimeSinceStartup + 3f;
        while (player.IsClearingLines && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!player.IsClearingLines && reports.Count == 1 && player.LinesCleared == 4,
            "Real glow completion did not publish exactly one resolved clear");
        Report(reports[0], 4, SpinKind.None, 1, false, false, 5, 0, 5);
        Check(Words.Contains("TETRIS") && Words.Contains("COMBO") && feedback.ActiveStarCount == 20,
            "Real Tetris combo did not display its callout and twenty accepted-line stars");
        Camera.main.aspect = 16f / 9f; board.FitCamera();
        yield return new WaitForSeconds(.25f);
        CheckFlyingStarsOnLines();
        CaptureScreenshot();
        CheckAuthoredObjects();

        // Disabling either owner and all round-ending/restart paths must stop this component's effects.
        feedback.enabled = false; CheckClean("Component disable"); feedback.enabled = true;
        Fresh(); Resolve(4); player.enabled = false;
        yield return null;
        CheckClean("Player disable"); player.enabled = true;
        Fresh(); Resolve(4); arena.enabled = false;
        yield return null;
        CheckClean("Arena disable"); arena.enabled = true;
        first.Paused = second.Paused = true;
        Fresh(); Resolve(4); player.ResetForBattle();
        CheckClean("Restart"); first.Paused = second.Paused = true;
        Fresh(); Resolve(4); Call(first, "Die"); Call(second, "Die");
        yield return null;
        Check(arena.PlayerWon && arena.IsRoundOver, "Victory fixture did not finish the round");
        CheckClean("Victory"); player.ResetForBattle(); first.Paused = second.Paused = true;
        Fresh(); Resolve(4);
        DeathScreen death = player.GetComponent<DeathScreen>();
        Set(death, "rowInterval", .005f); Set(death, "rowFlashDuration", .01f);
        Call(player, "EndGame");
        yield return null;
        CheckClean("Death");
        deadline = Time.realtimeSinceStartup + 3f;
        while (!death.CanRetry && Time.realtimeSinceStartup < deadline) yield return null;
        Check(death.CanRetry, "Death animation did not allow retry");
        player.RestartGame();
        CheckClean("Death retry");
        CheckAuthoredObjects();
        Destroy(probe);
        Debug.Log("CLEAR_ATTACK_FEEDBACK_CHECKS_PASSED screenshot=" + ScreenshotPath);
        EditorApplication.Exit(0);
    }

    private void CaptureScreenshot()
    {
        Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
            "A graphics-enabled Unity run is required; omit -nographics");
        Camera view = Camera.main;
        RenderTexture previousTarget = view.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        float previousAspect = view.aspect;
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
            byte[] png = pixels.EncodeToPNG();
            Check(png != null && png.Length > 1024, "The live feedback screenshot could not be encoded");
            File.WriteAllBytes(ScreenshotPath, png);
        }
        finally
        {
            view.targetTexture = previousTarget; view.aspect = previousAspect; RenderTexture.active = previousActive;
            target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}
#endif
