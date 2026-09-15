# DarkEngine3D_gl_csharp

OpenGL/C# game engine with ImGui-based IDE editor.

## Quick Start

1. Pastikan `glfw3.dll` berada di folder yang sama dengan executable Anda.
2. Jika belum ada bisa didownload dari https://www.glfw.org/download.html
3. Pastikan folder `Artifacts` berada di folder yang sama dengan executable Anda.
4. Build: `dotnet build`
5. Run: `dotnet run`

## Features

- **3D Engine**: OpenGL 4.x, PBR rendering, cascaded shadow maps, post-processing
- **Post FX** — reactive bloom (5-mip chain: tight hot cores + wide soft halos, adjustable radius), auto exposure (eye adaptation with min/max clamp + target luminance), ACES tonemapping, gamma; runs live in both the editor viewport and in-game from one shared processor; per-stage framebuffer debug views in the viewport (FX Debug button) and the FrameBuffer Debug panel; all parameters persist per-project
- **Per-Sprite Glow** — per-sprite emissive boost so only bright pixels (fire, lava, candles) bloom via Post FX; glow color tint (e.g. blue fire) and organic fire flicker (3-layer noise, per-sprite phase, frame-rate independent); Inspector controls per Sprite2D/Player2D object; persisted in the scene file
- **IDE Editor**: ImGui-based with Inspector, Hierarchy, Scene Manager, Asset Browser
- **UI System**: Buttons, Labels, Sliders, Checkboxes, Dropdowns, Containers with scroll
- **Project System**: `.projing` project files, per-project settings, auto-load scenes
- **Terrain**: Heightmap terrain with dynamic PBR layers, painting, sculpting
- **Scene Management**: Multiple scenes (MainMenu, GameScene, Loading), transitions
- **2D Game Support** (sidescroller, orthographic front view over the 3D viewport):
  - **Sprite Editor** — auto-detect/interactive sprite sheet slicing, animation clips with FPS + zoom, drag-drop import, saved to `Assets/Sprites/sprites.sheets.json`
  - **Map Editor** — tile painting (Paint/Erase/Fill/Pick + brush size) directly in the 3D viewport, multi-layers, marquee multi-select stamping, per-tile undo/redo (Ctrl+Z/Y)
  - **Tilemap in 3D** — rendered as an upright textured plane at world origin; camera auto-switches to ortho front view (editor and in-game)
  - **Parallax Backgrounds** — per-layer ScrollFactor/ZPosition/Alpha, aspect-preserving sizing, RepeatX/Y wrap, seamless scroll preview while panning, correct rendering in Play in Preview
  - **Collision Flags** — per-tile collision with dedicated Collision paint tool; full 3D translucent boxes with bright edges centered on the tile; per-layer collision IDs persisted
  - **Trigger Areas** — non-blocking event volumes with the same drag/resize editor as collision boxes (snap to grid, copy/paste/duplicate); conditions On Enter / On Stay (interval) / On Exit + optional moving-right gate; 16 action types incl. Save Game, Save/Load Checkpoint, Change Map, and earthquake Camera Shake; triggers persist with the map and fire in preview/in-game
  - **Player 2D** — capsule-collider character with animated sprite from the Sprite Editor (sheet + clip pickers, FPS/loop/speed respected), idle ↔ walk clip auto-switching while moving, sprite mirrors when facing left, sprite previews live in edit mode
  - **Start Marker** — player spawn point; Player2D spawns there in preview/in-game
  - **Player Physics** — gravity + capsule-vs-collision-tile resolution (ground/ceiling) active in preview/in-game only
  - **Checkpoints** — "Save Checkpoint" trigger records the player position; "Load Checkpoint" teleports back (fallback = start marker); pit-death respawn prefers the checkpoint; resets each play session
  - **Player Spawn** — draggable spawn marker; GameScene places the player at the map's spawn on scene enter
  - **Gizmo Frontmost** — transform gizmo always renders above the 2D grid/overlays
  - **Per-Map Camera Start** — saved view restored on Play in Preview; ortho/front locked for 2D levels
  - **Persistence** — maps saved to `Assets/Maps/*.tilemap.json` + scene `.ing`, autoloaded on project open; editor aids (grid/collision/spawn gizmos) auto-hidden in-game
  - **Player Info Panel & Stats** — live monitor/editor of player status (menu *View → Player Info*); see [Player Stats Reference](#player-stats-reference) below
  - **UI Bar Component** — progress/status bar built from 3 images (Background frame / Empty interior / Progress fill, plus the element's own Image as back layer) with fill direction, frame inset, and **Stat Binding**: pick Health/Mana/Level/Experience/Fitness and the bar mirrors `Player2DStats` live in editor preview and in-game; persisted per scene
- **IDE Settings Panel** — VSync, MSAA antialiasing, debug grid, font sizes with instant apply

## Player Stats Reference

The player's status is stored in `Player2DStats` (one live instance, single player). The **Player Info** panel shows and edits every value in real time — while playing, bars bound to a stat update the same frame. Values reset to defaults at each play session.

### Primary (vital) stats — shown in the Player Info panel and bindable to UI Bars

| Stat | Slot name | Default | What it represents |
|---|---|---|---|
| HP (Hit Points) | `Health` | 100 / 100 | How many hits it takes to knock the character out. Higher HP absorbs more or stronger hits.|
| MP (Magic Points) | `Mana` | 50 / 50 | Fuel for spells and special actions. Each action has a cost determining how often it can be used; replenished by items, zones, or rest.|
| Level | `Level` | 1 (max 99) | Overall character progression.|
| EXP (Experience) | `Experience` | 0 / 100 | Progress toward the next level.|
| Fitness | `Fitness` | 100 / 100 | Endurance/stamina — how long the character can exert before tiring.|

### Core attributes (classic tabletop six)

| Attribute | Governs |
|---|---|
| **Strength** | Pushing, pulling, lifting, climbing, and anything physical. In combat: weapon damage, and accuracy of short-ranged weapons.|
| **Dexterity** | Quickness and nimbleness. Combat: turn order, ranged accuracy (sometimes power), dodging. Out of battle: run speed, picking locks, pick-pocketing.|
| **Constitution** | Durability: resisting poison, endurance, and the character's HP pool.|
| **Intelligence** | Knowledge and spell effectiveness depending on character type.|
| **Wisdom** | Applied knowledge: how well spells are used and defense against spells.|
| **Charisma** | Communication and influence over other characters (and, per character, possibly usable spells).|

### Derived combat stats (video-game RPG layer)

| Stat | Effect |
|---|---|
| **Attack / Magic Attack** | Damage of weapon-based vs magical actions.|
| **Defense / Magic Defense** | Reduce incoming physical vs magic damage, e.g. `total attack − total defense = final damage` (exact formula varies per game — some divide instead of subtracting).|
| **Speed** | Turn order — faster characters usually act first; may also affect accuracy, evasion, or hit counts.|
| **Evasion** | Lowers the odds an incoming attack hits (not present in every game).|
| **Accuracy** | Raises the odds the user's attack lands (not present in every game).|
| **Critical** | Improves the odds of bonus damage on certain attacks.|
| **Luck** | May nudge accuracy, evasion, critical chance, or resist enemy actions (rare in modern games).|

> Only the primary stats above are implemented and bindable today (`Player2DStats.Health/Mana/Level/Experience/Fitness`); the attribute and derived tables are the design reference the runtime stats grow into — gameplay systems (damage formulas, dodge rolls, level-ups) consume them through the same named slots.

## Tech Stack

- C# (.NET 9)
- OpenGL 4.x via raw bindings
- GLFW (windowing/input)
- ImGui.NET (editor UI)
- StbImageSharp (texture loading)
- System.Text.Json (serialization)

