# Line clear smoke test

Keep `LineClearSmoke.cs` outside Assets in the working project. Copy it and
`Assets/Scripts/*.cs` into an isolated Unity 6000.3.8f1 project's Assets, with the
Input System and uGUI packages, then run Unity with:

```
-batchmode -nographics -projectPath <isolated-project> -executeMethod LineClearSmoke.Run -logFile <log-path>
```

Do not pass `-quit`; the coroutine exits Unity after the checks finish. This
creates and saves a scene, so run it only in an isolated project.

Checks cover one through four complete rows; detection without deleting blocks;
incomplete rows staying unchanged; adjacent and nonadjacent row compaction
preserving sprite, color, rotation, and scale; empty grid cells staying visible;
the four assigned clip mappings and an unassigned clip staying silent; only
completed rows receiving the glow; selected blocks showing the empty grid one
frame before the glow starts; the glow leaving board data and upper rows
untouched; completion hiding the glow without making cleared blocks reappear;
and cancellation restoring block presentation without changing occupancy.
The controller integration check locks a piece that completes two rows, verifies
that row collapse, line count, and spawning wait for the flash, and checks that
the next piece appears once the clear finishes.

Success prints `LINE_CLEAR_SMOKE_PASSED` and exits with status zero. This headless
test checks clip selection and renderer state, not audible playback or rendered
appearance. Verify assigned clips, flash appearance, and gameplay input during
the brief clear animation in the Unity Editor.
