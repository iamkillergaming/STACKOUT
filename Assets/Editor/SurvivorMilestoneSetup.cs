#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Authors saved milestone text and its independent voice source over the player's board.</summary>
public static class SurvivorMilestoneSetup
{
    public static void Author(BattleArena arena, FallingPiece player)
    {
        Board board = player.GetComponent<Board>();
        float cell = board.CellSize;
        Transform presentation = Child(board.transform, "Match presentation");
        Transform banner = Child(presentation, "Survivor announcement");
        Position(banner, Vector3.zero);
        TMP_FontAsset font = FontSetup.GetFont();
        TextMeshPro label = Label(Child(banner, "Milestone"), Vector2.zero, cell, font, Color.white, 40);
        // A saved AJN text shadow keeps the shared font material and every other label unchanged.
        TextMeshPro shadow = Label(Child(label.transform, "Shadow"), new Vector2(.045f, -.06f) * cell,
            cell, font, new Color(0f, 0f, 0f, .95f), 39);

        Transform audioRoot = Child(presentation, "Survivor announcement audio");
        Position(audioRoot, Vector3.zero);
        AudioSource source = audioRoot.GetComponent<AudioSource>();
        if (source == null) source = Undo.AddComponent<AudioSource>(audioRoot.gameObject);
        Undo.RecordObject(source, "Author survivor announcement audio");
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 1f;
        source.pitch = 1f;
        source.mute = false;
        Record(source);

        SurvivorMilestonePresentation component = arena.GetComponent<SurvivorMilestonePresentation>();
        if (component == null) component = Undo.AddComponent<SurvivorMilestonePresentation>(arena.gameObject);
        var data = new SerializedObject(component);
        Ref(data, "arena", arena);
        Ref(data, "player", player);
        Ref(data, "bannerRoot", banner.gameObject);
        Ref(data, "milestoneText", label);
        Ref(data, "shadowText", shadow);
        Ref(data, "announcementSource", source);
        Ref(data, "fiftyPlayersSound", Clip("me_game_remain_1.wav"));
        Ref(data, "tenPlayersSound", Clip("me_game_remain_2.wav"));
        Property(data, "duration").floatValue = 1.6f;
        data.ApplyModifiedPropertiesWithoutUndo();
        Record(component);
        banner.gameObject.SetActive(false);
        Record(banner.gameObject);
    }

    private static TextMeshPro Label(Transform child, Vector2 position, float cell,
        TMP_FontAsset font, Color color, int sortingOrder)
    {
        TextMeshPro label = child.GetComponent<TextMeshPro>();
        if (label == null) label = Undo.AddComponent<TextMeshPro>(child.gameObject);
        Undo.RecordObject(label, "Author survivor milestone text");
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.text = "50 PLAYERS LEFT";
        label.fontSize = 100f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = 100f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.sortingOrder = sortingOrder;
        label.margin = Vector4.zero;
        label.enabled = true;
        RectTransform rect = label.rectTransform;
        Undo.RecordObject(rect, "Position survivor milestone text");
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = new Vector2(8.6f, 1.6f) * cell;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition3D = position;
        label.ForceMeshUpdate(true);
        Record(rect);
        Record(label);
        return label;
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var instance = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(instance, "Author survivor milestones");
            instance.transform.SetParent(parent, false);
            child = instance.transform;
        }
        child.gameObject.SetActive(true);
        Record(child.gameObject);
        return child;
    }

    private static void Position(Transform child, Vector3 position)
    {
        Undo.RecordObject(child, "Position survivor milestones");
        child.localPosition = position;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        Record(child);
    }

    private static AudioClip Clip(string filename)
    {
        string path = "Assets/General Sounds/" + filename;
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) throw new InvalidOperationException("Missing supplied announcement sound: " + path);
        return clip;
    }

    private static SerializedProperty Property(SerializedObject data, string name) => data.FindProperty(name)
        ?? throw new InvalidOperationException(data.targetObject.GetType().Name + " is missing " + name);
    private static void Ref(SerializedObject data, string name, Object value) => Property(data, name).objectReferenceValue = value;

    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
