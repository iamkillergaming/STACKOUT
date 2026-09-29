#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run only in an isolated project: this creates and saves a temporary scene.
public sealed class HoldSmoke : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Board board;
    private FallingPiece player;
    private DeathScreen death;
    private Sprite[][] tiles;
    private Queue<int> bag;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Camera", typeof(Camera)).tag = "MainCamera";
        new GameObject("Board", typeof(Board), typeof(HoldSmoke));
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/HoldSmoke.unity");
        EditorApplication.EnterPlaymode();
    }

    private void Check(bool condition, string message)
    {
        if (condition) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new Exception(message);
    }

    private object Get(string field) => typeof(FallingPiece).GetField(field, Private).GetValue(player);
    private void Set(string field, object value) => typeof(FallingPiece).GetField(field, Private).SetValue(player, value);
    private object Call(string method, params object[] args) => typeof(FallingPiece).GetMethod(method, Private).Invoke(player, args);
    private void Property(string name, object value) => typeof(FallingPiece).GetProperty(name).GetSetMethod(true).Invoke(player, new[] { value });

    private void MakeFixtures()
    {
        board = GetComponent<Board>();
        death = gameObject.AddComponent<DeathScreen>();
        player = gameObject.AddComponent<FallingPiece>();
        player.enabled = false; // Exercise the real gameplay methods without imported sprites or keyboard input.
        Set("board", board);
        Set("deathScreen", death);
        Set("lineClearEffect", gameObject.AddComponent<LineClearEffect>());
        tiles = (Sprite[][])Get("tiles");
        var spriteTurns = (int[])Get("spriteTurns");
        for (int type = 0; type < tiles.Length; type++)
        {
            tiles[type] = new Sprite[4];
            spriteTurns[type] = type % 4;
            for (int cell = 0; cell < 4; cell++)
                tiles[type][cell] = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        foreach (string field in new[] { "renderers", "ghostRenderers" })
        {
            var renderers = (SpriteRenderer[])Get(field);
            for (int i = 0; i < renderers.Length; i++)
            {
                var block = new GameObject($"Fixture {field} {i}", typeof(SpriteRenderer));
                block.transform.SetParent(board.transform, false);
                renderers[i] = block.GetComponent<SpriteRenderer>();
            }
        }
        bag = (Queue<int>)Get("bag");
        for (int i = 0; i < 21; i++) bag.Enqueue(i % 7);
        Call("Spawn");
    }

    private void CheckQueue(int[] expected)
    {
        int[] actual = bag.ToArray();
        Check(actual.Length == expected.Length, "Hold unexpectedly consumed or appended queued pieces");
        for (int i = 0; i < actual.Length; i++) Check(actual[i] == expected[i], "Hold reordered queued pieces");
        Transform next = board.transform.Find("Next pieces");
        for (int slot = 0; slot < NextPiecePreview.Count; slot++)
            for (int cell = 0; cell < 4; cell++)
                Check(next.Find($"Next {slot}/Block {cell}").GetComponent<SpriteRenderer>().sprite == tiles[actual[slot]][cell],
                    "Hold changed the next preview without updating its queue");
    }

    private float CheckHoldArt(int expected)
    {
        Transform root = board.transform.Find("Hold piece");
        Check(root != null && root.gameObject.activeInHierarchy, "Hold section is missing");
        Check(root.Find("Hold label") != null, "Hold heading is missing");
        Check(root.GetComponentsInChildren<SpriteRenderer>(true).Length == 4, "Hold must contain exactly one four-block slot");
        float brightness = 0f;
        for (int cell = 0; cell < 4; cell++)
        {
            SpriteRenderer renderer = root.Find($"Block {cell}").GetComponent<SpriteRenderer>();
            if (expected < 0)
            {
                Check(!renderer.enabled || !renderer.gameObject.activeInHierarchy, "Empty hold slot still displays a piece");
                continue;
            }
            Check(renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sprite == tiles[expected][cell],
                "Hold displays incorrect artwork");
            Check(Quaternion.Angle(renderer.transform.localRotation, Quaternion.Euler(0f, 0f, -90f * (expected % 4))) < 0.01f,
                "Hold artwork lost its imported orientation");
            Check(board.transform.InverseTransformPoint(renderer.bounds.max).x < -Board.Width * board.CellSize * 0.5f,
                "Hold artwork overlaps the board instead of sitting on its left");
            brightness = renderer.color.grayscale;
        }
        return brightness;
    }

    private void CheckSpawnReset(int expected)
    {
        Check((int)Get("shape") == expected && (int)Get("turns") == 0, "Held piece did not return in spawn orientation");
        Check((Vector2Int)Get("origin") == new Vector2Int(expected == 1 ? 4 : 3,
            expected == 0 ? Board.Height - 1 : Board.Height - 2), "Held piece did not return at its spawn position");
        foreach (string field in new[] { "fallTimer", "repeatTimer", "horizontalHoldTime", "softDropHoldTime" })
            Check((float)Get(field) == 0f, $"Hold carried over {field}");
        Check((int)Get("heldDirection") == 0 && !(bool)Get("wasSoftDropping"), "Hold carried over held movement state");
        var timer = (PieceLockDelay)Get("lockTimer");
        Check((float)typeof(PieceLockDelay).GetField("elapsed", Private).GetValue(timer) == 0f &&
            (int)typeof(PieceLockDelay).GetField("resets", Private).GetValue(timer) == 0, "Hold carried over lock delay");
    }

    private void CheckCamera()
    {
        Camera camera = Camera.main;
        camera.aspect = 9f / 16f;
        camera.backgroundColor = new Color(0.13f, 0.29f, 0.43f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        Color background = camera.backgroundColor;
        board.FitCamera();
        Check(camera.backgroundColor == background && camera.clearFlags == CameraClearFlags.SolidColor, "Hold changed the background");
        Rect holdBounds = GetComponent<HoldPiecePreview>().GetLocalBounds(board);
        foreach (Renderer renderer in board.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Bounds bounds = renderer.bounds;
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float y in new[] { bounds.min.y, bounds.max.y })
            {
                Vector3 corner = new Vector3(x, y, bounds.center.z);
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                Check(viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f && viewport.z > 0f,
                    $"Portrait camera clips {renderer.name}");
                if (!renderer.transform.IsChildOf(board.transform.Find("Hold piece"))) continue;
                Vector3 local = board.transform.InverseTransformPoint(corner);
                Check(local.x >= holdBounds.xMin - 0.001f && local.x <= holdBounds.xMax + 0.001f &&
                    local.y >= holdBounds.yMin - 0.001f && local.y <= holdBounds.yMax + 0.001f,
                    $"Declared hold bounds omit {renderer.name}");
            }
        }
    }

    private IEnumerator Start()
    {
        yield return null;
        MakeFixtures();
        Check(player.HeldShape == -1 && player.CanHold, "A new game must start with an empty available slot");
        CheckHoldArt(-1);
        int[] upcoming = bag.ToArray();
        Check(player.TryHold() && player.HeldShape == 0 && !player.CanHold, "First hold did not save the active piece");
        CheckSpawnReset(upcoming[0]);
        CheckQueue(new List<int>(upcoming).GetRange(1, upcoming.Length - 1).ToArray());
        float unavailableBrightness = CheckHoldArt(0);
        int[] afterHold = bag.ToArray();
        Check(!player.TryHold() && (int)Get("shape") == 1 && player.HeldShape == 0, "Repeated hold changed the piece");
        CheckQueue(afterHold);
        Set("origin", Call("GetLandingOrigin"));
        Call("Lock", true);
        Check(player.CanHold && (int)Get("shape") == 2, "Locking did not enable hold for the next piece");
        Check(CheckHoldArt(0) > unavailableBrightness, "Hold artwork did not indicate availability");
        Property("IsClearingLines", true);
        upcoming = bag.ToArray();
        Check(!player.TryHold(), "Hold accepted input during a line clear");
        CheckQueue(upcoming);
        Property("IsClearingLines", false);
        Call("Rotate", true);
        Check((int)Get("turns") != 0, "Rotation fixture did not rotate");
        Set("fallTimer", 0.6f);
        Set("horizontalHoldTime", 0.5f);
        ((PieceLockDelay)Get("lockTimer")).OnSuccessfulAction(true, 15);
        ((PieceLockDelay)Get("lockTimer")).Tick(true, 0.2f, 0.5f);
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        Check(player.TryHold() && player.HeldShape == 2 && !player.CanHold, "Occupied hold did not swap pieces");
        Check(randomState.Equals(UnityEngine.Random.state), "Swapping hold consumed random values");
        CheckQueue(upcoming);
        CheckSpawnReset(0);
        CheckHoldArt(2);
        CheckCamera();
        Set("origin", Call("GetLandingOrigin"));
        Call("Lock", true);
        Check(player.CanHold, "Second lock did not enable hold");
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(3, Board.Height - 2), Color.white),
            "Could not create spawn-collision fixture");
        upcoming = bag.ToArray();
        player.TryHold();
        Check(player.IsGameOver && !player.CanHold, "Blocked held-piece spawn did not end the game");
        Check(!player.TryHold(), "Game over accepted hold input");
        CheckQueue(upcoming);
        typeof(DeathScreen).GetProperty("CanRetry").GetSetMethod(true).Invoke(death, new object[] { true });
        player.RestartGame();
        Check(!player.IsGameOver && player.HeldShape == -1 && player.CanHold, "Restart did not clear and unlock hold");
        CheckHoldArt(-1);
        foreach (Sprite[] shapeTiles in tiles)
            foreach (Sprite sprite in shapeTiles) Destroy(sprite);
        Debug.Log("HOLD_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }
}
#endif
