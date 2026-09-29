# Authored board integration smoke test

Run `StaticBoardSmoke.Run` in an isolated Unity 6000.3 project containing the current `Assets`, package dependencies, and this test copied into `Assets`. It opens `Assets/Scenes/SampleScene.unity`, verifies the already-authored sprite, 200 grid renderers, 28 preview renderers, and TMP references, then saves its fixture separately as `Assets/StaticBoardSmoke.unity` before entering Play Mode. Graphics-enabled execution is recommended because the actual piece startup reads sprite orientation through a GPU copy.

The test checks that startup preserves authored objects and custom TMP text, that `SetMatchStats` updates the assigned TMP values, that Hold and all six Next slots use the real queue and art, and that camera fitting keeps the sprite visible without changing the background. It completes a real row through `FallingPiece.Lock`, verifies blocks hide while rows and spawning wait for the clear animation, then verifies death disables the same locked-block renderers to reveal the grid in the static sprite. Targeting overlays, legacy TextMesh labels, and replacement cabinet meshes must remain absent.

Identity checks cover the authored sprite renderer, cell and preview renderers, and TMP components. TMP's internal text meshes may regenerate normally and are not treated as authored board objects.

Success logs `STATIC_BOARD_SMOKE_PASSED` and exits with code 0. Failures log the assertion/exception and exit with code 1. Only fixture animation timings are shortened; the sample scene, prefab, and configured gameplay/audio values are not saved by the test.

This replaces the scene-construction portions of older Hold/Next/line-clear smoke tests, whose empty `new GameObject(..., typeof(Board))` fixtures no longer have the required authored references. Those old fixture setups should not be used to judge the static scene migration.
