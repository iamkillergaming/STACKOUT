# Next preview smoke test

Keep `NextPreviewSmoke.cs` outside Assets in the working project. Copy it and
`Assets/Scripts/*.cs` into an isolated Unity 6000.3.8f1 project's Assets, with the
Input System and uGUI packages, then run Unity with:

```
-batchmode -nographics -projectPath <isolated-project> -executeMethod NextPreviewSmoke.Run -logFile <log-path>
```

Do not pass `-quit`; the coroutine exits Unity after completing its checks. The
runner creates and saves a scene, so run it only in an isolated project.

The test injects unique cell sprites and calls the real FallingPiece spawn path
49 times with an empty board. It checks that each spawned piece matches the
previously shown first entry, all six preview pieces preserve the queued order
across bag boundaries, and every group of seven spawned pieces includes each
shape exactly once. It also checks that preview rendering leaves the queue and
random state unchanged, reuses exactly 24 visible blocks, preserves sprite
orientation, and arranges the six pieces from top to bottom to the right of the
board. A portrait camera check verifies that the board and preview fit without
changing the background color or clear mode.

Success prints `NEXT_PREVIEW_SMOKE_PASSED` and exits with status zero. This
headless test checks queue behavior and renderer state; inspect your assigned
sprites and the preview layout in Unity for visual confirmation.
