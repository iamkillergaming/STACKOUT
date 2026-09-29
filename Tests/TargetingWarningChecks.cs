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

// Run only in an isolated copy containing the final authored SampleScene and prefabs.
public sealed class TargetingWarningChecks : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ScreenshotPath = "/tmp/tetris-targeting-warning.png";
    private const string OneOnOneScreenshotPath = "/tmp/tetris-targeting-warning-1v1.png";
    [SerializeField] private FallingPiece player;
    [SerializeField] private Board playerBoard;
    [SerializeField] private BoardPresentation presentation;
    [SerializeField] private BattleArena arena;
    [SerializeField] private BotPlayer first;
    [SerializeField] private BotPlayer second;
    [SerializeField] private BotPlayer third;
    [SerializeField] private GameObject authoredWarning;
    [SerializeField] private SpriteRenderer authoredWarningFrame;
    [SerializeField] private SpriteRenderer[] authoredLines;
    [SerializeField] private SpriteRenderer[] authoredCells;
    [SerializeField] private SpriteRenderer[] authoredFrames;
    [SerializeField] private TMP_Text[] authoredLabels;
    [SerializeField] private AudioSource authoredTargetingSource;
    [SerializeField] private AudioClip authoredTargetingClip;
    [SerializeField] private Color savedBackground;
    [SerializeField] private CameraClearFlags savedClearFlags;
    private Camera view;
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    private float cameraSize;
    private float cameraAspect;
    private AudioClip targetingProbe;

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            FallingPiece player = Object.FindFirstObjectByType<FallingPiece>();
            BattleArena arena = Object.FindFirstObjectByType<BattleArena>();
            Check(player != null && arena != null, "Missing authored player or battle arena");
            BotPlayer[] existing = arena.GetComponentsInChildren<BotPlayer>(true);
            Check(existing.Length == 1, "This fixture expects the saved one-opponent SampleScene");
            Transform slots = Get<Transform>(arena, "opponentGridRoot");
            Check(slots != null && slots.childCount == 98, "The authored 98-slot opponent grid is missing");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BotOpponent.prefab");
            Check(prefab != null, "The authored BotOpponent prefab is missing");
            var fixture = new GameObject("Targeting warning checks").AddComponent<TargetingWarningChecks>();
            fixture.player = player;
            fixture.playerBoard = player.GetComponent<Board>();
            fixture.presentation = player.GetComponent<BoardPresentation>();
            fixture.arena = arena;
            fixture.first = existing[0];
            fixture.second = AddOpponent(prefab, slots.GetChild(49), "BOT 02");
            fixture.third = AddOpponent(prefab, slots.GetChild(97), "BOT 03");
            // Inactive saved instances allow a genuine 1v1 startup, then registration during the checks.
            fixture.second.gameObject.SetActive(false);
            fixture.third.gameObject.SetActive(false);
            foreach (BotPlayer bot in new[] { fixture.first, fixture.second, fixture.third })
                Set(bot, "secondsPerPiece", 10000f);
            // Keep the player enabled: the warning intentionally hides when its controller is disabled.
            Set(player, "fallInterval", 10000f);
            fixture.authoredWarning = Get<GameObject>(fixture.presentation, "danger");
            fixture.authoredWarningFrame = Get<SpriteRenderer>(fixture.presentation, "warningFrame");
            fixture.authoredLines = Get<SpriteRenderer[]>(fixture.presentation, "targetingLines");
            fixture.authoredTargetingSource = Get<AudioSource>(fixture.presentation, "targetingSoundSource");
            fixture.authoredTargetingClip = Get<AudioClip>(fixture.presentation, "targetingSound");
            AudioClip expectedCue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/General Sounds/se_game_target.wav");
            Check(fixture.authoredTargetingSource != null && fixture.authoredTargetingSource.isActiveAndEnabled &&
                !fixture.authoredTargetingSource.playOnAwake && fixture.authoredTargetingSource.spatialBlend == 0f,
                "The saved dedicated 2D targeting AudioSource is missing or configured to play automatically");
            Check(expectedCue != null && fixture.authoredTargetingClip == expectedCue,
                "The saved targeting sound must reference se_game_target.wav before any test substitution");
            Check(fixture.authoredWarning != null && fixture.authoredWarning.name == "Targeting warning",
                "The saved warning was not renamed to Targeting warning");
            Check(fixture.authoredWarningFrame != null && fixture.authoredWarningFrame.sprite != null,
                "The saved warning frame sprite is missing");
            int segmentCount = 98 * BoardPresentation.TargetingSegmentsPerOpponent;
            Check(BoardPresentation.TargetingSegmentsPerOpponent == 4 && fixture.authoredLines != null &&
                fixture.authoredLines.Length == segmentCount &&
                new HashSet<SpriteRenderer>(fixture.authoredLines).Count == segmentCount,
                "Expected 392 distinct saved targeting segments, four for each opponent");
            foreach (SpriteRenderer line in fixture.authoredLines)
                Check(line != null && line.sprite != null && line.transform.IsChildOf(fixture.playerBoard.transform),
                    "A targeting line is not an authored sprite below the board");
            var cells = new List<SpriteRenderer>();
            foreach (Board board in new[] { fixture.playerBoard, fixture.first.GetComponent<Board>(),
                fixture.second.GetComponent<Board>(), fixture.third.GetComponent<Board>() })
                cells.AddRange(Get<SpriteRenderer[]>(board, "boardCells"));
            fixture.authoredCells = cells.ToArray();
            fixture.authoredFrames = new[]
            {
                Get<SpriteRenderer>(fixture.presentation, "boardSprite"), fixture.authoredWarningFrame,
                Get<SpriteRenderer>(fixture.first, "targetFrame"), Get<SpriteRenderer>(fixture.second, "targetFrame"),
                Get<SpriteRenderer>(fixture.third, "targetFrame")
            };
            fixture.authoredLabels = fixture.playerBoard.GetComponentsInChildren<TMP_Text>(true);
            fixture.savedBackground = Camera.main.backgroundColor;
            fixture.savedClearFlags = Camera.main.clearFlags;
            // The supplied scene/prefabs remain untouched; only this isolated fixture scene is saved.
            EditorSceneManager.SaveScene(player.gameObject.scene, "Assets/TargetingWarningChecks.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static BotPlayer AddOpponent(GameObject prefab, Transform slot, string label)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot);
        instance.name = label + " (warning fixture)";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = new Vector3(0.13f, 0.13f, 1f);
        BotPlayer bot = instance.GetComponent<BotPlayer>();
        Get<TMP_Text>(bot, "nameLabel").text = label;
        return bot;
    }
    private static void Check(bool condition, string message)
    {
        if (condition) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new InvalidOperationException(message);
    }
    private static T Get<T>(object target, string field)
        => (T)target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value)
        => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Call(object target, string method, params object[] values)
        => target.GetType().GetMethod(method, Private).Invoke(target, values);
    private void Refresh() => Call(presentation, "LateUpdate");
    private void RefreshTargets() { Call(arena, "RefreshTargeting"); Refresh(); }
    private GarbageCombat Combat(BotPlayer bot) => bot.GetComponent<GarbageCombat>();

    private void SetTargets(GarbageCombat firstTarget, GarbageCombat secondTarget, GarbageCombat thirdTarget)
    {
        var targets = Get<Dictionary<GarbageCombat, GarbageCombat>>(arena, "botTargets");
        targets.Clear();
        if (first.isActiveAndEnabled && !first.IsGameOver && firstTarget != null) targets[Combat(first)] = firstTarget;
        if (second.isActiveAndEnabled && !second.IsGameOver && secondTarget != null) targets[Combat(second)] = secondTarget;
        if (third.isActiveAndEnabled && !third.IsGameOver && thirdTarget != null) targets[Combat(third)] = thirdTarget;
        RefreshTargets();
    }
    private void TargetPlayerWithAll()
    {
        GarbageCombat target = player.GetComponent<GarbageCombat>();
        SetTargets(target, target, target);
    }
    private void ForceNewIncomingCue()
    {
        GarbageCombat playerCombat = player.GetComponent<GarbageCombat>();
        SetTargets(Combat(second), playerCombat, playerCombat);
        authoredTargetingSource.Stop();
        TargetPlayerWithAll();
        Check(authoredTargetingSource.isPlaying, "An attacker leaving and rejoining did not play the targeting cue");
    }
    private void CheckTargetingSoundRules()
    {
        GarbageCombat playerCombat = player.GetComponent<GarbageCombat>();
        SetTargets(Combat(second), Combat(third), Combat(first));
        authoredTargetingSource.Stop();
        RefreshTargets();
        Check(!authoredTargetingSource.isPlaying, "A refresh without incoming attackers played a targeting cue");

        SetTargets(playerCombat, Combat(third), Combat(first));
        Check(authoredTargetingSource.isPlaying, "A new live attacker did not play the targeting cue");
        authoredTargetingSource.Stop();
        RefreshTargets();
        presentation.SetAttackers(new[] { first, first });
        Check(!authoredTargetingSource.isPlaying, "The unchanged or duplicated attacker set replayed the targeting cue");
        arena.SetTarget(second);
        presentation.ShowAttackRoute(second.GetComponent<Board>(), .2f);
        Check(!authoredTargetingSource.isPlaying, "An outgoing selection or attack route replayed the incoming targeting cue");
        presentation.ClearAttackRoutes();
        CheckWarning(first);

        // Multiple new attackers arrive in one supplied set. Subsequent reordered/duplicate sets must stay silent.
        TargetPlayerWithAll();
        Check(authoredTargetingSource.isPlaying, "A batched addition of incoming attackers did not play the targeting cue");
        authoredTargetingSource.Stop();
        presentation.SetAttackers(new[] { third, first, second, second, null });
        RefreshTargets();
        Check(!authoredTargetingSource.isPlaying, "Reordering the same live attackers replayed the targeting cue");
        SetTargets(Combat(second), playerCombat, playerCombat);
        Check(!authoredTargetingSource.isPlaying, "Removing an attacker played the targeting cue");
        TargetPlayerWithAll();
        Check(authoredTargetingSource.isPlaying, "A returning attacker did not play a fresh targeting cue");
        SetTargets(Combat(second), Combat(third), Combat(first));
        Check(!authoredTargetingSource.isPlaying, "Removing the final incoming attacker did not stop the targeting cue");
    }
    private void CaptureCamera()
    {
        view = Camera.main;
        Check(view != null && view.orthographic, "The authored orthographic camera is missing");
        cameraPosition = view.transform.position;
        cameraRotation = view.transform.rotation;
        cameraSize = view.orthographicSize;
        cameraAspect = view.aspect;
        CheckCamera();
    }
    private void CheckCamera()
    {
        Check(view.backgroundColor == savedBackground && view.clearFlags == savedClearFlags,
            "Targeting changed the user's background or camera clear settings");
        Check(Vector3.Distance(view.transform.position, cameraPosition) < .0001f &&
            Quaternion.Angle(view.transform.rotation, cameraRotation) < .001f &&
            Mathf.Abs(view.orthographicSize - cameraSize) < .0001f && Mathf.Abs(view.aspect - cameraAspect) < .0001f,
            "Targeting changed the camera position, rotation, zoom, or aspect");
    }
    private void CheckAuthoredObjects()
    {
        Check(Get<GameObject>(presentation, "danger") == authoredWarning &&
            Get<SpriteRenderer>(presentation, "warningFrame") == authoredWarningFrame,
            "The authored warning or its frame was replaced");
        Check(Get<AudioSource>(presentation, "targetingSoundSource") == authoredTargetingSource,
            "The authored targeting AudioSource was recreated or replaced");
        SpriteRenderer[] lines = Get<SpriteRenderer[]>(presentation, "targetingLines");
        Check(lines.Length == authoredLines.Length, "The saved targeting-line pool changed size");
        for (int i = 0; i < lines.Length; i++)
            Check(lines[i] != null && lines[i] == authoredLines[i] && lines[i].sprite != null,
                "A targeting-line object was recreated during play or restart");
        var cells = new List<SpriteRenderer>();
        foreach (Board board in new[] { playerBoard, first.GetComponent<Board>(), second.GetComponent<Board>(), third.GetComponent<Board>() })
            cells.AddRange(Get<SpriteRenderer[]>(board, "boardCells"));
        Check(cells.Count == authoredCells.Length, "The authored board-cell pool changed size");
        for (int i = 0; i < cells.Count; i++) Check(cells[i] != null && cells[i] == authoredCells[i], "A saved board cell was recreated");
        SpriteRenderer[] frames =
        {
            Get<SpriteRenderer>(presentation, "boardSprite"), Get<SpriteRenderer>(presentation, "warningFrame"),
            Get<SpriteRenderer>(first, "targetFrame"), Get<SpriteRenderer>(second, "targetFrame"), Get<SpriteRenderer>(third, "targetFrame")
        };
        for (int i = 0; i < frames.Length; i++) Check(frames[i] != null && frames[i] == authoredFrames[i], "A saved frame was replaced");
        Check(new HashSet<TMP_Text>(playerBoard.GetComponentsInChildren<TMP_Text>(true)).SetEquals(authoredLabels),
            "Editable TMP labels were recreated during play or restart");
    }

    private void CheckWarning(params BotPlayer[] expected)
    {
        Refresh();
        Check(authoredWarning.activeSelf == (expected.Length > 0) &&
            authoredWarning.activeInHierarchy == (expected.Length > 0), "WATCH OUT visibility does not match live attackers");
        int segmentsPerOpponent = BoardPresentation.TargetingSegmentsPerOpponent;
        int activeGroups = 0;
        var matched = new HashSet<BotPlayer>();
        for (int group = 0; group < authoredLines.Length; group += segmentsPerOpponent)
        {
            int enabled = 0;
            bool reachedUnusedSegment = false;
            for (int segment = 0; segment < segmentsPerOpponent; segment++)
            {
                if (!authoredLines[group + segment].enabled) reachedUnusedSegment = true;
                else
                {
                    Check(!reachedUnusedSegment, "A targeting route retained an enabled segment after its unused suffix");
                    enabled++;
                }
            }
            if (enabled == 0) continue;
            activeGroups++;
            var points = new Vector3[enabled + 1];
            for (int segment = 0; segment < enabled; segment++)
            {
                SpriteRenderer line = authoredLines[group + segment];
                Check(line.gameObject.activeInHierarchy, "An enabled targeting segment is below an inactive object");
                Color color = line.color;
                Check(color.r > .7f && color.g > .5f && color.b < .65f && color.a > .1f,
                    "A targeting segment is not visible yellow");
                SegmentEndpoints(line, out Vector3 start, out Vector3 end);
                bool reverse = segment == 0 ? !NearWarningEdge(start) && NearWarningEdge(end)
                    : Vector3.Distance(points[segment], end) < Vector3.Distance(points[segment], start);
                if (reverse) { Vector3 swap = start; start = end; end = swap; }
                if (segment == 0)
                {
                    Check(NearWarningEdge(start), "The routed line does not start at the warning edge");
                    points[0] = start;
                }
                else Check(Vector3.Distance(points[segment], start) < .035f,
                    "Consecutive targeting segments do not meet");
                points[segment + 1] = end;
                CheckBoardClearance(start, end);
            }
            bool found = false;
            foreach (BotPlayer bot in expected)
            {
                if (matched.Contains(bot) || !NearBoardEdge(points[enabled], bot)) continue;
                CheckWarningAttachmentSide(points[0], bot);
                matched.Add(bot);
                found = true;
                break;
            }
            Check(found, "A routed yellow line does not finish at the correct attacker-board edge");
        }
        Check(activeGroups == expected.Length,
            $"Expected {expected.Length} complete attacker routes, found {activeGroups}");
        Check(matched.Count == expected.Length, "Two targeting routes point to the same attacker");
        CheckCamera();
        CheckAuthoredObjects();
    }
    private static void SegmentEndpoints(SpriteRenderer line, out Vector3 start, out Vector3 end)
    {
        Bounds bounds = line.localBounds;
        Vector3 center = line.transform.TransformPoint(bounds.center);
        Vector3 horizontal = line.transform.TransformVector(Vector3.right * bounds.extents.x);
        Vector3 vertical = line.transform.TransformVector(Vector3.up * bounds.extents.y);
        Vector3 halfLength = horizontal.sqrMagnitude >= vertical.sqrMagnitude ? horizontal : vertical;
        start = center - halfLength;
        end = center + halfLength;
    }
    private void CheckBoardClearance(Vector3 worldStart, Vector3 worldEnd)
    {
        Vector2 start = playerBoard.transform.InverseTransformPoint(worldStart);
        Vector2 end = playerBoard.transform.InverseTransformPoint(worldEnd);
        float cell = playerBoard.CellSize;
        // Include the visible stroke's half-width, so a centerline just outside the grid is not enough.
        float clearance = Get<float>(presentation, "targetingLineWidth") * cell * .5f + .01f * cell;
        Rect playfield = Inflate(new Rect(-Board.Width * cell * .5f, -Board.Height * cell * .5f,
            Board.Width * cell, Board.Height * cell), clearance);
        Check(!Intersects(start, end, playfield, out _, out _),
            $"A targeting segment crosses the main playfield: {start} -> {end}");
    }
    private void CheckDecorativeAreaShortcut()
    {
        // This authored 1v1 layout has a shorter safe route through the decorative area left of the grid.
        // Entering that area is allowed; the previous full-cabinet obstacle incorrectly prevented it.
        Rect cabinetInterior = Inflate(SpriteRectInBoard(Get<SpriteRenderer>(presentation, "boardSprite")),
            -.04f * playerBoard.CellSize);
        Rect warning = Inflate(SpriteRectInBoard(authoredWarningFrame), .04f * playerBoard.CellSize);
        bool crossedDecoration = false;
        foreach (SpriteRenderer line in authoredLines)
        {
            if (!line.enabled) continue;
            SegmentEndpoints(line, out Vector3 worldStart, out Vector3 worldEnd);
            Vector2 start = playerBoard.transform.InverseTransformPoint(worldStart);
            Vector2 end = playerBoard.transform.InverseTransformPoint(worldEnd);
            if (!Intersects(start, end, cabinetInterior, out float entry, out float exit) || exit - entry < .001f) continue;
            Vector2 midpoint = Vector2.Lerp(start, end, (entry + exit) * .5f);
            if (!warning.Contains(midpoint)) crossedDecoration = true;
        }
        Check(crossedDecoration,
            "The 1v1 route still detours around all cabinet artwork instead of using the decorative area outside the playfield");
    }
    private Rect SpriteRectInBoard(SpriteRenderer renderer)
    {
        Bounds bounds = renderer.sprite.bounds;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
        {
            Vector2 point = playerBoard.transform.InverseTransformPoint(renderer.transform.TransformPoint(
                bounds.center + new Vector3(bounds.extents.x * x, bounds.extents.y * y, 0f)));
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static Rect Inflate(Rect rectangle, float margin)
        => Rect.MinMaxRect(rectangle.xMin - margin, rectangle.yMin - margin,
            rectangle.xMax + margin, rectangle.yMax + margin);
    private static bool Intersects(Vector2 start, Vector2 end, Rect rectangle, out float entry, out float exit)
    {
        entry = 0f;
        exit = 1f;
        Vector2 direction = end - start;
        return Clip(-direction.x, start.x - rectangle.xMin, ref entry, ref exit) &&
            Clip(direction.x, rectangle.xMax - start.x, ref entry, ref exit) &&
            Clip(-direction.y, start.y - rectangle.yMin, ref entry, ref exit) &&
            Clip(direction.y, rectangle.yMax - start.y, ref entry, ref exit);
    }
    private static bool Clip(float direction, float distance, ref float entry, ref float exit)
    {
        if (Mathf.Abs(direction) < .000001f) return distance >= 0f;
        float crossing = distance / direction;
        if (direction < 0f) entry = Mathf.Max(entry, crossing);
        else exit = Mathf.Min(exit, crossing);
        return entry <= exit;
    }
    private bool NearWarningEdge(Vector3 point)
    {
        Bounds bounds = authoredWarningFrame.sprite.bounds;
        Transform frame = authoredWarningFrame.transform;
        Vector3 local = frame.InverseTransformPoint(point);
        float radius = bounds.extents.y;
        float halfSpine = Mathf.Max(0f, bounds.extents.x - radius);
        Vector3 nearestSpine = new Vector3(Mathf.Clamp(local.x, bounds.center.x - halfSpine,
            bounds.center.x + halfSpine), bounds.center.y, bounds.center.z);
        Vector3 outward = local - nearestSpine;
        outward.z = 0f;
        if (outward.sqrMagnitude < .000001f) return false;
        Vector3 boundary = nearestSpine + outward.normalized * radius;
        return Vector3.Distance(frame.TransformPoint(boundary), point) < .07f;
    }
    private void CheckWarningAttachmentSide(Vector3 point, BotPlayer attacker)
    {
        Bounds bounds = authoredWarningFrame.sprite.bounds;
        Transform frame = authoredWarningFrame.transform;
        Vector3 localStart = frame.InverseTransformPoint(point);
        Vector3 localAttacker = frame.InverseTransformPoint(attacker.transform.position);
        float halfSpine = Mathf.Max(0f, bounds.extents.x - bounds.extents.y);
        float tolerance = .07f / Mathf.Max(.0001f, frame.TransformVector(Vector3.right).magnitude);
        if (localAttacker.x < bounds.min.x)
            Check(localStart.x <= bounds.center.x - halfSpine + tolerance,
                "A left-side attacker did not attach to the warning's rounded left end");
        else if (localAttacker.x > bounds.max.x)
            Check(localStart.x >= bounds.center.x + halfSpine - tolerance,
                "A right-side attacker did not attach to the warning's rounded right end");
    }
    private static bool NearBoardEdge(Vector3 point, BotPlayer bot)
    {
        float cell = bot.GetComponent<Board>().CellSize;
        return NearEdge(point, bot.transform,
            new Rect(-Board.Width * cell * .5f, -Board.Height * cell * .5f, Board.Width * cell, Board.Height * cell), .035f);
    }
    private static bool NearEdge(Vector3 point, Transform transform, Rect rectangle, float worldTolerance)
    {
        Vector3 local = transform.InverseTransformPoint(point);
        float scaleX = transform.TransformVector(Vector3.right).magnitude;
        float scaleY = transform.TransformVector(Vector3.up).magnitude;
        if (scaleX < .00001f || scaleY < .00001f) return false;
        float toleranceX = worldTolerance / scaleX;
        float toleranceY = worldTolerance / scaleY;
        if (local.x < rectangle.xMin - toleranceX || local.x > rectangle.xMax + toleranceX ||
            local.y < rectangle.yMin - toleranceY || local.y > rectangle.yMax + toleranceY) return false;
        float edgeDistance = Mathf.Min(Mathf.Min(Mathf.Abs(local.x - rectangle.xMin), Mathf.Abs(local.x - rectangle.xMax)) * scaleX,
            Mathf.Min(Mathf.Abs(local.y - rectangle.yMin), Mathf.Abs(local.y - rectangle.yMax)) * scaleY);
        return edgeDistance <= worldTolerance;
    }

    private IEnumerator Start()
    {
        IEnumerator checks = RunChecks();
        while (true)
        {
            bool hasNext;
            object next;
            try
            {
                hasNext = checks.MoveNext();
                next = hasNext ? checks.Current : null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                hasNext = false;
                next = null;
            }
            if (!hasNext) yield break;
            yield return next;
        }
    }

    private IEnumerator RunChecks()
    {
        yield return null;
        yield return null;
        Check(player.enabled && !player.IsGameOver && !player.BattlePaused, "The authored player failed at startup");
        first.Paused = true;
        Camera.main.aspect = 16f / 9f;
        playerBoard.FitCamera();
        CaptureCamera();
        Check(Get<AudioClip>(presentation, "targetingSound") == authoredTargetingClip,
            "Runtime replaced the saved targeting sound before the audio probe was installed");
        targetingProbe = AudioClip.Create("Targeting cue lifecycle probe", 44100 * 10, 1, 44100, false);
        Check(targetingProbe != null, "Could not create the silent long targeting-cue probe");
        authoredTargetingSource.Stop();
        Set(presentation, "targetingSound", targetingProbe);
        GarbageCombat playerCombat = player.GetComponent<GarbageCombat>();
        Check(arena.AliveOpponents == 1 && playerBoard.IsEmpty, "The first fixture is not a real empty-board 1v1");
        SetTargets(playerCombat, null, null);
        CheckWarning(first); // A bot is targeting us before either board has any locked blocks or garbage.
        CheckDecorativeAreaShortcut();

        second.gameObject.SetActive(true);
        third.gameObject.SetActive(true);
        second.Paused = true;
        third.Paused = true;
        yield return null;
        Check(arena.AliveOpponents == 3, "The saved extra bot instances did not register");
        CheckTargetingSoundRules();
        // These are valid enemy targets, so RefreshTargeting must retain them instead of choosing randomly.
        SetTargets(Combat(second), Combat(third), Combat(first));
        CheckWarning();
        arena.SetTarget(first);
        Check(arena.Target == first, "Outgoing target selection failed");
        CheckWarning(); // Selecting an opponent is not evidence that the opponent targets us.
        Sprite cellSprite = Get<Sprite>(first, "blockSprite");
        Check(playerBoard.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, Board.Height - 2),
            Color.white, new[] { cellSprite }), "Cannot seed the high-stack fixture");
        CheckWarning(); // A high stack alone must never enable WATCH OUT.
        Check(!authoredTargetingSource.isPlaying, "A high stack played the incoming targeting cue");
        playerCombat.ReceiveGarbage(2, Combat(first), 3);
        CheckWarning(); // An old incoming packet is not the same as a current incoming target.
        Check(!authoredTargetingSource.isPlaying, "Pending garbage without a current attacker played the targeting cue");
        playerCombat.ClearPending();
        playerBoard.ClearBoard();

        SetTargets(playerCombat, playerCombat, Combat(first));
        CheckWarning(first, second);
        TargetPlayerWithAll();
        CheckWarning(first, second, third);
        Vector3 originalThirdPosition = third.transform.position;
        Transform slots = Get<Transform>(arena, "opponentGridRoot");
        // Use existing top/bottom positions on both sides; targeting must never reframe the camera.
        foreach (int slot in new[] { 1, 48, 50, 97 })
        {
            third.transform.position = slots.GetChild(slot).position;
            CheckWarning(first, second, third);
        }
        third.transform.position = originalThirdPosition;
        CheckWarning(first, second, third);

        // With no cabinet in the way, use one direct segment and hide the rest of the saved group.
        float bottom = Mathf.Min(SpriteRectInBoard(Get<SpriteRenderer>(presentation, "boardSprite")).yMin,
            SpriteRectInBoard(authoredWarningFrame).yMin);
        third.transform.position = playerBoard.transform.TransformPoint(new Vector3(0f,
            bottom - 3f * playerBoard.CellSize, 0f));
        SetTargets(Combat(second), Combat(first), playerCombat);
        CheckWarning(third);
        int directSegments = 0;
        foreach (SpriteRenderer line in authoredLines) if (line.enabled) directSegments++;
        Check(directSegments == 1, "An unobstructed attacker route did not simplify to one direct segment");
        third.transform.position = originalThirdPosition;
        TargetPlayerWithAll();
        CheckWarning(first, second, third);
        SetTargets(Combat(third), playerCombat, playerCombat);
        CheckWarning(second, third);

        TargetPlayerWithAll();
        Call(second, "Die");
        Check(second.IsGameOver && !arena.IsRoundOver, "A nonfinal bot KO failed");
        CheckWarning(first, third);
        authoredTargetingSource.Stop();
        presentation.SetAttackers(new[] { first, third, second });
        Check(!authoredTargetingSource.isPlaying, "Adding an eliminated bot to the supplied set played a targeting cue");
        RefreshTargets();
        first.enabled = false; // The real BotPlayer.OnDisable unregisters it from the arena.
        Check(arena.AliveOpponents == 1, "Disabled bot was not unregistered");
        CheckWarning(third);
        authoredTargetingSource.Stop();
        presentation.SetAttackers(new[] { third, first });
        Check(!authoredTargetingSource.isPlaying, "Adding a disabled bot to the supplied set played a targeting cue");
        RefreshTargets();
        first.enabled = true;
        first.Paused = true;
        SetTargets(Combat(third), null, playerCombat);
        CheckWarning(third);
        SetTargets(playerCombat, null, playerCombat);
        CheckWarning(first, third);
        Call(first, "Die");
        CheckWarning(third);
        Call(third, "Die");
        Check(arena.IsRoundOver && arena.PlayerWon && player.BattlePaused, "The last bot KO did not finish the round");
        CheckWarning();
        Check(!authoredTargetingSource.isPlaying, "Victory did not stop the targeting cue");
        player.ResetForBattle();
        foreach (BotPlayer bot in new[] { first, second, third }) bot.Paused = true;
        TargetPlayerWithAll();
        Check(!arena.IsRoundOver && !player.IsGameOver && !player.BattlePaused, "Victory restart failed");
        CheckWarning(first, second, third);
        Check(authoredTargetingSource.isPlaying, "The new round after victory did not announce its incoming attackers");

        ForceNewIncomingCue();
        player.BattlePaused = true;
        CheckWarning();
        Check(!authoredTargetingSource.isPlaying, "Pausing did not stop the targeting cue");
        player.BattlePaused = false;
        CheckWarning(first, second, third);
        RefreshTargets();
        Check(!authoredTargetingSource.isPlaying, "Resuming the same incoming set replayed the targeting cue");
        ForceNewIncomingCue();
        player.enabled = false;
        CheckWarning();
        Check(!authoredTargetingSource.isPlaying, "Disabling the player did not stop the targeting cue");
        player.enabled = true;
        CheckWarning(first, second, third);
        RefreshTargets();
        Check(!authoredTargetingSource.isPlaying, "Re-enabling the same player/attacker set replayed the targeting cue");
        ForceNewIncomingCue();
        presentation.enabled = false;
        presentation.SetAttackers(new[] { first, second, third });
        CheckWarning();
        Check(!authoredTargetingSource.isPlaying, "Disabling the presentation did not stop the targeting cue");
        presentation.enabled = true;
        RefreshTargets();
        CheckWarning(first, second, third);
        Check(!authoredTargetingSource.isPlaying, "Re-enabling the same presentation/attacker set replayed the targeting cue");
        ForceNewIncomingCue();
        arena.enabled = false;
        CheckWarning();
        Check(!authoredTargetingSource.isPlaying, "Disabling the arena did not stop the targeting cue");
        arena.enabled = true;
        foreach (BotPlayer bot in new[] { first, second, third }) bot.Paused = true;
        TargetPlayerWithAll();
        CheckWarning(first, second, third);
        Check(authoredTargetingSource.isPlaying, "Restoring the arena's cleared incoming set did not play the targeting cue");

        ForceNewIncomingCue();
        DeathScreen death = player.GetComponent<DeathScreen>();
        Set(death, "rowInterval", .005f);
        Set(death, "rowFlashDuration", .01f);
        Call(player, "EndGame");
        Check(player.IsGameOver && arena.IsRoundOver && !arena.PlayerWon, "The player death fixture failed");
        presentation.SetAttackers(new[] { first, second, third });
        CheckWarning();
        Check(!authoredTargetingSource.isPlaying, "Player death or a refresh during death replayed the targeting cue");
        RefreshTargets(); // The actual finished arena owns an empty incoming set until restart.
        float deadline = Time.realtimeSinceStartup + 3f;
        while (!death.CanRetry && Time.realtimeSinceStartup < deadline) yield return null;
        Check(death.CanRetry, "Death sweep did not complete in time for restart");
        player.RestartGame();
        foreach (BotPlayer bot in new[] { first, second, third }) bot.Paused = true;
        TargetPlayerWithAll();
        Check(!player.IsGameOver && !arena.IsRoundOver, "Defeat restart did not restore the round");
        CheckWarning(first, second, third);
        Check(authoredTargetingSource.isPlaying, "The new round after defeat did not announce its incoming attackers");

        // Leave real placed blocks on each mini board for the screenshot; no simulated display data is used.
        foreach (BotPlayer bot in new[] { first, second, third })
        {
            Set(bot, "clearDelay", 0f);
            for (int piece = 0; piece < 4; piece++)
            {
                Call(bot, "PlaceNextPiece");
                if (Get<bool>(bot, "clearing"))
                {
                    bot.Paused = false;
                    Call(bot, "Update");
                    bot.Paused = true;
                }
            }
            Check(!bot.IsGameOver && bot.PiecesPlaced == 4, "Screenshot miniature did not receive four real placements");
        }
        TargetPlayerWithAll();
        yield return null;
        CheckWarning(first, second, third);
        CaptureScreenshot(ScreenshotPath);
        second.gameObject.SetActive(false);
        third.gameObject.SetActive(false);
        SetTargets(playerCombat, null, null);
        Check(arena.AliveOpponents == 1, "Final capture is not a live 1v1");
        yield return null;
        CheckWarning(first);
        CaptureScreenshot(OneOnOneScreenshotPath);
        CheckCamera();
        CheckAuthoredObjects();
        authoredTargetingSource.Stop();
        Set(presentation, "targetingSound", authoredTargetingClip);
        Destroy(targetingProbe);
        Debug.Log("TARGETING_WARNING_CHECKS_PASSED screenshots=" + ScreenshotPath + "," + OneOnOneScreenshotPath);
        EditorApplication.Exit(0);
    }

    private void CaptureScreenshot(string path)
    {
        Check(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
            "A graphics-enabled Unity run is required for the requested screenshot; omit -nographics");
        RenderTexture previousTarget = view.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        float previousAspect = view.aspect;
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            target.Create();
            foreach (TMP_Text label in authoredLabels) label.ForceMeshUpdate(true);
            view.targetTexture = target;
            view.aspect = 16f / 9f;
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(view, new RenderPipeline.StandardRequest { destination = target });
            else view.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false);
            pixels.Apply(false, false);
            byte[] png = pixels.EncodeToPNG();
            Check(png != null && png.Length > 1024, "The live board screenshot could not be encoded");
            File.WriteAllBytes(path, png);
        }
        finally
        {
            view.targetTexture = previousTarget;
            view.aspect = previousAspect;
            RenderTexture.active = previousActive;
            target.Release();
            Destroy(target);
            Destroy(pixels);
        }
    }
}
#endif
