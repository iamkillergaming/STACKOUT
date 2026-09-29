#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copy into an isolated project's Assets and run StaticBoardSmoke.Run.
public sealed class StaticBoardSmoke : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SerializeField] private Board board;
    [SerializeField] private FallingPiece player;
    [SerializeField] private BoardPresentation presentation;
    [SerializeField] private SpriteRenderer authoredFrame;
    [SerializeField] private Sprite authoredFrameSprite;
    [SerializeField] private Sprite emptySprite;
    [SerializeField] private SpriteRenderer[] authoredCells;
    [SerializeField] private SpriteRenderer[] authoredNext;
    [SerializeField] private SpriteRenderer[] authoredHold;
    [SerializeField] private TMP_Text[] authoredLabels;
    [SerializeField] private TMP_Text place;
    [SerializeField] private TMP_Text knockouts;
    [SerializeField] private TMP_Text badges;
    [SerializeField] private Color cameraBackground;
    [SerializeField] private CameraClearFlags cameraClearFlags;

    private Sprite[][] tiles;
    private Queue<int> bag;

    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            BattleArena arena = FindFirstObjectByType<BattleArena>();
            if (arena != null) arena.gameObject.SetActive(false); // Verify the standalone authored board here.
            Board source = FindFirstObjectByType<FallingPiece>().GetComponent<Board>();
            Check(source != null, "The authored sample scene has no Board");
            StaticBoardSmoke smoke = source.gameObject.AddComponent<StaticBoardSmoke>();
            smoke.CaptureAuthoredScene(source);
            // Save a separate fixture; the working SampleScene and prefab stay unchanged.
            EditorSceneManager.SaveScene(source.gameObject.scene, "Assets/StaticBoardSmoke.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static object Read(object target, string field) =>
        target.GetType().GetField(field, Private).GetValue(target);

    private object Get(string field) => Read(player, field);
    private void Set(string field, object value) =>
        typeof(FallingPiece).GetField(field, Private).SetValue(player, value);
    private object Call(string method, params object[] args) =>
        typeof(FallingPiece).GetMethod(method, Private).Invoke(player, args);

    private static T ReadReference<T>(UnityEngine.Object target, string field) where T : UnityEngine.Object
    {
        SerializedProperty property = new SerializedObject(target).FindProperty(field);
        Check(property != null && property.propertyType == SerializedPropertyType.ObjectReference,
            $"Missing serialized scene reference {target.GetType().Name}.{field}");
        T result = property.objectReferenceValue as T;
        Check(result != null, $"Unassigned or incorrect scene reference {target.GetType().Name}.{field}");
        return result;
    }

    private static SpriteRenderer[] ReadBlocks(UnityEngine.Object target, string field, int expected)
    {
        SerializedProperty property = new SerializedObject(target).FindProperty(field);
        Check(property != null && property.isArray && property.arraySize == expected,
            $"Expected {expected} serialized renderers on {target.GetType().Name}.{field}");
        var blocks = new SpriteRenderer[expected];
        var unique = new HashSet<SpriteRenderer>();
        for (int i = 0; i < expected; i++)
        {
            blocks[i] = property.GetArrayElementAtIndex(i).objectReferenceValue as SpriteRenderer;
            Check(blocks[i] != null && unique.Add(blocks[i]),
                $"Missing or duplicate renderer {i} on {target.GetType().Name}.{field}");
        }
        return blocks;
    }

    private void CaptureAuthoredScene(Board source)
    {
        board = source;
        player = source.GetComponent<FallingPiece>();
        presentation = source.GetComponent<BoardPresentation>();
        NextPiecePreview next = source.GetComponent<NextPiecePreview>();
        HoldPiecePreview hold = source.GetComponent<HoldPiecePreview>();
        Check(player != null && presentation != null && next != null && hold != null,
            "The authored board is missing gameplay, presentation, or preview components");
        authoredFrame = ReadReference<SpriteRenderer>(presentation, "boardSprite");
        authoredFrameSprite = authoredFrame.sprite;
        Check(authoredFrameSprite != null && AssetDatabase.GetAssetPath(authoredFrameSprite).Length > 0,
            "The board frame must already reference an imported sprite in Edit Mode");
        Check(authoredFrame.enabled && authoredFrame.gameObject.activeInHierarchy,
            "The board sprite is not visible in Edit Mode");
        emptySprite = ReadReference<Sprite>(board, "emptyCellSprite");
        authoredCells = ReadBlocks(board, "boardCells", Board.Width * Board.Height);
        authoredNext = ReadBlocks(next, "previewBlocks", NextPiecePreview.Count * 4);
        authoredHold = ReadBlocks(hold, "previewBlocks", 4);
        ReadReference<Transform>(next, "root");
        ReadReference<Transform>(hold, "root");
        place = ReadReference<TMP_Text>(presentation, "placeText");
        knockouts = ReadReference<TMP_Text>(presentation, "knockoutsText");
        badges = ReadReference<TMP_Text>(presentation, "badgesText");
        authoredLabels = board.GetComponentsInChildren<TMP_Text>(true);
        Check(authoredLabels.Length >= 6, "Hold, Next, and match labels must already be editable TMP objects");
        foreach (TMP_Text label in authoredLabels)
            Check(label.font != null, $"TMP label {label.name} has no assigned font asset");
        CheckNoTargeting();
        Check(Camera.main != null, "The authored scene needs its main camera");
        cameraBackground = Camera.main.backgroundColor;
        cameraClearFlags = Camera.main.clearFlags;
        place.text = "TEST";
        EditorUtility.SetDirty(place);
    }

    private void CheckNoTargeting()
    {
        Check(board.GetComponentsInChildren<TextMesh>(true).Length == 0,
            "The board still contains legacy TextMesh labels");
        foreach (Transform child in board.GetComponentsInChildren<Transform>(true))
        {
            string name = child.name.ToLowerInvariant();
            bool incomingConnection = name == "targeting warning" || name == "targeting lines"
                || name.StartsWith("targeting line ");
            Check((!name.Contains("targeting") || incomingConnection) && !name.Contains("attackers") &&
                !name.Contains("randoms") && !name.Contains("board cabinet mesh"),
                $"Removed targeting/generated cabinet object remains: {child.name}");
        }
    }

    private static void CheckSameReferences<T>(T[] expected, T[] actual, string description)
        where T : UnityEngine.Object
    {
        Check(actual != null && actual.Length == expected.Length, $"{description} count changed at runtime");
        for (int i = 0; i < expected.Length; i++)
            Check(expected[i] != null && expected[i] == actual[i], $"{description} {i} was replaced at runtime");
    }

    private void CheckAuthoredObjects()
    {
        Check(authoredFrame != null && authoredFrame.sprite == authoredFrameSprite &&
            authoredFrame.enabled && authoredFrame.gameObject.activeInHierarchy,
            "The authored board sprite was replaced or hidden");
        CheckSameReferences(authoredCells, (SpriteRenderer[])Read(board, "boardCells"), "Board cell");
        CheckSameReferences(authoredNext,
            (SpriteRenderer[])Read(board.GetComponent<NextPiecePreview>(), "previewBlocks"), "Next block");
        CheckSameReferences(authoredHold,
            (SpriteRenderer[])Read(board.GetComponent<HoldPiecePreview>(), "previewBlocks"), "Hold block");
        CheckSameReferences(authoredLabels, board.GetComponentsInChildren<TMP_Text>(true), "TMP label");
        Check(Camera.main.backgroundColor == cameraBackground && Camera.main.clearFlags == cameraClearFlags,
            "Board startup or effects changed the scene background");
        CheckNoTargeting();
    }

    private void CheckQueue(int[] expected)
    {
        int[] actual = bag.ToArray();
        Check(actual.Length == expected.Length, "An unavailable hold or pending clear changed the queue length");
        for (int i = 0; i < expected.Length; i++)
            Check(actual[i] == expected[i], "An unavailable hold or pending clear changed the queue order");
    }

    private void CheckPreviewArt()
    {
        int[] upcoming = bag.ToArray();
        Check(upcoming.Length >= NextPiecePreview.Count, "The next queue contains fewer than six pieces");
        for (int slot = 0; slot < NextPiecePreview.Count; slot++)
        {
            for (int cell = 0; cell < 4; cell++)
            {
                SpriteRenderer renderer = authoredNext[slot * 4 + cell];
                Check(renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    renderer.sprite == tiles[upcoming[slot]][cell], "An authored Next slot shows incorrect art");
            }
        }
        for (int cell = 0; cell < authoredHold.Length; cell++)
        {
            SpriteRenderer renderer = authoredHold[cell];
            if (player.HeldShape < 0)
                Check(!renderer.enabled, "The empty Hold slot displays a block");
            else
                Check(renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    renderer.sprite == tiles[player.HeldShape][cell], "The authored Hold slot shows incorrect art");
        }
    }

    private void CheckCameraFraming()
    {
        Camera camera = Camera.main;
        float previousAspect = camera.aspect;
        foreach (float aspect in new[] { 9f / 16f, 16f / 9f })
        {
            camera.aspect = aspect;
            board.FitCamera();
            Bounds bounds = authoredFrame.bounds;
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float y in new[] { bounds.min.y, bounds.max.y })
            {
                Vector3 viewport = camera.WorldToViewportPoint(new Vector3(x, y, bounds.center.z));
                Check(viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f && viewport.z > 0f,
                    $"The authored board sprite clips at camera aspect {aspect}");
            }
        }
        camera.aspect = previousAspect;
        board.FitCamera();
    }

    private IEnumerator Start()
    {
        IEnumerator checks = CheckFlow();
        while (true)
        {
            object yielded;
            try
            {
                if (!checks.MoveNext()) break;
                yielded = checks.Current;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                yield break;
            }
            yield return yielded;
        }
        Debug.Log("STATIC_BOARD_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }

    private IEnumerator CheckFlow()
    {
        yield return null;
        yield return null;
        Check(player.enabled && !player.IsGameOver && player.CanHold,
            "The authored board did not start a playable game with its assigned sprites");
        player.enabled = false; // Drive real gameplay methods deterministically without keyboard/gravity.
        tiles = (Sprite[][])Get("tiles");
        bag = (Queue<int>)Get("bag");
        for (int type = 0; type < tiles.Length; type++)
            Check(tiles[type] != null && tiles[type].Length == 4 && tiles[type][0] != null,
                $"The assigned shape {type} did not create usable cell art");
        CheckAuthoredObjects();
        Check(place.text == "TEST", "Board startup overwrote the designer's authored TMP text");
        presentation.SetMatchStats(98, 99, 3, 2);
        Check(place.text == "98/99" && knockouts.text == "03" && badges.text == "002",
            "SetMatchStats did not update the serialized TMP values");
        CheckPreviewArt();
        CheckCameraFraming();

        int startingShape = (int)Get("shape");
        int nextShape = bag.Peek();
        Check(player.TryHold() && player.HeldShape == startingShape && !player.CanHold &&
            (int)Get("shape") == nextShape, "First Hold did not save the active piece and consume the next one");
        CheckPreviewArt();
        int[] afterFirstHold = bag.ToArray();
        Check(!player.TryHold(), "Repeated Hold was accepted before locking");
        CheckQueue(afterFirstHold);
        Set("origin", Call("GetLandingOrigin"));
        Call("Lock", true);
        Check(player.CanHold && !player.IsGameOver, "A hard-drop lock did not unlock Hold for the next piece");
        int outgoing = (int)Get("shape");
        int[] beforeSwap = bag.ToArray();
        Check(player.TryHold() && (int)Get("shape") == startingShape && player.HeldShape == outgoing,
            "An occupied Hold slot did not swap the saved piece");
        CheckQueue(beforeSwap);
        CheckPreviewArt();

        board.ClearBoard();
        Sprite marker = tiles[0][0];
        for (int x = 4; x < Board.Width; x++)
            Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(x, 0), Color.white,
                new[] { marker }), "Could not create the partial-row fixture");
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(5, 3), Color.magenta,
            new[] { marker }), "Could not create the upper-row marker");
        Call("ActivatePiece", 0);
        Set("origin", Vector2Int.zero);
        Call("Draw");
        Set("lineClearDuration", 0.2f);
        int beforeLines = player.LinesCleared;
        int[] beforeClear = bag.ToArray();
        Call("Lock", false);
        Check(player.IsClearingLines && player.LinesCleared == beforeLines && board.GetFullRows().Length == 1,
            "Completing a row did not defer compaction and scoring for its glow");
        CheckQueue(beforeClear);
        Check(!player.TryHold(), "Hold was accepted during the line-clear animation");
        foreach (SpriteRenderer renderer in (SpriteRenderer[])Get("renderers"))
            Check(!renderer.enabled, "An active piece remains visible during the clear");

        float deadline = Time.realtimeSinceStartup + 5f;
        while (!((bool[])Read(board, "hiddenRows"))[0] && player.IsClearingLines)
        {
            Check(Time.realtimeSinceStartup < deadline, "Timed out waiting for the pre-glow hidden row");
            CheckQueue(beforeClear);
            yield return null;
        }
        Check(player.IsClearingLines && board.GetFullRows().Length == 1,
            "The full row disappeared before its glow interval");
        for (int x = 0; x < Board.Width; x++)
            Check(!authoredCells[x].enabled && !board.IsCellFree(new Vector2Int(x, 0)),
                "Clearing-row art was not hidden while its collision data stayed in place");
        Check(!board.IsCellFree(new Vector2Int(5, 3)) && board.IsCellFree(new Vector2Int(5, 2)),
            "Upper blocks fell before the row glow completed");
        while (player.IsClearingLines)
        {
            Check(Time.realtimeSinceStartup < deadline, "Timed out waiting for the line-clear animation");
            CheckQueue(beforeClear);
            yield return null;
        }
        Check(player.LinesCleared == beforeLines + 1 && board.GetFullRows().Length == 0,
            "The clear did not finish with one scored line");
        Check(!board.IsCellFree(new Vector2Int(5, 2)) && board.IsCellFree(new Vector2Int(5, 3)),
            "Upper blocks did not compact after the glow");
        Check((int)Get("shape") == beforeClear[0], "The next piece did not spawn after the glow");
        CheckPreviewArt();
        CheckAuthoredObjects();

        DeathScreen death = board.GetComponent<DeathScreen>();
        Check(death != null, "The running scene has no death effect");
        typeof(DeathScreen).GetField("rowInterval", Private).SetValue(death, 0.01f);
        typeof(DeathScreen).GetField("rowFlashDuration", Private).SetValue(death, 0.02f);
        Check(board.TryLockPiece(new[] { Vector2Int.zero }, new Vector2Int(0, Board.Height - 1),
            Color.white, new[] { marker }), "Could not create the death-clear fixture");
        Call("EndGame");
        Check(player.IsGameOver && !player.CanHold, "EndGame did not stop gameplay");
        deadline = Time.realtimeSinceStartup + 5f;
        while (!death.CanRetry)
        {
            Check(Time.realtimeSinceStartup < deadline, "Timed out waiting for the death glow");
            yield return null;
        }
        for (int y = 0; y < Board.Height; y++)
        for (int x = 0; x < Board.Width; x++)
        {
            SpriteRenderer renderer = authoredCells[y * Board.Width + x];
            Check(board.IsCellFree(new Vector2Int(x, y)) && !renderer.enabled &&
                renderer.gameObject.activeInHierarchy,
                $"Death failed to reveal the static sprite grid at {x}, {y}");
        }
        CheckAuthoredObjects();
        Check(place.text == "98/99" && knockouts.text == "03" && badges.text == "002",
            "Gameplay or death rewrote match TMP values without a stats update");
    }
}
#endif
