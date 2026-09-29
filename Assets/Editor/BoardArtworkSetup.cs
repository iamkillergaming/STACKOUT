#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Aligns supplied board artwork to its 48-pixel cells, with legacy slice support.</summary>
public static class BoardArtworkSetup
{
    private const string ArtPath = OriginalArtAssets.Theme + "game_field00^q.png";
    private const string LegacyArtPath = "Assets/Art/Board/BoardFrame.png";
    private const string PlayerPrefab = "Assets/Prefabs/TetrisBoard.prefab";

    public static void Align(SpriteRenderer renderer, float cellSize)
    {
        Sprite sprite = renderer.sprite;
        if (sprite == null) return;
        string path = AssetDatabase.GetAssetPath(sprite);
        if (path == ArtPath)
        {
            float nativeScale = sprite.rect.width * cellSize / (48f * sprite.bounds.size.x);
            renderer.transform.localPosition = new Vector3(0f, 0f, renderer.transform.localPosition.z);
            renderer.transform.localScale = new Vector3(nativeScale, nativeScale, 1f);
            return;
        }
        if (path != LegacyArtPath) return;
        var importer = (TextureImporter)AssetImporter.GetAtPath(LegacyArtPath);
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        Rect sourceRect = new Rect(0, 0, width, height);
        if (importer.spriteImportMode == SpriteImportMode.Multiple)
        {
#pragma warning disable 0618 // Read existing slice metadata; never rewrite the user's importer.
            foreach (SpriteMetaData slice in importer.spritesheet)
                if (slice.name == sprite.name) { sourceRect = slice.rect; break; }
#pragma warning restore 0618
        }
        Vector2 pivotFraction = sprite.pivot / sprite.rect.size;
        Vector2 pivot = sourceRect.position + Vector2.Scale(sourceRect.size, pivotFraction);
        Vector2 gridOrigin = new Vector2(width * .5f, height * (10.9f / 21.2f));
        float units = cellSize / importer.spritePixelsPerUnit;
        Vector2 offset = (pivot - gridOrigin) * units;
        renderer.transform.localPosition = new Vector3(offset.x, offset.y, renderer.transform.localPosition.z);
        // Import downsampling must not change the physical size of the cells in the artwork.
        renderer.transform.localScale = new Vector3(sourceRect.width * units / sprite.bounds.size.x,
            sourceRect.height * units / sprite.bounds.size.y, 1f);
    }

    public static void RepairMissingFrame(Board sceneBoard)
    {
        Sprite sprite = OriginalArtAssets.ThemeSprite("game_field00^q.png");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                if (Repair(root.GetComponent<Board>(), sprite))
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Repair(sceneBoard, sprite);
    }

    private static bool Repair(Board board, Sprite sprite)
    {
        if (board == null) return false;
        Transform art = board.transform.Find("Board Art");
        if (art == null || !art.TryGetComponent(out SpriteRenderer renderer) || renderer.sprite != null) return false;
        renderer.sprite = sprite;
        Align(renderer, board.CellSize);
        EditorUtility.SetDirty(renderer);
        EditorUtility.SetDirty(renderer.transform);
        if (PrefabUtility.IsPartOfPrefabInstance(renderer))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.transform);
        }
        return true;
    }
}
#endif
