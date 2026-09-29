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

// Copy into an isolated project's Assets after OriginalSpriteSetup.BatchSetup.
public sealed class SurvivorMilestoneChecks : MonoBehaviour
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [SerializeField] private FallingPiece player;
    [SerializeField] private BattleArena arena;
    [SerializeField] private MatchFlow flow;
    [SerializeField] private SurvivorMilestonePresentation view;
    [SerializeField] private GameObject banner;
    [SerializeField] private TMP_Text label;
    [SerializeField] private AudioSource voice;
    private Board board;
    private Vector3 cameraPosition, boardPosition, boardScale;
    private Quaternion boardRotation;
    private float cameraSize;

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var check = new GameObject("Survivor milestone checks").AddComponent<SurvivorMilestoneChecks>();
            check.player = Object.FindFirstObjectByType<FallingPiece>();
            check.arena = Object.FindFirstObjectByType<BattleArena>();
            check.flow = Object.FindFirstObjectByType<MatchFlow>();
            Check(check.player != null && check.arena != null && check.flow != null, "Saved match is incomplete");
            check.view = check.arena.GetComponent<SurvivorMilestonePresentation>();
            Check(check.view != null && Get<BattleArena>(check.view, "arena") == check.arena
                && Get<FallingPiece>(check.view, "player") == check.player,
                "Milestone presentation is missing its saved arena/player bindings");
            check.banner = Get<GameObject>(check.view, "bannerRoot");
            check.label = Get<TMP_Text>(check.view, "milestoneText");
            check.voice = Get<AudioSource>(check.view, "announcementSource");
            Check(check.banner != null && check.label != null && !check.banner.activeSelf
                && check.label.transform.IsChildOf(check.banner.transform),
                "A saved milestone banner is visible before a match or lacks its saved text");
            Check(AssetDatabase.GetAssetPath(check.label.font) == "Assets/font/ajn-aokane.otf/ajn-aokane SDF.asset",
                "Milestone text must use the supplied AJN Aokane font");
            foreach (TMP_Text text in check.banner.GetComponentsInChildren<TMP_Text>(true))
                Check(text.font == check.label.font, "A milestone shadow or child label uses a different font");
            Board board = check.player.GetComponent<Board>();
            Check((check.label.rectTransform.sizeDelta - new Vector2(8.6f, 1.6f) * board.CellSize).sqrMagnitude < .00001f
                && check.label.GetComponent<Renderer>().sortingOrder >= 40,
                "The milestone label must fit the board width and render above gameplay");
            Vector3 center = board.transform.InverseTransformPoint(check.banner.transform.position);
            Check(Mathf.Abs(center.x) < .001f && Mathf.Abs(center.y) < .001f,
                "The survivor announcement is not centered over the player board");
            CheckClip(check.view, "fiftyPlayersSound", "Assets/General Sounds/me_game_remain_1.wav");
            CheckClip(check.view, "tenPlayersSound", "Assets/General Sounds/me_game_remain_2.wav");
            Check(check.voice != null && !check.voice.playOnAwake && !check.voice.loop && check.voice.spatialBlend == 0f
                && check.voice != Get<AudioSource>(check.flow, "cueSource")
                && check.voice != Get<AudioSource>(check.flow, "knockoutSource")
                && check.voice != Get<AudioSource>(check.player, "soundSource")
                && check.voice != Get<AudioSource>(check.player.GetComponent<DeathScreen>(), "soundSource"),
                "Milestone voices must use their own saved, non-looping 2D audio source");
            Check(Mathf.Approximately(Get<float>(check.view, "duration"), 1.6f),
                "The authored announcement duration changed");
            EditorSceneManager.SaveScene(check.player.gameObject.scene, "Assets/SurvivorMilestoneChecks.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private IEnumerator Start()
    {
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
        board = player.GetComponent<Board>();
        Set(player, "fallInterval", 10000f);
        foreach (BotPlayer bot in Get<BotPlayer[]>(arena, "bots")) Set(bot, "secondsPerPiece", 10000f);
        Set(flow, "minJoinInterval", 10000f); Set(flow, "maxJoinInterval", 10000f); Set(flow, "joinTimer", 10000f);
        Set(flow, "readyDuration", .02f); Set(flow, "countdownInterval", .02f); Set(flow, "goDuration", .02f);
        DeathScreen death = player.GetComponent<DeathScreen>();
        Set(death, "rowInterval", .01f); Set(death, "rowFlashDuration", .05f);
        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            source.volume = 0f;
        yield return null;
        Check(voice != Get<AudioSource>(Get<BackgroundMusic>(flow, "music"), "musicSource"),
            "The milestone voice shares the background music source");
        Camera.main.aspect = 16f / 9f; board.FitCamera();
        cameraPosition = Camera.main.transform.position; cameraSize = Camera.main.orthographicSize;
        boardPosition = board.transform.localPosition; boardScale = board.transform.localScale; boardRotation = board.transform.localRotation;
        CheckHidden("New lobby");

        BotPlayer[] roster = BeginRound(98);
        float deadline = Time.realtimeSinceStartup + 5f;
        while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline) yield return null;
        CheckPlaying(roster);
        var contents = new PlayerSnapshot(player);
        EliminateUntil(roster, 51);
        view.ObserveArena(); CheckHidden("51 survivors");
        EliminateUntil(roster, 50);
        view.ObserveArena(); CheckAnnouncement(50);
        Vector3 firstScale = label.transform.localScale;
        Vector3 firstRootScale = banner.transform.localScale;
        yield return new WaitForSecondsRealtime(.12f);
        Check(view.IsAnimating && view.CurrentThreshold == 50
            && ((label.transform.localScale - firstScale).sqrMagnitude > .0001f
                || (banner.transform.localScale - firstRootScale).sqrMagnitude > .0001f),
            "The survivor text did not stretch/squash during its entrance");
        int voiceSample = voice.timeSamples;
        for (int i = 0; i < 20; i++) view.ObserveArena();
        Check(voice.timeSamples >= voiceSample, "Repeated standings observations restarted the milestone voice");
        Capture("/tmp/tetris-top50-announcement.png");
        contents.CheckUnchanged(player); CheckPoses();
        deadline = Time.realtimeSinceStartup + 3f;
        while (view.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
        CheckHidden("Completed top-50 animation", false);
        for (int i = 0; i < 20; i++) view.ObserveArena();
        CheckHidden("Repeated top-50 observations", false);
        EliminateUntil(roster, 11);
        view.ObserveArena(); CheckHidden("11 survivors", false);
        EliminateUntil(roster, 10);
        view.ObserveArena(); CheckAnnouncement(10);
        yield return new WaitForSecondsRealtime(.12f);
        Capture("/tmp/tetris-top10-announcement.png");
        contents.CheckUnchanged(player); CheckPoses();
        Call(player, "EndGame");
        view.ObserveArena(); CheckHidden("Player eliminated during top-10 animation");
        EliminateUntil(roster, 1);
        view.ObserveArena(); CheckHidden("Opponents eliminated after the player");
        Check(arena.IsRoundOver && !arena.PlayerWon, "The controlled loss did not finish");
        deadline = Time.realtimeSinceStartup + 5f;
        while ((!death.CanRetry || flow.Phase != MatchFlow.MatchPhase.Finished) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(death.CanRetry && flow.Phase == MatchFlow.MatchPhase.Finished, "The normal loss flow did not permit retry");
        flow.BeginMatchmaking(); view.ObserveArena(); CheckLobby();

        // Real eliminations in one frame cross both thresholds while the first banner is still active.
        roster = BeginRound(98);
        deadline = Time.realtimeSinceStartup + 5f;
        while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline) yield return null;
        CheckPlaying(roster);
        contents = new PlayerSnapshot(player);
        EliminateUntil(roster, 10);
        view.ObserveArena(); CheckAnnouncement(50);
        deadline = Time.realtimeSinceStartup + 3f;
        while (view.CurrentThreshold == 50 && Time.realtimeSinceStartup < deadline) yield return null;
        CheckAnnouncement(10);
        contents.CheckUnchanged(player); CheckPoses();
        for (int i = 0; i < 20; i++) view.ObserveArena();
        EliminateUntil(roster, 1);
        view.ObserveArena(); CheckHidden("Victory during queued top-10 animation");
        Check(arena.IsRoundOver && arena.PlayerWon, "The controlled victory did not finish");
        yield return null;
        flow.BeginMatchmaking(); view.ObserveArena(); CheckLobby();

        // A short roster starts below both milestones; loading its participants must not announce either.
        roster = BeginRound(2);
        deadline = Time.realtimeSinceStartup + 5f;
        while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline) yield return null;
        CheckPlaying(roster); view.ObserveArena(); CheckHidden("Three-player round at GO");
        Call(roster[0], "Die"); view.ObserveArena(); CheckHidden("Two survivors from a short roster");
        view.enabled = false; CheckHidden("Disabled presentation");
        view.enabled = true; view.ObserveArena(); CheckHidden("Re-enabled presentation");
        Call(roster[1], "Die"); view.ObserveArena(); CheckHidden("Short-roster victory");
        yield return null;
        flow.BeginMatchmaking(); view.ObserveArena(); CheckLobby();
        Check(Get<GameObject>(view, "bannerRoot") == banner && Get<TMP_Text>(view, "milestoneText") == label
            && Get<AudioSource>(view, "announcementSource") == voice,
            "Round transitions replaced saved presentation objects or the voice source");
        CheckPoses();
        Debug.Log("SURVIVOR_MILESTONE_CHECKS_PASSED top50=once top10=once stretch=animated queue=50-then-10 death-and-win=cancel retry=reset short-roster=silent");
        EditorApplication.Exit(0);
    }

    private BotPlayer[] BeginRound(int botCount)
    {
        CheckLobby(); Set(flow, "joinTimer", 10000f);
        for (int i = 0; i < botCount; i++)
        {
            Check(arena.AdmitNextBot(), "Could not admit a saved opponent");
            view.ObserveArena(); CheckHidden("Lobby admission " + (i + 1));
        }
        var roster = new List<BotPlayer>(arena.Opponents).ToArray();
        Check(roster.Length == botCount && arena.ParticipantCount == botCount + 1, "Unexpected controlled roster size");
        Call(flow, "BeginReady"); view.ObserveArena(); CheckHidden("READY before GO");
        return roster;
    }

    private void CheckPlaying(BotPlayer[] roster)
    {
        Check(flow.Phase == MatchFlow.MatchPhase.Playing && arena.MatchStarted && !arena.IsRoundOver,
            "The normal countdown did not start the controlled round");
        foreach (BotPlayer bot in roster) bot.Paused = true;
        player.BattlePaused = true;
        view.ObserveArena(); CheckHidden("Fresh round before eliminations");
    }

    private void EliminateUntil(BotPlayer[] roster, int survivors)
    {
        foreach (BotPlayer bot in roster)
        {
            if (arena.AliveParticipants <= survivors) break;
            if (!bot.IsGameOver) Call(bot, "Die");
        }
        Check(arena.AliveParticipants == survivors, "Could not reach " + survivors + " real survivors");
    }

    private void CheckAnnouncement(int threshold)
    {
        Check(view.IsAnimating && view.CurrentThreshold == threshold && banner.activeInHierarchy && label.enabled,
            "Missing active top-" + threshold + " announcement");
        Check(label.text.Contains(threshold.ToString()) && label.text.ToUpperInvariant().Contains("PLAYER"),
            "The survivor announcement does not describe its threshold in English");
        Check(voice.isPlaying, "The supplied top-" + threshold + " voice did not start on its dedicated source");
        Check(voice.clip == Get<AudioClip>(view, threshold == 50 ? "fiftyPlayersSound" : "tenPlayersSound"),
            "The milestone played the wrong supplied voice");
    }

    private void CheckHidden(string context, bool requireSilence = true)
    {
        Check(!view.IsAnimating && view.CurrentThreshold == 0 && !banner.activeSelf,
            context + ": an ineligible or completed announcement remains visible");
        if (requireSilence) Check(!voice.isPlaying, context + ": an ineligible milestone voice remains audible");
    }

    private void CheckLobby()
    {
        Check(flow.Phase == MatchFlow.MatchPhase.Matchmaking && arena.ParticipantCount == 1
            && !arena.MatchStarted && !arena.IsRoundOver && !player.IsGameOver, "Retry did not restore a fresh lobby");
        CheckHidden("Fresh lobby");
    }

    private void CheckPoses()
    {
        Check(board.transform.localPosition == boardPosition && board.transform.localScale == boardScale
            && board.transform.localRotation == boardRotation, "The announcement moved or stretched the gameplay board");
        Check((Camera.main.transform.position - cameraPosition).sqrMagnitude < .000001f
            && Mathf.Abs(Camera.main.orthographicSize - cameraSize) < .00001f,
            "Survivor announcements or transitions changed camera framing");
    }

    private sealed class PlayerSnapshot
    {
        private readonly bool[,] occupied;
        private readonly int[] bag;
        public PlayerSnapshot(FallingPiece player)
        {
            occupied = player.GetComponent<Board>().CopyOccupancy();
            bag = Get<Queue<int>>(player, "bag").ToArray();
        }
        public void CheckUnchanged(FallingPiece player)
        {
            Board board = player.GetComponent<Board>();
            for (int y = 0; y < Board.Height; y++) for (int x = 0; x < Board.Width; x++)
                Check(board.IsCellFree(new Vector2Int(x, y)) != occupied[x, y], "An announcement changed the player's board");
            int[] actualBag = Get<Queue<int>>(player, "bag").ToArray();
            Check(actualBag.Length == bag.Length, "An announcement consumed a player piece");
            for (int i = 0; i < bag.Length; i++) Check(actualBag[i] == bag[i], "An announcement changed the player's seven-bag");
        }
    }

    private static void Capture(string path)
    {
        Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Announcement captures require graphics; omit -nographics");
        Camera camera = Camera.main; RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        float oldAspect = camera.aspect;
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            target.Create();
            foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                text.ForceMeshUpdate(true);
            camera.targetTexture = target; camera.aspect = 16f / 9f;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            }
            else camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); pixels.Apply(false, false);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
            target.Release(); Destroy(target); Destroy(pixels);
        }
    }

    private static void CheckClip(object component, string field, string path)
    {
        AudioClip clip = Get<AudioClip>(component, field);
        Check(clip != null && AssetDatabase.GetAssetPath(clip) == path && clip.samples > 0,
            "Incorrect or missing original milestone voice: " + field);
    }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Fields).Invoke(target, null);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
