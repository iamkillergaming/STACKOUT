#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Copy into an isolated test project's Assets; never run against the working scene.
public sealed class LineClearSmoke : MonoBehaviour
{
    private Board board;
    private LineClearEffect effect;
    private Sprite blockSprite;
    private Sprite emptySprite;
    private Color emptyColor;
    private Vector3 emptyScale;
    private readonly Vector2Int[] singleCell = { Vector2Int.zero };

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Camera", typeof(Camera)).tag = "MainCamera";
        new GameObject("Board", typeof(Board), typeof(LineClearEffect), typeof(LineClearSmoke));
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
            "Assets/LineClearSmoke.unity");
        EditorApplication.EnterPlaymode();
    }

    private void Assert(bool value, string message)
    {
        if (value) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new Exception(message);
    }

    private SpriteRenderer Cell(int x, int y) =>
        board.transform.Find($"Cell {x}, {y}").GetComponent<SpriteRenderer>();

    private void FillRow(int y, int count = Board.Width)
    {
        var cells = new Vector2Int[count];
        var art = new Sprite[count];
        for (int x = 0; x < count; x++)
        {
            cells[x] = new Vector2Int(x, 0);
            art[x] = blockSprite;
        }
        Assert(board.TryLockPiece(cells, new Vector2Int(0, y), Color.cyan, art, 1),
            $"Could not fill row {y}");
    }

    private void Marker(int x, int y, Color color, int turns)
    {
        Assert(board.TryLockPiece(singleCell, new Vector2Int(x, y), color,
            new[] { blockSprite }, turns), $"Could not place marker at {x}, {y}");
    }

    private void AssertMarker(int x, int y, Color color, int turns)
    {
        SpriteRenderer cell = Cell(x, y);
        Assert(!board.IsCellFree(new Vector2Int(x, y)), $"Missing marker at {x}, {y}");
        Assert(cell.sprite == blockSprite && cell.color == color,
            $"Marker art/color changed at {x}, {y}");
        Assert(Quaternion.Angle(cell.transform.localRotation,
            Quaternion.Euler(0, 0, -90f * turns)) < 0.01f,
            $"Marker rotation changed at {x}, {y}");
        Assert(Vector3.Distance(cell.transform.localScale, board.GetPieceScale(blockSprite)) < 0.0001f,
            $"Marker scale changed at {x}, {y}");
    }

    private void AssertEmpty(int x, int y)
    {
        Assert(board.IsCellFree(new Vector2Int(x, y)), $"Expected empty cell {x}, {y}");
        AssertEmptyAppearance(x, y);
    }

    private void AssertEmptyAppearance(int x, int y)
    {
        SpriteRenderer cell = Cell(x, y);
        Assert(cell.enabled && cell.gameObject.activeInHierarchy, $"Grid hidden at {x}, {y}");
        Assert(cell.sprite == emptySprite && cell.color == emptyColor,
            $"Empty grid appearance changed at {x}, {y}");
        Assert(Quaternion.Angle(cell.transform.localRotation, Quaternion.identity) < 0.01f &&
            Vector3.Distance(cell.transform.localScale, emptyScale) < 0.0001f,
            $"Empty grid transform changed at {x}, {y}");
    }

    private void CheckDetectionAndCompaction()
    {
        for (int count = 1; count <= 4; count++)
        {
            board.ClearBoard();
            for (int y = 0; y < count; y++) FillRow(y);
            Marker(3, count + 2, Color.magenta, 3);
            int[] rows = board.GetFullRows();
            Assert(rows.Length == count, $"Expected {count} detected full rows");
            for (int y = 0; y < count; y++)
            {
                Assert(rows[y] == y, "Full row coordinates are incorrect");
                for (int x = 0; x < Board.Width; x++) AssertMarker(x, y, Color.cyan, 1);
            }
            AssertMarker(3, count + 2, Color.magenta, 3);
            Assert(board.ClearFullLines() == count, $"Expected {count} cleared lines");
            AssertMarker(3, 2, Color.magenta, 3);
            Assert(board.GetFullRows().Length == 0, "Cleared rows remain full");
            for (int y = 0; y < Board.Height; y++)
                for (int x = 0; x < Board.Width; x++)
                    if (x != 3 || y != 2) AssertEmpty(x, y);
        }

        board.ClearBoard();
        FillRow(0, Board.Width - 1);
        Assert(board.GetFullRows().Length == 0, "Incomplete row reported as full");
        Assert(board.ClearFullLines() == 0, "Incomplete row cleared");
        for (int x = 0; x < Board.Width - 1; x++) AssertMarker(x, 0, Color.cyan, 1);
        AssertEmpty(Board.Width - 1, 0);

        board.ClearBoard();
        FillRow(1);
        FillRow(3);
        Marker(2, 0, Color.red, 1);
        Marker(4, 2, Color.green, 2);
        Marker(6, 4, Color.blue, 3);
        int[] separated = board.GetFullRows();
        Assert(separated.Length == 2 && separated[0] == 1 && separated[1] == 3,
            "Nonadjacent rows were not detected correctly");
        Assert(board.ClearFullLines() == 2, "Nonadjacent rows did not clear together");
        AssertMarker(2, 0, Color.red, 1);
        AssertMarker(4, 1, Color.green, 2);
        AssertMarker(6, 2, Color.blue, 3);
        AssertEmpty(6, 4);
    }

    private void CheckSoundMapping()
    {
        var holder = new GameObject("Sound mapping fixture");
        holder.SetActive(false); // Do not run Start or require assigned shape sprites/audio hardware.
        FallingPiece controller = holder.AddComponent<FallingPiece>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo choose = typeof(FallingPiece).GetMethod("GetLineClearSound", flags);
        Assert(choose != null, "Line clear sound selection method is missing");
        string[] names = { "singleClearSound", "doubleClearSound", "tripleClearSound", "tetrisClearSound" };
        var clips = new AudioClip[4];
        for (int i = 0; i < names.Length; i++)
        {
            clips[i] = AudioClip.Create(names[i], 32, 1, 44100, false);
            typeof(FallingPiece).GetField(names[i], flags).SetValue(controller, clips[i]);
        }
        for (int count = 1; count <= 4; count++)
            Assert(ReferenceEquals(choose.Invoke(controller, new object[] { count }), clips[count - 1]),
                $"Wrong sound selected for {count} lines");
        Assert(choose.Invoke(controller, new object[] { 0 }) == null, "No-clear selected a sound");
        Assert(choose.Invoke(controller, new object[] { 5 }) == null, "Unsupported count selected a sound");
        typeof(FallingPiece).GetField(names[1], flags).SetValue(controller, null);
        Assert(choose.Invoke(controller, new object[] { 2 }) == null, "Missing clip did not remain silent");
        Destroy(holder);
        foreach (AudioClip clip in clips) Destroy(clip);
    }

    private void AssertGlowRows(bool visible)
    {
        Transform root = board.transform.Find("Line clear glow");
        if (root == null && !visible) return; // Built only after the pre-glow frame.
        Assert(root != null, "Line clear glow hierarchy is missing");
        Assert(root.gameObject.activeSelf == visible, "Unexpected glow root visibility");
        for (int row = 0; row < Board.Height; row++)
        {
            bool expected = visible && (row == 1 || row == 3);
            foreach (string layer in new[] { "halo", "core" })
            {
                SpriteRenderer glow = root.Find($"Row {row} {layer}").GetComponent<SpriteRenderer>();
                Assert(glow.enabled == expected, $"Unexpected glow visibility at row {row} ({layer})");
            }
        }
    }

    private void AssertEffectDidNotClear(bool blocksHidden = false)
    {
        Assert(board.GetFullRows().Length == 2, "Glow changed full-row detection");
        for (int x = 0; x < Board.Width; x++)
        {
            foreach (int y in new[] { 1, 3 })
            {
                if (blocksHidden)
                {
                    Assert(!board.IsCellFree(new Vector2Int(x, y)),
                        $"Glow removed board data early at {x}, {y}");
                    AssertEmptyAppearance(x, y);
                }
                else AssertMarker(x, y, Color.cyan, 1);
            }
        }
        AssertMarker(5, 5, Color.magenta, 3);
        AssertEmpty(0, 2);
    }

    private IEnumerator CheckControllerClear()
    {
        board.ClearBoard();
        FillRow(0, Board.Width - 1);
        FillRow(1, Board.Width - 1);
        Marker(4, 6, Color.magenta, 3);
        FallingPiece controller = gameObject.AddComponent<FallingPiece>();
        controller.enabled = false; // Inject runtime fixtures without sprite import work in Start.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Action<string, object> set = (name, value) =>
            typeof(FallingPiece).GetField(name, flags).SetValue(controller, value);
        Func<string, object> get = name => typeof(FallingPiece).GetField(name, flags).GetValue(controller);
        set("board", board);
        set("lineClearEffect", effect);
        set("deathScreen", gameObject.AddComponent<DeathScreen>());
        set("lineClearDuration", 0.2f);
        set("cells", new[] { new Vector2Int(0, 0), new Vector2Int(0, 1),
            new Vector2Int(0, 2), new Vector2Int(0, 3) });
        set("origin", new Vector2Int(Board.Width - 1, 0));
        set("shape", 0);
        Sprite[][] tiles = (Sprite[][])get("tiles");
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = new[] { blockSprite, blockSprite, blockSprite, blockSprite };
        SpriteRenderer[] active = (SpriteRenderer[])get("renderers");
        SpriteRenderer[] ghost = (SpriteRenderer[])get("ghostRenderers");
        for (int i = 0; i < active.Length; i++)
        {
            active[i] = new GameObject($"Fixture active {i}", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            ghost[i] = new GameObject($"Fixture ghost {i}", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        }
        Queue<int> bag = (Queue<int>)get("bag");
        bag.Enqueue(1);
        typeof(FallingPiece).GetMethod("Lock", flags).Invoke(controller, new object[] { true });
        Assert(controller.IsClearingLines && controller.LinesCleared == 0,
            "Lock did not defer line removal until after the flash");
        Assert(board.GetFullRows().Length == 2 && bag.Count == 1,
            "Rows collapsed or next piece spawned before the flash completed");
        for (int i = 0; i < active.Length; i++)
            Assert(!active[i].enabled && !ghost[i].enabled, "Piece art remains visible during clear");
        AssertMarker(4, 6, Color.magenta, 3);
        float deadline = Time.realtimeSinceStartup + 5f;
        while (controller.IsClearingLines && Time.realtimeSinceStartup < deadline) yield return null;
        Assert(!controller.IsClearingLines && controller.LinesCleared == 2,
            "Clear did not finish and award exactly two lines");
        Assert(board.GetFullRows().Length == 0 && bag.Count >= NextPiecePreview.Count && (int)get("shape") == 1,
            "Clear did not remove rows and spawn the next piece");
        AssertMarker(4, 4, Color.magenta, 3);
        Assert(active[0].enabled, "Next piece was not drawn after clearing");
        AssertGlowRows(false);
        Destroy(controller);
        yield return null;
    }

    private IEnumerator Start()
    {
        board = GetComponent<Board>();
        effect = GetComponent<LineClearEffect>();
        emptySprite = Cell(0, 0).sprite;
        emptyColor = Cell(0, 0).color;
        emptyScale = Cell(0, 0).transform.localScale;
        blockSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f), 1f);
        CheckDetectionAndCompaction();
        CheckSoundMapping();

        board.ClearBoard();
        FillRow(1);
        FillRow(3);
        Marker(5, 5, Color.magenta, 3);
        IEnumerator animation = effect.Play(board, board.GetFullRows(), 0.15f, Color.cyan);
        Assert(animation.MoveNext(), "Pre-glow frame did not start");
        AssertGlowRows(false);
        AssertEffectDidNotClear(true);
        yield return null;
        Assert(animation.MoveNext(), "Glow did not start after the empty-grid frame");
        AssertGlowRows(true);
        AssertEffectDidNotClear(true);
        yield return animation;
        AssertGlowRows(false);
        AssertEffectDidNotClear(true);

        animation = effect.Play(board, board.GetFullRows(), 1f, Color.cyan);
        Assert(animation.MoveNext(), "Second pre-glow frame did not start");
        AssertGlowRows(false);
        AssertEffectDidNotClear(true);
        effect.Hide();
        AssertGlowRows(false);
        Assert(!animation.MoveNext(), "Cancelled glow kept running");
        AssertEffectDidNotClear();
        Assert(board.ClearFullLines() == 2, "Rows could not clear after glow");
        AssertMarker(5, 3, Color.magenta, 3);
        AssertEmpty(0, 1);
        AssertGlowRows(false);

        yield return CheckControllerClear();

        Destroy(blockSprite);
        Debug.Log("LINE_CLEAR_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }
}
#endif
