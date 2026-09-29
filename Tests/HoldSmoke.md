# Hold smoke test

Keep `HoldSmoke.cs` outside Assets in the working project. Copy it and
`Assets/Scripts/*.cs` into an isolated Unity 6000.3.8f1 project's Assets, with the
Input System and uGUI packages, then run Unity with:

```
-batchmode -nographics -projectPath <isolated-project> -executeMethod HoldSmoke.Run -logFile <log-path>
```

Do not pass `-quit`; the coroutine exits Unity after its checks. The runner
creates and saves a temporary scene, so use only an isolated project.

The test injects unique cell sprites and a deterministic queue into a disabled
FallingPiece component, then calls the real spawn, hold, rotation, lock, and
restart methods. It verifies that an empty hold consumes only the advertised
next piece, a populated hold swaps without changing the queue or random state,
and holding again is rejected until the active piece locks. It also checks
rejection during line clearing and game over, reset of spawn orientation,
position, movement and lock timers, blocked hold spawn causing game over, and
restart clearing the slot. Renderer checks cover the single left-side slot,
sprite orientation, availability dimming, actual heading bounds, and portrait
camera framing of the board and both previews without changing the background.

Success prints `HOLD_SMOKE_PASSED` and exits with status zero. This headless test
does not simulate physical Shift key presses or listen to audio. In Unity Play
mode, verify both Shift keys, once-per-piece swapping, the assigned Hold Sound,
and that rejected holds stay silent.
