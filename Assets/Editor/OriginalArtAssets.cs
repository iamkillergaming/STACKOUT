#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>Imports the supplied originals without changing their pixels. Atlas rectangles use bottom-left coordinates.</summary>
public static class OriginalArtAssets
{
    public const string Theme = "Assets/Nintendo Switch - Tetris 99 - Themes - Default (Tetris 99)/";
    public const string Common = "Assets/Nintendo Switch - Tetris 99 - Themes - Common Elements/";
    public static Sprite ThemeSprite(string relative) => Load(Theme + relative);
    public static Sprite CommonSprite(string relative) => Load(Common + relative);
    public static Sprite ThemeSlice(string relative, string name, Rect rect) => Slice(Theme + relative, name, rect);
    public static Sprite CommonSlice(string relative, string name, Rect rect) => Slice(Common + relative, name, rect);

    private static TextureImporter Import(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            throw new InvalidOperationException("Original sprite is missing: " + path);
        FilterMode filter = path.EndsWith("minos00.png") ? FilterMode.Point : FilterMode.Bilinear;
        bool changed = importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled
            || !importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp
            || importer.filterMode != filter
            || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize < 2048;
        importer.textureType = TextureImporterType.Sprite;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = Mathf.Max(2048, importer.maxTextureSize);
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = filter;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        if (settings.spriteMeshType != SpriteMeshType.FullRect) changed = true;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        if (importer.spriteImportMode == SpriteImportMode.None) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
        if (changed) importer.SaveAndReimport();
        return importer;
    }
    private static Sprite Load(string path)
    {
        Import(path);
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Sprite sprite && (sprite.name == "Full" || ((TextureImporter)AssetImporter.GetAtPath(path)).spriteImportMode == SpriteImportMode.Single)) return sprite;
        var source = (TextureImporter)AssetImporter.GetAtPath(path);
        source.GetSourceTextureWidthAndHeight(out int width, out int height);
        return Slice(path, "Full", new Rect(0, 0, width, height));
    }
    private static Sprite Slice(string path, string name, Rect rect)
    {
        TextureImporter importer = Import(path);
        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.SaveAndReimport();
        }
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var slices = new List<SpriteRect>(provider.GetSpriteRects());
        int index = slices.FindIndex(s => s.name == name);
        if (index < 0 || slices[index].rect != rect)
        {
            var slice = new SpriteRect { name = name, rect = rect, alignment = SpriteAlignment.Center,
                pivot = new Vector2(.5f,.5f), spriteID = index < 0 ? GUID.Generate() : slices[index].spriteID };
            if (index < 0) slices.Add(slice); else slices[index] = slice;
            provider.SetSpriteRects(slices.ToArray());
            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            var pairs = new List<SpriteNameFileIdPair>(names.GetNameFileIdPairs());
            pairs.RemoveAll(pair => pair.name == name);
            pairs.Add(new SpriteNameFileIdPair(name, slice.spriteID));
            names.SetNameFileIdPairs(pairs);
            provider.Apply();
            importer.SaveAndReimport();
        }
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Sprite sprite && sprite.name == name) return sprite;
        throw new InvalidOperationException("Original atlas slice did not import: " + path + " / " + name);
    }
}
#endif
