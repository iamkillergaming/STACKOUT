# Death effect smoke test

DeathSmoke.cs is kept outside Assets so it cannot enter the game build. Copy it
and Assets/Scripts/*.cs into an isolated Unity 6000.3.8f1 project with Input System
and uGUI, then run Unity with:

```
-batchmode -nographics -projectPath <isolated-project> -executeMethod DeathSmoke.Run -logFile <log-path>
```

Do not use `-quit`: the coroutine exits Unity itself after the checks complete.
The test creates and saves its own scene, so use an isolated project only.

The checks cover:

- Bottom rows are deleted before the top rows during the glow sweep.
- The first row shows the empty grid with no glow on its deletion frame, then
  starts glowing on the next frame (checked in LateUpdate after coroutines).
- All occupied cells are empty when the sweep completes.
- The original grid remains visible, with its empty-cell sprite, color, scale,
  and rotation restored after deleting blocks.
- No Canvas or EventSystem is created, including inactive objects.
- Hide cancels a running sweep without deleting untouched rows or restoring
  already-deleted blocks.

Success prints DEATH_SMOKE_PASSED and exits with status zero. This headless test
does not verify rendered appearance or audible playback. Check the glow and an
assigned death sound in the Unity Editor separately.
