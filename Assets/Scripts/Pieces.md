# Piece setup

The sample scene uses Assets/Prefabs/TetrisBoard.prefab. For a new scene, drag
that prefab in and assign the seven whole-shape sprites on FallingPiece as needed.
The board artwork, cell renderers, preview slots, and TMP labels are saved in the
prefab and can be edited before Play. No physics components are needed.

Each sprite rectangle must tightly enclose its shape with equal-sized square
blocks and no outer transparent padding. Keep these source sprites out of Sprite
Atlases. Sprite Mode Multiple is supported; texture Read/Write is not required.
Logical starting orientations (X is a block, dot is transparent):

```text
I     O    T     S     Z     J     L
XXXX  XX   .X.   .XX   XX.   X..   ..X
      XX   XXX   XX.   .XX   XXX   XXX
```

The script splits each whole sprite into four cell sprites at runtime so locked
pieces retain their art when rows clear. Sprites facing another direction are
automatically rotated in 90-degree steps before splitting. Detection uses the
sprite rectangle proportions and transparent gaps to match the diagram above;
sprites must have transparent backgrounds and must not be mirrored.
Block sprites match the grid cell width and height exactly and cover the grid
sprite underneath them. Use square block
artwork without outer transparent padding for an edge-to-edge appearance.

Controls: Left/Right move (hold to repeat), Up or X rotates clockwise, Z rotates
counterclockwise, Down soft drops,
Space hard drops, and either Shift key holds/swaps. WASD selects the attack mode.
Press R to start a fresh lobby after the entire match ends. Click the Game view first.
LinesCleared, IsGameOver, and IsClearingLines are public read-only properties for
a future UI.

Holding Left, Right, or Down gradually speeds up movement over 0.6 seconds.
Horizontal repeats accelerate from 0.09 to 0.025 seconds per cell (after the
initial repeat delay); soft drop accelerates from 0.05 to 0.015 seconds per cell.
Release, horizontal direction changes, and each new piece reset the hold speed.
Configure Fastest Move Interval, Fastest Soft Drop Interval, and Acceleration Time
under Held Movement Acceleration. Smaller intervals mean faster movement. Movement
still checks collision cell by cell, and soft drop retains the landing lock delay.

Rotation uses SRS with all eight quarter-turn transitions, ordered wall/floor
kicks, separate I-piece tables and pivot, and a stationary O piece. Sprite
orientation detection is separate from the logical SRS state. Kick table reference:
https://erikdemaine.org/papers/TetrisPieces_FUN2024/paper.pdf (Tables 2 and 3).

Active pieces can rotate above the visible board. Locking with any cell above
the board ends the game. Spawn height and top-out behavior remain prototype rules.
Landing gives 0.5 seconds of grounded time before locking, including during soft
drop. Successful grounded sideways moves or rotations restart that timer, up to
15 times per piece. Blocked inputs and soft drop do not reset it. Airborne time
pauses the timer without replenishing resets. Hard drop locks immediately.
Lock Delay and Max Lock Resets are adjustable in the FallingPiece Inspector.

A translucent ghost shows the hard-drop landing position using the same collision
checks as hard drop. It follows movement and rotation and hides when the active
piece reaches it. Adjust Ghost Opacity in the Inspector (zero hides it).

The next six pieces appear on the right of the board, with the next spawn at the
top. They use your existing piece sprites. The preview draws from the actual
upcoming queue, including across seven-piece bag boundaries; it advances only
when a piece spawns. It stays still during line-clear effects. Restart starts a
fresh queue. NextPiecePreview uses the prefab's existing preview renderers;
customize its size and spacing within the frame. Fit Camera On Start
includes the preview column without changing camera background settings.

The HOLD section on the left stores one piece. Either Shift key puts the active
piece there; the first hold takes the next piece from the queue, and later holds
swap with the saved piece without consuming the next queue. Each replacement
starts at its normal spawn position and rotation. Hold can be used once until
the active piece locks; the saved sprite dims while hold is unavailable. Hold is
blocked during line-clear effects and game over. Restart empties the slot.
Assign Hold Sound under FallingPiece > Sounds; accepted holds play it through
the existing effects AudioSource. An empty clip slot is silent. HoldPiecePreview
uses existing renderers in the prefab; its preview size is adjustable.
Camera fitting includes both the Hold and Next sections.

The sample scene includes a local player-versus-bot battle. Online multiplayer
and the original game's full targeting modes are not implemented.

## Board appearance

Assets/Art/Board/BoardFrame.png is the reusable frame and grid sprite, with a
transparent exterior and no baked text or targeting overlay. Its import settings
place the playable grid at Board's origin (100 source pixels per unit). The current
BoardFrame_0 slice is supported: editor setup reconnects it if the prefab still
references an obsolete single-sprite ID, preserving the edited PNG and importer.

Select the prefab's HUD children to edit the world-space TextMeshPro headings and
PLACE, K.O., and badge numbers. BoardPresentation holds references to the three
numeric TMP labels. SetMatchStats updates their content without rebuilding them;
the standalone prefab retains authored text. In the battle scene, BattleArena
updates these same TMP values with live placement, K.O.s, and badge bits.

All game text uses `Assets/font/ajn-aokane.otf/ajn-aokane SDF.asset`, including
the HUD, warning, opponents, and clear feedback. Editor authoring and the TMP
default font use this asset too. Keep the supplied font and license files;
use ASCII X for combo labels because this font has no multiplication sign.

The warning uses WarningFrame.png and a separate TMP label. WATCH OUT! appears
while a live opponent targets you. A thin yellow line connects the warning to
each attacking miniature board, following its position. A connection attaches
to a suitable point on the warning's capsule outline. It uses a straight line
when clear, or a short detour with a softened bend around the 10 by 20 playfield.
Connections can cross the decorative frame and side panels, while staying clear
of the grid where pieces move and lock. Stack height does not
trigger this warning. It hides when nobody targets you and on victory or defeat;
restart restores it from the new round's targets. BoardPresentation reuses four
available line segments per opponent under Targeting lines, with room for 98
opponents. Unused segments stay hidden. Targeting Line Width controls their thickness; Targeting Line Clearance
controls the gap outside the playable grid. Each sprite's color is editable. Runtime scripts reuse the saved
board, preview, and HUD objects; they do not create the
board on launch. The Tetris > Set Up Board Scene editor menu can prepare the
sample scene. Tetris > Set Up Targeting Warning repairs just the warning and
its saved connections in an existing setup. Your scene background is preserved.

## Sounds

Assign your clips under FallingPiece > Sounds: Move Sound, Rotate Sound,
Soft Drop Sound, Drop Sound (normal locking), and Hard Drop Sound. Successful
sideways steps/rotations play their clips; soft drop plays per successful downward
step while Down is held. Blocked inputs and automatic falling are silent. Hard
drop plays only its own clip, without also playing the normal locking clip.
Empty slots are silent. Adjust Sound Volume as needed.

Sound Source accepts your own AudioSource. If left empty, the script creates a
dedicated 2D AudioSource when play starts. The scene needs one active AudioListener
(normally on Main Camera).

For background music, add BackgroundMusic to the Board or a separate scene object.
Assign Music Clip and adjust Volume. Play On Start defaults to on and the music
loops. Leave Music Source empty to create a dedicated source, or assign a separate
music AudioSource (do not share the FallingPiece sound-effects source). Public
Play and Stop methods are available for future menu buttons.

## Line clears

Assign Single Clear Sound, Double Clear Sound, Triple Clear Sound, and Tetris Clear
Sound under FallingPiece > Line Clears. Each lock plays the clip matching the total
number of rows cleared (one through four), once. Empty sound slots are silent.
These clips use the existing FallingPiece Sound Source and Sound Volume.

Completed blocks disappear one frame before their rows glow together for 0.25
seconds. The upper rows stay in place until the glow ends, then the stack shifts
down and the next piece spawns. Only completed rows glow; movement and dropping
pause during the effect. Adjust Line Clear Duration and Line Clear
Color in the Inspector. LineClearEffect is created automatically. The grid remains
visible, and no bloom or camera changes are required.

## Clear and attack feedback

ClearAttackFeedback updates three saved AJN Aokane TMP labels beside the left
side of the board: the clear type, combo/back-to-back chain, and outgoing lines.
T-spins, mini T-spins, and all clears have their own callouts. Text pops briefly
and fades after 1.5 seconds. `+X LINES` is the attack left after cancelling your
incoming garbage; `CANCELLED X` means that attack was spent defending.

Actual attacks accepted by an opponent launch four gold stars per received line,
up to 48 per attack, using a saved pool of 192 sprites. Each wave retains its
original recipient when you select another target. Stars follow the yellow
connection exactly, starting on the warning outline and following each bend to
the opponent's board edge. Moving a miniature updates its route and stars together.
An outgoing connection stays visible during the flight even when that opponent
is not targeting you; WATCH OUT still means someone is targeting you.
Stars burst and fade only after reaching the opponent. The travel
animation does not delay garbage delivery. Fully cancelled attacks and attacks
discarded by a full opponent queue launch no stars.

The component has separate Callout Source and Attack Source audio references.
Its editable clip slots are Callout Sound, Combo Sound, Special Clear Sound,
All Clear Sound, Attack Launch Sound, and Attack Hit Sound. The setup assigns
the supplied msg/count/special/perfect/attack1/attack2 clips. It plays one callout
per clear, prioritizing all-clear and special moves, and one launch/hit cue per
star wave. Existing single/double/triple/Tetris clear sounds still play.

Tetris > Set Up Clear and Attack Feedback authors these objects before Play.
Tetris > Use AJN Aokane Font updates existing game text and the TMP default.
Death, victory, restart, or disabling the feedback stops its dedicated audio
and resets its saved stars and labels.

## Game over

FallingPiece automatically creates a DeathScreen component. On defeat, a glowing
flash sweeps through all 20 rows from bottom to top. Each row's blocks are deleted
one frame before that row's light starts.
The empty grid stays visible, and this does not add to Lines Cleared. After the
sweep, MatchFlow displays your rank and live standings while the bots continue
playing. Restart becomes available after the entire match ends.

Add DeathScreen to the Board before Play to assign your clip to Death Sound and
adjust Sound Volume, glow color, or animation timings. Sound Source is optional:
an empty slot creates a dedicated 2D AudioSource when the death sound first plays.
The clip plays once at the start of each death effect; an empty clip is silent.
To preview a defeat, use the FallingPiece component's context menu:
Test Game Over (Play Mode). The effect uses a generated glow sprite and does not
require bloom. Background music stops when game over begins. MatchFlow plays
Result (Eliminated) after the sweep and audible death cue finish. FallingPiece finds the
scene's BackgroundMusic component automatically, or you can assign it in its
Background Music slot.
Restart stops the death clip before starting music again. Give the death sound
its own AudioSource if assigning one manually.

The reference video host blocked playback during development. The bottom-to-top
sequence follows the documented Tetris 99 elimination description at
https://www.mariowiki.com/Game_Over; the glow is an approximation,
not a frame-matched recreation.


## Bot battle and the opponent grid

Open Assets/Scenes/SampleScene.unity and press Play for a local match with you
and up to 98 bots. Matchmaking lasts at most 90 seconds and starts early when
all 99 participants have joined. The saved bot boards begin inactive; each
arrival activates one board and plays the join sound. Unoccupied slots are
invisible. Bot boards start empty and no participant can play before GO.

MatchFlow plays the supplied menu theme during the lobby, then READY using
me_game_start1.wav, reveals the attack selector, counts 3/2/1 with se_game_count.wav,
and plays me_game_start2_vo.wav for GO. These are the original unsuffixed voice
files, not localized variants. Battle music starts at GO. All cues and phase
lengths are editable on MatchFlow; the defaults allow the full voice lines to play.

While you are alive, reaching 50 or 10 remaining players displays "50 PLAYERS LEFT"
or "10 PLAYERS LEFT" in the center of your board. AJN text stretches in, holds,
then stretches out and fades over 1.6 seconds while play continues. Each milestone
plays its original me_game_remain_1.wav or me_game_remain_2.wav cue once through a
separate audio source. Rapid crossings queue the messages; defeat, victory, or a
new lobby clears them. Starting with fewer players does not trigger a milestone.

The opponent layout keeps the existing two 7-by-7 groups and 0.85 camera zoom.
Slots reserve their layout space even while invisible, so arrivals do not resize
the board. Each occupied miniature renders its actual board and updates on locks,
clears, and garbage rises. The saved bot uses its own seven-bag and searches
reachable SRS placements. Up to three bots plan per frame through a fair queue;
waiting bots retain their due turn, and their initial turns are staggered.
Seconds Per Piece controls the pace (1.1 seconds by default).

Tetris > Set Up Matchmaking and Results authors the lobby, saved roster, attack
selector, survivor announcements, medals and result labels. Tetris > Set Up Battle also retains this
match flow. The 98 bot prefab instances are authored before Play. All UI uses
AJN Aokane and remains editable in the scene under Match presentation.

Arrow keys, Space and Shift retain their movement/drop/hold controls. W selects
K.O.s (the highest opponent stack), A selects Randoms, S selects Attackers,
and D selects Badges (the most collected medal bits). Attackers sends your
attack to every live opponent currently targeting you, falling back to one
opponent if nobody targets you. Click a mini board or press Tab for a manual
target; selecting an attack mode returns to automatic targeting. The WATCH OUT
warning and yellow connections identify incoming targets. Your attack stars
continue to follow those same routes.

A newly targeting opponent plays BoardPresentation's Targeting Sound through
its dedicated Targeting Sound Source. Its saved default is se_game_target.wav.
Unchanged attackers, redraws, outgoing selection and receiving garbage do not
replay the cue. Several new attackers in one update share a cue. It stops when
the warning disappears or play is paused, disabled, or ended. Tetris > Set Up
Targeting Sound adds missing references while preserving assigned choices.

The incoming meter sits under Hold, with one square block per pending line and
no number label. Its twelve SpriteRenderer blocks are saved children of Tetris
Battle. The old opponent-count label above the board is removed. PLACE shows joined
participants in the lobby, survivors during play, and your fixed rank afterward. GarbageMeter colors the blocks:
gray for queued attacks, yellow in the middle of the active packet's timer, red
in its final third and while ready. The default timer is three seconds; change
Incoming Delay on GarbageCombat. Packets waiting behind the active one stay gray
and start their own timer when they reach the front. Pending garbage is capped
at twelve lines and excess is discarded.

Each attack chooses one random hole column. All gray rows from that attack share
that hole; the next attack can choose a different one. Receiving an attack only
fills the meter. Ready garbage rises after a non-clearing piece locks, up to six
rows from the active packet per lock. Clearing any line postpones rising garbage.
An attack first cancels pending lines, then sends the remaining output to its
target. A base single sends zero; doubles, triples and Tetrises send 1, 2 and 4.
Full T-spin singles/doubles/triples send 2/4/6; mini singles/doubles send 0/1.
Combos, back-to-back difficult clears and all clears add bonuses. The player's
T-spin detector uses the last successful rotation, three pivot corners, facing
corners for mini/full classification, and the fifth SRS kick upgrade. The bot
retains its last successful rotation through placement and uses the
same mini/full detector and attack calculator. A translation removes spin
eligibility; board evaluation favors a valid spin when otherwise equal.
The exact clear and combo tables are listed in Docs/GarbageRules.md.

K.O.s award one medal bit plus the defeated opponent's bits. The lower 000 field
tracks those collected bits; four medal icons and a progress bar show completed
badges and progress toward the next one. Bonuses at 2, 6, 14
and 30 bits add 25%, 50%, 75% and 100%. Multiple attackers also boost output;
these bonuses reflect the actual bots targeting each participant.
Outgoing attacks are capped at twenty before cancellation. Attack tables follow
[TetrisWiki's documented Tetris 99 rules](https://tetris.wiki/Tetris_99).
Timing, bot strategy and top-out behavior remain configurable prototype choices.

The existing PLACE, K.O. and badge-bit TMP values update with battle state.
Garbage overflow or a blocked spawn eliminates a participant. Even fatal
garbage is inserted first, with blocks pushed beyond the top leaving
the visible board. The raised stack is shown for one frame before the death
sweep starts; elimination, input lockout and the death sound happen immediately
after insertion. The attacker still receives the knockout. The existing death
sweep and music stop still run. When you lose, bots keep fighting each other.
After the sweep, a results panel shows your fixed rank and a live leaderboard
while all mini boards remain visible around it. Q/E, Page Up/Page Down, or the
page labels browse all participants. Surviving entries show -- until their real
placement is decided. K.O. totals and collected medal bits update with results.

Only the last remaining participant ends the entire match. Your rank is never
changed by later eliminations. A victory plays the existing Victory Sound once
and stops battle music. After the audible outcome cue finishes, MatchFlow loops
Result (Win) for a victory or Result (Eliminated) for a defeat. Silent padding in
the supplied outcome cues does not delay the soundtrack. A new lobby restores
menu music.
The final leaderboard retains every joined participant. R or Play Again starts
a fresh lobby only after the whole match finishes and the death sweep permits it;
it cannot reset surviving bots while you are spectating.
