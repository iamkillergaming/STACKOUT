`BattleSmoke.cs` runs the saved battle scene in an isolated Unity editor project.
Copy it into that project's Assets and run with `-batchmode -force-glcore
-projectPath <isolated-project> -executeMethod BattleSmoke.Run -logFile <log>`.
Do not pass `-quit`: the check enters Play mode and exits after its coroutines.
Success logs `BATTLE_SMOKE_PASSED` and exits 0.

The fixture adds a second saved bot prefab without modifying SampleScene. It
checks all 98 authored slots and camera bounds in portrait and landscape, saved
cell/TMP identity across launch/restart, actual bot placement, player Tetris and
T-spin attacks after glow/compaction, selected-target routing, gray rows with
one hole, no receive-time stack mutation, and sprite preservation during rise.
It also checks the twelve-line cap and gray/yellow/red meter, K.O. attribution,
retargeting, badge retention, victory/loss pause, death sweep and full restart.
Victory audio checks use a temporary clip: win plays once and stops music;
non-final K.O.s and losses do not trigger it, duplicates do not replay it, and
restart stops the clip and resumes music. The supplied saved victory clip is
also checked before substituting the fixture clip.
Empty slots remain lightweight and do not become fake participants at launch.

Fatal garbage checks use a real player lock and real bot placements. Even a
fatal packet must insert its gray rows before elimination, shift surviving
blocks, retain the shared hole and award the correct attacker its knockout.
The player's raised board renders for one frame, the first death row is removed
on the next frame, and its glow starts one frame after deletion. No new piece
spawns, all opponents pause immediately, and retry clears the fatal-rise state.
