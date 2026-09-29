#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Uses the supplied AJN Aokane font on game text without changing its layout.</summary>
public static class FontSetup
{
    public const string FontPath = "Assets/font/ajn-aokane.otf/ajn-aokane SDF.asset";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    public static TMP_FontAsset GetFont()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) throw new InvalidOperationException("Missing game font: " + FontPath);
        return font;
    }

    public static bool Apply(GameObject root)
    {
        if (root == null) return false;
        TMP_FontAsset font = GetFont();
        bool useAsciiX = !font.HasCharacter('\u00D7');
        bool changed = false;
        foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
        {
            string value = useAsciiX ? label.text.Replace('\u00D7', 'X') : label.text;
            if (label.font == font && label.fontSharedMaterial == font.material && label.text == value) continue;
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.text = value;
            label.ForceMeshUpdate(true);
            EditorUtility.SetDirty(label);
            if (PrefabUtility.IsPartOfPrefabInstance(label))
                PrefabUtility.RecordPrefabInstancePropertyModifications(label);
            changed = true;
        }
        return changed;
    }

    public static bool Apply(Scene scene)
    {
        bool changed = false;
        foreach (GameObject root in scene.GetRootGameObjects()) changed |= Apply(root);
        return changed;
    }

    public static void ApplyPrefab(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (Apply(root) && PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                throw new InvalidOperationException("Could not save game font in " + path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void UpdateProjectDefault()
    {
        TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath);
        if (settings == null) throw new InvalidOperationException("Missing TMP project settings: " + SettingsPath);
        var data = new SerializedObject(settings);
        SerializedProperty defaultFont = data.FindProperty("m_defaultFontAsset");
        if (defaultFont == null) throw new InvalidOperationException("TMP settings have no default font field.");
        TMP_FontAsset font = GetFont();
        if (defaultFont.objectReferenceValue == font) return;
        defaultFont.objectReferenceValue = font;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    [MenuItem("Tetris/Use AJN Aokane Font")]
    public static void SetUpFont()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before updating saved game fonts.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        ApplyPrefab("Assets/Prefabs/TetrisBoard.prefab");
        ApplyPrefab("Assets/Prefabs/BotOpponent.prefab");
        Apply(scene);
        UpdateProjectDefault();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save game fonts in " + ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("GAME_FONT_SETUP_COMPLETE: AJN Aokane applied to game text and the TMP default font.");
    }
}
#endif
