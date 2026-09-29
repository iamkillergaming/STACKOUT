# Current 99-player match validation

Verified in Unity 6000.3.8f1 using an isolated copy of the final project.
The saved SampleScene now supports one local player and 98 bots.

- MatchFlowSetup.BatchSetup passed. The scene contains 98 inactive saved
  opponents; empty slots are hidden. Existing scene object transforms, camera
  component, zoom and background match the pre-lobby snapshots.
- MatchFlowChecks.Run passed. Verified timed joins, full-lobby early start,
  timeout start, pre-GO gameplay lock, READY / 3 / 2 / 1 / GO, original clip
  assignments, and menu/battle music transitions.
- Real queued keyboard events verified W = Knockouts, A = Randoms,
  S = Attackers, D = Badges. Mode targets and multicast attacks passed.
- All 98 bots received a real first turn in 0.13 seconds on the test machine,
  with at most three planning searches per frame. This is a bounded fairness
  check, not a frame-time benchmark.
- FullMatchSoak.Run passed a natural 98-bot match to one survivor: 21,495
  placements, 2,953 accepted attacks, 5,212 garbage lines, 97 natural bot
  eliminations and 95 attributed K.O.s. All ranks 1–99 were unique, the player's
  rank stayed 99, final standings appeared, and all queues cleared at completion.
  Pending lines never exceeded twelve; planning never exceeded three per frame.
  The 48.14-second run preserved the authored 1.1-second bot interval and sped
  scaled gameplay uniformly to 16X. This verifies match lifecycle under
  acceleration; it does not predict normal-speed match duration or frame rate.
- Actual bot attacks and garbage topout awarded a KO and medal bits. Disabling
  and re-enabling an eliminated bot preserved its rank and paused state.
- Player death retained its rank while another bot continued playing. The
  live leaderboard, paging, death/audio timing, final ranks 1–99, victory,
  early-retry rejection, and two clean lobby resets passed.
- GarbageChecks.Run and BotSpinChecks.Run passed against the final runtime.
  Attack tables, cancellation, queue timing, fatal row insertion, reachable
  SRS spins, translation invalidation, and planner cache/reset behavior passed.
- All five 1920×1080 previews were inspected: Docs/MatchLobbyPreview.png,
  MatchCountdownPreview.png, MatchBattlePreview.png,
  MatchSpectatingPreview.png and MatchResultsPreview.png. The results panel
  leaves all miniature boards visible. Every new TMP uses AJN Aokane.

Only the isolated checks substitute short silent cue probes and accelerated
join/countdown timers. The saved scene retains the original supplied clips
and 90-second lobby setting. AudioSource playback is verified; speaker output
and subjective sound balance were not assessed. No test harness is saved in
SampleScene. This implements local bots, not online matchmaking.

# Earlier feature checks

The following records describe earlier implementation stages. Their one-bot
fixtures predate MatchFlow; the current full-roster checks above cover the new
match lifecycle.

Verified in Unity 6000.3.8f1 using an isolated project copy.

- BattleSceneSetup.BatchSetup: one saved bot, 98 saved slots; rerun preserved the roster.
- BattleSmoke.Run: passed. Actual bot placement, routed Tetris/T-spin attacks,
  deferred garbage, 12-segment meter colors, camera bounds, K.O.s, badges,
  restart and victory during an in-flight line-clear animation.
- GarbageChecks.Run: passed. Queue cap, FIFO stages/timing, cancellation,
  attack tables/bonuses, badge transfer, fatal overflow and shifted artwork.
- SrsRotationChecks.Main: passed all shape cycles, walls/floor/stack kicks,
  rejection, O behavior and rotations above the visible board.
- StaticBoardSmoke.Run: passed saved object identity, Hold, Next, line-clear
  sequencing, death effect and preserved camera background.
- Final scene render: checked both the incoming meter and live miniature garbage
  using the current BoardFrame_0 slice. Docs/BattlePreview.png and
  Docs/BattleIncomingPreview.png show test gameplay, not the empty starting state.

The main scene contains no test harness. Tests use separate fixture scenes.
At this earlier stage, the supplied match was local 1v1 and the authored grid
reserved 98 opponent slots. The full roster is covered by the current checks above.

Latest UI/audio update: BattleSmoke passed with the authored victory clip assigned,
block-only garbage meter, and no top opponent label. Verified one-time win playback,
no win sound for partial K.O.s or defeats, music stop on victory, and restart cleanup.

The supplied clear/combo tables were rechecked with GarbageChecks.Run against the
current runtime scripts: passed, including all combo thresholds, spin values,
back-to-back preservation/reset, all-clear bonuses and cancellation.

Bot spin integration: BotSpinChecks passed using real reachable SRS placements.
Full singles/doubles send 2/4; mini singles send 0 and preserve the back-to-back
rules. Translation into a three-corner slot earns no spin. Spin state survives
the clear delay and resets between rounds. BattleSmoke also passed after the
bot change, including attack routing, victory audio and match restart.

Targeting warning update: BattleSmoke passed after replacing the high-stack
warning trigger with current incoming targets and saved connection sprites.
The scene file remains unchanged; camera zoom, background, fonts, and board
layout were preserved by the focused prefab migration.

TargetingWarningChecks.Run: passed. Empty 1v1 targeting, high-stack and
outgoing-only exclusions, one/multiple attacker endpoints, retargeting, K.O.,
unregistration, disable, victory/defeat and restart all behaved correctly.
The 98 saved lines and existing cell/TMP objects retained their identities;
camera settings stayed fixed. Inspected both 1920x1080 captures in
Docs/TargetingWarningPreview.png and Docs/TargetingWarningMultiplePreview.png.
The latter uses two extra bots in the isolated test fixture only.

Board-avoiding connections: TargetingWarningChecks passed with four saved
segments per opponent (392 total). Continuous routes and their visible stroke
stay outside the playfield and cabinet at top/bottom positions on both sides.
Target changes, removal and restart still pass; camera values stay fixed.
Both 1080p preview images were refreshed and visually inspected.

Natural connections: TargetingWarningChecks passed with capsule-outline
attachments, direct paths when clear, and shorter rounded detours. Verified
left/right outline attachment, stroke clearance, continuous variable-length
routes, hidden unused segments, and all prior lifecycle/camera checks.
The saved scene and prefab were unchanged; both 1080p previews were refreshed.

Gameplay-only avoidance: TargetingWarningChecks passed. Lines may cross the
decorative frame and side panels; every visible segment still clears the
10x20 playfield including its stroke width. The 1v1 regression confirms a
route actually uses the allowed decorative area. Capsule attachments,
rounding, lifecycle, saved objects and camera checks also passed. Both 1080p
previews were refreshed and visually inspected.

Clear feedback and AJN Aokane font: ClearAttackFeedbackChecks passed in Unity
6000.3.8f1. Verified saved TMP/font/material references, global TMP default,
six assigned feedback clips, clear/spin/combo/all-clear reports, cancellation,
accepted-line star counts, concurrent recipients, audio timing, pool limits,
and disable/restart/victory/death cleanup. A real I-piece Tetris waits for its
glow before displaying TETRIS / COMBO X1 / +5 LINES and sending twenty stars.

Attack stars follow the same yellow polyline as the targeting connection,
from the warning outline through every bend to the recipient edge. The fixture
checks their position against visible segment renderers, including after moving
the original recipient and selecting a different target. Outgoing-only attacks
show a temporary connection without activating WATCH OUT; clearing feedback
removes these routes while preserving incoming warnings.

GarbageChecks, TargetingWarningChecks, and BattleSmoke passed after integration.
The 1920x1080 capture in Docs/ClearAttackFeedbackPreview.png was visually inspected;
it uses two opponents for the test only. Both warning previews now use AJN.
At that stage, the saved game remained 1v1. Existing camera component, scene/prefab transforms,
zoom and background match the pre-change snapshots. No test fixtures were added
to the game scene.

Fatal garbage update: GarbageChecks and BattleSmoke passed in Unity 6000.3.8f1.
A fatal packet now inserts and consumes its rows before reporting top out,
preserving the six-row cap, shared hole, surviving block artwork and KO sender.
Real bot miniatures retain the inserted fatal rows. A real player lock shows
the final raised board on frame N, deletes the first death row on frame N+1,
and starts its glow on frame N+2. Elimination and battle pause are immediate,
the next queue does not advance, and the usual defeat/retry flow passes.

Incoming targeting sound: TargetingWarningChecks passed using the saved dedicated
AudioSource and se_game_target.wav assignment. Real source playback probes verify
new attackers and retargeting play a cue; unchanged/reordered/duplicate sets,
outgoing choices/routes, high stacks and pending garbage remain silent. Pause,
player/presentation/arena disable, victory and death stop the cue; new rounds
announce their incoming attackers again. Existing connection geometry, saved
objects and camera checks also pass. Audio authoring added only one saved child
to the player prefab and its two sound references; SampleScene stayed unchanged.
