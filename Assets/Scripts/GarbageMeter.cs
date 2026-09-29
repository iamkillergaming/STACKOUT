using UnityEngine;

/// <summary>Colors twelve authored segments. Never creates UI or moves the board.</summary>
[DisallowMultipleComponent]
public sealed class GarbageMeter : MonoBehaviour
{
    [SerializeField] private GarbageCombat combat;
    [Tooltip("Twelve saved segments, bottom first, matching incoming attack order.")]
    [SerializeField] private SpriteRenderer[] segments = new SpriteRenderer[12];
    [SerializeField] private Color queuedColor = new Color(.55f, .57f, .63f);
    [SerializeField] private Color warningColor = new Color(1f, .83f, .15f);
    [SerializeField] private Color imminentColor = new Color(1f, .16f, .22f);

    private void Awake()
    {
        if (combat == null) combat = GetComponentInParent<GarbageCombat>();
        Refresh();
    }

    private void LateUpdate() => Refresh();

    public void Refresh()
    {
        for (int i = 0; i < segments.Length; i++)
        {
            SpriteRenderer segment = segments[i];
            if (segment == null) continue;
            GarbageStage stage = combat != null ? combat.GetPendingStage(i) : GarbageStage.None;
            segment.enabled = stage != GarbageStage.None;
            segment.color = stage == GarbageStage.Imminent ? imminentColor
                : stage == GarbageStage.Warning ? warningColor : queuedColor;
        }
    }
}
