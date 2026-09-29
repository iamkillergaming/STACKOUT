#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors matchmaking and results into the existing scene without changing its framing.</summary>
public static class MatchFlowSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string Sounds = "Assets/General Sounds/";
    private static readonly Color Ink = new Color(.025f, .045f, .075f, .97f);
    private static readonly Color Mint = new Color(.3f, 1f, .82f);
    private static readonly Color Gold = new Color(1f, .82f, .24f);
    private static readonly Color Muted = new Color(.62f, .7f, .78f);

    [MenuItem("Tetris/Set Up Matchmaking and Results")]
    public static void SetUpMatchFlow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before authoring matchmaking.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        FallingPiece player = FindInScene<FallingPiece>(scene);
        if (player == null) throw new InvalidOperationException("The existing player board is missing.");
        BattleArena arena = player.GetComponentInChildren<BattleArena>(true);
        if (arena == null) throw new InvalidOperationException("Run Set Up Battle before authoring matchmaking.");
        BattleSceneSetup.AuthorMatchOpponents(arena);
        Author(arena, player, FindInScene<BackgroundMusic>(scene));
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = arena.gameObject;
        Debug.Log("MATCH_FLOW_SETUP_COMPLETE: 98 saved waiting opponents, AJN lobby/countdown, attack modes, live paged results, and medals.", arena);
    }

    public static void BatchSetup()
    {
        try { SetUpMatchFlow(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    [MenuItem("Tetris/Update Lobby Batches and KO Audio")]
    public static void SetUpLobbyPolish()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before authoring lobby/audio settings.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        FallingPiece player = FindInScene<FallingPiece>(scene);
        BattleArena arena = player != null ? player.GetComponentInChildren<BattleArena>(true) : null;
        if (arena == null) throw new InvalidOperationException("The authored battle arena is missing.");
        AuthorLobbyPolish(arena, player);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("LOBBY_POLISH_SETUP_COMPLETE: random join batches and dedicated original KO voice.", arena);
    }

    public static void BatchLobbyPolishSetup()
    {
        try { SetUpLobbyPolish(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    public static void AuthorLobbyPolish(BattleArena arena, FallingPiece player)
    {
        MatchFlow flow = arena.GetComponent<MatchFlow>();
        if (flow == null) throw new InvalidOperationException("The authored MatchFlow is missing.");
        var data = new SerializedObject(flow);
        Transform root = Child(player.GetComponent<Board>().transform, "Match presentation");
        RefIfEmpty(data, "knockoutSource", Audio(Child(root, "Knockout audio")));
        Clip(data, "knockoutSound", "me_game_ko_vo.wav");
        SerializedProperty minimum = Property(data, "minJoinInterval");
        SerializedProperty maximum = Property(data, "maxJoinInterval");
        if (Mathf.Approximately(minimum.floatValue, .45f) && Mathf.Approximately(maximum.floatValue, 1.05f))
        {
            minimum.floatValue = 1.2f;
            maximum.floatValue = 3.2f;
        }
        if (Property(data, "minJoinBatch").intValue <= 0) Property(data, "minJoinBatch").intValue = 1;
        if (Property(data, "maxJoinBatch").intValue <= 0) Property(data, "maxJoinBatch").intValue = 5;
        data.ApplyModifiedPropertiesWithoutUndo();
        Record(flow);
    }

    public static void Author(BattleArena arena, FallingPiece player, BackgroundMusic music)
    {
        Board board = player.GetComponent<Board>();
        float cell = board.CellSize;
        Sprite white = OriginalArtAssets.CommonSprite("_texture/footer_base^q.png");
        if (white == null) throw new InvalidOperationException("Missing authored white UI sprite.");
        TMP_FontAsset font = FontSetup.GetFont();
        Transform root = Child(board.transform, "Match presentation");
        MatchFlow flow = arena.GetComponent<MatchFlow>();
        if (flow == null) flow = Undo.AddComponent<MatchFlow>(arena.gameObject);
        AuthorLobbyPolish(arena, player);
        var data = new SerializedObject(flow);
        Ref(data, "arena", arena);
        Ref(data, "player", player);
        Ref(data, "music", music);
        RefIfEmpty(data, "cueSource", Audio(Child(root, "Match audio")));
        Clip(data, "menuMusic", "01. Menu Theme.mp3");
        Clip(data, "battleMusic", "02. Battle Theme [Tetris Type A GB Remix] [Default Theme].mp3");
        ClipAtPath(data, "winResultsMusic", "Assets/05. Result (Win).mp3");
        ClipAtPath(data, "lossResultsMusic", "Assets/06. Result (Eliminated).mp3");
        Clip(data, "trimmedDeathCue", "me_game_gameover.wav");
        Clip(data, "trimmedVictoryCue", "me_game_clear.wav");
        // Measured audible regions preserve the originals while avoiding silent padding at the handoff.
        Property(data, "deathCueAudibleDuration").floatValue = 5.56f;
        Property(data, "victoryCueAudibleDuration").floatValue = 7.06f;
        Property(data, "winResultsStartTime").floatValue = 1.5f;
        Property(data, "lossResultsStartTime").floatValue = 1.5f;
        Clip(data, "joinSound", "se_game_match.wav");
        Clip(data, "readySound", "me_game_start1.wav");
        Clip(data, "countSound", "se_game_count.wav");
        Clip(data, "goSound", "me_game_start2_vo.wav");
        DeathScreen death = player.GetComponent<DeathScreen>();
        if (death != null) Ref(data, "deathSoundSource", SavedSoundSource(death, root, "Death audio"));
        Ref(data, "victorySoundSource", SavedSoundSource(player, root, "Player audio"));
        if (music != null)
        {
            var musicData = new SerializedObject(music);
            Property(musicData, "playOnStart").boolValue = false;
            musicData.ApplyModifiedPropertiesWithoutUndo();
            Record(music);
        }

        Transform lobby = Child(root, "Matchmaking");
        Ref(data, "lobbyRoot", lobby.gameObject);

        Transform countdown = Child(root, "Start countdown");
        Panel(countdown, "Panel", white, new Vector2(0f, 1f), new Vector2(9.3f, 3.8f), Ink, 30, cell);
        Panel(countdown, "Accent", white, new Vector2(0f, 2.9f), new Vector2(9.3f, .06f), Gold, 31, cell);
        Ref(data, "countdownText", Label(countdown, "Countdown", "READY", 0f, 1f, 8.5f, 2.5f, Gold, font, cell));
        Ref(data, "countdownRoot", countdown.gameObject);

        Transform modes = Child(root, "Attack selection");
        string[] modeNames = { "Randoms", "Knockouts", "Badges", "Attackers" };
        string[] modeText = { "A  RANDOMS", "W  K.O.S", "D  BADGES", "S  ATTACKERS" };
        Vector2[] positions = { new Vector2(-2.3f, 8.15f), new Vector2(0f, 9.1f),
            new Vector2(2.3f, 8.15f), new Vector2(0f, 7.2f) };
        var modeLabels = new TMP_Text[4];
        var modePanels = new SpriteRenderer[4];
        for (int i = 0; i < modeLabels.Length; i++)
        {
            Vector2 size = new Vector2(i == 0 || i == 2 ? 4.25f : 3.85f, .82f);
            modePanels[i] = Panel(modes, modeNames[i] + " panel", white, positions[i], size, Ink, 14, cell);
            modeLabels[i] = Label(modes, modeNames[i] + " label", modeText[i], positions[i].x,
                positions[i].y, size.x - .18f, .62f, Color.white, font, cell, 15);
        }
        Refs(data, "modeLabels", modeLabels);
        Refs(data, "modePanels", modePanels);
        Ref(data, "attackOverlayRoot", modes.gameObject);

        Transform results = Child(root, "Match results");
        Panel(results, "Panel", white, Vector2.zero, new Vector2(9.65f, 19.2f), Ink, 30, cell);
        Panel(results, "Top accent", white, new Vector2(0f, 9.6f), new Vector2(9.65f, .08f), Mint, 31, cell);
        Ref(data, "resultTitle", Label(results, "Title", "YOUR RESULT", 0f, 8.5f, 8.6f, .95f, Mint, font, cell));
        Ref(data, "rankText", Label(results, "Rank", "#99", 0f, 6.8f, 8.5f, 2f, Gold, font, cell));
        Ref(data, "resultsStatus", Label(results, "Status", "LIVE STANDINGS", 0f, 5.25f, 8.6f, .6f, Color.white, font, cell));
        Label(results, "Columns", "<pos=0%>PLACE<pos=18%>PLAYER<pos=74%>K.O.<pos=90%>MED", 0f, 4.3f,
            8.55f, .48f, Muted, font, cell, 32, TextAlignmentOptions.Left);
        Panel(results, "Header rule", white, new Vector2(0f, 3.91f), new Vector2(8.6f, .025f), Muted, 31, cell);
        var rows = new TMP_Text[12];
        for (int i = 0; i < rows.Length; i++)
        {
            float y = 3.45f - i * .77f;
            Panel(results, "Row " + (i + 1).ToString("00") + " stripe", white, new Vector2(0f, y),
                new Vector2(8.8f, .71f), i % 2 == 0 ? new Color(.1f, .15f, .2f, .6f)
                    : new Color(.055f, .08f, .12f, .3f), 31, cell);
            rows[i] = Label(results, "Standing " + (i + 1).ToString("00"), "", 0f, y, 8.55f, .57f,
                Color.white, font, cell, 32, TextAlignmentOptions.Left);
        }
        Refs(data, "standingRows", rows);
        Ref(data, "previousPageText", Label(results, "Previous page", "Q  PREV", -3f, -6.35f, 2.5f, .65f, Mint, font, cell));
        Ref(data, "pageText", Label(results, "Page", "1 / 9", 0f, -6.35f, 2.5f, .65f, Color.white, font, cell));
        Ref(data, "nextPageText", Label(results, "Next page", "NEXT  E", 3f, -6.35f, 2.5f, .65f, Mint, font, cell));
        Ref(data, "restartText", Label(results, "Restart", "WATCHING THE MATCH", 0f, -8f, 8.6f, .85f, Gold, font, cell));
        Ref(data, "resultsRoot", results.gameObject);

        Transform medals = Child(root, "Medals");
        Sprite medalSprite = OriginalArtAssets.ThemeSlice("badge00.png", "Original badge earned", new Rect(168, 0, 56, 56));
        var medalIcons = new SpriteRenderer[4];
        for (int i = 0; i < medalIcons.Length; i++)
            medalIcons[i] = Panel(medals, "Medal " + (i + 1), medalSprite, new Vector2(5.65f + i * .57f, -7.15f),
                new Vector2(.49f, .61f), new Color(.2f, .22f, .25f), 6, cell);
        Panel(medals, "Progress track", white, new Vector2(6.5f, -8.1f), new Vector2(2.2f, .12f), new Color(.15f, .19f, .23f), 5, cell);
        SpriteRenderer progress = Panel(medals, "Medal progress", white, new Vector2(6.5f, -8.1f), new Vector2(2.2f, .12f), Gold, 6, cell);
        Refs(data, "medalIcons", medalIcons);
        Ref(data, "medalProgress", progress);

        data.ApplyModifiedPropertiesWithoutUndo();
        Record(flow);
        lobby.gameObject.SetActive(true);
        countdown.gameObject.SetActive(false);
        modes.gameObject.SetActive(false);
        results.gameObject.SetActive(false);
        progress.enabled = false;
        FontSetup.Apply(root.gameObject);
        FontSetup.UpdateProjectDefault();
        OriginalPhaseArtSetup.Author(arena, player);
        MatchmakingGridSetup.Author(flow, board, lobby);
        SurvivorMilestoneSetup.Author(arena, player);
    }

    private static AudioSource SavedSoundSource(Component owner, Transform parent, string name)
    {
        var data = new SerializedObject(owner);
        SerializedProperty property = Property(data, "soundSource");
        AudioSource source = property.objectReferenceValue as AudioSource;
        if (source != null) return source;
        source = Audio(Child(parent, name));
        property.objectReferenceValue = source;
        data.ApplyModifiedPropertiesWithoutUndo();
        Record(owner);
        return source;
    }

    private static SpriteRenderer Panel(Transform parent, string name, Sprite sprite, Vector2 position,
        Vector2 size, Color color, int order, float cell)
    {
        Transform child = Child(parent, name);
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer != null) return renderer;
        renderer = child.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        child.localPosition = position * cell;
        child.localScale = new Vector3(size.x * cell / sprite.bounds.size.x, size.y * cell / sprite.bounds.size.y, 1f);
        return renderer;
    }

    private static TextMeshPro Label(Transform parent, string name, string value, float x, float y,
        float width, float height, Color color, TMP_FontAsset font, float cell, int order = 32,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        Transform child = Child(parent, name);
        TextMeshPro label = child.GetComponent<TextMeshPro>();
        if (label == null)
        {
            label = child.gameObject.AddComponent<TextMeshPro>();
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.text = value;
            label.fontSize = 52f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 1f;
            label.fontSizeMax = 100f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            label.alignment = alignment;
            label.color = color;
            label.sortingOrder = order;
            label.margin = Vector4.zero;
        }
        // RectTransform restores its anchored position when loaded. Author that value explicitly,
        // including when repairing labels from the first setup, rather than only localPosition.
        RectTransform rect = label.rectTransform;
        Undo.RecordObject(rect, "Position match label");
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = new Vector2(width, height) * cell;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition3D = new Vector3(x * cell, y * cell, 0f);
        label.ForceMeshUpdate(true);
        Record(rect);
        Record(label);
        return label;
    }

    private static AudioSource Audio(Transform parent)
    {
        AudioSource source = parent.GetComponent<AudioSource>();
        if (source != null) return source;
        source = parent.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        return source;
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        var instance = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(instance, "Author match presentation");
        instance.transform.SetParent(parent, false);
        return instance.transform;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }

    private static SerializedProperty Property(SerializedObject data, string name) => data.FindProperty(name)
        ?? throw new InvalidOperationException(data.targetObject.GetType().Name + " is missing " + name);
    private static void Ref(SerializedObject data, string name, Object value) => Property(data, name).objectReferenceValue = value;
    private static void RefIfEmpty(SerializedObject data, string name, Object value)
    {
        SerializedProperty property = Property(data, name);
        if (property.objectReferenceValue == null) property.objectReferenceValue = value;
    }
    private static void Refs<T>(SerializedObject data, string name, T[] values) where T : Object
    {
        SerializedProperty property = Property(data, name);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
    private static void Clip(SerializedObject data, string name, string filename)
        => ClipAtPath(data, name, Sounds + filename);

    private static void ClipAtPath(SerializedObject data, string name, string path)
    {
        SerializedProperty property = Property(data, name);
        if (property.objectReferenceValue != null) return;
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) throw new InvalidOperationException("Missing supplied audio: " + path);
        property.objectReferenceValue = clip;
    }
    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
