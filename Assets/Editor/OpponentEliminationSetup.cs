#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors original opponent sprites and saved elimination visuals without moving any boards.</summary>
public static class OpponentEliminationSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath = "Assets/Prefabs/BotOpponent.prefab";

    [MenuItem("Tetris/Set Up Opponent Elimination")]
    public static void SetUpOpponentElimination()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before authoring opponent elimination.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        AuthorPrefab();
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (BotPlayer bot in root.GetComponentsInChildren<BotPlayer>(true))
            {
                Author(bot);
                count++;
            }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("OPPONENT_ELIMINATION_SETUP_COMPLETE: original KO medals, knockout effects and saved rank displays for " + count + " opponents.");
    }

    public static void BatchSetup()
    {
        try { SetUpOpponentElimination(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    public static void AuthorPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            throw new InvalidOperationException("The saved BotOpponent prefab is missing.");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Author(root.GetComponent<BotPlayer>());
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("Could not save " + PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void Author(BotPlayer bot)
    {
        if (bot == null) throw new InvalidOperationException("Opponent elimination needs an existing BotPlayer.");
        Board board = bot.GetComponent<Board>();
        float cell = board != null ? board.CellSize : 1f;
        // Complete all imports before retaining references, including the shared star sprite.
        Sprite[] knockoutArt = LoadKnockoutArt();
        Sprite normalFrame = OriginalArtAssets.CommonSprite("_texture/game_enemy.01.png");
        Sprite selectedFrame = OriginalArtAssets.CommonSprite("_texture/game_enemy.02.png");
        var botData = new SerializedObject(bot);
        HideLabel(botData, "nameLabel");
        HideLabel(botData, "statusLabel");

        SpriteRenderer frame = Property(botData, "targetFrame").objectReferenceValue as SpriteRenderer;
        if (frame == null) frame = bot.transform.Find("Mini board frame")?.GetComponent<SpriteRenderer>();
        if (frame != null)
        {
            frame.sprite = normalFrame;
            frame.color = Color.white;
            // The original 72x136 image contains a 60x120 board: six source pixels per cell.
            float scale = Board.Width * cell * 72f / (60f * normalFrame.bounds.size.x);
            frame.transform.localScale = new Vector3(scale, scale, 1f);
            Record(frame);
            Record(frame.transform);
            Ref(botData, "targetFrame", frame);
        }
        Ref(botData, "normalFrameSprite", normalFrame);
        Ref(botData, "selectedFrameSprite", selectedFrame);
        SpriteRenderer oldBackground = bot.transform.Find("Mini board background")?.GetComponent<SpriteRenderer>();
        if (oldBackground != null) { oldBackground.enabled = false; Record(oldBackground); }

        Transform locked = bot.transform.Find("Locked blocks");
        if (locked == null) throw new InvalidOperationException("Opponent has no saved Locked blocks artwork.");
        Transform root = Child(bot.transform, "Elimination presentation");
        ResetTransform(root);
        Transform pop = Child(root, "K.O. pop");
        ResetTransform(pop);
        SpriteRenderer medal = SpriteChild(root, "Eliminated medal", knockoutArt[1],
            new Vector2(0f, 1f), 8.5f, cell, 6);
        SpriteRenderer sprite = SpriteChild(pop, "Original K.O.", knockoutArt[0],
            new Vector2(0f, -2.2f), 7.2f, cell, 9);
        var effects = new SpriteRenderer[6];
        effects[0] = SpriteChild(pop, "Knockout burst", knockoutArt[2], new Vector2(0f, 1.7f), 11f, cell, 7);
        effects[1] = SpriteChild(pop, "Knockout sparks", knockoutArt[3], new Vector2(0f, 1.7f), 12f, cell, 6);
        Vector2[] starPositions = { new Vector2(-4f, 6f), new Vector2(4f, 5f),
            new Vector2(-4f, -4f), new Vector2(4f, -5f) };
        var letters = new SpriteRenderer[4];
        for (int i = 0; i < 4; i++)
        {
            effects[i + 2] = SpriteChild(pop, "Knockout star " + (i + 1), knockoutArt[4],
                starPositions[i], 1.5f, cell, 8);
            letters[i] = SpriteChild(pop, "BOOM letter " + (i + 1), knockoutArt[i + 5],
                new Vector2(-3.15f + i * 2.1f, 3f), 2.8f, cell, 9);
        }

        Transform labelRoot = Child(pop, "K.O. label");
        TextMeshPro knockout = labelRoot.GetComponent<TextMeshPro>();
        if (knockout == null) knockout = labelRoot.gameObject.AddComponent<TextMeshPro>();
        TMP_FontAsset knockoutFont = FontSetup.GetFont();
        knockout.font = knockoutFont;
        knockout.fontSharedMaterial = knockoutFont.material;
        knockout.text = "K.O.";
        knockout.color = new Color(1f, .84f, .28f);
        knockout.fontSize = 60f;
        knockout.enableAutoSizing = true;
        knockout.fontSizeMin = 1f;
        knockout.fontSizeMax = 100f;
        knockout.alignment = TextAlignmentOptions.Center;
        knockout.textWrappingMode = TextWrappingModes.NoWrap;
        knockout.overflowMode = TextOverflowModes.Overflow;
        knockout.margin = Vector4.zero;
        knockout.sortingOrder = 6;
        knockout.enabled = false;
        RectTransform knockoutRect = knockout.rectTransform;
        knockoutRect.anchorMin = knockoutRect.anchorMax = knockoutRect.pivot = Vector2.one * .5f;
        knockoutRect.sizeDelta = new Vector2(8.6f, 4.6f) * cell;
        knockoutRect.anchoredPosition3D = Vector3.zero;
        knockoutRect.localRotation = Quaternion.identity;
        knockoutRect.localScale = Vector3.one;
        knockout.gameObject.SetActive(false);
        knockout.ForceMeshUpdate(true);
        Record(knockout);
        Record(knockoutRect);
        Record(knockout.gameObject);

        Transform rankRoot = Child(root, "Rank");
        TMP_Text rank = rankRoot.GetComponent<TMP_Text>();
        if (rank == null) rank = rankRoot.gameObject.AddComponent<TextMeshPro>();
        TMP_FontAsset font = FontSetup.GetFont();
        rank.font = font;
        rank.fontSharedMaterial = font.material;
        rank.text = "";
        rank.color = new Color(.82f, .88f, .96f);
        rank.fontSize = 60f;
        rank.enableAutoSizing = true;
        rank.fontSizeMin = 1f;
        rank.fontSizeMax = 100f;
        rank.alignment = TextAlignmentOptions.Center;
        rank.textWrappingMode = TextWrappingModes.NoWrap;
        rank.overflowMode = TextOverflowModes.Truncate;
        rank.margin = Vector4.zero;
        rank.enabled = true;
        if (rank is TextMeshPro rankMesh) rankMesh.sortingOrder = 7;
        RectTransform rect = rank.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = new Vector2(8.6f, 3.2f) * cell;
        rect.anchoredPosition3D = new Vector3(0f, -5.5f * cell, 0f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rank.ForceMeshUpdate(true);
        Record(rect);
        Record(rank);
        OpponentEliminationPresentation presentation = bot.GetComponent<OpponentEliminationPresentation>();
        if (presentation == null) presentation = bot.gameObject.AddComponent<OpponentEliminationPresentation>();
        var presentationData = new SerializedObject(presentation);
        Ref(presentationData, "lockedArtwork", locked.gameObject);
        Ref(presentationData, "knockoutRoot", pop.gameObject);
        Ref(presentationData, "knockoutSprite", sprite);
        Ref(presentationData, "knockoutText", knockout);
        Ref(presentationData, "eliminatedMedal", medal);
        Ref(presentationData, "playerKnockoutMedal", knockoutArt[0]);
        Ref(presentationData, "otherKnockoutMedal", knockoutArt[1]);
        Refs(presentationData, "knockoutEffects", effects);
        Refs(presentationData, "knockoutLetters", letters);
        Ref(presentationData, "rankText", rank);
        presentationData.ApplyModifiedPropertiesWithoutUndo();
        Record(presentation);
        Ref(botData, "eliminationPresentation", presentation);
        botData.ApplyModifiedPropertiesWithoutUndo();
        Record(bot);
        locked.gameObject.SetActive(true);
        medal.gameObject.SetActive(false);
        pop.gameObject.SetActive(false);
        rank.gameObject.SetActive(false);
        Record(locked.gameObject);
        Record(medal.gameObject);
        Record(pop.gameObject);
        Record(rank.gameObject);
        FontSetup.Apply(root.gameObject);
        OpponentBoardPresentationSetup.Author(bot);
    }

    private static Sprite[] LoadKnockoutArt()
    {
        string[] paths = { "enUS/game_enemy.08^q.png", "enUS/game_enemy.07^q.png",
            "_Textures/boom00base.png", "_Textures/boom01spark0.png", "_Textures/boom03star0.png",
            "_Textures/boom04logo0.png", "_Textures/boom04logo1.png", "_Textures/boom04logo2.png",
            "_Textures/boom04logo3.png" };
        string[] names = { "Opponent KO", "Opponent Eliminated", "Opponent KO Burst", "Opponent KO Sparks",
            "Opponent KO Star", "Opponent KO B", "Opponent KO O1", "Opponent KO O2", "Opponent KO M" };
        Rect[] rects = { new Rect(9f, 44f, 54f, 49f), new Rect(7f, 41f, 60f, 53f),
            new Rect(4f, 6f, 183f, 173f), new Rect(4f, 21f, 197f, 170f), new Rect(2f, 2f, 19f, 20f),
            new Rect(3f, 4f, 73f, 89f), new Rect(4f, 6f, 48f, 53f), new Rect(4f, 5f, 47f, 63f),
            new Rect(4f, 7f, 71f, 99f) };
        for (int i = 0; i < paths.Length; i++) OriginalArtAssets.ThemeSlice(paths[i], names[i], rects[i]);
        var sprites = new Sprite[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(OriginalArtAssets.Theme + paths[i]))
                if (asset is Sprite candidate && candidate.name == names[i]) { sprites[i] = candidate; break; }
            if (sprites[i] == null) throw new InvalidOperationException("Missing knockout sprite: " + paths[i] + " / " + names[i]);
        }
        return sprites;
    }

    private static SpriteRenderer SpriteChild(Transform parent, string name, Sprite artwork,
        Vector2 position, float width, float cell, int order)
    {
        Transform child = Child(parent, name);
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = child.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = artwork;
        renderer.color = Color.white;
        renderer.sortingOrder = order;
        renderer.enabled = true;
        child.localPosition = new Vector3(position.x, position.y, 0f) * cell;
        child.localRotation = Quaternion.identity;
        float scale = width * cell / artwork.bounds.size.x;
        child.localScale = new Vector3(scale, scale, 1f);
        child.gameObject.SetActive(true);
        Record(renderer);
        Record(child);
        Record(child.gameObject);
        return renderer;
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

    private static void HideLabel(SerializedObject data, string field)
    {
        TMP_Text label = Property(data, field).objectReferenceValue as TMP_Text;
        if (label == null) return;
        label.gameObject.SetActive(false);
        Record(label.gameObject);
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        var instance = new GameObject(name);
        instance.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(instance, "Author opponent elimination");
        return instance.transform;
    }

    private static SerializedProperty Property(SerializedObject data, string name) => data.FindProperty(name)
        ?? throw new InvalidOperationException(data.targetObject.GetType().Name + " is missing " + name);
    private static void Ref(SerializedObject data, string name, Object value) => Property(data, name).objectReferenceValue = value;
    private static void Refs(SerializedObject data, string name, SpriteRenderer[] values)
    {
        SerializedProperty array = Property(data, name);
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
