#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors the board in the scene once; the game only updates these saved objects.</summary>
public static class BoardSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath = "Assets/Prefabs/TetrisBoard.prefab";
    private const string FontPath = FontSetup.FontPath;

    [MenuItem("Tetris/Set Up Board Scene")]
    public static void SetUpBoardScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before setting up the board scene.");

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Board board = FindBoard(scene);
        if (board == null) throw new InvalidOperationException("SampleScene needs its existing Board component.");
        if (HasSceneReferences(board) && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            TargetingWarningSetup.SetUpTargetingWarning();
            FontSetup.SetUpFont();
            Selection.activeGameObject = board.gameObject;
            Debug.Log("Board is already set up. Edit its sprite and HUD text directly in the scene or prefab.", board);
            return;
        }

        Sprite frame = OriginalArtAssets.ThemeSprite("game_field00^q.png");
        Sprite emptyCell = OriginalArtAssets.ThemeSlice("minos00.png", "Garbage", new Rect(4, 124, 48, 48));
        Sprite warningFrame = TargetingWarningSetup.WarningSprite();
        TMP_FontAsset font = RequiredAsset<TMP_FontAsset>(FontPath);
        board.gameObject.name = "Tetris Board";
        float cellSize = board.CellSize;
        var boardData = new SerializedObject(board);

        SpriteRenderer boardArt = SpriteChild(board.transform, "Board Art", frame, -2);
        boardArt.transform.localScale = Vector3.one * cellSize;
        BoardArtworkSetup.Align(boardArt, board.CellSize);
        var cells = new SpriteRenderer[Board.Width * Board.Height];
        for (int y = 0; y < Board.Height; y++)
        {
            for (int x = 0; x < Board.Width; x++)
            {
                SpriteRenderer cell = SpriteChild(board.transform, $"Cell {x}, {y}", emptyCell, 0);
                cell.transform.localPosition = new Vector3(
                    (x - (Board.Width - 1) * 0.5f) * cellSize,
                    (y - (Board.Height - 1) * 0.5f) * cellSize, 0f);
                cell.transform.localScale = board.GetPieceScale(emptyCell);
                // The PNG contains the empty grid. These saved renderers are only for locked blocks.
                cell.enabled = false;
                cells[y * Board.Width + x] = cell;
            }
        }
        SetReferences(boardData, "boardCells", cells);
        RequiredProperty(boardData, "emptyCellSprite").objectReferenceValue = emptyCell;
        boardData.ApplyModifiedPropertiesWithoutUndo();

        Transform hud = Child(board.transform, "HUD");
        Color mintInk = new Color(0.025f, 0.24f, 0.22f);
        Label(hud, "Hold label", "H O L D", -6.5f, 9.85f, 1.75f, 0.3f, mintInk, font, cellSize);
        Label(hud, "Next label", "N E X T", 6.5f, 9.85f, 1.75f, 0.3f, mintInk, font, cellSize);
        Label(hud, "Place label", "P L A C E", 6.5f, -3f, 2.12f, 0.35f, mintInk, font, cellSize);
        TextMeshPro place = Label(hud, "Place value", "--/--", 6.49f, -4.38f, 2.43f, 0.74f,
            new Color(0.43f, 0.89f, 0.9f), font, cellSize);
        Label(hud, "Knockouts label", "K.O.", 5.98f, -5.47f, 1.04f, 0.4f,
            new Color(0.81f, 0.28f, 0.28f), font, cellSize);
        TextMeshPro knockouts = Label(hud, "Knockouts value", "00", 7.16f, -5.43f, 0.98f, 0.58f,
            new Color(0.89f, 0.92f, 0.88f), font, cellSize);
        TextMeshPro badges = Label(hud, "Badges value", "000", 5.99f, -9.54f, 1.28f, 0.47f,
            new Color(0.93f, 0.94f, 0.68f), font, cellSize);

        Transform danger = Child(board.transform, "High stack warning");
        SpriteRenderer warning = SpriteChild(danger, "Warning frame", warningFrame, 5);
        warning.transform.localPosition = new Vector3(0f, -10.3f * cellSize, 0f);
        TextMeshPro warningLabel = Label(danger, "Watch out label", "WATCH OUT!", 0f, -10.3f,
            6.6f, 0.58f, new Color(1f, 0.36f, 0.16f), font, cellSize);
        warningLabel.fontStyle = FontStyles.Bold;
        warningLabel.sortingOrder = 7;
        warningLabel.ForceMeshUpdate(true);
        danger.gameObject.SetActive(false);

        BoardPresentation presentation = GetOrAdd<BoardPresentation>(board.gameObject);
        var presentationData = new SerializedObject(presentation);
        RequiredProperty(presentationData, "boardSprite").objectReferenceValue = boardArt;
        RequiredProperty(presentationData, "placeText").objectReferenceValue = place;
        RequiredProperty(presentationData, "knockoutsText").objectReferenceValue = knockouts;
        RequiredProperty(presentationData, "badgesText").objectReferenceValue = badges;
        RequiredProperty(presentationData, "danger").objectReferenceValue = danger.gameObject;
        presentationData.ApplyModifiedPropertiesWithoutUndo();
        TargetingWarningSetup.Author(presentation);
        FontSetup.Apply(board.gameObject);
        FontSetup.UpdateProjectDefault();

        AuthorPreview(GetOrAdd<NextPiecePreview>(board.gameObject), "Next pieces", NextPiecePreview.Count * 4);
        AuthorPreview(GetOrAdd<HoldPiecePreview>(board.gameObject), "Hold piece", 4);

        // Frame the saved scene while preserving the user's camera background and fit-on-start setting.
        board.FitCamera();
        Camera camera = Camera.main;
        if (camera != null)
        {
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.transform);
        }

        EnsureFolder("Assets/Prefabs");
        List<ExternalReference> externalReferences = CaptureExternalReferences(board.transform);
        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(board.gameObject, PrefabPath,
            InteractionMode.AutomatedAction);
        if (prefab == null) throw new InvalidOperationException("Could not save " + PrefabPath);
        // The reusable prefab cannot contain scene references; retain them as instance overrides.
        RestoreExternalReferences(externalReferences);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save " + ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = board.gameObject;
        Debug.Log("BOARD_SCENE_SETUP_COMPLETE: saved board sprite, TMP HUD, cells, previews, and prefab.", board);
    }

    /// <summary>Entry point for an isolated Unity editor batch process.</summary>
    public static void BatchSetup()
    {
        try
        {
            SetUpBoardScene();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static Board FindBoard(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Board board = root.GetComponentInChildren<Board>(true);
            if (board != null) return board;
        }
        return null;
    }

    private static bool HasSceneReferences(Board board)
    {
        var boardData = new SerializedObject(board);
        if (!HasReference(boardData, "emptyCellSprite") || !HasReferences(boardData, "boardCells", 200))
            return false;
        BoardPresentation presentation = board.GetComponent<BoardPresentation>();
        if (presentation == null) return false;
        var presentationData = new SerializedObject(presentation);
        foreach (string field in new[] { "boardSprite", "placeText", "knockoutsText", "badgesText", "danger" })
            if (!HasReference(presentationData, field)) return false;
        NextPiecePreview next = board.GetComponent<NextPiecePreview>();
        HoldPiecePreview hold = board.GetComponent<HoldPiecePreview>();
        if (next == null || hold == null) return false;
        var nextData = new SerializedObject(next);
        var holdData = new SerializedObject(hold);
        return HasReference(nextData, "root") && HasReferences(nextData, "previewBlocks", 24)
            && HasReference(holdData, "root") && HasReferences(holdData, "previewBlocks", 4);
    }

    private static bool HasReference(SerializedObject data, string name)
    {
        SerializedProperty property = data.FindProperty(name);
        return property != null && property.objectReferenceValue != null;
    }

    private static bool HasReferences(SerializedObject data, string name, int count)
    {
        SerializedProperty array = data.FindProperty(name);
        if (array == null || !array.isArray || array.arraySize != count) return false;
        for (int i = 0; i < count; i++)
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == null) return false;
        return true;
    }

    private static void AuthorPreview(Component component, string name, int count)
    {
        Transform root = Child(component.transform, name);
        root.gameObject.SetActive(true);
        var blocks = new SpriteRenderer[count];
        for (int i = 0; i < count; i++)
        {
            blocks[i] = SpriteChild(root, "Block " + i, null, 2);
            blocks[i].enabled = false;
        }
        var data = new SerializedObject(component);
        RequiredProperty(data, "root").objectReferenceValue = root;
        SetReferences(data, "previewBlocks", blocks);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static SpriteRenderer SpriteChild(Transform parent, string name, Sprite sprite, int order)
    {
        Transform child = Child(parent, name);
        SpriteRenderer renderer = GetOrAdd<SpriteRenderer>(child.gameObject);
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = order;
        renderer.enabled = true;
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return renderer;
    }

    private static TextMeshPro Label(Transform parent, string name, string value, float x, float y,
        float width, float height, Color color, TMP_FontAsset font, float cellSize)
    {
        Transform child = Child(parent, name);
        TextMeshPro label = GetOrAdd<TextMeshPro>(child.gameObject);
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.text = value;
        label.color = color;
        label.fontSize = 36f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = 60f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.margin = Vector4.zero;
        label.sortingOrder = 3;
        RectTransform rect = label.rectTransform;
        rect.sizeDelta = new Vector2(width, height) * cellSize;
        rect.localPosition = new Vector3(x * cellSize, y * cellSize, 0f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        label.ForceMeshUpdate(true);
        return label;
    }

    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
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
            + " needs the serialized field '" + name + "' before scene setup.");
        return property;
    }

    private static void SetReferences(SerializedObject data, string name, SpriteRenderer[] renderers)
    {
        SerializedProperty array = RequiredProperty(data, name);
        array.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }

    private sealed class ExternalReference
    {
        public Component Owner;
        public string Property;
        public Object Value;
    }

    private static List<ExternalReference> CaptureExternalReferences(Transform boardRoot)
    {
        var result = new List<ExternalReference>();
        foreach (Component component in boardRoot.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            var data = new SerializedObject(component);
            SerializedProperty property = data.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object value = property.objectReferenceValue;
                Transform target = value is Component targetComponent ? targetComponent.transform
                    : value is GameObject targetObject ? targetObject.transform : null;
                if (target == null || !target.gameObject.scene.IsValid() || target.IsChildOf(boardRoot)) continue;
                result.Add(new ExternalReference { Owner = component, Property = property.propertyPath, Value = value });
            }
        }
        return result;
    }

    private static void RestoreExternalReferences(List<ExternalReference> references)
    {
        foreach (ExternalReference reference in references)
        {
            if (reference.Owner == null) continue;
            var data = new SerializedObject(reference.Owner);
            SerializedProperty property = data.FindProperty(reference.Property);
            if (property == null) continue;
            property.objectReferenceValue = reference.Value;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(reference.Owner);
        }
    }
}
#endif
