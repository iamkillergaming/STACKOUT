using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Animates authored clear labels and star sprites from actual combat results.</summary>
[DisallowMultipleComponent]
public sealed class ClearAttackFeedback : MonoBehaviour
{
    [SerializeField] private FallingPiece player;
    [SerializeField] private GarbageCombat combat;
    [SerializeField] private BattleArena arena;
    [SerializeField] private TMP_Text clearText;
    [SerializeField] private TMP_Text comboText;
    [SerializeField] private TMP_Text attackText;
    [SerializeField] private SpriteRenderer tetrisLogo;
    [SerializeField] private SpriteRenderer tSpinLogo;
    [SerializeField] private SpriteRenderer allClearLogo;
    [SerializeField] private SpriteRenderer comboLogo;
    [SerializeField] private SpriteRenderer backToBackLogo;
    [SerializeField] private SpriteRenderer singleLogo;
    [SerializeField] private SpriteRenderer doubleLogo;
    [SerializeField] private SpriteRenderer tripleLogo;
    [SerializeField] private Sprite attackBurstSprite;
    [SerializeField] private SpriteRenderer[] stars = new SpriteRenderer[192];
    [SerializeField] private AudioSource calloutSource;
    [SerializeField] private AudioSource attackSource;
    [SerializeField] private AudioClip calloutSound;
    [SerializeField] private AudioClip comboSound;
    [SerializeField] private AudioClip specialClearSound;
    [SerializeField] private AudioClip allClearSound;
    [SerializeField] private AudioClip attackLaunchSound;
    [SerializeField] private AudioClip attackHitSound;
    [SerializeField, Min(.1f)] private float feedbackDuration = 1.5f;
    [SerializeField, Min(.05f)] private float flightDuration = .55f;
    [SerializeField, Min(0f)] private float starStagger = .006f;
    [SerializeField, Min(.01f)] private float burstDuration = .2f;

    private sealed class TextPopup
    {
        public TMP_Text Label;
        public Vector3 Scale;
        public Color Color;
        public float Age;
        public bool Showing;
    }

    private sealed class AttackWave
    {
        public GarbageCombat Target;
        public Board TargetBoard;
        public Vector3 TargetPosition;
        public Vector3[] Path;
        public int PathCount;
        public float PathLength;
        public int AcceptedLines;
        public float Elapsed;
        public float FlightDuration;
        public float BurstDuration;
        public float Duration;
        public bool HitPlayed;
    }

    private sealed class SpritePopup
    {
        public SpriteRenderer Renderer;
        public Vector3 Scale;
        public Color Color;
        public float Age;
        public bool Showing;
    }

    private sealed class StarParticle
    {
        public SpriteRenderer Renderer;
        public Vector3 HomePosition;
        public Quaternion HomeRotation;
        public Vector3 HomeScale;
        public Color HomeColor;
        public Sprite HomeSprite;
        public AttackWave Wave;
        public Vector3 Start;
        public Vector3 End;
        public Vector3 Burst;
        public float Delay;
        public float Angle;
        public float Spin;
        public bool Active;
    }

    private readonly List<AttackWave> waves = new List<AttackWave>();
    private readonly System.Random effectsRandom = new System.Random(Guid.NewGuid().GetHashCode());
    private StarParticle[] particles;
    private TextPopup[] popups;
    private SpritePopup[] logoPopups;
    private FallingPiece boundPlayer;
    private GarbageCombat boundCombat;
    private BattleArena boundArena;

    public int ActiveWaveCount => waves.Count;
    public int ActiveStarCount
    {
        get
        {
            int count = 0;
            if (particles != null)
                foreach (StarParticle particle in particles)
                    if (particle != null && particle.Active) count++;
            return count;
        }
    }

    private void Awake()
    {
        CacheAuthoredVisuals();
        ClearFeedback();
    }

    private void OnEnable()
    {
        CacheAuthoredVisuals();
        ClearFeedback();
        EnsureBindings();
    }

    private void Start() => EnsureBindings();

    private void OnDisable()
    {
        Unbind();
        ClearFeedback();
    }

    private void EnsureBindings()
    {
        if (player == null) player = GetComponent<FallingPiece>();
        if (combat == null && player != null) combat = player.GetComponent<GarbageCombat>();
        if (arena == null && player != null) arena = player.GetComponentInChildren<BattleArena>(true);
        if (arena == null) arena = GetComponentInParent<BattleArena>();
        if (player == boundPlayer && combat == boundCombat && arena == boundArena) return;
        Unbind();
        boundPlayer = player;
        boundCombat = combat;
        boundArena = arena;
        if (boundPlayer != null)
        {
            boundPlayer.Eliminated += PlayerStateChanged;
            boundPlayer.Restarted += PlayerStateChanged;
        }
        if (boundCombat != null) boundCombat.LockResolved += OnLockResolved;
        if (boundArena != null) boundArena.AttackDelivered += OnAttackDelivered;
    }

    private void Unbind()
    {
        if (boundPlayer != null)
        {
            boundPlayer.Eliminated -= PlayerStateChanged;
            boundPlayer.Restarted -= PlayerStateChanged;
        }
        if (boundCombat != null) boundCombat.LockResolved -= OnLockResolved;
        if (boundArena != null) boundArena.AttackDelivered -= OnAttackDelivered;
        boundPlayer = null;
        boundCombat = null;
        boundArena = null;
    }

    private bool CanShowFeedback => isActiveAndEnabled && player != null && player.isActiveAndEnabled
        && !player.IsGameOver && !player.BattlePaused && arena != null && arena.isActiveAndEnabled && !arena.IsRoundOver;

    private void PlayerStateChanged(FallingPiece source) => ClearFeedback();

    private void OnLockResolved(GarbageCombat source, ClearResult result)
    {
        if (source != combat) return;
        if (!CanShowFeedback) { ClearFeedback(); return; }
        if (result.ClearedLines <= 0) return;

        string name = result.ClearedLines == 1 ? "SINGLE" : result.ClearedLines == 2 ? "DOUBLE"
            : result.ClearedLines == 3 ? "TRIPLE" : "TETRIS";
        bool tetrisArt = ShowLogo(0, result.ClearedLines == 4);
        bool spinArt = ShowLogo(1, result.Spin != SpinKind.None);
        bool allClearArt = ShowLogo(2, result.AllClear);
        bool comboArt = ShowLogo(3, result.Combo > 0);
        bool backToBackArt = ShowLogo(4, result.BackToBack);
        bool singleArt = ShowLogo(5, result.ClearedLines == 1);
        bool doubleArt = ShowLogo(6, result.ClearedLines == 2);
        bool tripleArt = ShowLogo(7, result.ClearedLines == 3);
        if (tetrisArt || singleArt || doubleArt || tripleArt) name = "";
        if (result.Spin == SpinKind.Full && !spinArt) name = "T-SPIN" + (name.Length > 0 ? "\n" + name : "");
        else if (result.Spin == SpinKind.Mini) name = (spinArt ? "MINI" : "T-SPIN MINI") + (name.Length > 0 ? "\n" + name : "");
        if (result.AllClear && !allClearArt) name = "ALL CLEAR" + (name.Length > 0 ? "\n" + name : "");
        ShowText(popups[0], name);

        string chain = result.Combo > 0 ? (comboArt ? "X" : "COMBO X") + result.Combo + (result.Combo >= 10 ? "+" : "") : "";
        if (result.BackToBack && !backToBackArt) chain = "BACK-TO-BACK" + (chain.Length > 0 ? "\n" + chain : "");
        ShowText(popups[1], chain);
        string attack = result.OutgoingLines > 0 ? "+" + result.OutgoingLines + " LINES"
            : result.CancelledLines > 0 ? "CANCELLED " + result.CancelledLines : "";
        ShowText(popups[2], attack);

        // A clear gets one extra callout, with the most significant available cue taking priority.
        AudioClip clip = result.AllClear ? allClearSound
            : result.Spin != SpinKind.None || result.BackToBack ? specialClearSound
            : result.Combo > 0 ? comboSound : calloutSound;
        if (clip == null) clip = calloutSound;
        if (calloutSource != null)
        {
            calloutSource.Stop();
            if (clip != null) calloutSource.PlayOneShot(clip);
        }
    }

    private void OnAttackDelivered(GarbageCombat sender, GarbageCombat recipient, int acceptedLines)
    {
        if (sender != combat || recipient == null || recipient == combat || acceptedLines <= 0) return;
        if (!CanShowFeedback) { ClearFeedback(); return; }
        Board sourceBoard = player.GetComponent<Board>();
        Board recipientBoard = recipient.GetComponent<Board>();
        BoardPresentation presentation = player.GetComponent<BoardPresentation>();
        if (sourceBoard == null || recipientBoard == null || presentation == null || !presentation.isActiveAndEnabled) return;
        // Keep the actual recipient and use the same world-space polyline that draws its yellow route.
        var path = new Vector3[5];
        int pathCount = presentation.GetTargetingPath(recipientBoard, path);
        if (pathCount < 2 || pathCount > path.Length) return;
        float pathLength = 0f;
        for (int i = 1; i < pathCount; i++) pathLength += Vector3.Distance(path[i - 1], path[i]);
        if (pathLength <= .0001f || float.IsNaN(pathLength) || float.IsInfinity(pathLength)) return;
        var wave = new AttackWave
        {
            Target = recipient,
            TargetBoard = recipientBoard,
            TargetPosition = path[pathCount - 1],
            Path = path,
            PathCount = pathCount,
            PathLength = pathLength,
            AcceptedLines = acceptedLines,
            FlightDuration = Mathf.Max(.05f, flightDuration),
            BurstDuration = Mathf.Max(.01f, burstDuration)
        };
        float unit = sourceBoard.transform.TransformVector(Vector3.right * sourceBoard.CellSize).magnitude;
        int requested = Mathf.Clamp(acceptedLines, 1, 12) * 4;
        int emitted = 0;
        foreach (StarParticle particle in particles)
        {
            if (emitted >= requested) break;
            if (particle == null || particle.Active || particle.Renderer == null) continue;
            particle.Active = true;
            particle.Wave = wave;
            particle.Start = path[0];
            particle.End = wave.TargetPosition;
            particle.Burst = Scatter(.24f * unit);
            particle.Delay = emitted * Mathf.Max(0f, starStagger);
            particle.Angle = SignedRandom() * 180f;
            particle.Spin = SignedRandom() * 260f;
            particle.Renderer.transform.position = particle.Start;
            particle.Renderer.enabled = emitted == 0;
            emitted++;
        }
        // The fixed sprite pool may already be in use by earlier attacks. Do not create an invisible wave.
        if (emitted == 0) return;
        wave.Duration = wave.FlightDuration + Mathf.Max(0, emitted - 1) * Mathf.Max(0f, starStagger) + wave.BurstDuration;
        waves.Add(wave);
        presentation.ShowAttackRoute(recipientBoard, wave.Duration);
        PlayAttackSound(attackLaunchSound);
    }

    private static Vector3 PositionAlongPath(AttackWave wave, float progress)
    {
        float remaining = Mathf.Clamp01(progress) * wave.PathLength;
        for (int i = 1; i < wave.PathCount; i++)
        {
            float length = Vector3.Distance(wave.Path[i - 1], wave.Path[i]);
            if (length <= .0001f) continue;
            if (remaining <= length) return Vector3.Lerp(wave.Path[i - 1], wave.Path[i], remaining / length);
            remaining -= length;
        }
        return wave.TargetPosition;
    }

    private float SignedRandom() => (float)effectsRandom.NextDouble() * 2f - 1f;

    private Vector3 Scatter(float radius)
    {
        float angle = (float)effectsRandom.NextDouble() * Mathf.PI * 2f;
        float distance = Mathf.Sqrt((float)effectsRandom.NextDouble()) * radius;
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * distance;
    }

    private void LateUpdate()
    {
        EnsureBindings();
        if (!CanShowFeedback)
        {
            if (waves.Count > 0 || HasText() || (calloutSource != null && calloutSource.isPlaying)
                || (attackSource != null && attackSource.isPlaying)) ClearFeedback();
            return;
        }
        float delta = Mathf.Max(0f, Time.deltaTime);
        foreach (TextPopup popup in popups) UpdateText(popup, delta);
        foreach (SpritePopup popup in logoPopups) UpdateLogo(popup, delta);
        BoardPresentation presentation = player.GetComponent<BoardPresentation>();
        for (int i = waves.Count - 1; i >= 0; i--)
        {
            AttackWave wave = waves[i];
            if (!RecipientAvailable(wave.Target) || !RefreshWavePath(wave, presentation))
            {
                foreach (StarParticle particle in particles)
                    if (particle != null && particle.Active && particle.Wave == wave) ResetStar(particle);
                if (presentation != null) presentation.RemoveAttackRoute(wave.TargetBoard);
                waves.RemoveAt(i);
                continue;
            }
            wave.Elapsed += delta;
            if (!wave.HitPlayed && wave.Elapsed >= wave.FlightDuration)
            {
                wave.HitPlayed = true;
                PlayAttackSound(attackHitSound);
            }
        }
        foreach (StarParticle particle in particles)
            if (particle != null && particle.Active) UpdateStar(particle);
        for (int i = waves.Count - 1; i >= 0; i--)
            if (waves[i].Elapsed >= waves[i].Duration) waves.RemoveAt(i);
    }

    private bool RefreshWavePath(AttackWave wave, BoardPresentation presentation)
    {
        if (presentation == null || !presentation.isActiveAndEnabled || wave.TargetBoard == null
            || !wave.TargetBoard.isActiveAndEnabled) return false;
        int count = presentation.GetTargetingPath(wave.TargetBoard, wave.Path);
        if (count < 2 || count > wave.Path.Length) return false;
        float length = 0f;
        for (int i = 1; i < count; i++) length += Vector3.Distance(wave.Path[i - 1], wave.Path[i]);
        if (length <= .0001f || float.IsNaN(length) || float.IsInfinity(length)) return false;
        wave.PathCount = count;
        wave.PathLength = length;
        wave.TargetPosition = wave.Path[count - 1];
        foreach (StarParticle particle in particles)
        {
            if (particle == null || !particle.Active || particle.Wave != wave) continue;
            particle.Start = wave.Path[0];
            particle.End = wave.TargetPosition;
        }
        return true;
    }

    private bool RecipientAvailable(GarbageCombat target)
    {
        if (target == null || !target.isActiveAndEnabled || arena == null) return false;
        BotPlayer opponent = target.GetComponent<BotPlayer>();
        if (opponent == null || !opponent.isActiveAndEnabled || opponent.IsGameOver) return false;
        foreach (BotPlayer registered in arena.Opponents)
            if (registered == opponent) return true;
        return false;
    }

    private void UpdateStar(StarParticle particle)
    {
        if (particle.Renderer == null) { particle.Active = false; return; }
        float age = particle.Wave.Elapsed - particle.Delay;
        if (age < 0f) { particle.Renderer.enabled = false; return; }
        particle.Renderer.enabled = true;
        Sprite art = age < particle.Wave.FlightDuration || attackBurstSprite == null
            ? particle.HomeSprite : attackBurstSprite;
        particle.Renderer.sprite = art;
        float scale;
        float alpha = 1f;
        if (age < particle.Wave.FlightDuration)
        {
            float t = Mathf.Clamp01(age / particle.Wave.FlightDuration);
            float eased = t * t * (3f - 2f * t);
            particle.Renderer.transform.position = PositionAlongPath(particle.Wave, eased);
            scale = .7f + Mathf.Sin(t * Mathf.PI) * .3f;
        }
        else
        {
            float t = (age - particle.Wave.FlightDuration) / particle.Wave.BurstDuration;
            if (t >= 1f) { ResetStar(particle); return; }
            particle.Renderer.transform.position = particle.End + particle.Burst * t;
            scale = Mathf.Lerp(1.25f, .25f, t);
            alpha = (1f - t) * (1f - t);
        }
        Vector2 artworkScale = art != null && particle.HomeSprite != null ? new Vector2(
            particle.HomeSprite.bounds.size.x / Mathf.Max(.0001f, art.bounds.size.x),
            particle.HomeSprite.bounds.size.y / Mathf.Max(.0001f, art.bounds.size.y)) : Vector2.one;
        particle.Renderer.transform.localScale = new Vector3(particle.HomeScale.x * scale * artworkScale.x,
            particle.HomeScale.y * scale * artworkScale.y, particle.HomeScale.z);
        particle.Renderer.transform.localRotation = particle.HomeRotation
            * Quaternion.Euler(0f, 0f, particle.Angle + particle.Spin * age);
        Color color = particle.HomeColor;
        color.a *= alpha;
        particle.Renderer.color = color;
    }

    private void UpdateText(TextPopup popup, float delta)
    {
        if (!popup.Showing || popup.Label == null) return;
        popup.Age += delta;
        float duration = Mathf.Max(.1f, feedbackDuration);
        if (popup.Age >= duration) { HideText(popup); return; }
        float pop = Mathf.Clamp01(popup.Age / .16f);
        float scale = 1f + .1f * (1f - pop) * (1f - pop);
        popup.Label.transform.localScale = Vector3.Scale(popup.Scale, new Vector3(scale, scale, 1f));
        Color color = popup.Color;
        color.a *= 1f - Mathf.InverseLerp(duration * .55f, duration, popup.Age);
        popup.Label.color = color;
    }

    private static void ShowText(TextPopup popup, string text)
    {
        if (popup.Label == null) return;
        if (string.IsNullOrEmpty(text)) { HideText(popup); return; }
        popup.Age = 0f;
        popup.Showing = true;
        popup.Label.text = text;
        popup.Label.color = popup.Color;
        popup.Label.gameObject.SetActive(true);
        popup.Label.enabled = true;
        popup.Label.transform.localScale = Vector3.Scale(popup.Scale, new Vector3(1.1f, 1.1f, 1f));
    }

    private static void HideText(TextPopup popup)
    {
        popup.Showing = false;
        popup.Age = 0f;
        if (popup.Label == null) return;
        popup.Label.enabled = false;
        popup.Label.text = "";
        popup.Label.color = popup.Color;
        popup.Label.transform.localScale = popup.Scale;
    }

    private bool ShowLogo(int index, bool show)
    {
        SpritePopup popup = logoPopups[index];
        if (!show || popup.Renderer == null || popup.Renderer.sprite == null)
        {
            HideLogo(popup);
            return false;
        }
        popup.Age = 0f;
        popup.Showing = true;
        popup.Renderer.gameObject.SetActive(true);
        popup.Renderer.enabled = true;
        popup.Renderer.color = popup.Color;
        popup.Renderer.transform.localScale = popup.Scale * 1.1f;
        return true;
    }

    private void UpdateLogo(SpritePopup popup, float delta)
    {
        if (!popup.Showing || popup.Renderer == null) return;
        popup.Age += delta;
        float duration = Mathf.Max(.1f, feedbackDuration);
        if (popup.Age >= duration) { HideLogo(popup); return; }
        float pop = 1f - Mathf.Clamp01(popup.Age / .16f);
        popup.Renderer.transform.localScale = popup.Scale * (1f + .1f * pop * pop);
        Color color = popup.Color;
        color.a *= 1f - Mathf.InverseLerp(duration * .55f, duration, popup.Age);
        popup.Renderer.color = color;
    }

    private static void HideLogo(SpritePopup popup)
    {
        popup.Showing = false;
        popup.Age = 0f;
        if (popup.Renderer == null) return;
        popup.Renderer.enabled = false;
        popup.Renderer.color = popup.Color;
        popup.Renderer.transform.localScale = popup.Scale;
    }

    private bool HasText()
    {
        if (popups != null) foreach (TextPopup popup in popups) if (popup.Showing) return true;
        if (logoPopups != null) foreach (SpritePopup popup in logoPopups) if (popup.Showing) return true;
        return false;
    }

    private void PlayAttackSound(AudioClip clip)
    {
        if (attackSource != null && clip != null) attackSource.PlayOneShot(clip);
    }

    /// <summary>Stops only the dedicated feedback sources and resets the existing visual pool.</summary>
    public void ClearFeedback()
    {
        waves.Clear();
        if (particles != null) foreach (StarParticle particle in particles) if (particle != null) ResetStar(particle);
        if (popups != null) foreach (TextPopup popup in popups) HideText(popup);
        if (logoPopups != null) foreach (SpritePopup popup in logoPopups) HideLogo(popup);
        if (calloutSource != null) calloutSource.Stop();
        if (attackSource != null) attackSource.Stop();
        BoardPresentation presentation = player != null ? player.GetComponent<BoardPresentation>()
            : GetComponent<BoardPresentation>();
        if (presentation != null) presentation.ClearAttackRoutes();
    }

    private static void ResetStar(StarParticle particle)
    {
        particle.Active = false;
        particle.Wave = null;
        if (particle.Renderer == null) return;
        particle.Renderer.enabled = false;
        particle.Renderer.sprite = particle.HomeSprite;
        particle.Renderer.color = particle.HomeColor;
        particle.Renderer.transform.localPosition = particle.HomePosition;
        particle.Renderer.transform.localRotation = particle.HomeRotation;
        particle.Renderer.transform.localScale = particle.HomeScale;
    }

    private void CacheAuthoredVisuals()
    {
        if (logoPopups == null)
        {
            SpriteRenderer[] logos = { tetrisLogo, tSpinLogo, allClearLogo, comboLogo, backToBackLogo,
                singleLogo, doubleLogo, tripleLogo };
            logoPopups = new SpritePopup[logos.Length];
            for (int i = 0; i < logos.Length; i++)
                logoPopups[i] = new SpritePopup { Renderer = logos[i], Scale = logos[i] != null
                    ? logos[i].transform.localScale : Vector3.one, Color = logos[i] != null ? logos[i].color : Color.white };
        }
        if (popups == null)
        {
            TMP_Text[] labels = { clearText, comboText, attackText };
            popups = new TextPopup[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                popups[i] = new TextPopup { Label = labels[i], Scale = labels[i] != null
                    ? labels[i].transform.localScale : Vector3.one, Color = labels[i] != null ? labels[i].color : Color.white };
        }
        if (particles != null) return;
        particles = new StarParticle[Mathf.Min(stars != null ? stars.Length : 0, 192)];
        for (int i = 0; i < particles.Length; i++)
        {
            SpriteRenderer renderer = stars[i];
            if (renderer == null) continue;
            particles[i] = new StarParticle
            {
                Renderer = renderer, HomePosition = renderer.transform.localPosition,
                HomeRotation = renderer.transform.localRotation, HomeScale = renderer.transform.localScale,
                HomeColor = renderer.color, HomeSprite = renderer.sprite
            };
        }
    }
}
