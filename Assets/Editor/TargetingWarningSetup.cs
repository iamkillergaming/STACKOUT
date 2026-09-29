#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors the supplied warning artwork and saved targeting lines.</summary>
public static class TargetingWarningSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath = "Assets/Prefabs/TetrisBoard.prefab";
    private const string TargetingSoundPath = "Assets/General Sounds/se_game_target.wav";
    private const int OpponentCount = 98;

    [MenuItem("Tetris/Set Up Targeting Warning")]
    public static void SetUpTargetingWarning()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before setting up the targeting warning.");

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (Author(prefabRoot.GetComponent<BoardPresentation>()))
                {
                    if (PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath) == null)
                        throw new InvalidOperationException("Could not save targeting warning in " + PrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        BoardPresentation presentation = FindPlayerPresentation(scene);
        Author(presentation);
        // Saving existing scene overrides retains all external audio references and camera values.
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save targeting warning in " + ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = presentation.gameObject;
        Debug.Log("TARGETING_WARNING_SETUP_COMPLETE: existing warning wired to "
            + OpponentCount * BoardPresentation.TargetingSegmentsPerOpponent
            + " saved targeting segments for 98 opponents.", presentation);
    }

    public static void BatchSetup()
    {
        try
        {
            SetUpTargetingWarning();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tetris/Set Up Targeting Sound")]
    public static void SetUpTargetingSound()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before setting up the targeting sound.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (AuthorSound(root.GetComponent<BoardPresentation>())
                    && PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                    throw new InvalidOperationException("Could not save targeting sound in " + PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        BoardPresentation presentation = FindPlayerPresentation(scene);
        AuthorSound(presentation);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save targeting sound in " + ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = presentation.gameObject;
        Debug.Log("TARGETING_SOUND_SETUP_COMPLETE: saved dedicated source and supplied targeting clip.", presentation);
    }

    public static void BatchSoundSetup()
    {
        try { SetUpTargetingSound(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    public static Sprite WarningSprite() => OriginalArtAssets.ThemeSlice(
        "enUS/caution00.png", "Original WATCH OUT", new Rect(29, 5, 398, 52));

    /// <summary>Keeps the complete warning graphic at its native aspect ratio in every setup path.</summary>
    public static bool AuthorWarningArtwork(SpriteRenderer warningFrame, float cellSize)
    {
        if (warningFrame == null)
            throw new InvalidOperationException("The saved warning needs its existing Warning frame renderer.");

        Sprite sprite = WarningSprite();
        bool changed = false;
        if (warningFrame.sprite != sprite || warningFrame.color != Color.white || !warningFrame.enabled)
        {
            warningFrame.sprite = sprite;
            warningFrame.color = Color.white;
            warningFrame.enabled = true;
            RecordChanges(warningFrame);
            changed = true;
        }
        float scale = 8.4f * cellSize / sprite.bounds.size.x;
        var localScale = new Vector3(scale, scale, 1f);
        if (warningFrame.transform.localScale != localScale)
        {
            warningFrame.transform.localScale = localScale;
            RecordChanges(warningFrame.transform);
            changed = true;
        }
        Transform label = warningFrame.transform.parent != null
            ? warningFrame.transform.parent.Find("Watch out label") : null;
        if (label != null && label.gameObject.activeSelf)
        {
            label.gameObject.SetActive(false);
            RecordChanges(label.gameObject);
            changed = true;
        }
        return changed;
    }

    /// <summary>Adds only missing sound wiring, preserving any source or clip already chosen.</summary>
    public static bool AuthorSound(BoardPresentation presentation)
    {
        if (presentation == null) throw new InvalidOperationException("The player needs its existing BoardPresentation.");
        var data = new SerializedObject(presentation);
        SerializedProperty sourceProperty = RequiredProperty(data, "targetingSoundSource");
        SerializedProperty clipProperty = RequiredProperty(data, "targetingSound");
        bool changed = false;
        if (sourceProperty.objectReferenceValue == null)
        {
            Transform audioRoot = presentation.transform.Find("Targeting audio");
            if (audioRoot == null)
            {
                var audioObject = new GameObject("Targeting audio");
                audioRoot = audioObject.transform;
                audioRoot.SetParent(presentation.transform, false);
                Undo.RegisterCreatedObjectUndo(audioObject, "Create targeting audio");
            }
            AudioSource source = audioRoot.GetComponent<AudioSource>();
            if (source == null)
            {
                source = audioRoot.gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                RecordChanges(source);
            }
            sourceProperty.objectReferenceValue = source;
            changed = true;
        }
        if (clipProperty.objectReferenceValue == null)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(TargetingSoundPath);
            if (clip == null) throw new InvalidOperationException("Missing targeting sound: " + TargetingSoundPath);
            clipProperty.objectReferenceValue = clip;
            changed = true;
        }
        if (data.ApplyModifiedPropertiesWithoutUndo()) changed = true;
        if (changed) RecordChanges(presentation);
        return changed;
    }

    /// <summary>Idempotently authors warning artwork, references, and its separate line pool.</summary>
    public static bool Author(BoardPresentation presentation)
    {
        if (presentation == null)
            throw new InvalidOperationException("The player needs its existing BoardPresentation component.");
        Sprite whiteCell = OriginalArtAssets.CommonSprite("_texture/footer_base^q.png");

        var data = new SerializedObject(presentation);
        SerializedProperty dangerProperty = RequiredProperty(data, "danger");
        GameObject danger = dangerProperty.objectReferenceValue as GameObject;
        if (danger == null)
        {
            Transform existing = presentation.transform.Find("Targeting warning")
                ?? presentation.transform.Find("High stack warning");
            if (existing != null) danger = existing.gameObject;
        }
        if (danger == null)
            throw new InvalidOperationException("The board needs its saved warning artwork before targeting setup.");
        Transform frameObject = danger.transform.Find("Warning frame");
        SpriteRenderer warningFrame = frameObject != null ? frameObject.GetComponent<SpriteRenderer>() : null;
        if (warningFrame == null)
            throw new InvalidOperationException("The saved warning needs its existing Warning frame renderer.");

        Board board = presentation.GetComponent<Board>();
        if (board == null) throw new InvalidOperationException("The targeting warning needs its existing Board.");
        bool changed = AuthorWarningArtwork(warningFrame, board.CellSize);
        if (danger.name != "Targeting warning")
        {
            Undo.RecordObject(danger, "Rename targeting warning");
            danger.name = "Targeting warning";
            RecordChanges(danger);
            changed = true;
        }
        if (dangerProperty.objectReferenceValue != danger)
        {
            dangerProperty.objectReferenceValue = danger;
            changed = true;
        }
        SerializedProperty warningProperty = RequiredProperty(data, "warningFrame");
        if (warningProperty.objectReferenceValue != warningFrame)
        {
            warningProperty.objectReferenceValue = warningFrame;
            changed = true;
        }

        // Keep rays outside the warning object so their length cannot enlarge warning/camera bounds.
        Transform lineRoot = presentation.transform.Find("Targeting lines");
        if (lineRoot == null)
        {
            var rootObject = new GameObject("Targeting lines");
            lineRoot = rootObject.transform;
            lineRoot.SetParent(presentation.transform, false);
            Undo.RegisterCreatedObjectUndo(rootObject, "Create targeting lines");
            changed = true;
        }
        float width = Mathf.Max(0.001f, RequiredProperty(data, "targetingLineWidth").floatValue);
        SerializedProperty lines = RequiredProperty(data, "targetingLines");
        int lineCount = OpponentCount * BoardPresentation.TargetingSegmentsPerOpponent;
        if (lines.arraySize != lineCount) { lines.arraySize = lineCount; changed = true; }
        for (int opponent = 0; opponent < OpponentCount; opponent++)
        {
            string baseName = "Targeting line " + (opponent + 1).ToString("00");
            Color firstSegmentColor = new Color(1f, 0.84f, 0.18f, 0.9f);
            for (int segment = 0; segment < BoardPresentation.TargetingSegmentsPerOpponent; segment++)
            {
                // Keep the original line object as segment zero; additional segments are its siblings.
                string name = segment == 0 ? baseName : baseName + " segment " + (segment + 1);
                Transform child = lineRoot.Find(name);
                if (child == null)
                {
                    var lineObject = new GameObject(name);
                    child = lineObject.transform;
                    child.SetParent(lineRoot, false);
                    Undo.RegisterCreatedObjectUndo(lineObject, "Create targeting line segment");
                    changed = true;
                }
                SpriteRenderer line = child.GetComponent<SpriteRenderer>();
                if (line == null)
                {
                    line = child.gameObject.AddComponent<SpriteRenderer>();
                    line.color = firstSegmentColor;
                    changed = true;
                }
                if (segment == 0) firstSegmentColor = line.color;
                var scale = new Vector3(0f, width, 1f);
                if (line.sprite != whiteCell || line.sortingOrder != 4 || line.enabled)
                {
                    line.sprite = whiteCell;
                    line.sortingOrder = 4;
                    line.enabled = false;
                    RecordChanges(line);
                    changed = true;
                }
                if (child.localPosition != Vector3.zero || child.localRotation != Quaternion.identity || child.localScale != scale)
                {
                    child.localPosition = Vector3.zero;
                    child.localRotation = Quaternion.identity;
                    child.localScale = scale;
                    RecordChanges(child);
                    changed = true;
                }
                int index = opponent * BoardPresentation.TargetingSegmentsPerOpponent + segment;
                SerializedProperty reference = lines.GetArrayElementAtIndex(index);
                if (reference.objectReferenceValue != line)
                {
                    reference.objectReferenceValue = line;
                    changed = true;
                }
            }
        }
        if (data.ApplyModifiedPropertiesWithoutUndo()) changed = true;
        if (changed) RecordChanges(presentation);
        return AuthorSound(presentation) || changed;
    }

    private static BoardPresentation FindPlayerPresentation(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            FallingPiece player = root.GetComponentInChildren<FallingPiece>(true);
            if (player != null) return player.GetComponent<BoardPresentation>();
        }
        throw new InvalidOperationException("SampleScene needs its existing player board.");
    }

    private static SerializedProperty RequiredProperty(SerializedObject data, string name)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property == null) throw new InvalidOperationException("BoardPresentation needs the serialized field '"
            + name + "' before targeting warning setup.");
        return property;
    }

    private static void RecordChanges(UnityEngine.Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
