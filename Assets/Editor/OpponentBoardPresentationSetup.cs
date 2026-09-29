#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Authors reusable arrival and target effects from the supplied opponent artwork.</summary>
public static class OpponentBoardPresentationSetup
{
    private const string SpiralPath = "_Textures/eff_spiral0.png";
    private const string FlashPath = "_texture/game_enemy.05.png";
    private static readonly string[] CursorPaths =
        { "_Textures/target00a.png", "_Textures/target00b.png", "_Textures/target00c.png" };

    public static void Author(BotPlayer bot)
    {
        if (bot == null) throw new InvalidOperationException("Opponent presentation needs an existing BotPlayer.");
        Board board = bot.GetComponent<Board>();
        float cell = board != null ? board.CellSize : 1f;
        // Import every frame before keeping references to the shared atlas's sprites.
        Rect[] frames = { new Rect(0f, 128f, 128f, 128f), new Rect(128f, 128f, 128f, 128f),
            new Rect(256f, 128f, 128f, 128f), new Rect(384f, 128f, 128f, 128f),
            new Rect(0f, 0f, 128f, 128f) };
        for (int i = 0; i < frames.Length; i++)
            OriginalArtAssets.ThemeSlice(SpiralPath, "Opponent Arrival " + (i + 1), frames[i]);
        OriginalArtAssets.CommonSprite(FlashPath);
        foreach (string path in CursorPaths) OriginalArtAssets.ThemeSprite(path);
        var arrivalFrames = new Sprite[frames.Length];
        for (int i = 0; i < arrivalFrames.Length; i++)
            arrivalFrames[i] = LoadSprite(OriginalArtAssets.Theme + SpiralPath, "Opponent Arrival " + (i + 1));
        Sprite flashArt = LoadSprite(OriginalArtAssets.Common + FlashPath);
        Sprite coreArt = LoadSprite(OriginalArtAssets.Theme + CursorPaths[0]);
        Sprite ringArt = LoadSprite(OriginalArtAssets.Theme + CursorPaths[1]);
        Sprite flareArt = LoadSprite(OriginalArtAssets.Theme + CursorPaths[2]);

        Transform root = Child(bot.transform, "Opponent presentation");
        ResetTransform(root);
        Transform arrival = Child(root, "Arrival");
        ResetTransform(arrival);
        Transform cursor = Child(root, "Target cursor");
        ResetTransform(cursor);
        SpriteRenderer spiral = SpriteChild(arrival, "Spiral", arrivalFrames[0], 12f, cell, 6,
            new Color(.45f, 1f, 1f, 1f));
        SpriteRenderer flash = SpriteChild(arrival, "Board flash", flashArt, 12f, cell, 5, Color.white);
        Transform frame = bot.transform.Find("Mini board frame");
        if (frame != null)
        {
            flash.transform.localPosition = frame.localPosition;
            flash.transform.localRotation = frame.localRotation;
            flash.transform.localScale = frame.localScale;
            Record(flash.transform);
        }
        Color cursorColor = new Color(1f, .84f, .12f, 1f);
        SpriteRenderer core = SpriteChild(cursor, "Core", coreArt, 12f, cell, 10, cursorColor);
        SpriteRenderer ring = SpriteChild(cursor, "Ring", ringArt, 14f, cell, 11, cursorColor);
        SpriteRenderer flare = SpriteChild(cursor, "Flare", flareArt, 14f, cell, 9, cursorColor);

        OpponentBoardPresentation presentation = bot.GetComponent<OpponentBoardPresentation>();
        if (presentation == null) presentation = bot.gameObject.AddComponent<OpponentBoardPresentation>();
        var data = new SerializedObject(presentation);
        Ref(data, "arrivalRoot", arrival.gameObject);
        Ref(data, "cursorRoot", cursor.gameObject);
        Ref(data, "arrivalSpiral", spiral);
        Ref(data, "arrivalFlash", flash);
        Ref(data, "cursorCore", core);
        Ref(data, "cursorRing", ring);
        Ref(data, "cursorFlare", flare);
        SerializedProperty sprites = Property(data, "arrivalFrames");
        sprites.arraySize = arrivalFrames.Length;
        for (int i = 0; i < arrivalFrames.Length; i++)
            sprites.GetArrayElementAtIndex(i).objectReferenceValue = arrivalFrames[i];
        Property(data, "arrivalDuration").floatValue = .6f;
        data.ApplyModifiedPropertiesWithoutUndo();
        Record(presentation);
        var botData = new SerializedObject(bot);
        Ref(botData, "boardPresentation", presentation);
        botData.ApplyModifiedPropertiesWithoutUndo();
        Record(bot);
        arrival.gameObject.SetActive(false);
        cursor.gameObject.SetActive(false);
        Record(arrival.gameObject);
        Record(cursor.gameObject);
    }

    private static Sprite LoadSprite(string path, string name = null)
    {
        bool single = ((TextureImporter)AssetImporter.GetAtPath(path)).spriteImportMode == SpriteImportMode.Single;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Sprite sprite && (name != null ? sprite.name == name : single || sprite.name == "Full"))
                return sprite;
        throw new InvalidOperationException("Missing opponent artwork: " + path + " / " + (name ?? "Full"));
    }

    private static SpriteRenderer SpriteChild(Transform parent, string name, Sprite artwork,
        float width, float cell, int order, Color color)
    {
        Transform child = Child(parent, name);
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = child.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = artwork;
        renderer.color = color;
        renderer.sortingOrder = order;
        renderer.enabled = true;
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        float scale = width * cell / artwork.bounds.size.x;
        child.localScale = new Vector3(scale, scale, 1f);
        child.gameObject.SetActive(true);
        Record(renderer);
        Record(child);
        Record(child.gameObject);
        return renderer;
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        var instance = new GameObject(name);
        instance.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(instance, "Author opponent presentation");
        return instance.transform;
    }

    private static void ResetTransform(Transform transform)
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        transform.gameObject.SetActive(true);
        Record(transform);
        Record(transform.gameObject);
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
