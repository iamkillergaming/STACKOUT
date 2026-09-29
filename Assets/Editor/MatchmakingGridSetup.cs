#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Authors the original matchmaking indicator directly over the player's board.</summary>
public static class MatchmakingGridSetup
{
    private static readonly Color Idle = new Color(.19f, .20f, .21f, 1f);
    private static readonly Color[] PieceColors =
    {
        new Color(.1f, .85f, .95f), new Color(1f, .85f, .1f),
        new Color(.75f, .2f, .95f), new Color(.35f, .95f, .15f),
        new Color(1f, .15f, .3f), new Color(.15f, .35f, 1f), new Color(1f, .5f, .1f)
    };
    private static readonly string[] RemovedChildren =
    {
        "Panel", "Top accent", "Title", "Players heading", "Divider", "Timer",
        "Timer heading", "Lobby hint"
    };

    public static void Author(MatchFlow flow, Board board, Transform lobby)
    {
        foreach (string name in RemovedChildren)
        {
            Transform obsolete = lobby.Find(name);
            if (obsolete != null) Undo.DestroyObjectImmediate(obsolete.gameObject);
        }

        float cell = board.CellSize;
        Position(lobby, new Vector3(0f, -6.2f * cell, 0f), Vector3.one);
        Sprite square = OriginalArtAssets.ThemeSprite("_Textures/effect00.png");
        Sprite capsule = OriginalArtAssets.CommonSlice("_texture/game_battle99_status00^q.png",
            "Original matchmaking status", new Rect(5, 4, 437, 64));
        TMP_FontAsset font = FontSetup.GetFont();
        MatchmakingGridPresentation presentation = lobby.GetComponent<MatchmakingGridPresentation>();
        if (presentation == null) presentation = Undo.AddComponent<MatchmakingGridPresentation>(lobby.gameObject);

        Transform grid = Child(lobby, "Block grid");
        Position(grid, new Vector3(0f, 2.6f * cell, 0f), Vector3.one);
        var cells = new SpriteRenderer[16];
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            int index = y * 4 + x;
            bool highlighted = y == 0 && (x == 1 || x == 2)
                || y == 1 && (x == 0 || x == 1);
            cells[index] = Sprite(Child(grid, "Cell " + index.ToString("00")), square,
                new Vector2((x - 1.5f) * .5f, (y - 1.5f) * .5f) * cell,
                new Vector2(.43f, .43f) * cell, highlighted ? PieceColors[4] : Idle, 32);
        }

        float capsuleWidth = 9.1f * cell;
        Sprite(Child(lobby, "Search bar"), capsule, new Vector2(0f, -.15f * cell),
            new Vector2(capsuleWidth, capsuleWidth * capsule.bounds.size.y / capsule.bounds.size.x),
            Color.white, 31);
        TextMeshPro status = Label(Child(lobby, "Search status"), "SEARCHING FOR PLAYERS...",
            new Vector2(0f, -.15f) * cell, new Vector2(8.35f, .7f) * cell, font);
        TextMeshPro participants = Label(Child(lobby, "Participants"), "01",
            new Vector2(0f, -1.95f) * cell, new Vector2(4.5f, 1.85f) * cell, font);

        var presentationData = new SerializedObject(presentation);
        SerializedProperty cellReferences = Property(presentationData, "cells");
        cellReferences.arraySize = cells.Length;
        for (int i = 0; i < cells.Length; i++) cellReferences.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
        Property(presentationData, "holdDuration").floatValue = .3f;
        Property(presentationData, "blankDuration").floatValue = .2f;
        Property(presentationData, "idleColor").colorValue = Idle;
        SerializedProperty colors = Property(presentationData, "pieceColors");
        colors.arraySize = PieceColors.Length;
        for (int i = 0; i < PieceColors.Length; i++) colors.GetArrayElementAtIndex(i).colorValue = PieceColors[i];
        presentationData.ApplyModifiedPropertiesWithoutUndo();
        Record(presentation);

        var flowData = new SerializedObject(flow);
        Property(flowData, "matchmakingText").objectReferenceValue = status;
        Property(flowData, "participantsText").objectReferenceValue = participants;
        Property(flowData, "timerText").objectReferenceValue = null;
        flowData.ApplyModifiedPropertiesWithoutUndo();
        Record(flow);
    }

    private static SpriteRenderer Sprite(Transform child, Sprite sprite, Vector2 position,
        Vector2 size, Color color, int order)
    {
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(child.gameObject);
        Undo.RecordObject(renderer, "Author matchmaking sprite");
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        renderer.enabled = true;
        renderer.drawMode = SpriteDrawMode.Simple;
        renderer.flipX = renderer.flipY = false;
        Position(child, position, new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f));
        Record(renderer);
        return renderer;
    }

    private static TextMeshPro Label(Transform child, string value, Vector2 position, Vector2 size, TMP_FontAsset font)
    {
        TextMeshPro label = child.GetComponent<TextMeshPro>();
        if (label == null) label = Undo.AddComponent<TextMeshPro>(child.gameObject);
        Undo.RecordObject(label, "Author matchmaking text");
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.text = value;
        label.fontSize = 52f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = 100f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Truncate;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.sortingOrder = 33;
        label.margin = Vector4.zero;
        label.enabled = true;
        RectTransform rect = label.rectTransform;
        Undo.RecordObject(rect, "Position matchmaking text");
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition3D = position;
        label.ForceMeshUpdate(true);
        Record(rect);
        Record(label);
        return label;
    }

    private static void Position(Transform child, Vector3 position, Vector3 scale)
    {
        Undo.RecordObject(child, "Position matchmaking artwork");
        child.localPosition = position;
        child.localRotation = Quaternion.identity;
        child.localScale = scale;
        Record(child);
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var instance = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(instance, "Author matchmaking artwork");
            instance.transform.SetParent(parent, false);
            child = instance.transform;
        }
        child.gameObject.SetActive(true);
        Record(child.gameObject);
        return child;
    }

    private static SerializedProperty Property(SerializedObject data, string name) => data.FindProperty(name)
        ?? throw new InvalidOperationException(data.targetObject.GetType().Name + " is missing " + name);

    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
