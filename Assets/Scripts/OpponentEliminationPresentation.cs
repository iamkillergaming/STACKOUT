using TMPro;
using UnityEngine;

/// <summary>Animates a credited knockout and retains its original medal without changing board data.</summary>
[DisallowMultipleComponent]
public sealed class OpponentEliminationPresentation : MonoBehaviour
{
    [SerializeField] private GameObject lockedArtwork;
    [SerializeField] private GameObject knockoutRoot;
    [SerializeField] private SpriteRenderer knockoutSprite;
    [SerializeField] private TMP_Text knockoutText;
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private SpriteRenderer eliminatedMedal;
    [SerializeField] private Sprite playerKnockoutMedal;
    [SerializeField] private Sprite otherKnockoutMedal;
    [SerializeField] private SpriteRenderer[] knockoutEffects = new SpriteRenderer[0];
    [SerializeField] private SpriteRenderer[] knockoutLetters = new SpriteRenderer[0];
    [SerializeField, Min(.05f)] private float knockoutDuration = .85f;

    private struct SpritePose
    {
        public SpriteRenderer Renderer;
        public Vector3 Position;
        public Vector3 Scale;
        public Quaternion Rotation;
        public Color Color;

        public SpritePose(SpriteRenderer renderer)
        {
            Renderer = renderer;
            Position = renderer != null ? renderer.transform.localPosition : Vector3.zero;
            Scale = renderer != null ? renderer.transform.localScale : Vector3.one;
            Rotation = renderer != null ? renderer.transform.localRotation : Quaternion.identity;
            Color = renderer != null ? renderer.color : UnityEngine.Color.white;
        }

        public void Apply(float size, float distance, float angle, float opacity)
        {
            if (Renderer == null) return;
            Renderer.transform.localPosition = Position * distance;
            Renderer.transform.localScale = Scale * size;
            Renderer.transform.localRotation = Rotation * Quaternion.Euler(0f, 0f, angle);
            Color color = Color;
            color.a *= Mathf.Clamp01(opacity);
            Renderer.color = color;
        }
    }

    private Vector3 baseScale;
    private Quaternion baseRotation;
    private Color baseColor;
    private Color baseTextColor;
    private float medalWidth;
    private float elapsed;
    private bool captured;
    private bool playerCredit;
    private SpritePose[] effectPoses;
    private SpritePose[] letterPoses;

    public int DisplayedRank { get; private set; }
    public bool IsAnimating { get; private set; }

    private void Awake() => CaptureDefaults();

    private void OnEnable()
    {
        CaptureDefaults();
        if (DisplayedRank > 0) ShowRank();
        else ResetView();
    }

    private void CaptureDefaults()
    {
        if (captured) return;
        if (knockoutRoot != null)
        {
            baseScale = knockoutRoot.transform.localScale;
            baseRotation = knockoutRoot.transform.localRotation;
        }
        if (knockoutSprite != null) baseColor = knockoutSprite.color;
        if (knockoutText != null) baseTextColor = knockoutText.color;
        if (eliminatedMedal != null && eliminatedMedal.sprite != null)
            medalWidth = eliminatedMedal.sprite.bounds.size.x * eliminatedMedal.transform.localScale.x;
        effectPoses = CapturePoses(knockoutEffects);
        letterPoses = CapturePoses(knockoutLetters);
        captured = true;
    }

    private static SpritePose[] CapturePoses(SpriteRenderer[] renderers)
    {
        var poses = new SpritePose[renderers.Length];
        for (int i = 0; i < poses.Length; i++) poses[i] = new SpritePose(renderers[i]);
        return poses;
    }

    /// <summary>One presentation per elimination. Only a newly credited player KO animates.</summary>
    public void Show(int rank, bool creditedToPlayer)
    {
        if (rank < 1 || DisplayedRank > 0) return;
        CaptureDefaults();
        DisplayedRank = rank;
        playerCredit = creditedToPlayer;
        if (eliminatedMedal != null) eliminatedMedal.gameObject.SetActive(false);
        if (lockedArtwork != null) lockedArtwork.SetActive(false);
        if (rankText != null)
        {
            rankText.text = "#" + rank.ToString("00");
            rankText.color = playerCredit ? new Color(1f, .84f, .28f) : new Color(.82f, .88f, .96f);
            rankText.gameObject.SetActive(false);
        }
        if (!creditedToPlayer || !isActiveAndEnabled || knockoutRoot == null
            || (knockoutText == null && knockoutSprite == null))
        {
            ShowRank();
            return;
        }
        elapsed = 0f;
        IsAnimating = true;
        knockoutRoot.SetActive(true);
        if (knockoutText != null)
        {
            // Prefer the supplied K.O. graphic; older saves can still use their AJN label.
            knockoutText.gameObject.SetActive(knockoutSprite == null);
            knockoutText.enabled = knockoutSprite == null;
        }
        if (knockoutSprite != null)
        {
            knockoutSprite.gameObject.SetActive(true);
            knockoutSprite.enabled = true;
        }
        AnimatePose(0f);
    }

    private void Update()
    {
        if (!IsAnimating) return;
        elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(.05f, knockoutDuration));
        if (progress >= 1f) ShowRank();
        else AnimatePose(progress);
    }

    private void AnimatePose(float progress)
    {
        float scale;
        if (progress < .2f)
        {
            float t = progress / .2f;
            scale = Mathf.Lerp(.28f, 1.16f, 1f - Mathf.Pow(1f - t, 3f));
        }
        else if (progress < .38f) scale = Mathf.Lerp(1.16f, .94f, (progress - .2f) / .18f);
        else if (progress < .55f) scale = Mathf.Lerp(.94f, 1f, (progress - .38f) / .17f);
        else scale = progress < .83f ? 1f : Mathf.Lerp(1f, .85f, (progress - .83f) / .17f);
        float wobble = Mathf.Sin(progress * Mathf.PI * 5f) * Mathf.Exp(-progress * 7f);
        knockoutRoot.transform.localScale = new Vector3(baseScale.x * scale * (1f + .09f * wobble),
            baseScale.y * scale * (1f - .09f * wobble), baseScale.z);
        float angle = Mathf.Lerp(-12f, -4f, Mathf.Clamp01(progress / .2f))
            + Mathf.Sin(progress * Mathf.PI * 8f) * 3f * Mathf.Exp(-progress * 6f);
        knockoutRoot.transform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);
        float opacity = progress < .83f ? 1f : 1f - (progress - .83f) / .17f;
        if (knockoutSprite != null)
        {
            Color color = baseColor;
            color.a *= opacity;
            knockoutSprite.color = color;
        }
        if (knockoutText != null)
        {
            Color color = baseTextColor;
            color.a *= opacity;
            knockoutText.color = color;
        }
        // Burst/rays open first, followed by staggered letters and outward-moving stars.
        for (int i = 0; i < effectPoses.Length; i++)
        {
            if (i < 2)
                effectPoses[i].Apply(Mathf.Lerp(.5f, 1.15f, progress), 1f,
                    i == 1 ? 55f * progress : 0f, Mathf.Clamp01((.8f - progress) / .5f));
            else
                effectPoses[i].Apply(Mathf.Lerp(.7f, .25f, progress), Mathf.Lerp(.35f, 1.15f, progress),
                    (i % 2 == 0 ? 1f : -1f) * 150f * progress, 1f - progress);
        }
        for (int i = 0; i < letterPoses.Length; i++)
        {
            float appearance = Mathf.Clamp01((progress - .025f * i) / .15f);
            float bounce = 1f + .2f * Mathf.Sin(appearance * Mathf.PI);
            letterPoses[i].Apply(Mathf.Lerp(.55f, bounce, appearance), 1f,
                Mathf.Sin(progress * Mathf.PI * 4f + i) * 4f,
                appearance * Mathf.Clamp01((.85f - progress) / .25f));
        }
    }

    private void ShowRank()
    {
        IsAnimating = false;
        RestorePose();
        if (knockoutRoot != null) knockoutRoot.SetActive(false);
        if (lockedArtwork != null) lockedArtwork.SetActive(DisplayedRank == 0);
        if (rankText != null) rankText.gameObject.SetActive(DisplayedRank > 0);
        ShowMedal();
    }

    private void ShowMedal()
    {
        if (eliminatedMedal == null) return;
        Sprite medal = playerCredit ? playerKnockoutMedal : otherKnockoutMedal;
        bool visible = DisplayedRank > 0 && medal != null;
        eliminatedMedal.sprite = medal;
        if (visible && medalWidth > 0f)
        {
            float scale = medalWidth / medal.bounds.size.x;
            eliminatedMedal.transform.localScale = new Vector3(scale, scale, 1f);
        }
        eliminatedMedal.color = Color.white;
        eliminatedMedal.enabled = visible;
        eliminatedMedal.gameObject.SetActive(visible);
    }

    /// <summary>Restores rendering for a new round. Board occupancy is owned by BotPlayer/Board.</summary>
    public void ResetView()
    {
        CaptureDefaults();
        DisplayedRank = 0;
        IsAnimating = false;
        elapsed = 0f;
        playerCredit = false;
        RestorePose();
        if (lockedArtwork != null) lockedArtwork.SetActive(true);
        if (knockoutRoot != null) knockoutRoot.SetActive(false);
        if (eliminatedMedal != null) eliminatedMedal.gameObject.SetActive(false);
        if (rankText != null)
        {
            rankText.text = "";
            rankText.gameObject.SetActive(false);
        }
    }

    private void RestorePose()
    {
        if (knockoutRoot != null)
        {
            knockoutRoot.transform.localScale = baseScale;
            knockoutRoot.transform.localRotation = baseRotation;
        }
        if (knockoutSprite != null) knockoutSprite.color = baseColor;
        if (knockoutText != null) knockoutText.color = baseTextColor;
        if (effectPoses != null) foreach (SpritePose pose in effectPoses) pose.Apply(1f, 1f, 0f, 1f);
        if (letterPoses != null) foreach (SpritePose pose in letterPoses) pose.Apply(1f, 1f, 0f, 1f);
    }

    private void OnDisable()
    {
        // Re-enabling a dead opponent retains its rank, never a delayed/replayed K.O. pop.
        if (DisplayedRank > 0) ShowRank();
    }
}
