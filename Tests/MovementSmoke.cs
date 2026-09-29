#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Run only in an isolated Unity project: -executeMethod MovementSmoke.Run (without -quit).
public sealed class MovementSmoke : MonoBehaviour
{
    private FallingPiece player;
    private Keyboard keyboard;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private object Get(string name) => typeof(FallingPiece).GetField(name, Private).GetValue(player);
    private void Set(string name, object value) => typeof(FallingPiece).GetField(name, Private).SetValue(player, value);
    private void Call(string name) => typeof(FallingPiece).GetMethod(name, Private).Invoke(player, null);
    private void Check(bool value, string message)
    {
        if (value) return;
        Debug.LogError(message);
        EditorApplication.Exit(1);
        throw new Exception(message);
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Camera", typeof(Camera)).tag = "MainCamera";
        new GameObject("Board", typeof(Board), typeof(MovementSmoke));
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/MovementSmoke.unity");
        EditorApplication.EnterPlaymode();
    }

    private void Input(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
        Call("Update");
    }

    private IEnumerator Start()
    {
        Time.captureDeltaTime = 0.02f;
        yield return null;
        keyboard = InputSystem.AddDevice<Keyboard>();
        player = gameObject.AddComponent<FallingPiece>();
        player.enabled = false; // Inject fixtures; exercise real Update without sprite import or device output.
        Set("board", GetComponent<Board>());
        Set("fallInterval", 100f);
        Set("moveRepeatDelay", 0.18f);
        Set("origin", new Vector2Int(3, 18));
        Set("cells", new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) });
        Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        var tiles = (Sprite[][])Get("tiles");
        for (int i = 0; i < tiles.Length; i++) tiles[i] = new[] { sprite, sprite, sprite, sprite };
        foreach (string field in new[] { "renderers", "ghostRenderers" })
        {
            var renderers = (SpriteRenderer[])Get(field);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i] = new GameObject(field + i, typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        }

        int early = 0, late = 0;
        for (int frame = 0; frame < 45; frame++)
        {
            Set("origin", new Vector2Int(3, 18)); // Keep open space while comparing equal time windows.
            Input(Key.RightArrow);
            int distance = ((Vector2Int)Get("origin")).x - 3;
            if (frame >= 10 && frame < 20) early += distance;
            if (frame >= 35) late += distance;
            yield return null;
        }
        Check(late > early, "Holding Right did not accelerate");
        Input(Key.LeftArrow);
        Check((float)Get("horizontalHoldTime") == 0f, "Reversing direction kept accelerated speed");
        Input();
        Check((int)Get("heldDirection") == 0, "Release kept horizontal hold state");
        Set("origin", new Vector2Int(6, 18));
        Input(Key.RightArrow);
        Check(((Vector2Int)Get("origin")).x == 6, "Movement crossed the wall");

        Call("ResetHeldMovement");
        Set("fallTimer", 0.7f);
        Set("origin", new Vector2Int(3, 18));
        Input(Key.DownArrow);
        Check(((Vector2Int)Get("origin")).y >= 17, "Soft-drop entry consumed old gravity backlog");
        early = late = 0;
        for (int frame = 0; frame < 45; frame++)
        {
            Set("origin", new Vector2Int(3, 18));
            Input(Key.DownArrow);
            int distance = 18 - ((Vector2Int)Get("origin")).y;
            if (frame < 10) early += distance;
            if (frame >= 35) late += distance;
            yield return null;
        }
        Check(late > early, "Holding Down did not accelerate");
        Input();
        Check((float)Get("softDropHoldTime") == 0f, "Release kept soft-drop acceleration");
        Set("origin", new Vector2Int(3, 0));
        Input(Key.DownArrow);
        Check(((Vector2Int)Get("origin")).y == 0, "Soft drop crossed the floor");
        Check(!player.IsGameOver && GetComponent<Board>().IsCellFree(new Vector2Int(3, 0)), "Soft drop bypassed lock delay");
        Call("ResetHeldMovement");
        Check((float)Get("fallTimer") == 0f && (float)Get("repeatTimer") == 0f, "New-piece reset retained timing debt");
        InputSystem.RemoveDevice(keyboard);
        Destroy(player);
        Destroy(sprite);
        Time.captureDeltaTime = 0f;
        Debug.Log("MOVEMENT_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }
}
#endif
