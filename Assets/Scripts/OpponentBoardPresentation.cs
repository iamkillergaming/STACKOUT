using UnityEngine;

/// <summary>Plays the saved arrival sprites and target reticle without moving the opponent's board.</summary>
[DisallowMultipleComponent]
public sealed class OpponentBoardPresentation : MonoBehaviour
{
    [SerializeField] private GameObject arrivalRoot;
    [SerializeField] private SpriteRenderer arrivalSpiral;
    [SerializeField] private SpriteRenderer arrivalFlash;
    [SerializeField] private Sprite[] arrivalFrames;
    [SerializeField, Min(.1f)] private float arrivalDuration = .6f;
    [SerializeField] private GameObject cursorRoot;
    [SerializeField] private SpriteRenderer cursorCore;
    [SerializeField] private SpriteRenderer cursorRing;
    [SerializeField] private SpriteRenderer cursorFlare;

    private Vector3 spiralScale, coreScale, ringScale, flareScale;
    private Color spiralColor, flashColor, coreColor, ringColor, flareColor;
    private Quaternion ringRotation;
    private bool captured, arrived, eliminated;
    private float arrivalElapsed, cursorElapsed;

    public bool IsArriving { get; private set; }
    public bool IsTargeted { get; private set; }

    private void Awake() => Capture();

    private void Capture()
    {
        if (captured) return;
        captured = true;
        if (arrivalSpiral != null) { spiralScale = arrivalSpiral.transform.localScale; spiralColor = arrivalSpiral.color; }
        if (arrivalFlash != null) flashColor = arrivalFlash.color;
        if (cursorCore != null) { coreScale = cursorCore.transform.localScale; coreColor = cursorCore.color; }
        if (cursorRing != null)
        {
            ringScale = cursorRing.transform.localScale;
            ringRotation = cursorRing.transform.localRotation;
            ringColor = cursorRing.color;
        }
        if (cursorFlare != null) { flareScale = cursorFlare.transform.localScale; flareColor = cursorFlare.color; }
    }

    /// <summary>Called after a successful lobby admission, never from OnEnable.</summary>
    public void BeginArrival()
    {
        if (!isActiveAndEnabled || arrived || eliminated || arrivalRoot == null) return;
        Capture();
        arrived = true;
        IsArriving = true;
        arrivalElapsed = 0f;
        arrivalRoot.SetActive(true);
        ShowArrivalPose(0f);
    }

    public void FinishArrival()
    {
        IsArriving = false;
        if (arrivalRoot != null) arrivalRoot.SetActive(false);
    }

    public void SetTargeted(bool selected)
    {
        selected &= isActiveAndEnabled && !eliminated;
        if (selected == IsTargeted) return;
        Capture();
        IsTargeted = selected;
        cursorElapsed = 0f;
        if (cursorRoot != null) cursorRoot.SetActive(selected);
        if (selected) ShowCursorPose(0f);
    }

    public void ShowElimination()
    {
        eliminated = true;
        FinishArrival();
        SetTargeted(false);
    }

    public void ResetView()
    {
        Capture();
        FinishArrival();
        SetTargeted(false);
        arrived = eliminated = false;
        arrivalElapsed = cursorElapsed = 0f;
        ShowArrivalPose(0f);
        ShowCursorPose(0f);
        if (cursorRoot != null) cursorRoot.SetActive(false);
    }

    private void OnDisable()
    {
        FinishArrival();
        SetTargeted(false);
    }

    private void Update()
    {
        if (IsArriving)
        {
            arrivalElapsed += Time.deltaTime;
            float progress = arrivalElapsed / Mathf.Max(.1f, arrivalDuration);
            if (progress >= 1f) FinishArrival();
            else ShowArrivalPose(progress);
        }
        if (IsTargeted)
        {
            cursorElapsed += Time.deltaTime;
            ShowCursorPose(cursorElapsed);
        }
    }

    private void ShowArrivalPose(float progress)
    {
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.52f, 1f, progress));
        if (arrivalSpiral != null)
        {
            if (arrivalFrames != null && arrivalFrames.Length > 0)
            {
                // Grow through the atlas, then hold the final frame while its glow fades.
                int frame = Mathf.Min(arrivalFrames.Length - 1,
                    Mathf.FloorToInt(Mathf.Clamp01(progress / .65f) * arrivalFrames.Length));
                arrivalSpiral.sprite = arrivalFrames[frame];
            }
            arrivalSpiral.transform.localScale = spiralScale * Mathf.Lerp(.85f, 1.1f, progress);
            arrivalSpiral.color = WithAlpha(spiralColor, fade);
        }
        if (arrivalFlash != null)
            arrivalFlash.color = WithAlpha(flashColor, .65f * (1f - Mathf.SmoothStep(0f, 1f, progress)));
    }

    private void ShowCursorPose(float elapsed)
    {
        float acquire = Mathf.Clamp01(elapsed / .22f);
        float pulse = Mathf.Sin(elapsed * Mathf.PI * 2f / 1.15f);
        if (cursorCore != null)
        {
            cursorCore.transform.localScale = coreScale * Mathf.Lerp(1.25f, 1f, Mathf.SmoothStep(0f, 1f, acquire));
            cursorCore.color = WithAlpha(coreColor, .9f + .1f * pulse);
        }
        if (cursorRing != null)
        {
            cursorRing.transform.localScale = ringScale * (1f + .055f * pulse);
            cursorRing.transform.localRotation = ringRotation * Quaternion.Euler(0f, 0f, 3f * pulse);
            cursorRing.color = WithAlpha(ringColor, .55f + .25f * pulse);
        }
        if (cursorFlare != null)
        {
            cursorFlare.transform.localScale = flareScale * Mathf.Lerp(.65f, 1.1f, acquire);
            cursorFlare.color = WithAlpha(flareColor, .75f * (1f - acquire));
        }
    }

    private static Color WithAlpha(Color color, float opacity)
    {
        color.a *= opacity;
        return color;
    }
}
