#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Assigns supplied phase, selector, standings, and badge artwork to the saved match presentation.</summary>
public static class OriginalPhaseArtSetup
{
    public static void Author(BattleArena arena, FallingPiece player)
    {
        MatchFlow flow = arena != null ? arena.GetComponent<MatchFlow>() : null;
        if (flow == null || player == null) throw new InvalidOperationException("Author MatchFlow before its original artwork.");
        var data = new SerializedObject(flow);
        TMP_Text countdown = Property(data, "countdownText").objectReferenceValue as TMP_Text;
        Transform root = player.GetComponent<Board>().transform.Find("Match presentation");
        if (countdown == null || root == null) throw new InvalidOperationException("The saved match presentation is incomplete.");

        // Countdown words and numerals use AJN Aokane, like every other game label.
        SpriteRenderer phase = Property(data, "phaseArtwork").objectReferenceValue as SpriteRenderer;
        if (phase != null) { phase.enabled = false; Record(phase); }
        countdown.enabled = true;
        Ref(data, "phaseArtwork", null);
        Ref(data, "readyArtwork", null);
        Ref(data, "goArtwork", null);
        Refs(data, "countdownArtwork", new Sprite[0]);

        Sprite normal = OriginalArtAssets.CommonSlice("_texture/game_tactics00^t.png", "Original tactic pill",
            new Rect(128, 112, 208, 48));
        Sprite selected = OriginalArtAssets.CommonSprite("_texture/game_tactics01^q.png");
        Ref(data, "modeNormalArtwork", normal);
        Ref(data, "modeSelectedArtwork", selected);
        var owned = new System.Collections.Generic.HashSet<SpriteRenderer>();
        if (phase != null) owned.Add(phase);
        SerializedProperty panels = Property(data, "modePanels");
        for (int i = 0; i < panels.arraySize; i++)
        {
            var renderer = panels.GetArrayElementAtIndex(i).objectReferenceValue as SpriteRenderer;
            if (renderer == null) continue;
            ReplaceArtwork(renderer, normal);
            renderer.color = Color.white;
            owned.Add(renderer);
        }

        var badges = new Sprite[16];
        for (int i = 0; i < badges.Length; i++)
            badges[i] = OriginalArtAssets.ThemeSlice("badge00.png", "Original badge stage " + i.ToString("00"),
                new Rect((i % 4) * 56, (3 - i / 4) * 56, 56, 56));
        // Finish atlas imports before retaining references for the saved renderers.
        for (int i = 0; i < badges.Length; i++)
            badges[i] = OriginalArtAssets.ThemeSlice("badge00.png", "Original badge stage " + i.ToString("00"),
                new Rect((i % 4) * 56, (3 - i / 4) * 56, 56, 56));
        Refs(data, "medalArtwork", badges);
        SerializedProperty icons = Property(data, "medalIcons");
        for (int i = 0; i < icons.arraySize; i++)
        {
            var renderer = icons.GetArrayElementAtIndex(i).objectReferenceValue as SpriteRenderer;
            if (renderer == null) continue;
            ReplaceArtwork(renderer, badges[0], true);
            renderer.color = new Color(.3f, .32f, .35f);
            owned.Add(renderer);
        }
        var progress = Property(data, "medalProgress").objectReferenceValue as SpriteRenderer;
        if (progress != null)
        {
            ReplaceArtwork(progress, OriginalArtAssets.CommonSprite("_texture/get_point_gage2^q.png"));
            progress.color = Color.white;
            owned.Add(progress);
        }

        Sprite neutral = OriginalArtAssets.CommonSprite("_texture/footer_base^q.png");
        Sprite resultPanel = OriginalArtAssets.CommonSprite("_texture/game_ranking_element0^q.png");
        foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.GetComponentInParent<MatchmakingGridPresentation>(true) != null) continue;
            if (owned.Contains(renderer)) continue;
            bool background = renderer.name == "Panel" || renderer.name.EndsWith(" stripe", StringComparison.Ordinal);
            ReplaceArtwork(renderer, background ? resultPanel : neutral);
            // The supplied ranking strip is already black; keep its original opacity instead of tinting twice.
            if (background) renderer.color = Color.white;
            Record(renderer);
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        Record(flow);
        Record(countdown);
        foreach (SpriteRenderer renderer in owned) { Record(renderer); Record(renderer.transform); }
    }

    private static void ReplaceArtwork(SpriteRenderer renderer, Sprite sprite, bool preserveAspect = false)
    {
        Vector3 size = renderer.sprite != null
            ? Vector3.Scale(renderer.sprite.bounds.size, renderer.transform.localScale) : Vector3.one;
        renderer.sprite = sprite;
        if (preserveAspect)
        {
            float scale = Mathf.Min(Mathf.Abs(size.x / sprite.bounds.size.x), Mathf.Abs(size.y / sprite.bounds.size.y));
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
        }
        else renderer.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        Record(renderer.transform);
    }

    private static SerializedProperty Property(SerializedObject data, string name) => data.FindProperty(name)
        ?? throw new InvalidOperationException(data.targetObject.GetType().Name + " is missing " + name);
    private static void Ref(SerializedObject data, string name, Object value) => Property(data, name).objectReferenceValue = value;
    private static void Refs(SerializedObject data, string name, Sprite[] values)
    {
        SerializedProperty property = Property(data, name);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
