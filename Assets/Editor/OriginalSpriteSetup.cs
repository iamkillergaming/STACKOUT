#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Applies the supplied default theme to the saved game and reusable board prefabs.</summary>
public static class OriginalSpriteSetup
{
    [MenuItem("Tetris/Use Original Sprites")]
    public static void SetUp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before applying artwork.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }
        Sprite[] tiles = Tiles();
        Sprite gray = OriginalArtAssets.ThemeSlice("minos00.png", "Garbage", new Rect(4,124,48,48));
        Dictionary<string, Sprite> replacements = LegacyReplacements(gray);
        UpgradePrefab("Assets/Prefabs/TetrisBoard.prefab", root =>
        {
            AuthorPlayer(root.GetComponent<FallingPiece>(), tiles, gray);
            ReplaceLegacyReferences(root, replacements);
            FontSetup.Apply(root);
        });
        UpgradePrefab("Assets/Prefabs/BotOpponent.prefab", root =>
        {
            AuthorBot(root.GetComponent<BotPlayer>(), tiles, gray);
            ReplaceLegacyReferences(root, replacements);
            FontSetup.Apply(root);
        });
        FallingPiece player = Object.FindFirstObjectByType<FallingPiece>();
        if (player == null) throw new InvalidOperationException("The saved player is missing.");
        AuthorPlayer(player, tiles, gray);
        BattleArena arena = player.GetComponentInChildren<BattleArena>(true);
        foreach (BotPlayer bot in arena.GetComponentsInChildren<BotPlayer>(true)) AuthorBot(bot, tiles, gray);
        MatchFlowSetup.Author(arena, player, Object.FindFirstObjectByType<BackgroundMusic>());
        AuthorMeter(arena, gray);
        AuthorBackground(Camera.main);
        foreach (GameObject root in scene.GetRootGameObjects()) ReplaceLegacyReferences(root, replacements);
        FontSetup.Apply(scene);
        FontSetup.UpdateProjectDefault();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save original artwork.");
        AssetDatabase.SaveAssets();
        Debug.Log("ORIGINAL_SPRITE_SETUP_COMPLETE: original theme, blocks, UI, effects and opponent elimination.");
    }
    public static void BatchSetup()
    {
        try { SetUp(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    private static Sprite[] Tiles()
    {
        int[] columns = {6,7,4,2,5,1,3};
        string[] names = {"I","O","T","S","Z","J","L"};
        var tiles = new Sprite[7];
        // Complete atlas imports before holding references to its sub-assets.
        OriginalArtAssets.ThemeSlice("minos00.png", "Garbage", new Rect(4,124,48,48));
        for (int i=0;i<tiles.Length;i++) OriginalArtAssets.ThemeSlice("minos00.png", names[i],new Rect(4+56*columns[i],124,48,48));
        for (int i=0;i<tiles.Length;i++) tiles[i]=OriginalArtAssets.ThemeSlice("minos00.png", names[i],new Rect(4+56*columns[i],124,48,48));
        return tiles;
    }

    private static Dictionary<string, Sprite> LegacyReplacements(Sprite gray)
    {
        return new Dictionary<string, Sprite>
        {
            { "Assets/Art/Board/WhiteCell.png", OriginalArtAssets.CommonSprite("_texture/footer_base^q.png") },
            { "Assets/Art/Board/MiniBlock.png", gray },
            { "Assets/Art/Board/MiniBoardFrame.png", OriginalArtAssets.CommonSprite("_texture/game_enemy.01.png") },
            { "Assets/Art/Board/BoardFrame.png", OriginalArtAssets.ThemeSprite("game_field00^q.png") },
            { "Assets/Art/Board/WarningFrame.png", TargetingWarningSetup.WarningSprite() },
            { "Assets/Art/Board/AttackStar.png", OriginalArtAssets.ThemeSprite("_Textures/eff_attack03.png") },
            { "Assets/Art/Board/Medal.png", OriginalArtAssets.ThemeSlice("badge00.png", "Original badge earned", new Rect(168,0,56,56)) }
        };
    }

    // Includes disabled slot artwork, targeting lines, meter tracks, and component fallback sprites.
    // Preserve physical sizes: the supplied textures have different dimensions and pixels per unit.
    private static void ReplaceLegacyReferences(GameObject root, Dictionary<string, Sprite> replacements)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            SpriteRenderer renderer = component as SpriteRenderer;
            Sprite previous = renderer != null ? renderer.sprite : null;
            var data = new SerializedObject(component);
            SerializedProperty property = data.GetIterator();
            bool changed = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference
                    || !(property.objectReferenceValue is Sprite sprite)
                    || !replacements.TryGetValue(AssetDatabase.GetAssetPath(sprite), out Sprite replacement)) continue;
                property.objectReferenceValue = replacement;
                changed = true;
            }
            if (!changed) continue;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (renderer != null && previous != null && renderer.sprite != previous)
            {
                Vector3 scale = renderer.transform.localScale;
                scale.x *= previous.bounds.size.x / renderer.sprite.bounds.size.x;
                scale.y *= previous.bounds.size.y / renderer.sprite.bounds.size.y;
                renderer.transform.localScale = scale;
                Record(renderer.transform);
            }
            Record(component);
        }
    }
    private static void UpgradePrefab(string path, Action<GameObject> author)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try { author(root); if (PrefabUtility.SaveAsPrefabAsset(root,path)==null) throw new InvalidOperationException("Could not save "+path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static void AuthorPlayer(FallingPiece player, Sprite[] tiles, Sprite gray)
    {
        Board board = player.GetComponent<Board>();
        float cell=board.CellSize;
        Refs(player,"cellSprites",tiles);
        // The complete original cell atlas is now authoritative; drop unused whole-shape artwork.
        var playerData = new SerializedObject(player);
        foreach (string field in new[] { "iSprite", "oSprite", "tSprite", "sSprite", "zSprite", "jSprite", "lSprite" })
            playerData.FindProperty(field).objectReferenceValue = null;
        playerData.ApplyModifiedPropertiesWithoutUndo(); Record(player);
        AuthorBoardCells(board,gray);
        // The scene camera is already framed for the complete roster. Art padding should not change its zoom.
        var boardData=new SerializedObject(board);
        boardData.FindProperty("fitCameraOnStart").boolValue=false;
        boardData.ApplyModifiedPropertiesWithoutUndo(); Record(board);
        Sprite frame=OriginalArtAssets.ThemeSprite("game_field00^q.png");
        SpriteRenderer art=SpriteAt(board.transform,"Board Art",frame,-2);
        art.transform.localPosition=Vector3.zero;
        NativeScale(art,48f,cell);
        Record(art.transform);
        Transform hud=Child(board.transform,"HUD");
        // Keep the user's AJN typography while replacing the surrounding texture artwork.
        Hide(hud.Find("Original HOLD")); Hide(hud.Find("Original NEXT")); Hide(hud.Find("Original PLACE"));
        Show(hud.Find("Hold label")); Show(hud.Find("Next label")); Show(hud.Find("Place label"));
        SpriteRenderer stats=SpriteAt(hud,"Original stats inset",OriginalArtAssets.ThemeSprite("game_field_ko^t.png"),-1);
        stats.transform.localPosition=new Vector3(6.55f*cell,-6.5f*cell,0);
        NativeScale(stats,48f,cell); Record(stats.transform);
        SpriteRenderer rail=SpriteAt(hud,"Original incoming rail",OriginalArtAssets.ThemeSprite("game_field_notice^t.png"),-1);
        rail.transform.localPosition=new Vector3(-5.85f*cell,-1.7f*cell,0);
        NativeScale(rail,48f,cell); Record(rail.transform);
        BoardPresentation presentation=player.GetComponent<BoardPresentation>();
        var presentationData=new SerializedObject(presentation);
        SpriteRenderer warning=presentationData.FindProperty("warningFrame").objectReferenceValue as SpriteRenderer;
        if (warning!=null)
        {
            TargetingWarningSetup.AuthorWarningArtwork(warning, cell);
        }
        ConfigureGarbage(player.GetComponent<GarbageCombat>(),gray);
        OriginalEffectsSetup.Author(player);
    }
    private static void AuthorBot(BotPlayer bot, Sprite[] tiles, Sprite gray)
    {
        Refs(bot,"cellSprites",tiles);
        AuthorBoardCells(bot.GetComponent<Board>(),gray);
        ConfigureGarbage(bot.GetComponent<GarbageCombat>(),gray);
        OpponentEliminationSetup.Author(bot);
    }
    private static void AuthorBoardCells(Board board,Sprite gray)
    {
        var data=new SerializedObject(board);
        data.FindProperty("emptyCellSprite").objectReferenceValue=gray;
        var cells=data.FindProperty("boardCells");
        for(int i=0;i<cells.arraySize;i++)
        {
            SpriteRenderer sprite=cells.GetArrayElementAtIndex(i).objectReferenceValue as SpriteRenderer;
            if(sprite==null)continue;
            sprite.sprite=gray; sprite.color=Color.white;
            sprite.transform.localScale=board.GetPieceScale(gray);
            Record(sprite);Record(sprite.transform);
        }
        data.ApplyModifiedPropertiesWithoutUndo(); Record(board);
    }
    private static void ConfigureGarbage(GarbageCombat combat,Sprite gray)
    {
        if(combat==null)return;
        var data=new SerializedObject(combat);
        data.FindProperty("garbageSprite").objectReferenceValue=gray;
        data.FindProperty("garbageColor").colorValue=Color.white;
        data.ApplyModifiedPropertiesWithoutUndo(); Record(combat);
    }
    private static void AuthorMeter(BattleArena arena,Sprite gray)
    {
        GarbageMeter meter=arena.GetComponentInChildren<GarbageMeter>(true);
        if(meter==null)return;
        var data=new SerializedObject(meter);
        var segments=data.FindProperty("segments");
        for(int i=0;i<segments.arraySize;i++)
        {
            SpriteRenderer r=segments.GetArrayElementAtIndex(i).objectReferenceValue as SpriteRenderer;
            if(r==null)continue;
            r.sprite=gray;
            float cell=arena.GetComponentInParent<Board>().CellSize;
            r.transform.localScale=new Vector3(cell/gray.bounds.size.x,cell/gray.bounds.size.y,1);
            Record(r);Record(r.transform);
        }
        data.FindProperty("queuedColor").colorValue=Color.white;
        data.ApplyModifiedPropertiesWithoutUndo();Record(meter);
    }
    private static void AuthorBackground(Camera camera)
    {
        if(camera==null)return;
        SpriteRenderer background=SpriteAt(camera.transform,"Original theme background",OriginalArtAssets.ThemeSprite("bg0002_bg0.png"),-100);
        background.transform.localPosition=new Vector3(0,0,20);
        background.transform.localRotation=Quaternion.identity;
        float height=2*camera.orthographicSize;
        float scale=Mathf.Max(height/background.sprite.bounds.size.y,height*(16f/9f)/background.sprite.bounds.size.x);
        // Slight uniform overscan covers pixel-perfect camera rounding at 1080p.
        scale*=1.025f;
        background.transform.localScale=new Vector3(scale,scale,1);
        Record(background.transform);
    }
    private static void NativeScale(SpriteRenderer r,float pixelsPerCell,float cell)
    {
        float scale=r.sprite.rect.width/pixelsPerCell*cell/r.sprite.bounds.size.x;
        r.transform.localScale=new Vector3(scale,scale,1);
    }
    private static void Refs(Component owner,string field,Sprite[] refs)
    {
        var data=new SerializedObject(owner);var array=data.FindProperty(field);array.arraySize=refs.Length;
        for(int i=0;i<refs.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=refs[i];
        data.ApplyModifiedPropertiesWithoutUndo();Record(owner);
    }
    private static Transform Child(Transform parent,string name)
    {
        Transform result=parent.Find(name);
        if(result==null){result=new GameObject(name).transform;result.SetParent(parent,false);}
        return result;
    }
    private static SpriteRenderer SpriteAt(Transform parent,string name,Sprite sprite,int order)
    {
        Transform child=Child(parent,name);SpriteRenderer r=child.GetComponent<SpriteRenderer>();
        if(r==null)r=child.gameObject.AddComponent<SpriteRenderer>();
        r.sprite=sprite;r.color=Color.white;r.sortingOrder=order;r.enabled=true;Record(r);return r;
    }
    private static void Hide(Transform target){if(target!=null){target.gameObject.SetActive(false);Record(target.gameObject);}}
    private static void Show(Transform target){if(target!=null){target.gameObject.SetActive(true);Record(target.gameObject);}}
    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if(PrefabUtility.IsPartOfPrefabInstance(target))PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
#endif
