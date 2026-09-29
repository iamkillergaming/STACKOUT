#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors saved opponent boards and match wiring; no board objects are built at launch.</summary>
public static class BattleSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath = "Assets/Prefabs/BotOpponent.prefab";
    private const string FontPath = FontSetup.FontPath;
    private const int OpponentCapacity = 98;
    private static readonly Color MiniFrameColor = new Color(0.67f, 0.67f, 0.78f, 0.85f);
    private static readonly Color MiniBackgroundColor = new Color(0.015f, 0.025f, 0.065f, 0.56f);

    [MenuItem("Tetris/Set Up Battle")]
    public static void SetUpBattle()
    {
        Setup(false);
    }

    [MenuItem("Tetris/Add Bot Opponent")]
    public static void AddBotOpponent()
    {
        Setup(true);
    }

    /// <summary>Entry point for an isolated Unity editor batch process.</summary>
    public static void BatchSetup()
    {
        try
        {
            SetUpBattle();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Setup(bool addOpponent)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before editing the battle setup.");

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        FallingPiece player = FindPlayer(scene);
        if (player == null) throw new InvalidOperationException("SampleScene needs its existing FallingPiece player.");
        Board board = player.GetComponent<Board>();
        if (board == null) throw new InvalidOperationException("The player needs a Board component.");
        BoardArtworkSetup.RepairMissingFrame(board);
        TargetingWarningSetup.Author(board.GetComponent<BoardPresentation>());
        Sprite whiteCell = OriginalArtAssets.CommonSprite("_texture/footer_base^q.png");
        Sprite beveledTile = OriginalArtAssets.ThemeSlice("minos00.png", "Garbage", new Rect(4, 124, 48, 48));
        Sprite miniFrame = OriginalArtAssets.CommonSprite("_texture/game_enemy.01.png");
        TMP_FontAsset font = RequiredAsset<TMP_FontAsset>(FontPath);
        GameObject botPrefab = CreateOrUpgradeBotPrefab(whiteCell, beveledTile, miniFrame, font);

        GarbageCombat playerCombat = GetOrAdd<GarbageCombat>(board.gameObject);
        ConfigureCombat(playerCombat, board, beveledTile);

        BattleArena arena = board.GetComponentInChildren<BattleArena>(true);
        if (arena == null)
        {
            Transform arenaRoot = Child(board.transform, "Tetris Battle");
            arena = GetOrAdd<BattleArena>(arenaRoot.gameObject);
        }

        // Existing bot objects remain editable prefab instances; rerunning setup does not replace them.
        var bots = new List<BotPlayer>(arena.GetComponentsInChildren<BotPlayer>(true));
        if (bots.Count > OpponentCapacity)
            throw new InvalidOperationException("The opponent grid has 98 slots. Remove the extra bot objects before setup.");
        while (bots.Count < OpponentCapacity)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(botPrefab, arena.transform);
            if (instance == null) throw new InvalidOperationException("Could not instantiate " + PrefabPath);
            Undo.RegisterCreatedObjectUndo(instance, "Add bot opponent");
            bots.Add(instance.GetComponent<BotPlayer>());
        }
        Transform opponentGrid = AuthorOpponentGrid(arena.transform, whiteCell, miniFrame, board.CellSize);
        PositionBots(bots, opponentGrid, board.CellSize);

        RemoveLegacyLabel(arena.transform, "Incoming garbage");
        RemoveLegacyLabel(arena.transform, "Opponents label");
        AuthorGarbageMeter(arena.transform, playerCombat, whiteCell, beveledTile, board.CellSize);

        var arenaData = new SerializedObject(arena);
        SetReference(arenaData, "player", player);
        SetReference(arenaData, "playerBoard", board);
        SetReference(arenaData, "presentation", board.GetComponent<BoardPresentation>());
        SetReference(arenaData, "playerCombat", playerCombat);
        SetReference(arenaData, "opponentGridRoot", opponentGrid);
        SetReferences(arenaData, "bots", bots.ToArray());
        arenaData.ApplyModifiedPropertiesWithoutUndo();
        RecordChanges(arena);
        MatchFlowSetup.Author(arena, player, Object.FindFirstObjectByType<BackgroundMusic>());
        FontSetup.Apply(board.gameObject);
        FontSetup.UpdateProjectDefault();

        // Keep the battle and scene audio references as scene overrides, outside the reusable player prefab.
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save " + ScenePath);
        AssetDatabase.SaveAssets();
        // Restore the full original atlas and presentation after authoring any newly added objects.
        OriginalSpriteSetup.SetUp();
        Selection.activeGameObject = arena.gameObject;
        Debug.Log("BATTLE_SCENE_SETUP_COMPLETE: 98 saved inactive opponents, ready for matchmaking.", arena);
    }

    /// <summary>Fills the existing slots without changing their placement, the camera, or board artwork.</summary>
    public static void AuthorMatchOpponents(BattleArena arena)
    {
        if (arena == null) throw new InvalidOperationException("Matchmaking needs an existing battle arena.");
        Board board = arena.GetComponentInParent<Board>();
        Transform grid = arena.transform.Find("Opponent grid");
        if (board == null || grid == null) throw new InvalidOperationException("Run Set Up Battle before authoring matchmaking.");
        GameObject prefab = RequiredAsset<GameObject>(PrefabPath);
        var opponents = new BotPlayer[OpponentCapacity];
        for (int i = 0; i < opponents.Length; i++)
        {
            Transform slot = grid.Find("Slot " + (i + 1).ToString("00"));
            if (slot == null) throw new InvalidOperationException("Missing saved opponent slot " + (i + 1));
            Transform empty = slot.Find("Empty slot");
            if (empty != null) { empty.gameObject.SetActive(false); RecordChanges(empty.gameObject); }
            BotPlayer bot = slot.GetComponentInChildren<BotPlayer>(true);
            if (bot == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot);
                Undo.RegisterCreatedObjectUndo(instance, "Save matchmaking opponent");
                instance.name = "Bot " + (i + 1).ToString("00");
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = new Vector3(.13f * board.CellSize, .13f * board.CellSize, 1f);
                bot = instance.GetComponent<BotPlayer>();
                var botData = new SerializedObject(bot);
                TMP_Text name = RequiredProperty(botData, "nameLabel").objectReferenceValue as TMP_Text;
                if (name != null) { name.text = "BOT " + (i + 1).ToString("00"); RecordChanges(name); }
            }
            bot.gameObject.SetActive(false);
            OpponentEliminationSetup.Author(bot);
            RecordChanges(bot.gameObject);
            RecordChanges(bot.transform);
            opponents[i] = bot;
        }
        var data = new SerializedObject(arena);
        SetReferences(data, "bots", opponents);
        SetReference(data, "opponentGridRoot", grid);
        data.ApplyModifiedPropertiesWithoutUndo();
        RecordChanges(arena);
    }

    private static GameObject CreateOrUpgradeBotPrefab(Sprite whiteCell, Sprite beveledTile,
        Sprite miniFrame, TMP_FontAsset font)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        bool upgrading = existing != null;
        GameObject root = upgrading ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Bot Opponent");
        try
        {
            Board board = GetOrAdd<Board>(root);
            GarbageCombat combat = GetOrAdd<GarbageCombat>(root);
            BotPlayer bot = GetOrAdd<BotPlayer>(root);

            SpriteRenderer border = SpriteChild(root.transform, "Mini board frame", miniFrame, -3);
            border.color = MiniFrameColor;
            SpriteRenderer interior = SpriteChild(root.transform, "Mini board background", whiteCell, -2);
            interior.color = MiniBackgroundColor;
            interior.transform.localScale = new Vector3(Board.Width / whiteCell.bounds.size.x,
                Board.Height / whiteCell.bounds.size.y, 1f);

            Transform lockedBlocks = Child(root.transform, "Locked blocks");
            var cells = new SpriteRenderer[Board.Width * Board.Height];
            for (int y = 0; y < Board.Height; y++)
            {
                for (int x = 0; x < Board.Width; x++)
                {
                    SpriteRenderer cell = SpriteChild(lockedBlocks, $"Cell {x}, {y}", beveledTile, 0);
                    cell.transform.localPosition = new Vector3(x - (Board.Width - 1) * 0.5f,
                        y - (Board.Height - 1) * 0.5f, 0f);
                    cell.transform.localScale = board.GetPieceScale(beveledTile);
                    cell.enabled = false;
                    cells[y * Board.Width + x] = cell;
                }
            }
            var boardData = new SerializedObject(board);
            RequiredProperty(boardData, "cellSize").floatValue = 1f;
            RequiredProperty(boardData, "fitCameraOnStart").boolValue = false;
            SetReference(boardData, "emptyCellSprite", beveledTile);
            SetReferences(boardData, "boardCells", cells);
            boardData.ApplyModifiedPropertiesWithoutUndo();
            ConfigureCombat(combat, board, beveledTile);

            TextMeshPro name = ExistingOrNewLabel(root.transform, "Bot name", "BOT 01", 0f, 10.7f,
                10f, 0.8f, new Color(0.79f, 0.79f, 0.86f), font, 1f);
            name.color = new Color(0.79f, 0.79f, 0.86f);
            TextMeshPro status = ExistingOrNewLabel(root.transform, "Bot status", "", 0f, -10.7f,
                10f, 0.8f, new Color(0.72f, 0.78f, 0.77f), font, 1f);
            FitMiniLabel(name, 10.7f);
            FitMiniLabel(status, -10.7f);
            var botData = new SerializedObject(bot);
            SetReference(botData, "board", board);
            SetReference(botData, "combat", combat);
            SetReference(botData, "blockSprite", beveledTile);
            SetReference(botData, "nameLabel", name);
            SetReference(botData, "statusLabel", status);
            SetReference(botData, "targetFrame", border);
            if (!upgrading)
            {
                RequiredProperty(botData, "secondsPerPiece").floatValue = 1.1f;
                RequiredProperty(botData, "clearDelay").floatValue = 0.25f;
            }
            botData.ApplyModifiedPropertiesWithoutUndo();
            OpponentEliminationSetup.Author(bot);
            FontSetup.Apply(root);

            EnsureFolder("Assets/Prefabs");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Could not save " + PrefabPath);
            return prefab;
        }
        finally
        {
            if (upgrading) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    private static Transform AuthorOpponentGrid(Transform parent, Sprite whiteCell, Sprite miniFrame,
        float cellSize)
    {
        Transform grid = Child(parent, "Opponent grid");
        grid.localPosition = Vector3.zero;
        grid.localRotation = Quaternion.identity;
        grid.localScale = Vector3.one;
        for (int i = 0; i < OpponentCapacity; i++)
        {
            Transform slot = Child(grid, "Slot " + (i + 1).ToString("00"));
            int sideIndex = i / 2;
            int column = sideIndex % 7;
            int row = sideIndex / 7;
            float side = i % 2 == 0 ? -1f : 1f;
            slot.localPosition = new Vector3(side * (9f + column * 1.4f) * cellSize,
                (8.7f - row * 2.9f) * cellSize, 0f);
            slot.localRotation = Quaternion.identity;
            slot.localScale = Vector3.one;

            // Placeholders remain hidden; a saved board appears only when its opponent joins.
            Transform empty = Child(slot, "Empty slot");
            SpriteRenderer frame = SpriteChild(empty, "Frame", miniFrame, -3);
            frame.color = new Color(1f, 1f, 1f, .5f);
            float frameScale = .13f * cellSize * Board.Width * 72f / (60f * miniFrame.bounds.size.x);
            frame.transform.localScale = new Vector3(frameScale, frameScale, 1f);
            SpriteRenderer background = SpriteChild(empty, "Background", whiteCell, -2);
            background.color = new Color(MiniBackgroundColor.r, MiniBackgroundColor.g,
                MiniBackgroundColor.b, 0.36f);
            background.transform.localScale = new Vector3(1.3f * cellSize / whiteCell.bounds.size.x,
                2.6f * cellSize / whiteCell.bounds.size.y, 1f);
            // The supplied mini frame already contains its translucent black interior.
            background.enabled = false;
            empty.gameObject.SetActive(false);
        }
        return grid;
    }

    private static void PositionBots(List<BotPlayer> bots, Transform grid, float cellSize)
    {
        for (int i = 0; i < bots.Count; i++)
        {
            BotPlayer bot = bots[i];
            Transform slot = grid.Find("Slot " + (i + 1).ToString("00"));
            Undo.RecordObject(bot.transform, "Arrange bot boards");
            if (bot.transform.parent != slot) Undo.SetTransformParent(bot.transform, slot, "Assign opponent slot");
            bot.gameObject.name = "Bot " + (i + 1).ToString("00");
            bot.transform.localPosition = Vector3.zero;
            bot.transform.localRotation = Quaternion.identity;
            bot.transform.localScale = new Vector3(0.13f * cellSize, 0.13f * cellSize, 1f);
            bot.gameObject.SetActive(false);
            slot.Find("Empty slot").gameObject.SetActive(false);
            RecordChanges(bot.transform);
            RecordChanges(bot.gameObject);

            var data = new SerializedObject(bot);
            var label = RequiredProperty(data, "nameLabel").objectReferenceValue as TMP_Text;
            if (label != null)
            {
                label.text = "BOT " + (i + 1).ToString("00");
                label.ForceMeshUpdate(true);
                RecordChanges(label);
            }
        }
    }

    private static void AuthorGarbageMeter(Transform parent, GarbageCombat combat, Sprite whiteCell,
        Sprite beveledTile, float cellSize)
    {
        Transform root = Child(parent, "Garbage meter");
        root.localPosition = new Vector3(-5.83f * cellSize, -9.2f * cellSize, 0f);
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        GarbageMeter meter = GetOrAdd<GarbageMeter>(root.gameObject);
        var segments = new SpriteRenderer[12];
        for (int i = 0; i < segments.Length; i++)
        {
            float y = i * (13.7f / (segments.Length - 1)) * cellSize;
            SpriteRenderer track = SpriteChild(root, "Track " + i, whiteCell, 1);
            track.color = new Color(0.025f, 0.045f, 0.065f, 0.8f);
            track.transform.localPosition = new Vector3(0f, y, 0f);
            track.transform.localScale = new Vector3(cellSize / whiteCell.bounds.size.x,
                cellSize / whiteCell.bounds.size.y, 1f);
            SpriteRenderer segment = SpriteChild(root, "Segment " + i, beveledTile, 2);
            segment.color = Color.white;
            segment.transform.localPosition = new Vector3(0f, y, 0f);
            segment.transform.localScale = new Vector3(cellSize / beveledTile.bounds.size.x,
                cellSize / beveledTile.bounds.size.y, 1f);
            segment.enabled = false;
            segments[i] = segment;
        }
        var data = new SerializedObject(meter);
        SetReference(data, "combat", combat);
        SetReferences(data, "segments", segments);
        data.ApplyModifiedPropertiesWithoutUndo();
        RecordChanges(meter);
    }

    private static void ConfigureCombat(GarbageCombat combat, Board board, Sprite beveledTile)
    {
        var data = new SerializedObject(combat);
        SetReference(data, "board", board);
        // Preserve custom garbage artwork and timing, while retiring generated placeholders.
        SerializedProperty sprite = RequiredProperty(data, "garbageSprite");
        if (sprite.objectReferenceValue == null
            || AssetDatabase.GetAssetPath(sprite.objectReferenceValue).StartsWith("Assets/Art/Board/", StringComparison.Ordinal))
        {
            sprite.objectReferenceValue = beveledTile;
            RequiredProperty(data, "garbageColor").colorValue = Color.white;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        RecordChanges(combat);
    }

    private static FallingPiece FindPlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            FallingPiece player = root.GetComponentInChildren<FallingPiece>(true);
            if (player != null) return player;
        }
        return null;
    }

    private static TextMeshPro ExistingOrNewLabel(Transform parent, string name, string value, float x,
        float y, float width, float height, Color color, TMP_FontAsset font, float cellSize)
    {
        Transform existing = parent.Find(name);
        if (existing != null && existing.TryGetComponent(out TextMeshPro existingLabel)) return existingLabel;
        Transform child = Child(parent, name);
        TextMeshPro label = GetOrAdd<TextMeshPro>(child.gameObject);
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.text = value;
        label.color = color;
        label.fontSize = 36f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = 100f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.margin = Vector4.zero;
        label.sortingOrder = 3;
        label.rectTransform.sizeDelta = new Vector2(width, height) * cellSize;
        label.rectTransform.localPosition = new Vector3(x * cellSize, y * cellSize, 0f);
        label.rectTransform.localRotation = Quaternion.identity;
        label.rectTransform.localScale = Vector3.one;
        label.ForceMeshUpdate(true);
        return label;
    }

    private static void FitMiniLabel(TextMeshPro label, float y)
    {
        // Also update saved labels when upgrading an existing opponent prefab.
        label.rectTransform.localPosition = new Vector3(0f, y, 0f);
        label.rectTransform.sizeDelta = new Vector2(10f, 0.8f);
        label.enableAutoSizing = true;
        label.ForceMeshUpdate(true);
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static void RemoveLegacyLabel(Transform parent, string name)
    {
        Transform label = parent.Find(name);
        if (label != null && label.GetComponent<TMP_Text>() != null)
            Undo.DestroyObjectImmediate(label.gameObject);
    }

    private static SpriteRenderer SpriteChild(Transform parent, string name, Sprite sprite, int order)
    {
        Transform child = Child(parent, name);
        SpriteRenderer renderer = GetOrAdd<SpriteRenderer>(child.gameObject);
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = order;
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return renderer;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static T RequiredAsset<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new InvalidOperationException("Missing setup asset: " + path);
        return asset;
    }

    private static SerializedProperty RequiredProperty(SerializedObject data, string name)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property == null) throw new InvalidOperationException(data.targetObject.GetType().Name
            + " needs the serialized field '" + name + "' before battle setup.");
        return property;
    }

    private static void SetReference(SerializedObject data, string name, Object value)
    {
        RequiredProperty(data, name).objectReferenceValue = value;
    }

    private static void SetReferences<T>(SerializedObject data, string name, T[] objects) where T : Object
    {
        SerializedProperty array = RequiredProperty(data, name);
        array.arraySize = objects.Length;
        for (int i = 0; i < objects.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
    }

    private static void RecordChanges(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }
}
#endif
