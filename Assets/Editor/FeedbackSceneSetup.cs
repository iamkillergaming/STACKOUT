#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors reusable clear callouts and attack particles; gameplay only updates saved objects.</summary>
public static class FeedbackSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PlayerPrefab = "Assets/Prefabs/TetrisBoard.prefab";
    private const string BotPrefab = "Assets/Prefabs/BotOpponent.prefab";
    private const int StarCount = 192;

    [MenuItem("Tetris/Set Up Clear and Attack Feedback")]
    public static void SetUpFeedback()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before setting up clear feedback.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // Modify only the owned player/bot prefabs, the scene, and the TMP default font reference.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                Author(root.GetComponent<Board>());
                FontSetup.Apply(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab) == null)
                    throw new InvalidOperationException("Could not save clear feedback in " + PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        FontSetup.ApplyPrefab(BotPrefab);

        FallingPiece player = FindPlayer(scene);
        Author(player.GetComponent<Board>());
        FontSetup.Apply(scene);
        FontSetup.UpdateProjectDefault();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save clear feedback in " + ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = player.gameObject;
        Debug.Log("CLEAR_FEEDBACK_SETUP_COMPLETE: AJN game text, three saved callouts, 192 saved stars, and assigned sounds.", player);
    }

    public static void BatchSetup()
    {
        try { SetUpFeedback(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    public static void Author(Board board)
    {
        if (board == null || board.GetComponent<FallingPiece>() == null)
            throw new InvalidOperationException("Clear feedback needs the existing player board.");
        TMP_FontAsset font = FontSetup.GetFont();
        Sprite starSprite = OriginalArtAssets.ThemeSprite("_Textures/eff_attack03.png");
        Transform feedbackRoot = Child(board.transform, "Clear feedback");
        TextMeshPro clear = Label(feedbackRoot, "Clear text", 3f, 1.15f,
            new Color(1f, 0.87f, 0.34f), font, board.CellSize);
        TextMeshPro combo = Label(feedbackRoot, "Combo text", 1.55f, 0.95f,
            new Color(1f, 0.94f, 0.82f), font, board.CellSize);
        TextMeshPro attack = Label(feedbackRoot, "Attack text", 0.25f, 0.95f,
            new Color(1f, 0.64f, 0.2f), font, board.CellSize);

        AudioSource calloutSource = Audio(Child(feedbackRoot, "Callout audio"));
        AudioSource attackSource = Audio(Child(feedbackRoot, "Attack audio"));
        Transform starRoot = Child(board.transform, "Attack stars");
        var stars = new SpriteRenderer[StarCount];
        for (int i = 0; i < stars.Length; i++)
        {
            Transform child = Child(starRoot, "Attack star " + (i + 1).ToString("000"));
            SpriteRenderer star = child.GetComponent<SpriteRenderer>();
            if (star == null)
            {
                star = child.gameObject.AddComponent<SpriteRenderer>();
                star.sprite = starSprite;
                star.color = Color.white;
                star.sortingOrder = 8;
                float scale = .22f * board.CellSize / starSprite.bounds.size.x;
                child.localScale = new Vector3(scale, scale, 1f);
            }
            if (star.sprite == null || AssetDatabase.GetAssetPath(star.sprite).StartsWith("Assets/Art/Board/", StringComparison.Ordinal))
            {
                star.sprite = starSprite;
                star.color = Color.white;
                float scale = .22f * board.CellSize / starSprite.bounds.size.x;
                child.localScale = new Vector3(scale, scale, 1f);
                RecordChanges(child);
            }
            star.enabled = false;
            RecordChanges(star);
            stars[i] = star;
        }

        ClearAttackFeedback feedback = board.GetComponent<ClearAttackFeedback>();
        if (feedback == null) feedback = board.gameObject.AddComponent<ClearAttackFeedback>();
        var data = new SerializedObject(feedback);
        SetReference(data, "player", board.GetComponent<FallingPiece>());
        SetReference(data, "combat", board.GetComponent<GarbageCombat>());
        SetReference(data, "arena", board.GetComponentInChildren<BattleArena>(true));
        SetReference(data, "clearText", clear);
        SetReference(data, "comboText", combo);
        SetReference(data, "attackText", attack);
        SetReference(data, "calloutSource", calloutSource);
        SetReference(data, "attackSource", attackSource);
        SetClipIfEmpty(data, "calloutSound", "se_game_msg");
        SetClipIfEmpty(data, "comboSound", "se_game_count");
        SetClipIfEmpty(data, "specialClearSound", "se_game_special");
        SetClipIfEmpty(data, "allClearSound", "se_game_perfect");
        SetClipIfEmpty(data, "attackLaunchSound", "se_game_attack1");
        SetClipIfEmpty(data, "attackHitSound", "se_game_attack2");
        SerializedProperty starReferences = RequiredProperty(data, "stars");
        starReferences.arraySize = stars.Length;
        for (int i = 0; i < stars.Length; i++)
            starReferences.GetArrayElementAtIndex(i).objectReferenceValue = stars[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        RecordChanges(feedback);
        FontSetup.Apply(board.gameObject);
    }

    private static TextMeshPro Label(Transform parent, string name, float y, float height, Color color,
        TMP_FontAsset font, float cellSize)
    {
        Transform child = Child(parent, name);
        TextMeshPro label = child.GetComponent<TextMeshPro>();
        if (label != null) return label;
        label = child.gameObject.AddComponent<TextMeshPro>();
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.text = "";
        label.color = color;
        label.fontSize = 36f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = 80f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.margin = Vector4.zero;
        label.sortingOrder = 9;
        label.rectTransform.sizeDelta = new Vector2(1.75f, height) * cellSize;
        label.rectTransform.localPosition = new Vector3(-7.35f * cellSize, y * cellSize, 0f);
        label.ForceMeshUpdate(true);
        return label;
    }

    private static AudioSource Audio(Transform parent)
    {
        AudioSource source = parent.GetComponent<AudioSource>();
        if (source == null) source = parent.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        RecordChanges(source);
        return source;
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static void SetClipIfEmpty(SerializedObject data, string field, string name)
    {
        SerializedProperty property = RequiredProperty(data, field);
        if (property.objectReferenceValue != null) return;
        string path = "Assets/General Sounds/" + name + ".wav";
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) throw new InvalidOperationException("Missing feedback sound: " + path);
        property.objectReferenceValue = clip;
    }

    private static void SetReference(SerializedObject data, string name, Object value)
        => RequiredProperty(data, name).objectReferenceValue = value;

    private static SerializedProperty RequiredProperty(SerializedObject data, string name)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property == null) throw new InvalidOperationException("ClearAttackFeedback needs the serialized field '" + name + "'.");
        return property;
    }

    private static FallingPiece FindPlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            FallingPiece player = root.GetComponentInChildren<FallingPiece>(true);
            if (player != null) return player;
        }
        throw new InvalidOperationException("SampleScene needs its existing player.");
    }

    private static void RecordChanges(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
