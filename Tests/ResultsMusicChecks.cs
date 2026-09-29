#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Copy only into an isolated project's Assets, after running MatchFlowSetup/OriginalSpriteSetup.
public sealed class ResultsMusicChecks : MonoBehaviour
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [SerializeField] private FallingPiece player;
    [SerializeField] private BattleArena arena;
    [SerializeField] private MatchFlow flow;
    [SerializeField] private BackgroundMusic music;
    [SerializeField] private DeathScreen death;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource deathSource;
    [SerializeField] private AudioSource victorySource;
    private AudioClip probe;
    private AudioClip originalDeathCue;
    private AudioClip originalVictoryCue;
    private float originalDeathAudibleDuration;
    private float originalVictoryAudibleDuration;

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var check = new GameObject("Results music checks").AddComponent<ResultsMusicChecks>();
            check.player = Object.FindFirstObjectByType<FallingPiece>();
            check.arena = Object.FindFirstObjectByType<BattleArena>();
            check.flow = Object.FindFirstObjectByType<MatchFlow>();
            Check(check.player != null && check.arena != null && check.flow != null, "Saved match is incomplete");
            check.music = Get<BackgroundMusic>(check.flow, "music");
            check.death = check.player.GetComponent<DeathScreen>();
            Check(check.music != null && check.death != null, "Saved music or death presentation is missing");
            check.musicSource = Get<AudioSource>(check.music, "musicSource");
            check.deathSource = Get<AudioSource>(check.flow, "deathSoundSource");
            check.victorySource = Get<AudioSource>(check.flow, "victorySoundSource");
            CheckClip(check.flow, "winResultsMusic", "Assets/05. Result (Win).mp3");
            CheckClip(check.flow, "lossResultsMusic", "Assets/06. Result (Eliminated).mp3");
            CheckClip(check.flow, "trimmedDeathCue", "Assets/General Sounds/me_game_gameover.wav");
            CheckClip(check.flow, "trimmedVictoryCue", "Assets/General Sounds/me_game_clear.wav");
            Check(Get<AudioClip>(check.flow, "trimmedDeathCue") == Get<AudioClip>(check.death, "deathSound")
                && Get<AudioClip>(check.flow, "trimmedVictoryCue") == Get<AudioClip>(check.player, "victorySound"),
                "Silence-tail timing must be restricted to the supplied outcome cues");
            Check(Get<AudioClip>(check.flow, "winResultsMusic") != Get<AudioClip>(check.flow, "lossResultsMusic"),
                "Winning and losing must retain their separate supplied tracks");
            Check(check.deathSource != null && check.victorySource != null,
                "The death and victory cues must use their saved audio sources");
            Check(check.deathSource == Get<AudioSource>(check.death, "soundSource")
                && check.victorySource == Get<AudioSource>(check.player, "soundSource"),
                "Match flow must observe the sources that actually play the death and victory cues");
            Check(check.musicSource != check.deathSource && check.musicSource != check.victorySource
                && check.deathSource != check.victorySource
                && check.deathSource != Get<AudioSource>(check.flow, "cueSource")
                && check.victorySource != Get<AudioSource>(check.flow, "cueSource"),
                "Music, death, victory, and countdown sources must not replace one another's playback");
            EditorSceneManager.SaveScene(check.player.gameObject.scene, "Assets/ResultsMusicChecks.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private IEnumerator Start()
    {
        // BackgroundMusic may create its source when MatchFlow starts the menu track.
        musicSource = Get<AudioSource>(music, "musicSource");
        Check(musicSource != null && musicSource != deathSource && musicSource != victorySource
            && !musicSource.playOnAwake && musicSource.loop && musicSource.spatialBlend == 0f,
            "The initialized music source must be a separate flow-controlled looping 2D source");
        Set(music, "volume", 0f);
        musicSource.volume = deathSource.volume = victorySource.volume = 0f;
        Get<AudioSource>(flow, "cueSource").volume = 0f;
        Get<AudioSource>(flow, "knockoutSource").volume = 0f;
        Set(player, "fallInterval", 10000f);
        foreach (BotPlayer bot in Get<BotPlayer[]>(arena, "bots")) Set(bot, "secondsPerPiece", 10000f);
        Set(flow, "minJoinInterval", 10000f); Set(flow, "maxJoinInterval", 10000f); Set(flow, "joinTimer", 10000f);
        Set(flow, "readyDuration", .02f); Set(flow, "countdownInterval", .02f); Set(flow, "goDuration", .02f);
        Set(death, "rowInterval", .01f); Set(death, "rowFlashDuration", .05f);
        originalDeathCue = Get<AudioClip>(death, "deathSound");
        originalVictoryCue = Get<AudioClip>(player, "victorySound");
        originalDeathAudibleDuration = Get<float>(flow, "deathCueAudibleDuration");
        originalVictoryAudibleDuration = Get<float>(flow, "victoryCueAudibleDuration");
        // A mismatched custom cue must ignore the supplied cue's timing, even after it expires.
        Set(flow, "deathCueAudibleDuration", .01f);
        Set(flow, "victoryCueAudibleDuration", .01f);
        probe = AudioClip.Create("Thirty-second death and victory cue probe", 44100 * 30, 1, 44100, false);
        Set(death, "deathSound", probe); Set(player, "victorySound", probe);
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
        CheckLobbyMusic();
        BotPlayer[] roster = BeginRound(2);
        float deadline = Time.realtimeSinceStartup + 5f;
        while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline) yield return null;
        CheckPlaying(roster);

        Call(player, "EndGame");
        Check(player.IsGameOver && deathSource.isPlaying && !music.IsPlaying && !death.CanRetry,
            "Player death must stop battle music and start its actual cue and visual sweep");
        deadline = Time.realtimeSinceStartup + 5f;
        while (!death.CanRetry && Time.realtimeSinceStartup < deadline)
        {
            Check(!music.IsPlaying && !Get<GameObject>(flow, "resultsRoot").activeSelf,
                "Results music or the results panel interrupted the death sweep");
            yield return null;
        }
        yield return null;
        Check(death.CanRetry && flow.Phase == MatchFlow.MatchPhase.Spectating && !arena.IsRoundOver
            && arena.PlayerRank == 3 && Get<GameObject>(flow, "resultsRoot").activeInHierarchy,
            "A loss must show spectator results after the sweep while both opponents remain alive");
        Check(deathSource.isPlaying && !music.IsPlaying,
            "Loss music started before the actual death source finished its longer cue");
        flow.BeginMatchmaking();
        Check(flow.Phase == MatchFlow.MatchPhase.Spectating && deathSource.isPlaying && !music.IsPlaying,
            "Retry bypassed a still-running round or interrupted its death cue");
        deathSource.Stop();
        yield return null;
        CheckResultsMusic("lossResultsMusic");
        int lossStartSample = musicSource.timeSamples;
        yield return new WaitForSecondsRealtime(.2f);
        Check(musicSource.timeSamples > lossStartSample,
            "The original eliminated-results track did not advance through real audio playback");
        int lossBeforeFinish = musicSource.timeSamples;
        Call(roster[0], "Die");
        yield return null;
        Check(arena.IsRoundOver && !arena.PlayerWon && arena.PlayerRank == 3
            && flow.Phase == MatchFlow.MatchPhase.Finished,
            "The surviving opponent did not finish the spectator's round");
        CheckResultsMusic("lossResultsMusic");
        Check(musicSource.timeSamples >= lossBeforeFinish,
            "Finishing the spectator's round restarted the eliminated-results track");
        yield return new WaitForSecondsRealtime(.1f);
        Check(musicSource.timeSamples > lossBeforeFinish,
            "Repeated finished-results updates restarted or stopped the loss track");

        flow.BeginMatchmaking();
        CheckLobbyMusic();
        yield return new WaitForSecondsRealtime(.1f);
        CheckLobbyMusic();

        roster = BeginRound(1);
        deadline = Time.realtimeSinceStartup + 5f;
        while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline) yield return null;
        CheckPlaying(roster);
        Call(roster[0], "Die");
        Check(arena.IsRoundOver && arena.PlayerWon && arena.PlayerRank == 1 && victorySource.isPlaying
            && !music.IsPlaying && !player.IsGameOver,
            "Player victory must stop battle music and start the actual victory cue");
        yield return null;
        Check(flow.Phase == MatchFlow.MatchPhase.Finished && Get<GameObject>(flow, "resultsRoot").activeInHierarchy
            && victorySource.isPlaying && !music.IsPlaying,
            "Winning results did not wait for the victory cue");
        yield return new WaitForSecondsRealtime(.1f);
        Check(victorySource.isPlaying && !music.IsPlaying,
            "Winning-results updates interrupted the longer victory cue");
        victorySource.Stop();
        yield return null;
        CheckResultsMusic("winResultsMusic");
        int winStartSample = musicSource.timeSamples;
        yield return new WaitForSecondsRealtime(.2f);
        CheckResultsMusic("winResultsMusic");
        Check(musicSource.timeSamples > winStartSample,
            "The original winning-results track did not advance or was restarted every frame");
        flow.BeginMatchmaking();
        CheckLobbyMusic();

        // Retrying directly from a victory cue must cancel its pending results-music transition.
        roster = BeginRound(1);
        deadline = Time.realtimeSinceStartup + 5f;
        while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline) yield return null;
        CheckPlaying(roster);
        Call(roster[0], "Die");
        yield return null;
        Check(flow.Phase == MatchFlow.MatchPhase.Finished && victorySource.isPlaying && !music.IsPlaying,
            "The early-retry fixture did not reach the victory cue");
        flow.BeginMatchmaking();
        CheckLobbyMusic();
        yield return new WaitForSecondsRealtime(.2f);
        CheckLobbyMusic();

        // Use the real cue lengths and decoded MP3 output as well as the controlled gate probes.
        IEnumerator naturalChecks = NaturalOutcomeChecks();
        while (naturalChecks.MoveNext()) yield return naturalChecks.Current;

        Check(Get<AudioSource>(music, "musicSource") == musicSource
            && Get<AudioSource>(flow, "deathSoundSource") == deathSource
            && Get<AudioSource>(flow, "victorySoundSource") == victorySource,
            "Results transitions replaced the initialized audio sources");
        Destroy(probe);
        Debug.Log("RESULTS_MUSIC_CHECKS_PASSED loss=spectating-and-finished win=finished retries=after-results-and-during-cue natural-cues=2 decoded-signal=2 output-signal=2 recovery=2");
        EditorApplication.Exit(0);
    }

    private IEnumerator NaturalOutcomeChecks()
    {
        Set(death, "deathSound", originalDeathCue);
        Set(player, "victorySound", originalVictoryCue);
        Set(flow, "deathCueAudibleDuration", originalDeathAudibleDuration);
        Set(flow, "victoryCueAudibleDuration", originalVictoryAudibleDuration);
        Set(music, "volume", .05f);
        musicSource.volume = .05f;
        Check(!musicSource.mute && musicSource.volume > 0f,
            "Natural soundtrack checks require an unmuted music source with nonzero volume");
        var output = new float[2048];

        for (int outcome = 0; outcome < 2; outcome++)
        {
            bool won = outcome == 1;
            string resultField = won ? "winResultsMusic" : "lossResultsMusic";
            string durationField = won ? "victoryCueAudibleDuration" : "deathCueAudibleDuration";
            string offsetField = won ? "winResultsStartTime" : "lossResultsStartTime";
            AudioClip cue = won ? originalVictoryCue : originalDeathCue;
            AudioSource cueSource = won ? victorySource : deathSource;
            float audibleDuration = Get<float>(flow, durationField);
            float startOffset = Get<float>(flow, offsetField);
            Check(audibleDuration > 5f && audibleDuration < cue.length - 1f,
                "The supplied cue must retain its audible portion and omit its long silent tail");
            Check(startOffset >= 1.45f && startOffset <= 1.55f,
                "Results should skip the supplied MP3's silent introduction without cutting the music");

            BotPlayer[] roster = BeginRound(won ? 1 : 2);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (flow.Phase != MatchFlow.MatchPhase.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            CheckPlaying(roster);
            float began = Time.realtimeSinceStartup;
            if (won) Call(roster[0], "Die");
            else Call(player, "EndGame");
            Check(cueSource.isPlaying && !music.IsPlaying,
                "The real outcome cue did not start before its results soundtrack: " + resultField);

            deadline = began + 8f;
            while (!music.IsPlaying && Time.realtimeSinceStartup < deadline)
            {
                if (Time.realtimeSinceStartup - began < audibleDuration - .1f)
                    Check(cueSource.isPlaying, "The audible portion of the real outcome cue was cut short");
                yield return null;
            }
            float handoff = Time.realtimeSinceStartup - began;
            CheckResultsMusic(resultField);
            Check(handoff >= audibleDuration - .1f && handoff < audibleDuration + .75f,
                "Natural results handoff retained silent padding or cut the audible cue: " + handoff);
            Check(Mathf.Abs(musicSource.timeSamples / (float)musicSource.clip.frequency - startOffset) < .3f,
                "Natural results playback did not begin at the audible MP3 introduction");
            Check(won || death.CanRetry, "Natural loss playback covered an unfinished death sweep");

            AudioClip result = Get<AudioClip>(flow, resultField);
            Check(result.loadState == AudioDataLoadState.Loaded, "Results playback started before audio data loaded");
            var decoded = new float[4096 * result.channels];
            Check(result.GetData(decoded, Mathf.CeilToInt((startOffset + .2f) * result.frequency))
                && Rms(decoded) > .0001f,
                "The exact supplied result track has no decoded signal after the start offset");

            float maximumRms = 0f;
            deadline = Time.realtimeSinceStartup + .8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                CheckResultsMusic(resultField);
                musicSource.GetOutputData(output, 0);
                maximumRms = Mathf.Max(maximumRms, Rms(output));
                if (maximumRms > .00001f) break;
                yield return null;
            }
            Check(maximumRms > .00001f,
                "The advancing result track produced no nonzero Unity audio output: " + resultField);
            Debug.Log("RESULTS_AUDIO_SIGNAL " + resultField + " handoff=" + handoff.ToString("F3")
                + " start=" + startOffset.ToString("F2") + " sourceRms=" + maximumRms.ToString("G5"));

            musicSource.Stop();
            deadline = Time.realtimeSinceStartup + .5f;
            while (!music.IsPlaying && Time.realtimeSinceStartup < deadline) yield return null;
            CheckResultsMusic(resultField);
            int resumedSample = musicSource.timeSamples;
            yield return new WaitForSecondsRealtime(.15f);
            Check(musicSource.timeSamples > resumedSample,
                "Results playback did not recover and continue after its source stopped");

            if (!won)
            {
                Call(roster[0], "Die");
                yield return null;
                Check(arena.IsRoundOver && flow.Phase == MatchFlow.MatchPhase.Finished,
                    "The natural-loss round did not finish before retry");
            }
            flow.BeginMatchmaking();
            CheckLobbyMusic();
        }
    }

    private static float Rms(float[] samples)
    {
        double sum = 0d;
        foreach (float sample in samples) sum += sample * sample;
        return Mathf.Sqrt((float)(sum / samples.Length));
    }

    private BotPlayer[] BeginRound(int botCount)
    {
        Check(flow.Phase == MatchFlow.MatchPhase.Matchmaking && arena.ParticipantCount == 1,
            "A new audio test round did not start from a clean lobby");
        Set(flow, "joinTimer", 10000f);
        for (int i = 0; i < botCount; i++) Check(arena.AdmitNextBot(), "Could not admit a saved opponent");
        var roster = new List<BotPlayer>(arena.Opponents).ToArray();
        Check(roster.Length == botCount && arena.ParticipantCount == botCount + 1, "Unexpected test roster size");
        Call(flow, "BeginReady");
        return roster;
    }

    private void CheckPlaying(BotPlayer[] roster)
    {
        Check(flow.Phase == MatchFlow.MatchPhase.Playing && arena.MatchStarted && !arena.IsRoundOver,
            "The countdown did not start the controlled round");
        Check(music.IsPlaying && musicSource.clip == Get<AudioClip>(flow, "battleMusic"),
            "GO did not replace menu music with the battle track");
        foreach (BotPlayer bot in roster) bot.Paused = true;
        player.BattlePaused = true;
    }

    private void CheckLobbyMusic()
    {
        Check(flow.Phase == MatchFlow.MatchPhase.Matchmaking && arena.ParticipantCount == 1
            && !arena.MatchStarted && !arena.IsRoundOver && !arena.PlayerWon && !player.IsGameOver,
            "Retry did not reset the match outcome and roster");
        Check(music.IsPlaying && musicSource.clip == Get<AudioClip>(flow, "menuMusic") && musicSource.loop
            && !deathSource.isPlaying && !victorySource.isPlaying,
            "Lobby music did not replace the result track and stop pending outcome cues");
        Check(!Get<GameObject>(flow, "resultsRoot").activeSelf,
            "The previous round's results panel remained visible in the lobby");
    }

    private void CheckResultsMusic(string field)
    {
        Check(music.IsPlaying && music.CurrentClip == Get<AudioClip>(flow, field)
            && musicSource.clip == Get<AudioClip>(flow, field) && musicSource.loop
            && !deathSource.isPlaying && !victorySource.isPlaying,
            "The completed outcome cue did not hand off to the correct looping results soundtrack: " + field);
    }

    private static void CheckClip(MatchFlow target, string field, string path)
    {
        AudioClip clip = Get<AudioClip>(target, field);
        Check(clip != null && AssetDatabase.GetAssetPath(clip) == path && clip.samples > 0
            && clip.channels > 0 && clip.length > 1f, "Missing or incorrect supplied results track: " + field);
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Fields).Invoke(target, null);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
