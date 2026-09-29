#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Assigns supplied effect sprites while preserving AJN Aokane game labels.</summary>
public static class OriginalEffectsSetup
{
    public static void Author(FallingPiece player)
    {
        if (player == null || !player.TryGetComponent<Board>(out Board board))
            throw new InvalidOperationException("Original effects need the existing player board.");
        AuthorClearFeedback(player, board);
        AuthorLineClear(player, board);
        AuthorDeath(player, board);
    }

    private static void AuthorClearFeedback(FallingPiece player, Board board)
    {
        ClearAttackFeedback feedback = player.GetComponent<ClearAttackFeedback>();
        if (feedback == null) feedback = player.gameObject.AddComponent<ClearAttackFeedback>();
        var data = new SerializedObject(feedback);
        Ref(data, "player", player);
        Ref(data, "combat", player.GetComponent<GarbageCombat>());
        Ref(data, "arena", player.GetComponentInChildren<BattleArena>(true));
        Transform root = Child(board.transform, "Clear feedback");
        float cell = board.CellSize;
        // Preserve AJN Aokane clear/combo labels; supplied textures provide the particle effects.
        foreach (string field in new[] { "tetrisLogo", "tSpinLogo", "allClearLogo", "singleLogo",
            "doubleLogo", "tripleLogo", "comboLogo", "backToBackLogo" })
        {
            SpriteRenderer oldLogo = Property(data, field).objectReferenceValue as SpriteRenderer;
            if (oldLogo != null) { oldLogo.enabled = false; Record(oldLogo); }
            Ref(data, field, null);
        }
        LabelPosition(data, "clearText", 3f, 1.15f, cell);
        LabelPosition(data, "comboText", 1.65f, .9f, cell);
        LabelPosition(data, "attackText", .4f, .7f, cell);

        Sprite[] flight =
        {
            OriginalArtAssets.ThemeSprite("_Textures/eff_attack03.png"),
            OriginalArtAssets.ThemeSprite("_Textures/eff_attack04.png"),
            OriginalArtAssets.ThemeSprite("_Textures/eff_attack05.png"),
            OriginalArtAssets.ThemeSprite("_Textures/kira.png")
        };
        Transform pool = Child(board.transform, "Attack stars");
        var stars = new SpriteRenderer[192];
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i] = Renderer(Child(pool, "Attack star " + (i + 1).ToString("000")), flight[i % flight.Length], 8);
            Size(stars[i], .24f * cell);
        }
        Refs(data, "stars", stars);
        Ref(data, "attackBurstSprite", OriginalArtAssets.ThemeSprite("_Textures/eff_attack00.png"));
        data.ApplyModifiedPropertiesWithoutUndo();
        Record(feedback);
    }

    private static void AuthorLineClear(FallingPiece player, Board board)
    {
        LineClearEffect effect = player.GetComponent<LineClearEffect>();
        if (effect == null) effect = player.gameObject.AddComponent<LineClearEffect>();
        Sprite halo = OriginalArtAssets.ThemeSprite("_Textures/eff_damage00.png");
        Sprite core = OriginalArtAssets.ThemeSprite("_Textures/eff_damage01.png");
        Transform root = Child(board.transform, "Line clear glow");
        var halos = new SpriteRenderer[Board.Height];
        var cores = new SpriteRenderer[Board.Height];
        for (int row = 0; row < Board.Height; row++)
        {
            halos[row] = Renderer(Child(root, "Row " + row + " halo"), halo, 8);
            cores[row] = Renderer(Child(root, "Row " + row + " core"), core, 9);
            Vector3 position = new Vector3(0f, (row - (Board.Height - 1) * .5f) * board.CellSize, 0f);
            halos[row].transform.localPosition = cores[row].transform.localPosition = position;
            Size(halos[row], Board.Width * board.CellSize);
            Size(cores[row], Board.Width * board.CellSize);
        }
        root.gameObject.SetActive(false);
        var data = new SerializedObject(effect);
        Ref(data, "glowRoot", root.gameObject); Ref(data, "haloSprite", halo); Ref(data, "coreSprite", core);
        Refs(data, "halos", halos); Refs(data, "cores", cores);
        data.ApplyModifiedPropertiesWithoutUndo(); Record(effect); Record(root.gameObject);
    }

    private static void AuthorDeath(FallingPiece player, Board board)
    {
        DeathScreen death = player.GetComponent<DeathScreen>();
        if (death == null) death = player.gameObject.AddComponent<DeathScreen>();
        Sprite glow = OriginalArtAssets.ThemeSprite("_Textures/eff_damage01.png");
        Transform root = Child(board.transform, "Death row glow");
        var rows = new SpriteRenderer[Board.Height];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = Renderer(Child(root, "Row " + row + " flash"), glow, 10);
            rows[row].transform.localPosition = new Vector3(0f, (row - (Board.Height - 1) * .5f) * board.CellSize, 0f);
            Size(rows[row], Board.Width * board.CellSize);
        }
        root.gameObject.SetActive(false);
        var data = new SerializedObject(death);
        Ref(data, "glowRoot", root.gameObject); Ref(data, "glowSprite", glow); Refs(data, "rowGlows", rows);
        data.ApplyModifiedPropertiesWithoutUndo(); Record(death); Record(root.gameObject);
    }

    private static void LabelPosition(SerializedObject data, string field, float y, float height, float cell)
    {
        TMP_Text text = Property(data, field).objectReferenceValue as TMP_Text;
        if (text == null) return; // A standalone component still supports its text-only runtime fallback.
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(1.9f, height) * cell;
        rect.anchoredPosition3D = new Vector3(-7.35f * cell, y * cell, 0f);
        text.text = ""; text.enabled = false;
        Record(text); Record(text.rectTransform);
    }

    private static SpriteRenderer Renderer(Transform child, Sprite sprite, int order)
    {
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = child.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = order;
        renderer.enabled = false;
        Record(renderer);
        return renderer;
    }

    private static void Size(SpriteRenderer renderer, float width)
    {
        float scale = width / Mathf.Max(.0001f, renderer.sprite.bounds.size.x);
        renderer.transform.localScale = new Vector3(scale, scale, 1f);
        Record(renderer.transform);
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
        }
        child.localRotation = Quaternion.identity;
        return child;
    }
    private static SerializedProperty Property(SerializedObject data, string field)
        => data.FindProperty(field) ?? throw new InvalidOperationException(data.targetObject.name + " needs field " + field);
    private static void Ref(SerializedObject data, string field, Object value) => Property(data, field).objectReferenceValue = value;
    private static void Refs(SerializedObject data, string field, SpriteRenderer[] values)
    {
        SerializedProperty array = Property(data, field); array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
    private static void Record(Object value)
    {
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }
}
#endif
