using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Updates the existing board sprite and editable match labels in the scene.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Board))]
public sealed class BoardPresentation : MonoBehaviour
{
    public const int TargetingSegmentsPerOpponent = 4;

    [SerializeField] private SpriteRenderer boardSprite;
    [SerializeField] private TMP_Text placeText;
    [SerializeField] private TMP_Text knockoutsText;
    [SerializeField] private TMP_Text badgesText;
    [SerializeField] private GameObject danger;
    [SerializeField] private SpriteRenderer warningFrame;
    [Tooltip("Saved line segments, four consecutive renderers for each of the 98 opponents.")]
    [SerializeField] private SpriteRenderer[] targetingLines = new SpriteRenderer[0];
    [SerializeField, Min(0.001f)] private float targetingLineWidth = 0.035f;
    [Tooltip("Space between targeting lines and the playable 10 by 20 grid.")]
    [SerializeField, Min(0.02f)] private float targetingLineClearance = 0.12f;
    [SerializeField] private AudioSource targetingSoundSource;
    [SerializeField] private AudioClip targetingSound;

    private sealed class OutgoingRoute
    {
        public Board Recipient;
        public float Until;
    }

    private readonly List<OutgoingRoute> outgoingRoutes = new List<OutgoingRoute>();
    private readonly Vector3[] routePoints = new Vector3[TargetingSegmentsPerOpponent + 1];
    private readonly List<BotPlayer> attackers = new List<BotPlayer>();
    private readonly HashSet<BotPlayer> knownTargetingAttackers = new HashSet<BotPlayer>();
    private readonly HashSet<BotPlayer> refreshedTargetingAttackers = new HashSet<BotPlayer>();
    private Board board;
    private FallingPiece fallingPiece;
    private bool playedTargetingSound;

    private bool CanShowWarning => isActiveAndEnabled && fallingPiece != null && fallingPiece.isActiveAndEnabled
        && !fallingPiece.IsGameOver && !fallingPiece.BattlePaused;

    private void Awake()
    {
        board = GetComponent<Board>();
    }

    private void OnEnable()
    {
        UpdateWarning();
    }

    private void OnDisable()
    {
        StopTargetingSound();
        outgoingRoutes.Clear();
        if (danger != null) danger.SetActive(false);
        HideLinesFrom(0);
    }

    private void LateUpdate()
    {
        UpdateWarning();
    }

    /// <summary>The arena supplies opponents that currently target this player.</summary>
    public void SetAttackers(IReadOnlyList<BotPlayer> incomingAttackers)
    {
        attackers.Clear();
        if (incomingAttackers != null)
            foreach (BotPlayer attacker in incomingAttackers)
                if (attacker != null && !attackers.Contains(attacker)) attackers.Add(attacker);

        bool addedAttacker = false;
        refreshedTargetingAttackers.Clear();
        foreach (BotPlayer attacker in attackers)
        {
            if (!attacker.isActiveAndEnabled || attacker.IsGameOver) continue;
            refreshedTargetingAttackers.Add(attacker);
            if (!knownTargetingAttackers.Contains(attacker)) addedAttacker = true;
        }
        // Retain the supplied live set through redraws, pause, and presentation re-enabling.
        // Explicitly clearing targets empties it, so a new round can announce its attackers again.
        knownTargetingAttackers.Clear();
        knownTargetingAttackers.UnionWith(refreshedTargetingAttackers);
        UpdateWarning();
        if (addedAttacker && Application.isPlaying && CanShowWarning
            && targetingSoundSource != null && targetingSoundSource.isActiveAndEnabled && targetingSound != null)
        {
            // Several additions in one refresh produce one cue; outgoing routes never use this path.
            targetingSoundSource.PlayOneShot(targetingSound);
            playedTargetingSound = true;
        }
    }

    private void UpdateWarning()
    {
        if (board == null) board = GetComponent<Board>();
        if (fallingPiece == null) fallingPiece = GetComponent<FallingPiece>();
        bool canShow = CanShowWarning;
        bool targeted = false;
        int lineIndex = 0;
        if (canShow)
        {
            foreach (BotPlayer attacker in attackers)
            {
                if (attacker == null || !attacker.isActiveAndEnabled || attacker.IsGameOver) continue;
                targeted = true;
                if (warningFrame != null && warningFrame.sprite != null
                    && lineIndex + TargetingSegmentsPerOpponent <= targetingLines.Length)
                {
                    Board opponentBoard = attacker.GetComponent<Board>();
                    if (opponentBoard != null)
                    {
                        DrawTargetingRoute(lineIndex, opponentBoard);
                        lineIndex += TargetingSegmentsPerOpponent;
                    }
                }
            }
        }
        if (!canShow) outgoingRoutes.Clear();
        for (int i = outgoingRoutes.Count - 1; i >= 0; i--)
        {
            OutgoingRoute route = outgoingRoutes[i];
            BotPlayer recipient = route.Recipient != null ? route.Recipient.GetComponent<BotPlayer>() : null;
            if (Time.time >= route.Until || recipient == null || !recipient.isActiveAndEnabled || recipient.IsGameOver)
            {
                outgoingRoutes.RemoveAt(i);
                continue;
            }
            // A reciprocal attack shares the existing warning line instead of drawing it twice.
            if (attackers.Contains(recipient) || lineIndex + TargetingSegmentsPerOpponent > targetingLines.Length) continue;
            DrawTargetingRoute(lineIndex, route.Recipient);
            lineIndex += TargetingSegmentsPerOpponent;
        }
        if (danger != null && danger.activeSelf != targeted) danger.SetActive(targeted);
        HideLinesFrom(lineIndex);
        if (!canShow || !targeted) StopTargetingSound();
    }

    private void StopTargetingSound()
    {
        if (!playedTargetingSound) return;
        if (targetingSoundSource != null) targetingSoundSource.Stop();
        playedTargetingSound = false;
    }

    /// <summary>Keeps an outgoing connection visible while its attack stars travel, without activating WATCH OUT.</summary>
    public void ShowAttackRoute(Board recipient, float duration)
    {
        if (recipient == null || duration <= 0f) return;
        foreach (OutgoingRoute route in outgoingRoutes)
            if (route.Recipient == recipient)
            {
                route.Until = Mathf.Max(route.Until, Time.time + duration);
                UpdateWarning();
                return;
            }
        outgoingRoutes.Add(new OutgoingRoute { Recipient = recipient, Until = Time.time + duration });
        UpdateWarning();
    }

    public void ClearAttackRoutes()
    {
        outgoingRoutes.Clear();
        UpdateWarning();
    }

    public void RemoveAttackRoute(Board recipient)
    {
        for (int i = outgoingRoutes.Count - 1; i >= 0; i--)
            if (outgoingRoutes[i].Recipient == recipient) outgoingRoutes.RemoveAt(i);
        UpdateWarning();
    }

    private void DrawTargetingRoute(int firstSegment, Board opponent)
    {
        int count = GetTargetingPath(opponent, routePoints);
        for (int i = 0; i < count - 1; i++)
            DrawTargetingSegment(targetingLines[firstSegment + i],
                board.transform.InverseTransformPoint(routePoints[i]), board.transform.InverseTransformPoint(routePoints[i + 1]));
        HideRouteTail(firstSegment, Mathf.Max(0, count - 1));
    }

    /// <summary>Returns the same world-space path used by the yellow connection, without changing targeting.</summary>
    public int GetTargetingPath(Board opponent, Vector3[] points)
    {
        if (points == null || points.Length < TargetingSegmentsPerOpponent + 1)
            throw new System.ArgumentException("The targeting path buffer needs five points.", nameof(points));
        if (board == null) board = GetComponent<Board>();
        if (opponent == null || board == null || warningFrame == null || warningFrame.sprite == null) return 0;
        float cell = board.CellSize;
        Rect playfield = new Rect(-Board.Width * cell * 0.5f, -Board.Height * cell * 0.5f,
            Board.Width * cell, Board.Height * cell);
        // The decorative frame and side panels can be crossed; protect the gameplay grid only.
        float strokeMargin = (targetingLineWidth * 0.5f + 0.01f) * cell;
        Rect obstacle = Inflate(playfield, strokeMargin);
        Vector3 warningCenter = board.transform.InverseTransformPoint(
            warningFrame.transform.TransformPoint(warningFrame.sprite.bounds.center));
        Vector3 end = OpponentEdge(opponent, warningCenter);
        Vector3 start = WarningOutlineToward(end);
        end = OpponentEdge(opponent, start);
        int count;

        if (SegmentClear(start, end, obstacle))
        {
            // An unobstructed connection needs no detour or fixed attachment point.
            points[0] = start;
            points[1] = end;
            count = 2;
        }
        else
        {
            float clearance = Mathf.Max(targetingLineClearance, targetingLineWidth + 0.02f) * cell;
            Vector3 left = new Vector3(playfield.xMin - clearance, playfield.yMin - clearance, 0f);
            Vector3 right = new Vector3(playfield.xMax + clearance, playfield.yMin - clearance, 0f);
            float leftLength = RouteViaCorner(opponent, left, obstacle, out Vector3 leftStart, out Vector3 leftEnd);
            float rightLength = RouteViaCorner(opponent, right, obstacle, out Vector3 rightStart, out Vector3 rightEnd);
            if (float.IsPositiveInfinity(leftLength) && float.IsPositiveInfinity(rightLength))
            {
                // A miniature placed over the playfield has no clear route in this side-board layout.
                return 0;
            }
            bool useLeft = leftLength <= rightLength;
            Vector3 corner = useLeft ? left : right;
            start = useLeft ? leftStart : rightStart;
            end = useLeft ? leftEnd : rightEnd;

            Vector3 incoming = corner - start;
            Vector3 outgoing = end - corner;
            float trim = Mathf.Min(clearance * 2f,
                Mathf.Min(incoming.magnitude, outgoing.magnitude) * 0.25f);
            Vector3 entry = corner;
            Vector3 middle = corner;
            Vector3 exit = corner;
            bool rounded = false;
            for (int attempt = 0; attempt < 5 && trim > 0.001f; attempt++, trim *= 0.5f)
            {
                entry = corner - incoming.normalized * trim;
                exit = corner + outgoing.normalized * trim;
                middle = entry * 0.25f + corner * 0.5f + exit * 0.25f;
                if (!SegmentClear(start, entry, obstacle) || !SegmentClear(entry, middle, obstacle)
                    || !SegmentClear(middle, exit, obstacle) || !SegmentClear(exit, end, obstacle)) continue;
                rounded = true;
                break;
            }
            if (rounded)
            {
                points[0] = start;
                points[1] = entry;
                points[2] = middle;
                points[3] = exit;
                points[4] = end;
                count = 5;
            }
            else
            {
                points[0] = start;
                points[1] = corner;
                points[2] = end;
                count = 3;
            }
        }
        for (int i = 0; i < count; i++) points[i] = board.transform.TransformPoint(points[i]);
        return count;
    }

    private float RouteViaCorner(Board opponent, Vector3 corner, Rect obstacle, out Vector3 start, out Vector3 end)
    {
        start = WarningOutlineToward(corner);
        end = OpponentEdge(opponent, corner);
        return SegmentClear(start, corner, obstacle) && SegmentClear(corner, end, obstacle)
            ? Vector3.Distance(start, corner) + Vector3.Distance(corner, end) : float.PositiveInfinity;
    }

    // The warning is a capsule: the attachment can slide along its straight edges and rounded ends.
    private Vector3 WarningOutlineToward(Vector3 boardPoint)
    {
        Bounds bounds = warningFrame.sprite.bounds;
        Vector3 local = warningFrame.transform.InverseTransformPoint(board.transform.TransformPoint(boardPoint));
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.y);
        Vector3 spine = new Vector3(Mathf.Clamp(local.x, bounds.min.x + radius, bounds.max.x - radius),
            bounds.center.y, bounds.center.z);
        Vector3 direction = local - spine;
        direction.z = 0f;
        if (direction.sqrMagnitude < 0.000001f) direction = Vector3.down;
        return board.transform.InverseTransformPoint(warningFrame.transform.TransformPoint(spine + direction.normalized * radius));
    }

    private Vector3 OpponentEdge(Board opponent, Vector3 boardPoint)
    {
        var bounds = new Bounds(Vector3.zero,
            new Vector3(Board.Width * opponent.CellSize, Board.Height * opponent.CellSize, 0f));
        return board.transform.InverseTransformPoint(EdgeToward(opponent.transform, bounds,
            board.transform.TransformPoint(boardPoint)));
    }

    private void HideRouteTail(int firstSegment, int used)
    {
        for (int i = used; i < TargetingSegmentsPerOpponent; i++)
            if (targetingLines[firstSegment + i] != null) targetingLines[firstSegment + i].enabled = false;
    }

    private static Rect Inflate(Rect rectangle, float margin)
        => Rect.MinMaxRect(rectangle.xMin - margin, rectangle.yMin - margin,
            rectangle.xMax + margin, rectangle.yMax + margin);

    private static bool SegmentClear(Vector3 start, Vector3 end, Rect rectangle)
    {
        float entry = 0f;
        float exit = 1f;
        Vector3 delta = end - start;
        return !ClipSegment(-delta.x, start.x - rectangle.xMin, ref entry, ref exit)
            || !ClipSegment(delta.x, rectangle.xMax - start.x, ref entry, ref exit)
            || !ClipSegment(-delta.y, start.y - rectangle.yMin, ref entry, ref exit)
            || !ClipSegment(delta.y, rectangle.yMax - start.y, ref entry, ref exit);
    }

    private static bool ClipSegment(float direction, float distance, ref float entry, ref float exit)
    {
        if (Mathf.Abs(direction) < 0.000001f) return distance >= 0f;
        float crossing = distance / direction;
        if (direction < 0f) entry = Mathf.Max(entry, crossing);
        else exit = Mathf.Min(exit, crossing);
        return entry <= exit;
    }

    private void DrawTargetingSegment(SpriteRenderer line, Vector3 start, Vector3 end)
    {
        if (line == null || line.sprite == null) return;
        start = board.transform.TransformPoint(start);
        end = board.transform.TransformPoint(end);
        Transform parent = line.transform.parent;
        if (parent != null)
        {
            start = parent.InverseTransformPoint(start);
            end = parent.InverseTransformPoint(end);
        }
        Vector3 direction = end - start;
        Bounds spriteBounds = line.sprite.bounds;
        Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        Vector3 scale = new Vector3(direction.magnitude / Mathf.Max(spriteBounds.size.x, 0.001f),
            targetingLineWidth * board.CellSize / Mathf.Max(spriteBounds.size.y, 0.001f), 1f);
        line.transform.localRotation = rotation;
        line.transform.localScale = scale;
        line.transform.localPosition = (start + end) * 0.5f - rotation * Vector3.Scale(spriteBounds.center, scale);
        line.enabled = true;
    }

    // Connect at the edges, keeping the line clear of the warning text and miniature's stack.
    private static Vector3 EdgeToward(Transform space, Bounds bounds, Vector3 destination)
    {
        Vector3 direction = space.InverseTransformPoint(destination) - bounds.center;
        direction.z = 0f;
        float ratio = Mathf.Max(Mathf.Abs(direction.x) / Mathf.Max(bounds.extents.x, 0.001f),
            Mathf.Abs(direction.y) / Mathf.Max(bounds.extents.y, 0.001f));
        return space.TransformPoint(bounds.center + direction / Mathf.Max(1f, ratio));
    }

    private void HideLinesFrom(int first)
    {
        for (int i = first; i < targetingLines.Length; i++)
            if (targetingLines[i] != null) targetingLines[i].enabled = false;
    }

    /// <summary>Includes the authored sprite, match values, and warning in board-local space.</summary>
    public Rect GetLocalBounds(Board source)
    {
        float size = source.CellSize;
        Rect bounds = Rect.MinMaxRect(-8.1f * size, -10.9f * size, 8.1f * size, 10.3f * size);
        if (boardSprite != null && boardSprite.sprite != null)
            bounds = RendererBounds(boardSprite, source.transform);

        IncludeLabel(ref bounds, placeText, source.transform);
        IncludeLabel(ref bounds, knockoutsText, source.transform);
        IncludeLabel(ref bounds, badgesText, source.transform);
        if (danger != null)
        {
            foreach (Renderer renderer in danger.GetComponentsInChildren<Renderer>(true))
                Include(ref bounds, RendererBounds(renderer, source.transform));
        }
        return bounds;
    }

    /// <summary>Supply real match values when available; unknown player counts display dashes.</summary>
    public void SetMatchStats(int remaining, int total, int koCount, int badgeCount)
    {
        if (placeText != null)
            placeText.text = remaining >= 0 && total > 0
                ? remaining.ToString() + "/" + total.ToString() : "--/--";
        if (knockoutsText != null)
            knockoutsText.text = Mathf.Max(0, koCount).ToString("00");
        if (badgesText != null)
            badgesText.text = Mathf.Max(0, badgeCount).ToString("000");
    }

    private static void IncludeLabel(ref Rect bounds, TMP_Text label, Transform source)
    {
        if (label == null) return;
        // The text rectangle is available before TMP has generated its first text mesh.
        Rect rect = label.rectTransform.rect;
        Rect local = TransformBounds(new Bounds(rect.center, rect.size), label.transform, source);
        Include(ref bounds, local);
    }

    private static Rect RendererBounds(Renderer renderer, Transform source)
    {
        return TransformBounds(renderer.localBounds, renderer.transform, source);
    }

    private static Rect TransformBounds(Bounds bounds, Transform target, Transform source)
    {
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                Vector3 corner = bounds.center + new Vector3(x * bounds.extents.x, y * bounds.extents.y, 0f);
                Vector3 point = source.InverseTransformPoint(target.TransformPoint(corner));
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Include(ref Rect bounds, Rect addition)
    {
        bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, addition.xMin),
            Mathf.Min(bounds.yMin, addition.yMin), Mathf.Max(bounds.xMax, addition.xMax),
            Mathf.Max(bounds.yMax, addition.yMax));
    }
}
