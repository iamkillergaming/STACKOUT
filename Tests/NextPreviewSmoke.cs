#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copy into an isolated project's Assets; Run creates and saves a temporary scene.
public sealed class NextPreviewSmoke : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Board board;
    private FallingPiece player;
    private NextPiecePreview preview;
    private Sprite[][] tiles;
    private Vector2Int[][] shapes;
    private int[] spriteTurns;
    private Queue<int> bag;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Camera", typeof(Camera)).tag = "MainCamera";
        new GameObject("Board", typeof(Board), typeof(NextPreviewSmoke));
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
            "Assets/NextPreviewSmoke.unity");
        EditorApplication.EnterPlaymode();
    }

    private void Check(bool value, string message)
    {
        if (value) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new Exception(message);
    }

    private object Get(string name) => typeof(FallingPiece).GetField(name, Private).GetValue(player);
    private void Set(string name, object value) => typeof(FallingPiece).GetField(name, Private).SetValue(player, value);

    private void MakeFixtures()
    {
        board = GetComponent<Board>();
        preview = gameObject.AddComponent<NextPiecePreview>();
        player = gameObject.AddComponent<FallingPiece>();
        player.enabled = false; // Run actual Spawn, without imported shape sprites or audio hardware.
        Set("board", board);
        Set("nextPreview", preview);
        tiles = (Sprite[][])Get("tiles");
        spriteTurns = (int[])Get("spriteTurns");
        shapes = (Vector2Int[][])typeof(FallingPiece).GetField("Shapes",
            BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        bag = (Queue<int>)Get("bag");
        for (int type = 0; type < tiles.Length; type++)
        {
            tiles[type] = new Sprite[4];
            spriteTurns[type] = type % 4;
            for (int cell = 0; cell < 4; cell++)
            {
                // Unique instances detect both wrong piece types and swapped cells.
                tiles[type][cell] = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                    new Vector2(0.5f, 0.5f), 1f);
                tiles[type][cell].name = $"Fixture type {type} cell {cell}";
            }
        }
        foreach (string field in new[] { "renderers", "ghostRenderers" })
        {
            var renderers = (SpriteRenderer[])Get(field);
            for (int cell = 0; cell < renderers.Length; cell++)
            {
                var block = new GameObject($"Fixture {field} {cell}", typeof(SpriteRenderer));
                block.transform.SetParent(board.transform, false);
                renderers[cell] = block.GetComponent<SpriteRenderer>();
            }
        }
    }

    private void CheckPreview(int[] upcoming)
    {
        Check(upcoming.Length >= 6, "Queue does not contain six future pieces");
        Transform root = board.transform.Find("Next pieces");
        Check(root != null && root.gameObject.activeInHierarchy, "Next preview is missing or hidden");
        Check(root.Find("Next label") != null, "Next preview label is missing");
        Check(root.GetComponentsInChildren<SpriteRenderer>().Length == 24,
            "Preview does not contain exactly six four-block pieces");
        float previousCenterY = float.PositiveInfinity;
        for (int slot = 0; slot < 6; slot++)
        {
            Transform piece = root.Find($"Next {slot}");
            Check(piece != null && piece.gameObject.activeInHierarchy, $"Preview slot {slot} is missing");
            int type = upcoming[slot];
            float centerY = 0f;
            for (int cell = 0; cell < 4; cell++)
            {
                Transform block = piece.Find($"Block {cell}");
                Check(block != null, $"Slot {slot} block {cell} is missing");
                SpriteRenderer renderer = block.GetComponent<SpriteRenderer>();
                Check(renderer != null && renderer.enabled && renderer.sprite == tiles[type][cell],
                    $"Slot {slot} displays incorrect piece artwork at cell {cell}");
                Check(Quaternion.Angle(block.localRotation,
                    Quaternion.Euler(0f, 0f, -90f * spriteTurns[type])) < 0.01f,
                    $"Slot {slot} lost imported sprite orientation");
                Vector3 local = board.transform.InverseTransformPoint(block.position);
                Check(local.x > Board.Width * board.CellSize * 0.5f,
                    $"Slot {slot} overlaps the board instead of sitting to its right");
                centerY += local.y / 4f;
            }
            Check(centerY < previousCenterY, "Next pieces are not ordered from top to bottom");
            previousCenterY = centerY;
        }
    }

    private void CheckCamera()
    {
        Camera camera = Camera.main;
        camera.aspect = 9f / 16f;
        camera.backgroundColor = new Color(0.13f, 0.29f, 0.43f, 1f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        Color originalBackground = camera.backgroundColor;
        board.FitCamera();
        Check(camera.backgroundColor == originalBackground && camera.clearFlags == CameraClearFlags.SolidColor,
            "Fitting the preview changed camera background settings");
        Rect previewBounds = preview.GetLocalBounds(board);
        foreach (Renderer renderer in board.transform.Find("Next pieces").GetComponentsInChildren<Renderer>())
        {
            Bounds bounds = renderer.bounds;
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float y in new[] { bounds.min.y, bounds.max.y })
            {
                Vector3 corner = new Vector3(x, y, bounds.center.z);
                Vector3 local = board.transform.InverseTransformPoint(corner);
                Check(local.x >= previewBounds.xMin - 0.001f && local.x <= previewBounds.xMax + 0.001f &&
                    local.y >= previewBounds.yMin - 0.001f && local.y <= previewBounds.yMax + 0.001f,
                    $"Declared preview bounds omit renderer {renderer.name}");
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                Check(viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f && viewport.z > 0f,
                    $"Portrait camera clips preview renderer {renderer.name}");
            }
        }
        float halfWidth = Board.Width * board.CellSize * 0.5f;
        float halfHeight = Board.Height * board.CellSize * 0.5f;
        Rect combined = Rect.MinMaxRect(Mathf.Min(-halfWidth, previewBounds.xMin),
            Mathf.Min(-halfHeight, previewBounds.yMin), Mathf.Max(halfWidth, previewBounds.xMax),
            Mathf.Max(halfHeight, previewBounds.yMax));
        foreach (float x in new[] { combined.xMin, combined.xMax })
        foreach (float y in new[] { combined.yMin, combined.yMax })
        {
            Vector3 viewport = camera.WorldToViewportPoint(board.transform.TransformPoint(new Vector3(x, y, 0f)));
            Check(viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f && viewport.z > 0f,
                "Portrait camera does not contain the whole board and next preview");
        }
    }

    private IEnumerator Start()
    {
        yield return null; // Board initialization and initial camera fitting finish first.
        MakeFixtures();
        UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(73421);
            MethodInfo spawn = typeof(FallingPiece).GetMethod("Spawn", Private);
            var generated = new List<int>();
            int[] previouslyShown = null;
            for (int index = 0; index < 49; index++)
            {
                // Keep an empty stack; every invocation exercises the real queue consumption path.
                spawn.Invoke(player, null);
                int type = (int)Get("shape");
                generated.Add(type);
                Check(!player.IsGameOver, "Spawning on an empty board caused game over");
                if (previouslyShown != null)
                    Check(type == previouslyShown[0], $"Spawn {index} did not use the first shown piece");
                int[] upcoming = bag.ToArray();
                CheckPreview(upcoming);
                if (previouslyShown != null)
                    for (int slot = 0; slot < 5; slot++)
                        Check(upcoming[slot] == previouslyShown[slot + 1],
                            $"Spawn {index} changed a previously advertised future piece");
                UnityEngine.Random.State stateBeforeRedraw = UnityEngine.Random.state;
                preview.Show(board, upcoming, shapes, tiles, spriteTurns);
                Check(stateBeforeRedraw.Equals(UnityEngine.Random.state), "Drawing preview consumed random values");
                int[] afterRedraw = bag.ToArray();
                Check(afterRedraw.Length == upcoming.Length, "Drawing preview changed queue length");
                for (int slot = 0; slot < upcoming.Length; slot++)
                    Check(afterRedraw[slot] == upcoming[slot], "Drawing preview reordered future pieces");
                CheckPreview(upcoming); // Repeated refresh must reuse the 24 visible blocks.
                previouslyShown = upcoming;
            }
            for (int start = 0; start < generated.Count; start += 7)
            {
                var group = new HashSet<int>();
                for (int i = start; i < start + 7; i++) group.Add(generated[i]);
                Check(group.Count == 7, $"Seven-bag group {start / 7} repeats or omits a shape");
            }
            CheckCamera();
        }
        finally
        {
            UnityEngine.Random.state = previousRandom;
        }
        foreach (Sprite[] piece in tiles)
            foreach (Sprite sprite in piece) Destroy(sprite);
        Debug.Log("NEXT_PREVIEW_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }
}
#endif
