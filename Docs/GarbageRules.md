# Garbage attack rules

The player and bots use the same attack calculator in
[GarbageCombat.cs](../Assets/Scripts/GarbageCombat.cs).

| Line clear | Lines sent |
| --- | ---: |
| Single | 0 |
| Double | 1 |
| Triple | 2 |
| Tetris | 4 |
| T-Spin Single | 2 |
| Mini T-Spin Single | 0 |
| T-Spin Double | 4 |
| Mini T-Spin Double | 1 |
| T-Spin Triple | 6 |
| Back-to-Back bonus | +1 |
| All Clear bonus | +4 |

| Combo | Additional lines |
| --- | ---: |
| 0 | +0 |
| 1 | +1 |
| 2 | +1 |
| 3 | +2 |
| 4 | +2 |
| 5 | +3 |
| 6 | +3 |
| 7 | +4 |
| 8 | +4 |
| 9 | +4 |
| 10+ | +5 |

The first clear is combo 0. Each consecutive piece that clears a line advances
the combo; a piece that clears no lines resets it. Eligible bonuses add to the
base attack. For example, two consecutive Tetrises send 4, then 6 (4 base + 1
back-to-back + 1 combo), before badges, targeting bonuses, or cancellation.

Back-to-back applies to consecutive difficult clears (Tetrises and T-spin
clears). A piece that clears no lines preserves that chain; an ordinary line
clear breaks it. An All Clear adds four when no locked blocks remain after
compaction. T-spins require a valid final rotation and the pivot-corner checks.

The existing attacker bonus and badge multiplier apply afterward. Attacks are
capped at 20, then cancel pending garbage before any surplus is sent. These
modifiers can make the final output differ from the base values above.
