using TMPro;
using UnityEngine;

/// <summary>Celebrates the player's first crossing of the 50- and 10-survivor milestones.</summary>
[DisallowMultipleComponent]
public sealed class SurvivorMilestonePresentation : MonoBehaviour
{
    [SerializeField] private BattleArena arena;
    [SerializeField] private FallingPiece player;
    [SerializeField] private GameObject bannerRoot;
    [SerializeField] private TMP_Text milestoneText;
    [SerializeField] private TMP_Text shadowText;
    [SerializeField] private AudioSource announcementSource;
    [SerializeField] private AudioClip fiftyPlayersSound;
    [SerializeField] private AudioClip tenPlayersSound;
    [SerializeField, Min(.5f)] private float duration = 1.6f;

    private static readonly Color FiftyColor = new Color(.5f, 1f, .87f);
    private static readonly Color TenColor = new Color(1f, .83f, .24f);
    private BattleArena subscribedArena;
    private bool observingRound;
    private bool fiftyAnnounced;
    private bool tenAnnounced;
    private int previousAlive;
    private int pendingThreshold;
    private float elapsed;

    public int CurrentThreshold { get; private set; }
    public bool IsAnimating => CurrentThreshold != 0;

    private void OnEnable()
    {
        if (arena == null) arena = GetComponent<BattleArena>();
        if (player == null) player = GetComponentInParent<FallingPiece>();
        if (arena != null)
        {
            subscribedArena = arena;
            subscribedArena.StandingsChanged += ObserveArena;
        }
        ObserveArena();
    }

    private void OnDisable()
    {
        if (subscribedArena != null) subscribedArena.StandingsChanged -= ObserveArena;
        subscribedArena = null;
        ClearPresentation();
        if (arena == null || !arena.MatchStarted) ResetRound();
    }

    private void Update()
    {
        ObserveArena();
        if (!IsAnimating) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < Mathf.Max(.5f, duration))
        {
            ShowPose(elapsed / Mathf.Max(.5f, duration));
            return;
        }
        HideBanner();
        int next = pendingThreshold;
        pendingThreshold = 0;
        if (next != 0) BeginAnnouncement(next);
    }

    /// <summary>Consumes actual survivor decreases; admissions and small matches do not award milestones.</summary>
    public void ObserveArena()
    {
        if (!isActiveAndEnabled) return;
        if (arena == null || !arena.MatchStarted)
        {
            ResetRound();
            return;
        }
        if (!arena.isActiveAndEnabled || player == null || player.IsGameOver || arena.IsRoundOver)
        {
            ClearPresentation();
            return;
        }
        if (!observingRound)
        {
            observingRound = true;
            // Starting from the joined roster also catches several eliminations before our first Update.
            previousAlive = arena.ParticipantCount;
        }

        int alive = arena.AliveParticipants;
        if (!fiftyAnnounced && previousAlive > 50 && alive <= 50)
        {
            fiftyAnnounced = true;
            EnqueueAnnouncement(50);
        }
        if (!tenAnnounced && previousAlive > 10 && alive <= 10)
        {
            tenAnnounced = true;
            EnqueueAnnouncement(10);
        }
        previousAlive = alive;
    }

    private void EnqueueAnnouncement(int threshold)
    {
        if (IsAnimating) pendingThreshold = threshold;
        else BeginAnnouncement(threshold);
    }

    private void BeginAnnouncement(int threshold)
    {
        CurrentThreshold = threshold;
        elapsed = 0f;
        string message = threshold == 50 ? "50 PLAYERS LEFT" : "10 PLAYERS LEFT";
        if (milestoneText != null)
        {
            milestoneText.text = message;
            milestoneText.color = threshold == 50 ? FiftyColor : TenColor;
        }
        if (shadowText != null) shadowText.text = message;
        if (bannerRoot != null) bannerRoot.SetActive(true);
        ShowPose(0f);
        AudioClip clip = threshold == 50 ? fiftyPlayersSound : tenPlayersSound;
        if (announcementSource != null && clip != null)
        {
            announcementSource.clip = clip;
            announcementSource.Play();
        }
    }

    private void ShowPose(float progress)
    {
        Vector3 scale;
        float alpha;
        if (progress < .14f)
        {
            float t = 1f - Mathf.Pow(1f - progress / .14f, 3f);
            scale = Vector3.LerpUnclamped(new Vector3(.15f, 1.8f, 1f), new Vector3(1.15f, .85f, 1f), t);
            alpha = Mathf.Clamp01(progress / .045f);
        }
        else if (progress < .28f)
        {
            float t = Mathf.SmoothStep(0f, 1f, (progress - .14f) / .14f);
            scale = Vector3.LerpUnclamped(new Vector3(1.15f, .85f, 1f), Vector3.one, t);
            alpha = 1f;
        }
        else if (progress < .78f)
        {
            scale = Vector3.one;
            alpha = 1f;
        }
        else
        {
            float t = Mathf.Clamp01((progress - .78f) / .22f);
            scale = Vector3.Lerp(Vector3.one, new Vector3(1.3f, .15f, 1f), t * t);
            alpha = 1f - t;
        }
        if (milestoneText != null)
        {
            milestoneText.transform.localScale = scale;
            milestoneText.alpha = alpha;
        }
        if (shadowText != null) shadowText.alpha = alpha * .95f;
    }

    private void HideBanner()
    {
        CurrentThreshold = 0;
        elapsed = 0f;
        if (bannerRoot != null) bannerRoot.SetActive(false);
        if (milestoneText != null)
        {
            milestoneText.transform.localScale = Vector3.one;
            milestoneText.alpha = 1f;
        }
        if (shadowText != null) shadowText.alpha = .95f;
    }

    private void ClearPresentation()
    {
        pendingThreshold = 0;
        HideBanner();
        if (announcementSource != null && announcementSource.isPlaying) announcementSource.Stop();
    }

    private void ResetRound()
    {
        observingRound = false;
        fiftyAnnounced = tenAnnounced = false;
        previousAlive = 0;
        ClearPresentation();
    }
}
