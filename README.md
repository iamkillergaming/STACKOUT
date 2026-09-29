# STACKOUT

Editable Unity falling-block battle project with one local player and 98 bots.

## Open the project

1. Install **Unity 6000.3.8f1** through Unity Hub.
2. Clone or download this repository, then add its root folder in Unity Hub.
3. Open the project and allow Unity to import assets and resolve the dependencies in `Packages/manifest.json`.
4. Open `Assets/Scenes/SampleScene.unity`, press Play, and click the Game view to enable keyboard input.

The repository includes `Assets` and their `.meta` files, `Packages`, and `ProjectSettings`. Unity regenerates its local cache on first launch.

## Controls

| Input | Action |
| --- | --- |
| Left / Right | Move |
| Up or X | Rotate clockwise |
| Z | Rotate counterclockwise |
| Down | Soft drop |
| Space | Hard drop |
| Shift | Hold / swap |
| W / A / S / D | Target K.O.s / Randoms / Attackers / Badges |
| Click an opponent or Tab | Choose a manual target |
| Q / E or Page Up / Page Down | Browse standings |
| R | Start a fresh lobby after the match ends |

## Editing and validation

The authored scene, prefabs, scripts, and editor setup tools are in `Assets`. Detailed setup notes are in [Assets/Scripts/Pieces.md](Assets/Scripts/Pieces.md). Preview images and gameplay notes are in `Docs`.

`Tests` contains the existing validation fixtures and their instructions. Run these in an isolated project copy as described in the accompanying Markdown files. [Tests/BattleValidation.md](Tests/BattleValidation.md) records earlier validation runs.

## Fonts and supplied artwork

Game text uses **AJN Aokane**, including the TextMeshPro default font. Preserve `Assets/font/ajn-aokane.otf/ajn-aokane.otf`, its SDF asset, and the supplied license files. Combo labels use ASCII `X` because the font has no multiplication-sign glyph.

The supplied K.O. emblems, knockout animation artwork, and WATCH OUT texture remain graphical status indicators. See [AGENTS.md](AGENTS.md) for the project's text and artwork requirements. Existing asset license files are retained with their assets.
