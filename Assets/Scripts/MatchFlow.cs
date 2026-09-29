using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Runs the lobby and match presentation using the scene's saved boards, labels, and audio.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class MatchFlow : MonoBehaviour
{
    public enum MatchPhase { Matchmaking, Ready, Countdown, Playing, Spectating, Finished }

    [SerializeField] private BattleArena arena;
    [SerializeField] private FallingPiece player;
    [SerializeField] private BackgroundMusic music;
    [SerializeField] private AudioSource cueSource;
    [SerializeField] private AudioSource knockoutSource;
    [SerializeField] private AudioSource deathSoundSource;
    [SerializeField] private AudioSource victorySoundSource;
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private AudioClip battleMusic;
    [SerializeField] private AudioClip winResultsMusic;
    [SerializeField] private AudioClip lossResultsMusic;
    [Header("Supplied results audio playback regions")]
    [SerializeField] private AudioClip trimmedDeathCue;
    [SerializeField] private AudioClip trimmedVictoryCue;
    [SerializeField, Min(0f)] private float deathCueAudibleDuration = 5.56f;
    [SerializeField, Min(0f)] private float victoryCueAudibleDuration = 7.06f;
    [SerializeField, Min(0f)] private float winResultsStartTime = 1.5f;
    [SerializeField, Min(0f)] private float lossResultsStartTime = 1.5f;
    [Header("Match cues")]
    [SerializeField] private AudioClip joinSound;
    [SerializeField] private AudioClip knockoutSound;
    [SerializeField] private AudioClip readySound;
    [SerializeField] private AudioClip countSound;
    [SerializeField] private AudioClip goSound;
    [SerializeField, Min(0f)] private float matchmakingDuration = 90f;
    [SerializeField, Min(0.01f)] private float minJoinInterval = 1.2f;
    [SerializeField, Min(0.01f)] private float maxJoinInterval = 3.2f;
    [SerializeField, Range(1, 98)] private int minJoinBatch = 1;
    [SerializeField, Range(1, 98)] private int maxJoinBatch = 5;
    [SerializeField, Min(0.01f)] private float readyDuration = 1.5f;
    [SerializeField, Min(0.01f)] private float countdownInterval = 1f;
    [SerializeField, Min(0f)] private float goDuration = 0.8f;

    [Header("Saved presentation")]
    [SerializeField] private GameObject lobbyRoot;
    [SerializeField] private GameObject countdownRoot;
    [SerializeField] private GameObject attackOverlayRoot;
    [SerializeField] private GameObject resultsRoot;
    [SerializeField] private TMP_Text matchmakingText;
    [SerializeField] private TMP_Text participantsText;
    [SerializeField, HideInInspector] private TMP_Text timerText;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private SpriteRenderer phaseArtwork;
    [SerializeField] private Sprite readyArtwork;
    [SerializeField] private Sprite[] countdownArtwork = new Sprite[0];
    [SerializeField] private Sprite goArtwork;
    [SerializeField] private Vector2 phaseArtworkSize = new Vector2(8.5f, 2.5f);
    [SerializeField] private TMP_Text resultTitle;
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text resultsStatus;
    [SerializeField] private TMP_Text pageText;
    [SerializeField] private TMP_Text restartText;
    [SerializeField] private TMP_Text previousPageText;
    [SerializeField] private TMP_Text nextPageText;
    [SerializeField] private TMP_Text[] standingRows = new TMP_Text[0];
    [SerializeField] private TMP_Text[] modeLabels = new TMP_Text[0];
    [SerializeField] private SpriteRenderer[] modePanels = new SpriteRenderer[0];
    [SerializeField] private Sprite modeNormalArtwork;
    [SerializeField] private Sprite modeSelectedArtwork;
    [SerializeField] private SpriteRenderer[] medalIcons = new SpriteRenderer[0];
    [SerializeField] private Sprite[] medalArtwork = new Sprite[0];
    [SerializeField] private SpriteRenderer medalProgress;

    private readonly List<MatchStanding> visibleStandings = new List<MatchStanding>(99);
    private readonly HashSet<MatchStanding> announcedKnockouts = new HashSet<MatchStanding>();
    private GarbageCombat playerCombat;
    private DeathScreen death;
    private float phaseTimer;
    private float joinTimer;
    private float goTimer;
    private bool resultsMusicStarted;
    private double resultCueStartedAt;
    private bool standingsDirty = true;
    private Vector3 progressScale;
    private Vector3 progressPosition;
    private float progressWidth;
    private Vector2[] modePanelSizes;

    public MatchPhase Phase { get; private set; }
    public float RemainingSeconds { get; private set; }
    public int CountdownValue { get; private set; }
    public int StandingsPage { get; private set; }
    public int StandingsPageCount => Mathf.Max(1, Mathf.CeilToInt((arena != null ? arena.Standings.Count : 0)
        / (float)Mathf.Max(1, standingRows.Length)));

    private void Awake()
    {
        if (arena == null) arena = GetComponent<BattleArena>();
        if (player == null) player = GetComponentInParent<FallingPiece>();
        if (player != null)
        {
            playerCombat = player.GetComponent<GarbageCombat>();
            death = player.GetComponent<DeathScreen>();
        }
        if (medalProgress != null)
        {
            progressScale = medalProgress.transform.localScale;
            progressPosition = medalProgress.transform.localPosition;
            progressWidth = progressScale.x * (medalProgress.sprite != null ? medalProgress.sprite.bounds.size.x : 1f);
        }
        modePanelSizes = new Vector2[modePanels.Length];
        for (int i = 0; i < modePanels.Length; i++)
            if (modePanels[i] != null && modePanels[i].sprite != null)
                modePanelSizes[i] = Vector2.Scale(modePanels[i].sprite.bounds.size, modePanels[i].transform.localScale);
        BeginLobbyState();
    }

    private void OnEnable()
    {
        if (arena != null)
        {
            arena.StandingsChanged += MarkStandingsDirty;
            arena.ParticipantEliminated += ParticipantEliminated;
        }
        standingsDirty = true;
    }

    private void Start()
    {
        if (Phase == MatchPhase.Matchmaking && music != null) music.PlayClip(menuMusic);
    }

    private void OnDisable()
    {
        if (arena != null)
        {
            arena.StandingsChanged -= MarkStandingsDirty;
            arena.ParticipantEliminated -= ParticipantEliminated;
        }
        if (cueSource != null) cueSource.Stop();
        if (knockoutSource != null) knockoutSource.Stop();
    }

    /// <summary>Returns to matchmaking only after the whole match and the player's death sweep finish.</summary>
    public void BeginMatchmaking()
    {
        if (Phase != MatchPhase.Finished || arena == null || !arena.IsRoundOver
            || (player != null && player.IsGameOver && death != null && !death.CanRetry)) return;
        BeginLobbyState();
        if (music != null) music.PlayClip(menuMusic);
    }

    private void BeginLobbyState()
    {
        if (cueSource != null) cueSource.Stop();
        if (knockoutSource != null) knockoutSource.Stop();
        announcedKnockouts.Clear();
        Phase = MatchPhase.Matchmaking;
        RemainingSeconds = Mathf.Max(0f, matchmakingDuration);
        CountdownValue = 0;
        phaseTimer = goTimer = 0f;
        joinTimer = NextJoinInterval();
        StandingsPage = 0;
        resultsMusicStarted = false;
        resultCueStartedAt = 0d;
        standingsDirty = true;
        if (arena != null) arena.BeginLobby();
        Show(lobbyRoot, true);
        Show(countdownRoot, false);
        SetPhaseArtwork(null);
        Show(attackOverlayRoot, false);
        Show(resultsRoot, false);
        RefreshLobby();
        RefreshMedals();
    }

    private float NextJoinInterval() => Random.Range(Mathf.Max(0.01f, minJoinInterval),
        Mathf.Max(Mathf.Max(0.01f, minJoinInterval), maxJoinInterval));

    private int NextJoinBatch()
    {
        int minimum = Mathf.Clamp(minJoinBatch, 1, 98);
        int maximum = Mathf.Clamp(maxJoinBatch, minimum, 98);
        return Random.Range(minimum, maximum + 1);
    }

    private void ParticipantEliminated(MatchStanding standing)
    {
        if (!isActiveAndEnabled || standing == null || standing.IsPlayer || !standing.KilledByPlayer ||
            standing.Bot == null || !standing.Bot.IsGameOver || standing.Rank <= 1 ||
            !announcedKnockouts.Add(standing)) return;
        if (knockoutSource != null && knockoutSource.isActiveAndEnabled && knockoutSound != null)
            knockoutSource.PlayOneShot(knockoutSound);
    }

    private void Update()
    {
        if (arena == null) return;
        float delta = Time.unscaledDeltaTime;
        if (Phase == MatchPhase.Matchmaking)
        {
            RemainingSeconds = Mathf.Max(0f, RemainingSeconds - delta);
            joinTimer -= delta;
            if (joinTimer <= 0f)
            {
                int joined = 0;
                int requested = NextJoinBatch();
                while (joined < requested && arena.AdmitNextBot()) joined++;
                if (joined > 0) PlayCue(joinSound);
                joinTimer = NextJoinInterval();
            }
            RefreshLobby();
            if (arena.ParticipantCount >= 99 || RemainingSeconds <= 0f) BeginReady();
        }
        else if (Phase == MatchPhase.Ready || Phase == MatchPhase.Countdown)
        {
            phaseTimer -= delta;
            if (phaseTimer <= 0f)
            {
                if (Phase == MatchPhase.Ready) BeginCountdown();
                else if (CountdownValue > 1) ShowCount(CountdownValue - 1);
                else BeginPlaying();
            }
        }

        if (Phase == MatchPhase.Playing || Phase == MatchPhase.Spectating || Phase == MatchPhase.Finished)
        {
            if (arena.IsRoundOver && Phase != MatchPhase.Finished) ShowResults(true);
            else if (player != null && player.IsGameOver && Phase == MatchPhase.Playing) ShowResults(false);
        }
        if (Phase == MatchPhase.Playing)
        {
            goTimer -= delta;
            if (goTimer <= 0f) Show(countdownRoot, false);
        }
        if (Phase == MatchPhase.Spectating || Phase == MatchPhase.Finished)
        {
            // Keep the established block-removal/glow sweep visible before covering the board.
            Show(resultsRoot, player == null || !player.IsGameOver || death == null || death.CanRetry);
            AudioClip resultTrack = arena.PlayerWon ? winResultsMusic : lossResultsMusic;
            if ((!resultsMusicStarted || music == null || !music.IsPlaying || music.CurrentClip != resultTrack)
                && CanPlayResultsMusic())
            {
                resultsMusicStarted = music != null && music.TryPlayClip(resultTrack,
                    arena.PlayerWon ? winResultsStartTime : lossResultsStartTime);
            }
            if (standingsDirty) RefreshResults();
            SetText(restartText, Phase == MatchPhase.Finished && CanRestart()
                ? "R  PLAY AGAIN" : "WATCHING THE MATCH");
        }
        HandleInput();
        if (Phase == MatchPhase.Countdown || Phase == MatchPhase.Playing) RefreshAttackModes();
        RefreshMedals();
    }

    private void BeginReady()
    {
        Phase = MatchPhase.Ready;
        CountdownValue = 0;
        phaseTimer = Mathf.Max(0.01f, readyDuration);
        Show(lobbyRoot, false);
        Show(countdownRoot, true);
        SetText(countdownText, "READY");
        SetPhaseArtwork(readyArtwork);
        if (music != null) music.Stop();
        // The 99th join can happen this frame; let its short cue finish under READY.
        PlayCue(readySound, false);
    }

    private void BeginCountdown()
    {
        Phase = MatchPhase.Countdown;
        Show(attackOverlayRoot, true);
        RefreshAttackModes();
        ShowCount(3);
    }

    private void ShowCount(int count)
    {
        CountdownValue = count;
        phaseTimer = Mathf.Max(0.01f, countdownInterval);
        SetText(countdownText, count.ToString());
        SetPhaseArtwork(count > 0 && count <= countdownArtwork.Length ? countdownArtwork[count - 1] : null);
        PlayCue(countSound);
    }

    private void BeginPlaying()
    {
        Phase = MatchPhase.Playing;
        CountdownValue = 0;
        goTimer = Mathf.Max(0f, goDuration);
        SetText(countdownText, "GO!");
        SetPhaseArtwork(goArtwork);
        PlayCue(goSound);
        if (music != null) music.PlayClip(battleMusic);
        arena.StartMatch();
    }

    private void ShowResults(bool finished)
    {
        if (Phase == MatchPhase.Playing)
        {
            // The outcome cue has already begun; preserve this clock when spectator results become final.
            resultCueStartedAt = AudioSettings.dspTime;
            AudioClip track = arena.PlayerWon ? winResultsMusic : lossResultsMusic;
            if (track != null && track.loadState == AudioDataLoadState.Unloaded) track.LoadAudioData();
        }
        Phase = finished ? MatchPhase.Finished : MatchPhase.Spectating;
        if (cueSource != null) cueSource.Stop();
        Show(lobbyRoot, false);
        Show(countdownRoot, false);
        Show(attackOverlayRoot, false);
        Show(resultsRoot, player == null || !player.IsGameOver || death == null || death.CanRetry);
        standingsDirty = true;
        RefreshResults();
    }

    private bool CanPlayResultsMusic()
    {
        if (player != null && player.IsGameOver)
            return (death == null || death.CanRetry) && OutcomeCueFinished(deathSoundSource,
                death != null ? death.DeathSound : null, trimmedDeathCue, deathCueAudibleDuration);
        return OutcomeCueFinished(victorySoundSource, player != null ? player.VictorySound : null,
            trimmedVictoryCue, victoryCueAudibleDuration);
    }

    private bool OutcomeCueFinished(AudioSource source, AudioClip actualClip, AudioClip suppliedClip,
        float audibleDuration)
    {
        if (source == null || !source.isPlaying) return true;
        // Only trim the measured silent tail of the supplied cue. Custom clips retain their full duration.
        if (suppliedClip == null || actualClip != suppliedClip || audibleDuration <= 0f || source.pitch <= 0f
            || AudioSettings.dspTime - resultCueStartedAt < audibleDuration / source.pitch) return false;
        source.Stop();
        return true;
    }

    private bool CanRestart() => arena != null && arena.IsRoundOver
        && (player == null || !player.IsGameOver || death == null || death.CanRetry);

    private void RefreshLobby()
    {
        SetText(matchmakingText, "SEARCHING FOR PLAYERS...");
        SetText(participantsText, (arena != null ? arena.ParticipantCount : 1).ToString("00"));
    }

    private void MarkStandingsDirty() => standingsDirty = true;

    private void RefreshResults()
    {
        standingsDirty = false;
        SetText(resultTitle, arena.PlayerWon ? "VICTORY" : "YOUR RESULT");
        SetText(rankText, "#" + Mathf.Max(1, arena.PlayerRank).ToString("00"));
        SetText(resultsStatus, arena.IsRoundOver ? "FINAL STANDINGS" : "LIVE  " + arena.AliveParticipants + " REMAINING");
        visibleStandings.Clear();
        foreach (MatchStanding standing in arena.Standings) visibleStandings.Add(standing);
        visibleStandings.Sort((a, b) =>
        {
            int aRank = a.Rank > 0 ? a.Rank : 0;
            int bRank = b.Rank > 0 ? b.Rank : 0;
            int rankComparison = aRank.CompareTo(bRank);
            if (rankComparison != 0) return rankComparison;
            int koComparison = b.Knockouts.CompareTo(a.Knockouts);
            return koComparison != 0 ? koComparison : string.CompareOrdinal(a.DisplayName, b.DisplayName);
        });
        StandingsPage = Mathf.Clamp(StandingsPage, 0, StandingsPageCount - 1);
        for (int row = 0; row < standingRows.Length; row++)
        {
            TMP_Text label = standingRows[row];
            if (label == null) continue;
            int index = StandingsPage * standingRows.Length + row;
            if (index >= visibleStandings.Count) { label.text = ""; continue; }
            MatchStanding standing = visibleStandings[index];
            string rank = standing.Rank > 0 ? standing.Rank.ToString("00") : "--";
            label.text = "<pos=0%>" + rank + "<pos=18%>" + (standing.IsPlayer ? "YOU" : standing.DisplayName)
                + "<pos=74%>" + standing.Knockouts.ToString("00") + "<pos=93%>" + standing.Medals;
            label.color = standing.IsPlayer ? new Color(0.3f, 1f, 0.82f)
                : standing.Rank == 0 ? new Color(0.92f, 0.95f, 1f) : new Color(0.64f, 0.69f, 0.77f);
        }
        SetText(pageText, (StandingsPage + 1) + " / " + StandingsPageCount);
    }

    public void NextStandingsPage()
    {
        if (Phase != MatchPhase.Spectating && Phase != MatchPhase.Finished) return;
        StandingsPage = (StandingsPage + 1) % StandingsPageCount;
        RefreshResults();
    }

    public void PreviousStandingsPage()
    {
        if (Phase != MatchPhase.Spectating && Phase != MatchPhase.Finished) return;
        StandingsPage = (StandingsPage + StandingsPageCount - 1) % StandingsPageCount;
        RefreshResults();
    }

    private void HandleInput()
    {
        Keyboard keyboard = Keyboard.current;
        bool results = Phase == MatchPhase.Spectating || Phase == MatchPhase.Finished;
        if (results)
        {
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame || keyboard.pageUpKey.wasPressedThisFrame) PreviousStandingsPage();
                if (keyboard.eKey.wasPressedThisFrame || keyboard.pageDownKey.wasPressedThisFrame) NextStandingsPage();
                if (Phase == MatchPhase.Finished && keyboard.rKey.wasPressedThisFrame) BeginMatchmaking();
            }
            if (Clicked(previousPageText)) PreviousStandingsPage();
            else if (Clicked(nextPageText)) NextStandingsPage();
            else if (Clicked(restartText) && Phase == MatchPhase.Finished) BeginMatchmaking();
            return;
        }
        if (Phase != MatchPhase.Playing && Phase != MatchPhase.Countdown) return;
        if (keyboard != null)
        {
            if (keyboard.wKey.wasPressedThisFrame) arena.SetAttackMode(AttackMode.Knockouts);
            else if (keyboard.aKey.wasPressedThisFrame) arena.SetAttackMode(AttackMode.Randoms);
            else if (keyboard.sKey.wasPressedThisFrame) arena.SetAttackMode(AttackMode.Attackers);
            else if (keyboard.dKey.wasPressedThisFrame) arena.SetAttackMode(AttackMode.Badges);
        }
        for (int i = 0; i < modeLabels.Length && i < 4; i++)
            if (Clicked(modeLabels[i])) arena.SetAttackMode((AttackMode)i);
    }

    private static bool Clicked(TMP_Text label)
    {
        Mouse mouse = Mouse.current;
        Camera camera = Camera.main;
        if (label == null || !label.gameObject.activeInHierarchy || mouse == null || camera == null
            || !mouse.leftButton.wasPressedThisFrame) return false;
        Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        var plane = new Plane(label.transform.forward, label.transform.position);
        return plane.Raycast(ray, out float distance)
            && label.rectTransform.rect.Contains(label.transform.InverseTransformPoint(ray.GetPoint(distance)));
    }

    private void RefreshAttackModes()
    {
        for (int i = 0; i < modeLabels.Length; i++)
        {
            bool selected = i == (int)arena.CurrentAttackMode;
            if (modeLabels[i] != null) modeLabels[i].color = selected ? new Color(0.08f, 0.1f, 0.12f) : Color.white;
            if (i < modePanels.Length && modePanels[i] != null)
            {
                Sprite artwork = selected ? modeSelectedArtwork : modeNormalArtwork;
                if (artwork != null)
                {
                    modePanels[i].sprite = artwork;
                    modePanels[i].color = Color.white;
                    if (modePanelSizes != null && i < modePanelSizes.Length)
                        modePanels[i].transform.localScale = new Vector3(modePanelSizes[i].x / artwork.bounds.size.x,
                            modePanelSizes[i].y / artwork.bounds.size.y, 1f);
                }
                else modePanels[i].color = selected ? new Color(1f, 0.82f, 0.2f, 0.98f)
                    : new Color(0.035f, 0.065f, 0.09f, 0.92f);
            }
        }
    }

    private void RefreshMedals()
    {
        int count = playerCombat != null ? playerCombat.BadgeCount : 0;
        int bits = playerCombat != null ? playerCombat.BadgeBits : 0;
        int previous = count == 0 ? 0 : count == 1 ? 2 : count == 2 ? 6 : count == 3 ? 14 : 30;
        int next = count == 0 ? 2 : count == 1 ? 6 : count == 2 ? 14 : 30;
        float progress = count >= 4 ? 1f : Mathf.Clamp01((bits - previous) / (float)(next - previous));
        for (int i = 0; i < medalIcons.Length; i++)
        {
            if (medalIcons[i] == null) continue;
            if (medalArtwork.Length == 16)
            {
                int stage = i < count ? 15 : i == count ? Mathf.Min(14, Mathf.FloorToInt(progress * 15f)) : 0;
                medalIcons[i].sprite = medalArtwork[stage];
                medalIcons[i].color = i < count || (i == count && progress > 0f)
                    ? Color.white : new Color(.3f, .32f, .35f);
            }
            else medalIcons[i].color = i < count ? new Color(1f, 0.8f, 0.18f) : new Color(0.2f, 0.22f, 0.25f);
        }
        if (medalProgress == null) return;
        medalProgress.transform.localScale = new Vector3(progressScale.x * progress, progressScale.y, progressScale.z);
        medalProgress.transform.localPosition = progressPosition + Vector3.left * (progressWidth * (1f - progress) * .5f);
        medalProgress.enabled = progress > 0f;
    }

    private void SetPhaseArtwork(Sprite artwork)
    {
        if (phaseArtwork == null) return;
        phaseArtwork.sprite = artwork;
        phaseArtwork.enabled = artwork != null;
        if (countdownText != null) countdownText.enabled = artwork == null;
        if (artwork == null) return;
        float scale = Mathf.Min(phaseArtworkSize.x / artwork.bounds.size.x,
            phaseArtworkSize.y / artwork.bounds.size.y);
        phaseArtwork.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void PlayCue(AudioClip clip, bool stopPrevious = true)
    {
        if (cueSource == null || clip == null) return;
        if (stopPrevious) cueSource.Stop();
        cueSource.PlayOneShot(clip);
    }

    private static void Show(GameObject target, bool visible)
    {
        if (target != null && target.activeSelf != visible) target.SetActive(visible);
    }

    private static void SetText(TMP_Text label, string value)
    {
        if (label != null && label.text != value) label.text = value;
    }
}
