#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;

public sealed class DeathSmoke : MonoBehaviour
{
    private Board board;
    private DeathScreen screen;
    private Sprite emptySprite;
    private Color emptyColor;
    private Vector3 emptyScale;
    private Sprite blockSprite;
    private int sweepStartFrame;
    private bool observeGlowStart;
    private bool sawNextFrameGlow;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var camera = new GameObject("Camera", typeof(Camera));
        camera.tag = "MainCamera";
        new GameObject("Board", typeof(Board), typeof(DeathScreen), typeof(DeathSmoke));
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
            "Assets/Smoke.unity");
        EditorApplication.EnterPlaymode();
    }

    private void Assert(bool value, string message)
    {
        if (value) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new Exception(message);
    }

    private SpriteRenderer Cell(int x, int y)
    {
        return board.transform.Find($"Cell {x}, {y}").GetComponent<SpriteRenderer>();
    }

    private void FillBoard()
    {
        var row = new Vector2Int[Board.Width];
        var art = new Sprite[Board.Width];
        for (int x = 0; x < Board.Width; x++)
        {
            row[x] = new Vector2Int(x, 0);
            art[x] = blockSprite;
        }
        for (int y = 0; y < Board.Height; y++)
            Assert(board.TryLockPiece(row, new Vector2Int(0, y), Color.white, art, 1),
                $"Could not populate row {y}");
    }

    private void AssertRowCleared(int y)
    {
        for (int x = 0; x < Board.Width; x++)
        {
            Assert(board.IsCellFree(new Vector2Int(x, y)), $"Block {x}, {y} was not deleted");
            SpriteRenderer cell = Cell(x, y);
            Assert(cell.enabled && cell.gameObject.activeInHierarchy, $"Grid cell {x}, {y} is hidden");
            Assert(cell.color == emptyColor && cell.color.a > 0f, $"Grid cell {x}, {y} lost its color");
            Assert(cell.sprite == emptySprite, $"Grid cell {x}, {y} retained block art");
            Assert(Quaternion.Angle(cell.transform.localRotation, Quaternion.identity) < 0.01f,
                $"Grid cell {x}, {y} retained block rotation");
            Assert(Vector3.Distance(cell.transform.localScale, emptyScale) < 0.0001f,
                $"Grid cell {x}, {y} retained block scale");
        }
    }

    private void AssertNoOverlay()
    {
        Assert(UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length == 0, "Death effect created a Canvas");
        Assert(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length == 0, "Death effect created an EventSystem");
    }

    private void LateUpdate()
    {
        if (!observeGlowStart) return;
        SpriteRenderer glow = board.transform.Find("Death row glow/Row 0 flash")
            .GetComponent<SpriteRenderer>();
        AssertRowCleared(0);
        if (Time.frameCount == sweepStartFrame)
            Assert(!glow.enabled, "Death glow started on the block deletion frame");
        else
        {
            Assert(Time.frameCount == sweepStartFrame + 1 && glow.enabled,
                "Death glow did not start one frame after deleting the blocks");
            sawNextFrameGlow = true;
            observeGlowStart = false;
        }
    }

    private IEnumerator Start()
    {
        board = GetComponent<Board>();
        screen = GetComponent<DeathScreen>();
        SpriteRenderer empty = Cell(0, 0);
        emptySprite = empty.sprite;
        emptyColor = empty.color;
        emptyScale = empty.transform.localScale;
        blockSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f), 1f);
        FillBoard();

        screen.Show(board);
        sweepStartFrame = Time.frameCount;
        observeGlowStart = true;
        AssertRowCleared(0);
        Assert(!board.transform.Find("Death row glow/Row 0 flash")
            .GetComponent<SpriteRenderer>().enabled, "Death glow started before the empty-grid frame");
        Assert(!screen.CanRetry, "Retry allowed during sweep");
        AssertNoOverlay();
        yield return new WaitForSecondsRealtime(0.4f);
        Assert(sawNextFrameGlow, "First-frame death glow timing was not checked");
        AssertRowCleared(0);
        Assert(!board.IsCellFree(new Vector2Int(0, Board.Height - 1)), "Top row cleared too soon");
        Assert(Cell(0, Board.Height - 1).sprite == blockSprite, "Top row art changed before the sweep");

        float deadline = Time.realtimeSinceStartup + 5f;
        while (!screen.CanRetry && Time.realtimeSinceStartup < deadline) yield return null;
        Assert(screen.CanRetry, "Sweep never completed");
        for (int y = 0; y < Board.Height; y++) AssertRowCleared(y);
        Assert(!board.transform.Find("Death row glow").gameObject.activeSelf, "Glow remained active");
        AssertNoOverlay();

        screen.Hide();
        Assert(!screen.CanRetry, "Hide left retry enabled");
        FillBoard();
        screen.Show(board);
        yield return new WaitForSecondsRealtime(0.4f);
        AssertRowCleared(0);
        Assert(!board.IsCellFree(new Vector2Int(0, Board.Height - 1)), "Second sweep cleared top too soon");
        screen.Hide();
        var freeAtCancellation = new bool[Board.Width, Board.Height];
        for (int y = 0; y < Board.Height; y++)
            for (int x = 0; x < Board.Width; x++)
                freeAtCancellation[x, y] = board.IsCellFree(new Vector2Int(x, y));

        yield return new WaitForSecondsRealtime(1.6f);
        Assert(!screen.CanRetry, "Cancelled sweep completed later");
        Assert(!board.transform.Find("Death row glow").gameObject.activeSelf, "Hide left glow active");
        for (int y = 0; y < Board.Height; y++)
            for (int x = 0; x < Board.Width; x++)
                Assert(board.IsCellFree(new Vector2Int(x, y)) == freeAtCancellation[x, y],
                    $"Cancelled sweep changed cell {x}, {y}");
        AssertRowCleared(0);
        Assert(Cell(0, Board.Height - 1).enabled && Cell(0, Board.Height - 1).sprite == blockSprite,
            "Hide erased untouched blocks");
        AssertNoOverlay();
        Destroy(blockSprite);
        Debug.Log("DEATH_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }
}
#endif
