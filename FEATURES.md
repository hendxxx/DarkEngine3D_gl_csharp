# DarkEngine3D — Functional Report

> **Purpose**: Complete technical reference for reproducing this OpenGL/C# game engine.
> **Tech Stack**: C# (.NET 9), OpenGL 4.x via raw bindings, GLFW, ImGui (dear imgui), StbImageSharp, System.Text.Json
> **3D Formats**: glTF 2.0 (.gltf/.glb)

---

## 1. Architecture Overview

```
DarkEngine3D_gl_csharp/
├── Engine/
│   ├── Config/          — Settings persistence (PostFX, Shadows, Fog, Quality)
│   ├── Helpers/         — Path resolution, JSON converters
│   ├── IDE/             — ImGui editor (panels, menus, shortcuts)
│   │   └── Panels/      — Inspector, Hierarchy, SceneView, AssetBrowser, etc.
│   ├── Inputs/          — GLFW keyboard/mouse input wrappers
│   ├── Libs/            — OpenGL loader, GLFW bindings, Shader compiler, Texture utils
│   ├── Objects/          — EditorObject, Object3D, TerrainMesh, GLB/GLTF loaders
│   ├── Project/         — ProjectManager, .projing file handling
│   ├── Scene/            — Scene management, Save/Load (.ing JSON format)
│   ├── Terrains/         — TerrainChunk, MapLoader, Noise generation
│   └── Visual/           — Camera, CSM, Lights, Skybox, PostFX, Billboard, HUD
├── Artifacts/
│   ├── shaders/          — 40 GLSL shaders (vertex, fragment, geometry)
│   ├── Textures/         — Default textures
│   └── fonts/            — IDE fonts (copied to project on create)
└── bin/Debug/net9.0/     — Build output (exe runs from here)

Project Folder (user-created):
ProjectRoot/
├── {Name}.projing         — Project metadata+ scene inventory
├── settings.json          — Per-project engine settings
├── shadow_presets.json    — Per-project shadow presets
├── imgui.ini              — Per-project IDE layout
├── recent_files.json      — Per-project recent scenes
├── game.ing               — Combined scene file (auto-loaded on open)
├── saves/                 — Save game slots (per-project)
│   └── slot_N/
├── Assets/
│   ├── fonts/             — Copied from exe on project create
│   ├── images/
│   ├── models/
│   └── Maps/              — Heightmaps
└── Scenes/                — Individual scene files (.ing)
```

### Core Classes

| Class | File | Purpose |
|-------|------|---------|
| `IDE` | `IDE.cs` | Main editor loop, ImGui rendering, menu bar |
| `GameScene` | `GameScene.cs` | Runtime scene rendering |
| `EditorObject` | `EditorObject.cs` | All editor objects (Box, Sphere, Plane, Camera, Light, Sky, GLB) |
| `EditorTerrainMesh` | `EditorTerrainMesh.cs` | Terrain rendering (heightmap, layers, PBR, painting) |
| `Object3D` | `Object3D.cs` | GPU mesh representation (VAO/VBO/EBO) |
| `SceneManager` | `SceneManager.cs` | Scene loading/switching |
| `PostProcessStack` | `PostProcessStack.cs` | MSAA + PostFX pipeline |
| `PostFxProcessor` | `PostFxProcessor.cs` | Bloom + Auto-exposure + Tonemapping |
| `CSM` | `CSM.cs` | Cascaded Shadow Maps (3 cascades) |
| `LocalLightShadow` | `LocalLightShadow.cs` | Point (cube map) + Spot (projective) shadows |
| `Camera` | `Camera.cs` | Editor + game camera (FPS/Orbit) |
| `Lights` | `Lights.cs` | Sun + local point/spot lights |

---

## 2. IDE Features (ImGui)

### 2.1 Menu Bar

| Menu | Items | Shortcuts |
|------|-------|-----------|
| **File** | New Scene, Open Scene, Open Recent (Ctrl+F1-F10), Save All, Save As, Exit | Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S |
| **Model** | Add Plane/Box/Sphere/Camera/Light/Sky, Add GLB Reference, Gizmo modes | Ctrl+1..6 |
| **Edit** | Undo/Redo, Cut/Copy/Paste, Duplicate, Delete | Ctrl+Z/Y/X/C/V/D, Del |
| **View** | IDE Mode, In-Game Mode, Preview Mode, Snap to Grid, Viewport Shadows, IDE Font, Viewport Font | F5, F8 |
| **Help** | About | — |

### 2.2 Panels

| Panel | Purpose | Key Features |
|-------|---------|--------------|
| **SceneView** | 3D viewport | Gizmo translate/rotate/scale, selection highlight, grid |
| **Hierarchy** | Object tree | Add/delete/rename objects, drag reorder |
| **Inspector** | Property editor | Context-sensitive per object type (see §2.6) |
| **AssetBrowser** | File browser | Drag-drop images/models to Inspector |
| **Console** | Log output | Stdout/stderr capture |
| **SceneManager** | Scene list | Add/remove/switch scenes, save/load, scene rename sync |
| **ShadowSettings** | Shadow config | CSM bias/blend, local light shadows |
| **PostFxPanel** | Post-processing | Reactive bloom (mip chain), auto-exposure, tonemapping, gamma, DoF |
| **FrameBufferDebugPanel** | Debug | Live thumbnails of every FBO: scene targets, DoF scratch/mask, Post FX stages (composite/output/luma/bloom mips) + completeness validation |
| **TerrainBrush** | Terrain tools | Brush size/strength/softness, paint layers, sculpt |
| **PbrPanel** | PBR material | Per-object texture slots + tuning + PBR Splat Terrain (4-layer paint, height sculpt, LOD/occlusion, height layers — see §5.5) |
| **RenderTime** | Performance | FPS, frame time, GPU timing |
| **FramebufferViewer** | Debug | View any FBO texture |
| **SpriteEditor** | 2D sprite sheets | Auto-detect frames, interactive slicing, animation clips, zoom, drag-drop import (see §2.4) |
| **MapEditor** | 2D tilemap editor | Tile painting, layers, palette, collision flags, parallax layers (see §2.5) |
| **CollisionEditor** | 2D collision shapes | Shape list + per-shape editing |
| **IDESettings** | Editor settings | VSync, MSAA antialiasing, debug grid, font sizes — changes apply instantly |

### 2.2.1 UI Editor — Container & Selection Features

| Feature | Description |
|---------|-------------|
| **Nested Container Selection** | Alt+Click on any child to select nearest parent Container |
| **Scrollable Containers** | Container clips children to bounds, scrollbar when content exceeds height |
| **Nested Scroll Propagation** | Parent scroll offset propagates to nested containers correctly |
| **Clip Bounds Hit Test** | Elements outside container visible area cannot be hovered/clicked |
| **Multi-Select Group Drag** | Ctrl+Click to multi-select, drag yellow bounding box to move all together |
| **Container Selection Rules** | When multi-select active, parent Container cannot be selected (preserves group) |
| **Marquee Selection** | Rubber-band selection for multiple UI elements |
| **GroupMove with Undo** | Group drag records undo per-element for single Ctrl+Z restore |

### 2.4 Sprite Editor (2D)

Panel for slicing sprite sheets and building 2D animation clips.

| Feature | Description |
|---------|-------------|
| **Sheet Import** | File dialog or drag-drop image from Asset Browser |
| **Auto-Detect Frames** | Guesses frame size/columns from image dimensions; preview grid overlays the sheet |
| **Interactive Mode** | Click-drag on the sheet to define frame rect manually (no numeric input needed) |
| **Zoom** | Independent zoom for sheet preview and animation preview (default 1x) |
| **Animation Clips** | Create clip from frame range (Start/End), FPS control, play/stop preview |
| **Frame Thumbnails** | Selected frame image shown in Frame Properties |
| **Persistence** | All sheets + clips saved to `Assets/Sprites/sprites.sheets.json`; **auto-loaded on project open** via `IDE.OnProjectChanged(projectRoot)` → `SpriteEditorPanel.OnProjectChanged(projectRoot)` (canonical `sprites.sheets.json` only — `*-sprite-anim.json` pattern exports are NOT loaded here; they are only merged from the Sprite Editor "Auto Load" button / Load dialog); also available via the Sprite Editor "Auto Load" toolbar button; project close clears tracker sets + resets pattern-action defaults |

| **Sheet Sections** | The sheet list is grouped into 4 collapsible sections — **Base / FX / Base Equipment / Items** — via a per-sheet `Category` persisted in sprites.sheets.json (files without the field load as Base; hand-edited unknown values fall back to Base instead of killing the file parse). A category combo above the list re-categorizes the whole current selection; with nothing selected it sets the section new imports land in (Import / drag-and-drop / pattern exports). Sections are display-only — the master list and indexes never change, so undo/delete keep working |
| **Sheet Multi-Select** | Ctrl+Click toggles sheets into the selection, Shift+Click extends a range from the previous primary, plain click selects one. The primary selection still drives Sheet Settings + previews; **Delete Sheet** removes EVERY selected sheet (highest index first), and the category combo re-categorizes all of them at once — bulk-organize an imported folder into sections in two clicks |
| **Save As JSON** | Toolbar `Save As JSON`: writes sheets + clips JSON to ANY path via save dialog (starts in `Assets/Sprites`, filter `*.json`, indented JSON + binary sidecar cache). Load dialog accepts both `*.sheets.json` and generated `*-sprite-anim.json` |
| **Save As Pattern** | Batch auto-clip per folder: copies the selected sheet's pattern (grid/padding/offset + **flip X/Y** + master box + render offsets) to EVERY image file in the same folder (png/jpg/jpeg/bmp/tga). Action names & count come from the free-text **`Pattern actions`** field (pipe-separated, default `idle|walk|run|jump start|jump end|attack|dead`, persisted in the save file; e.g. `effect` → a 1-row sheet saves one `<sheet>-effect` clip): one clip per ROW, rows past the action list are NOT saved. Empty frames (all alpha 0, or rect outside the image — JPEG has no alpha so never empty) are skipped, fully-empty rows produce no clip; **FPS = frame count** (1s per clip); clip name `<sheet>-<action>`; loop decided BY ACTION NAME — `jump start/jump end/attack/dead` play once, everything else (incl. custom names like `effect`) loops. Output per file: `<file name>-sprite-anim.json` next to the image (same `SpriteSheetsSaveData` schema → loadable via Load) |

| **Pattern Export Auto-Load** | `Sprite Sheets` + `Animation Clips` auto-merge every `*-sprite-anim.json` (project `Assets` tree + each sheet's image folder) from the Sprite Editor "Auto Load" button / Load dialog and right after Save As Pattern — the SHEET entry and its clips both appear with no manual Load. Project open does NOT trigger this merge. Refresh-in-place: the previous auto-loaded set is replaced on re-run — tracked **BY INSTANCE** (a manual sheet/clip sharing a name is never removed or duplicated); files are enumerated before removal so a folder reachable only through an auto-loaded sheet's image path stays reachable; indicator line shows `(N sheets + M clips auto-loaded from *-sprite-anim.json)`.

| **Play Integration** | Play in Preview loads the selected animation clip |

### 2.5 Map Editor (2D Tilemap)

Tilemap editor rendering into the 3D viewport as an upright textured plane (`EditorObject` type `Map2D`). The grid appears at world origin; camera auto-switches to orthographic front view.

| Feature | Description |
|---------|-------------|
| **New/Resize Map** | Grid of empty tiles shown immediately in viewport; GameScene type enforced (warning otherwise). Resize = modal (Width/Height tiles): every layer's grid grows/shrinks preserving tiles at their grid indices (top-left anchor); trigger areas+ player spawn clamped into the new bounds; viewport mesh rebakes automatically (cache key includes W/H) |
| **Tile Palette** | Auto-detected cols/rows from tileset image (read-only); multi-select (marquee) preserves block shape when stamping |
| **Tools** | Paint, Erase (with brush size), Fill (flood), Pick (default), Collision, Trigger — paint directly in the 3D viewport |
| **Layers** | Multiple tile layers, visibility/lock per layer, all visible layers render (stacked in depth, tiny lift per layer) |
| **Undo/Redo** | Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y over the 2D level (per-tile granularity). Object-level undo covers editor objects (add / delete / duplicate — delete keeps the instance alive so undo restores the SAME object with its GPU state) and trigger areas (create / delete / cut / paste / duplicate), all through the Hierarchy panel's shared history |
| **Sprite Repeat Width** | `Sprite2DWorldWidth` (Inspector → Sprite 2D → Width, 0 = off): tiles the animated frame across a world width — animated water surfaces, hedges, fences. Each repeat is a full frame quad sharing the ONE clip clock (whole strip animates in lockstep); strip centered on Position.X, base on Position.Y; UNBOUNDED width (DragFloat, no upper limit) — all tiles batch into ONE draw call (chunked vertex flush), so thousands of tiles stay one draw; DoF sprite-shape mask carves every tile |
| **Duplicate (Ctrl+D)** | Deep-copies the object following the FULL .ing save contract — sprite sheet/clip+ all Sprite2D/Player2D physics & render props, animation actions (deep clone incl. projectile block), NPC binding+ badge+ alert image, camera follow, Effect2D emitter, glow, render layers, equipment slots, tilemap payload (deep clone)+ parallax layers (TextureId excluded — rebuilds lazily), PBR/terrain/sculpt/splat state |
| **Collision Flags** | Per-tile-id collision toggles; dedicated Collision paint tool (click/drag toggles, hover preview shows add vs remove); full 3D translucent boxes with bright edges CENTERED on the tile; per-layer `CollisionTileIds` persist in the tilemap json; Show Collision checkbox persisted in settings |
| **Parallax Layers** | Image layers behind/in front of the grid: ScrollFactor, ZPosition, Alpha (per-vertex tint), WidthPx/HeightPx (0 = proportional to image aspect), RepeatX/Y (GL_REPEAT), TopPx offset, TileHorizontal wrapping |
| **Parallax Preview** | Panning the editor camera slides each layer by `-camX × ScrollFactor` (fractional UV phase = seamless wrap) |
| **Grid Overlay** | Show/hide tile grid+ grid color, real-time; auto-hidden in preview/in-game |
| **Camera Start** | Per-map saved view; "Set Current View"/"Reset" in Grid Settings; Play in Preview restores it; auto-captured on first framing |
| **Player Spawn** | Draggable cyan cross marker in viewport (or "Set at Hover"); GameScene places the player there on Enter (unless a save slot loads) |
| **Save/Load** | `Assets/Maps/{Name}.tilemap.json` (carries tiles+ parallax+ spawn+ camera start) and canonical scene `.ing`; autoload first map on project open |
| **Trigger Areas** | Dedicated Trigger tool: click-drag on the grid draws a snap-to-tile box; drag body to move, 8 handles to resize; Delete / Ctrl+C/X/V / Ctrl+D supported; amber translucent boxes (selected = brighter+ white handles); edited in the Trigger Areas panel (see §2.9) |
| **Portals** | Portal / Portal One Way trigger actions render a 4-state sprite animation (NotActive/Active/Enter/Out clips from one Sprite Editor sheet, visual size px overrides the area size). Sheet with NO authored clips falls back to its implicit frame grid (8 FPS) — an authored portal never renders invisible. Arrival spawns at the destination portal center ± ONE PORTAL WIDTH (side = player travel direction)+ arrival-inside guard (auto portals only), so two-way pairs don't ping-pong. Button-mode portals (PortalAutoEnter off) show a `[KEY]` badge above the portal while the player stands inside |
| **In-Game Parity** | Parallax layers/textures sync every frame in preview mode; startup `-load=` in-game re-anchors camera (lazy reframe when tilemap adopts late) |

### 2.6 Player2D System (2D Character)

Player character object with capsule collider+ animated sprite, spawned from a Start marker.

| Feature | Description |
|---------|-------------|
| **Player 2D Object** | `EditorPrimitiveType.Player2D` — added from the Hierarchy toolbar or Add-element dropdown (🏃 icon) |
| **Capsule Collider** | Feet-anchored capsule (Position.Y = capsule bottom), radius/height/visibility tunable in Inspector; rendered as a world-upright blue outline in edit mode (hidden in-game via `Editor2DAidsHidden`) |
| **Animated Sprite** | Sprite Sheet+ Animation Clip pickers (from the Sprite Editor via the static `IDEBridge` registry, refreshed every frame in all modes); clip FPS/loop/reverse/speed respected; animation previews live in edit mode |
| **Sprite UV** | Frame UVs converted (`v' = 1 − v_raw`) for the top-row-first texture upload — sprite stands upright |
| **Start Object** | `EditorPrimitiveType.Start2D` (🚩 arrow marker) — the spawn point; Player2D teleports there (velocity+ anim clock reset) when in-game mode begins |
| **Deferred Spawn** | `EditorObject.Player2DSpawnPending` static flag set on in-game entry; consumed by `Player2DSystem.Update` on the first frame AFTER the .ing reload re-creates objects |
| **Physics** | `Player2DSystem` — gravity (tunable per-player)+ capsule-AABB vs collision-tile resolution (ground/ceiling on the active layer); runs only in preview/in-game |
| **Persistence** | Sheet/clip/height/capsule/gravity saved in the scene `.ing` via `EditorObjectData` |
| **Equipment Sprite Layers** | Composite equipped art over Player2D/Sprite2D: paperdoll slot → item with Equip Art, ordered by EquipLayer, frame-synced with the base sprite. **Direct layers (no item required)**: Inspector → Equipment Layers → `+ Add layer` attaches a Sheet/Clip straight to the object — no Item Editor registration; merged with paperdoll items (stable order: same layer = item first, then direct), empty-Sheet rows skipped; edited per layer (Layer/Offset X,Y/Height/Frame sync, Remove); persisted in scene `.ing` via `EditorObjectData.DirectEquipLayers` (save+ editor load+ runtime load+ Duplicate) and rendered in editor preview AND in-game |
| **Equipment Auto Clip Match** | Synced layers (Frame sync ON) automatically follow EVERY base animation, not just one authored clip: while the character plays clip X, the overlay finds the clip on its own sheet named X or `&lt;anything&gt;-X` (action-token match — `baju-run` follows base `run` and pattern-style `hero-run`) and samples the same POSITION within that clip, so a clothing sheet may use a different grid layout / frame count than the character (shorter clips freeze on their last frame). Outfits authored with Save As Pattern (`&lt;sheet&gt;-idle/-walk/-run/...`) need no per-action wiring. No matching clip → the old same-grid behavior (authored clip+ raw sheet-frame sync); Frame sync OFF keeps the independent-FPS mode for auras/fire |
| **Render Layer vs Tilemap Z** | Tilemap planes live in a sub-character band (`Map2dLayerIndex × 0.001` — always below Z≈0.05 where characters start). A Player2D/Sprite2D with **Render Layer ≥ 1 draws in front of the tilemap**; negative layers draw behind it. The layer index is NEVER mapped 1:1 to world Z (the old z=index put a layer-1 map at z=1.0, covering the character's sword even with the render layer raised) |

### 2.7 Gizmo Z-Order

The transform gizmo is ALWAYS frontmost: 2D overlays (tile grid, hover highlight, collision box edges) draw with depth test disabled and would paint over an earlier gizmo, so the depth buffer is cleared after `EditorObjectManager.Draw()` and before `gizmo.Render()` in both the editor (no-scene) and in-scene render paths.

### 2.8 Inspector — Object Types

**Box/Sphere/Plane**:
- Transform: Position (XYZ), Rotation (Euler XYZ), Scale (XYZ)
- Pivot Position
- Color (RGB picker)
- Cast Shadow checkbox
- Texture slot (albedo)+ Texture Settings (filter, wrap, mip, anisotropy)
- PBR Maps: Normal, Metallic, Roughness, AO, Height, Emission texture slots
- PBR Tuning: All parameters (see §5)

**Camera**:
- FOV, Near Clip, Far Clip

**Light**:
- Type: Directional / Point / Spot
- Direction (Euler), Color, Intensity, Range
- Spot: Cone angle (inner/outer)
- Cast Shadow toggle

**Sky**:
- Turbulence, Sun Size/Intensity
- Sun Direction (Euler)
- Cloud layer (enable, scale, speed, coverage)

**GLB Reference**:
- Model path, Position, Rotation, Scale
- Cast Shadow toggle
- PBR Maps+ Tuning (same as primitives)

**Map2D (2D Level)**:
- Tilemap binding (auto-created from Map Editor "New Map" / scene `.ing` restore)
- Tileset cols/rows+ FlipV (read-only, auto-detected)
- Show Grid+ Grid Color, Show Collision+ Collision Color
- Grid Settings: camera start capture/reset, spawn info
- Rendered as upright world-space plane; object transform intentionally not applied

**Player 2D**:
- Sprite Sheet+ Animation Clip combos (auto-select first item; clip summary shown when resolvable)
- Sprite Height, Capsule Radius/Height, Show Capsule, Gravity
- Glow (bloom) slider+ Glow Tint picker+ Flicker checkbox (see §2.10)
- Selection shows via line gizmos (no stencil outline — no solid mesh)

### 2.9 Trigger Area / Event System

Non-blocking event volumes on the tilemap: the player passes through them; entering/staying/exiting fires an ordered list of actions.

| Feature | Description |
|---------|-------------|
| **Trigger Area** | Rectangle in grid pixel coords (LeftPx/TopPx/WidthPx/HeightPx) on the tilemap; no physical collision — detection only |
| **Conditions** | On Enter / On Stay (with repeat interval in seconds) / On Exit; optional gate "only when moving right" |
| **Actions (18 types)** | Save Game, Save Checkpoint, Load Checkpoint, Change Map, Play Sound, Play Music, Spawn Effect, Spawn Object, Start Dialogue, Show Bubble, Hide Bubble, Start Cutscene, Camera Shake, Unlock Door, Give Item, Activate Quest, Complete Quest, Run Script — each with Param/Param2/Delay |
| **Wired Runtime** | Save Game (next empty slot), Save Checkpoint (records player position), Load Checkpoint (teleport to checkpoint, fallback = start point), Change Map (loads `Assets/Maps/{name}.tilemap.json` in place), Camera Shake (earthquake-style, intensity × duration), Start Dialogue (opens the Dialogue System conversation), Show/Hide Bubble (player-following speech bubble; Param2 = `type\|duration`) |
| **Checkpoint Chain** | Save Checkpoint → player position stored for the session; Load Checkpoint zeroed velocity+ grounded reset; pit-death respawn prefers the checkpoint; checkpoint state resets on each new play session |
| **Camera Shake** | View-height-relative amplitude (7% × intensity), 3-layer noise+ ±1.2° camera roll, quadratic decay, random seed per shake |
| **Visual Editor** | Trigger tool: drag-create (snaps to tile bounds), move by dragging the body, resize via 8 handles, Delete/Ctrl+C/X/V/Ctrl+D; "Show Triggers" checkbox (persisted); Trigger Areas panel: list+ rename+ enable, conditions, per-action editor with contextual params+ reorder, precise geometry |
| **Rendering** | Amber translucent boxes drawn in edit mode only (hidden in-game via `Editor2DAidsHidden`); overlay projected via `SceneToScreen` so it sticks to the viewport image |
| **Persistence** | `Assets/Maps/{map}.tilemap.json` AND scene `.ing` (`Tilemap2DData.TriggerAreas`) — triggers load with the project |

### 2.9.1 Projectile Impact — Hit CLIP First (User Decision)

The user's rule: a projectile impact plays the **Hit Clip animation**, not particle FX. Particles are opt-in and sizeable.

| Feature | Description |
|---------|-------------|
| **Hit Clip+ Hit Scale** | `Player2DAction.ProjectileHitSheet/Clip/Scale` — impact sprite plays once at the hit point; `Hit Scale` multiplies its size (1 = projectile's own World Height, 0.5 = half, 2 = double). Impact height = `WorldHeight × HitScale` |
| **Hit FX opt-in** | `ProjectileHitFx` default changed from "Explosion" to **"" (none)** — old scenes that saved "Explosion" keep their particles; new projectiles are clean unless the user picks a preset |
| **Hit FX Scale** | `ProjectileHitFxScale` (0.05–8) — multiplies the optional burst. `SpawnBurst` scale now grows **BOTH count AND particle sizes** (`WithSizeScale`: SizeMin/SizeMax/AlignVelScale/Grow × scale) — previously scale only multiplied the count, so the sliders never changed how big the FX looked |
| **Action FX Scale** | `Player2DAction.FxScale` — sizes a character action's own FX: burst = count+size (SpawnBurst), follow stream = per-particle size (`WithSizeScale` applied in Player2DSystem's follow-FX emitter) |
| **Effects Panel** | New dedicated panel (2D Sidescroller menu): per-action Hit Sheet/Clip/Scale, Hit FX+ FX Scale, Action FX Preset/Follow/Scale, Effect2D emitter overrides (preset/rate/wind/offset/enable), and the Global Weather section below. The Inspector's "Hit FX" combo was removed → replaced by a pointer to this panel |
| **Weather Presets** | One-click scene weather in the Effects panel: **Clear Day** (all off, faint warm tint), **Sunset** (warm orange wash), **Rain** (rain+ ground fog+ cool tint), **Storm** (heavy rain, thick fog, strong wind, gloomy tint), **Snow** (snowfall+ light fog+ cold tint), **Fog** (dense ground fog only)+ **Clear** (all layers off). Presets map to `Effect2DSystem.ApplyWeatherPreset` |
| **Weather Layers** | Manual per-layer control under the presets: Rain (intensity **0–100** — 0.7 = sparse reference look, 100 = storm wall capped by the 4000-particle pool; wind slant; **Offset X/Y** — shifts the spawn area; **Size** — drop size; SPARSE vertical dashes, camera-relative sizing; **leans with the wind**: each dash rotates toward the CURRENT velocity every frame (`LeanToVel`, θ = atan2(−vx, −vy) for the SizeY long axis) — wind direction+ strength set the tilt, the longer the fall the more it slants (advection), terminal cap 2×|WindX·wind|+1; **ground splashes**: every drop hitting ground collision OR A SPRITE'S HEAD spawns a small 3-point fan splash (left/center/right) with return gravity — visible but subtle); **surface = max(ground collision, sprite top)** — rain & snow also hit Player2D/Sprite2D via `SpriteTopYAt` (live quads from TryGet*DrawData: offset+ custom frame+ mirror included), Snow (intensity **0–100**, **Offset X/Y**, **Size**, pure opaque white, **falls all the way to the ground** — dies ON ground collision via GroundKill, now actually executed; life = a SAFETY NET only (180 s — the old 3/30/8 s timers killed particles MID-AIR before reaching the ground at tall viewports — user: "the default must fall to the ground"); **SNOW REST**: flakes hitting an obstacle/collision tile STAY on that surface for `SnowRestSeconds` (a 0–20 s slider, default 6; velocity freezes+ fades in place, 0 = vanish immediately) — snow physics: fall → contact (a tile OR a sprite's head) → rest → fade; **Weather surface FINAL v2 (user: "the tent should have splashes" — the tent is a decor tile)**: a **collision** tile = a full box (all layers/maps); a **decor** tile = a **PER-PIXEL TILESET OUTLINE** (`GetTileTopProfile`: a per-pixel-column top profile from the tileset image, cached per tileId — a slanted tent roof splashes along its shape, transparent areas are passed through; the old any-tile-box rule is NOT coming back)+ **per-pixel sprite alpha** (`SpriteHitAt`, every Player2D/Sprite2D in the scene)+ **ALL MAPS** (`_surfaceMaps`, the active map first). The column surface = the highest hit, Fog (atmospheric cloud FRONT — see below), Tint (full-view ambient wash), Wind X (**the spawn band is WIDENED+ SHIFTED upwind** by the computed drift → still 1 full screen at any wind — user: "x=5 add an offset"; snow gets a horizontal terminal `0.4+0.75|WindX|` so it doesn't fly away)+ **CAMERA-LEAD**: the camera velocity (smoothed, `TrackCameraTravel`) widens & shifts the band toward the movement by the camera's travel during the fall time — walking/running used to open rain holes on the rear side (particles landed where the camera USED to be — user: "a hole on the bottom left"). Rain/snow/splash sizes are CAMERA-RELATIVE (fraction of view height — fixed world sizes were sub-pixel at far zoom and invisible). Session state; resets on project close/session start |
| **Fog Front Layer (reference look)** | Fog = a FULL atmospheric cloud layer at the **VERY FRONT** (user revision: "the fog should appear at the very front" — the backdrop-behind version was REJECTED): a translucent dark wash+ 3 rows of cyan/blue billows (brighter toward the bottom) drifting+ following WindX (a seamless wrap)+ a horizon bloom near the bottom — the world (tiles/player/particles) stays VISIBLE through its alpha. Drawn by `RenderFogFront(camera)` at the end of `EditorObjectManager.Draw`'s 2D pass (depth off, before the editor gizmos so the design tools stay crisp). The active Tint steers the cloud color (Sunset = warm clouds, Snow = pale clouds) |
| **Weather Overlay Rendering** | `Effect2DSystem.RenderWeatherOverlay(camera)` runs in the 2D pass AFTER projectiles — tint = full-view `TexSquare` quad (hard-edged = uniform alpha to every corner; a soft blob texture would vignette). Static 54-float scratch buffer (no stackalloc in loops — CA2014; Span locals cannot be captured by local functions — CS8175) |
| **Persistence** | New fields persist ×4 sites: `Player2DActionData` (SceneAsset), save ×2 (SceneManagerPanel), load (SceneManagerPanel+ LoadingScene) |

### 2.10 Per-Sprite Glow (Emissive Post-FX)

Per-sprite emissive boost so only bright sprite pixels (fire, lava, candles) cross the bloom threshold — the rest of the sprite and scene stay normal. Requires Post FX enabled.

| Feature | Description |
|---------|-------------|
| **Glow (bloom)** | `Sprite2DGlow` / `Player2DGlow` (0-1) — vertex color multiplied ×1..×4; bright pixels bloom, dark pixels untouched |
| **Glow Tint** | `Sprite2DGlowColor` / `Player2DGlowColor` — colors the bloom (e.g. blue fire); brightest channel normalized to 1 at render, so any brightness of the hue works; white = natural colors |
| **Flicker** | `Sprite2DGlowFlicker` / `Player2DGlowFlicker` — organic fire breathing: 3 out-of-phase sine layers over absolute engine time (±22% around the Glow value), per-object seed from the name hash (two fires never sync), frame-gated via `Glfw.FrameId` for multi-pass consistency |
| **Baked to Vertices** | Boost/tint/flicker multiply the per-vertex tint (`aTint`) — the map2d shader has no tint uniform by design |
| **Inspector** | "Glow (bloom)" slider+ "Glow Tint" picker+ "Flicker" checkbox in both the Sprite2D and Player2D sections |
| **Persistence** | Saved in the scene `.ing` via `EditorObjectData` (Glow+ GlowColorX/Y/Z+ Flicker), symmetric save/load |

### 2.11 Dialogue System (Conversation+ Bubble)

Data-driven dialogue: RPG conversation window+ world-space speech bubbles, authored entirely in the editor.

| Feature | Description |
|---------|-------------|
| **Dialogue Assets** | `DialogueAsset` (Id, Name, StartNodeId, ThemeName) with `DialogueNode` pages — text, speaker, portrait, emotion, choices, auto-advance, per-node start/end actions; branching via NextNodeId / choice targets (empty = end). Stored in `Assets/Dialogue/dialogues.json` |
| **Dialogue Editor** | Panel (2D Sidescroller menu): asset list+ add/duplicate/delete, node editor (speaker/emotion/portrait/text/next/choices/actions), speaker manager, theme editor (colors, fonts, typewriter speed), language dropdown+ translation table, ▶ Start/■ Stop preview |
| **Conversation Runtime** | `DialogueSystem` — bottom window with portrait frame, colored speaker name, typewriter text, numbered choices (1-9 / arrows+ E / Space next / Esc close); closes with a fade; freezes player input+ editor camera via the `DialogueOverlay` modal gate |
| **Speakers** | Reusable `SpeakerData` (Id, Name, portrait, name color); portraits support emotion variants (`<name>_Happy.png` tried before the base) |
| **Themes** | `DialogueThemeData` with optional parent inheritance — window/border/name/text/choice/bubble colors, fonts, typewriter speed, optional background image; built-in Default+ Medieval |
| **Bubbles** | `ShowBubble/HideBubble` follow any object (player/NPC) with fade in/out, auto-hide on duration/distance, type-tinted borders (Speech/Thought/Quest/Warning); also fired from trigger actions |
| **NPC Interaction** | `EditorObject.NpcDialogueId` (Inspector: NPC Dialogue section) — Sprite2D/Player2D with a dialogue id shows an "[E] Talk" prompt in range and starts the conversation on E. Per-NPC badge customization: `NpcDisplayName` (white name line drawn above the badge — empty = Hierarchy name), `NpcBadgeSize` (badge text size in screen px, default 15 — also scales the alert image ×2), `NpcBadgeLift` (badge float height above the VISUAL head top in world units, default 0.15 — snug; badges anchor via `BadgeHeadHeight`, not the bubble margin); all persisted with the NPC binding and shared by the HUD (GameScene) and ImGui-overlay (preview) render paths |
| **HUD Z-Index (panels are top layer)** | Open UI panels (inventory `[I]`, shop, quest log) hide ALL world-anchored markers — NPC name badges, "!" indicators, "[E] Talk" prompts, speech bubbles, portal/interact-key badges. Two mechanisms: (1) flag gate `DialogueSystem.PanelUiOpen` (`InventoryHud.PanelOpen \|\| ShopHud.IsOpen \|\| QuestHud.LogOpen`) checked in both render paths, because the panels queue their occluder rects into the SHARED HUD only AFTER `DialogueSystem.Tick` ran (same frame, different HUD instance) and `Flush` purges last frame's list before Tick ever sees it — the point probe alone never fired on the GameScene path; (2) full-extent occluder probe `HUD.PanelCoversRect(x,y,w,h)` (rect overlap vs the per-frame `DrawSolidBox` occluder list) replacing the anchor-point-only `PointInPanelOccluder` for every marker — a badge name line+ plate tower ~30–80px ABOVE its anchor point, so a point-only test let the marker's upper half punch through the parchment ("Agus/Budi" on top of the open inventory) |
| **HUD Mouse Accuracy (panel hover/click)** | Every inventory/shop hover+ click test reads the cursor mapped through `InventoryHud/ShopHud.WindowToScene`. Two fixes: (1) cursor SOURCE is `ImGui.GetIO().MousePos` (screen space — the same space the ViewportPanel letterbox rect `_imageMin/_imageSize` lives in), not the raw GLFW cursor: the two DIVERGE on Windows display scaling ≠ 100% (OS screen units vs framebuffer px), offsetting every hover/click by the scale factor; GLFW is only the fallback outside an ImGui frame. (2) The F8 fullscreen path now publishes its own `WindowToScene` every frame in `IDE.RenderInGameMode` (scene-image letterbox inverse, same fit math as the displayed image) — F8 early-returns before `ViewportPanel.Render`, so the panel's per-frame publisher never ran there and a STALE letterbox lambda from the last docked frame kept mapping clicks through an outdated rect (hover landed on the wrong slot); no scene texture → mapper set null (identity) |
| **HUD Text Mirror (F8 fullscreen+ flush order)** | `HUD.DrawText` mirrors every string into `HUD.FrameTextOut`, drawn by an ImGui overlay with proven fonts when the stb atlas renders no glyphs. Two bugs kept the mirror dead everywhere: (1) `HUD.Flush` cleared `FrameTextOut` at the END of every flush while the overlay reads the list AFTER the flush in the same frame — every string was wiped before it could be drawn (panel quads visible, all glyphs gone); the flush-end clear is gone, strings are frame-stamped (`TextOutItem.Frame`) and stale frames drop in `TextOut()` (clear when `Glfw.FrameId` changes) and in `DrawTextOutOverlay` (filter `Frame != Glfw.FrameId`), with draw-then-clear unchanged. (2) The F8 path never published the overlay pieces nor called `DrawTextOutOverlay` — `IDE.RenderInGameMode` now publishes `ImGuiFontResolver` (`ResolveHudFont` → `_imgui.GetFont`), `OverlayDrawList` (foreground draw list) and `OverlayFontPath` (Worldstar) inside its `WindowToScene` block and draws the mirror with the SAME letterbox fit as the scene image and the mouse mapper; no scene texture → `OverlayDrawList` nulled. **Anti-overlap (user: "things still overlap")**: (a) `HUD.TextOut` de-duplicates same-text/same-spot (≤6px) entries — `HUD.DrawText` auto-mirrors AND the call site adds an explicitly-sized TextOut for the same string, so the overlay used to draw BOTH (16px ghost under the 26px title / tooltip lines); (b) auto-mirrored entries carry their `Source` HUD instance and every overlay pass (`ViewportPanel`, F8) filters through `InventoryHud.AcceptMirrored` — only the shared inventory HUD's strings (its stb atlas is the broken one) plus untagged explicit TextOuts draw, so GameScene dialogue/debug text with a healthy stb never double-draws; the ViewportPanel publish+ `DrawTextOutOverlay` also moved OUTSIDE the `HudDrawFrameId` dialogue guard (dock in-game mirrors too — guard now protects the dialogue overlay only); (c) explicit TextOut sizes match the DrawText slot via `hud.GetFontSlotSize(slot)` (size drift = ghost) and stb-only decorations opt out with `DrawText(..., mirror: false)` (quest log ✓); (d) mirror-font glyph rule — Worldstar/atlas has no `× ◈ • ✓ → ← — ·`, they render as `?`: all mirrored HUD strings are ASCII (`Gold120`, plain qty digits, `- ` bullets, `== DONE ==`, `->`/`<-`, `({n})` qty in names); Console output is exempt (IDE font has them) |
| **HUD Item Qty+ Hover Desc (inventory)** | Quantity badge shows `×N` on EVERY occupied grid/hotbar slot — previously only `count > 1`, so a single potion showed nothing (user: "beri informasi jumlah itemnya"); chip sized from text extents, 15px mirrored digit, bottom-right of the cell; paperdoll cells pass count 0 (gear never stacks, no useless ×1 on equipment). The always-on info strip under the grid now follows the HOVERED item (grid/hotbar/paperdoll — name ×qty+ notes/use line+ price; fallback: first grid slot), giving a guaranteed-visible desc next to the mouse-adjacent tooltip (which also shows `(n)` on the name line for single items too). Glyphs are plain ASCII (no `×` — the mirror font renders it as `?`) and the summary lives INSIDE the panel's left column under the name plate (word-wrapped to the column width) — the old grid-bottom position straddled the panel's bottom frame and floated over the game scene |
| **HUD Tooltip Z-Front (mirrored plate)** | Tooltip plates (inventory+ shop) now render ON TOP of the slot icons: `HUD.Flush` draws ALL images (`DrawImageUV` icons) AFTER every box, so a batched parchment plate could never cover the hovered icon (potion punching through the tooltip — user: "make sure the tooltips' Z is frontmost"). The plate (fill+ 2px wooden edge) is mirrored into the overlay list via the new `HUD.RectOut(x,y,w,h,rgb,alpha,source)` (`IsRect` entry in `FrameTextOut`, drawn as `AddRectFilled` with no font resolution) queued BEFORE the tooltip's text lines — the overlay composites above the scene texture and draws in INSERT order, giving plate < edge < text Z-order. `InventoryUI.Render` now calls `DrawTooltip` LAST (after flash+ info summary) so the tooltip owns the highest insert order of any HUD entry; the batch `FillSolid`/`DrawBox` plate stays as fallback for paths with no overlay. Shop tooltip mirrors identically (already the last call in its Render), and its explicit TextOut switched from a hardcoded `14f` to `hud.GetFontSlotSize(labelFont)` for size parity. **Hidden during conversations (user: "hide the hotbar during dialogue")**: `InventoryHud.Render` returns early while `DialogueSystem.IsConversationActive` — hotbar, gold chip, flash and tooltip queue NOTHING (neither the batch nor the mirror), because both render composites put the shared inventory HUD ON TOP of the conversation window (preview: the mirror draws after the dialogue overlay; in-game: `invHud.Flush()` runs after the main HUD) — previously the mirrored tooltip covered the "1. Yes" choice and hotbar digits floated on the panel. `PanelOpen` state is kept so an open [I] panel reappears after the dialogue ends; ShopSystem still renders (a shop opened from a conversation must float above it) |
| **Conditions** | Choice conditions: `level:5`, `flag:name`, `item:potion`, `quest:id`, `questdone:id`, `gold:100`, `var:name:10` — unknown conditions fail closed |
| **Actions** | Choices/nodes execute trigger-catalog actions (Give Item, Activate Quest, Change Map, Play Sound, Camera Shake…) — no new action types needed. `Change Sprite` gets a dedicated contextual editor row: object name+ `Sheet|Clip|Loop` art string with a live clip picker from the Sprite Editor registry (loop segment optional — `Loop`/`Once`; runtime also honors it from trigger actions). Art names resolve CASE-INSENSITIVELY ('Cooking Area' finds the registered 'Cooking area'), the override stores the canonical spelling, and a genuinely unregistered art logs a visible `[ChangeSprite]` warning instead of silently keeping the old sprite |
| **Localization** | `DialogueLibrary.CurrentLanguage`+ per-language override tables (English/Indonesia/Japanese/Chinese/Korean/Thai/Vietnamese); untranslated text passes through |
| **Save/Load** | `SaveData.DialogueCompleted/Flags/Variables` captured on save, restored on load; session state resets on new play sessions |
| **Trigger Integration** | Start Dialogue (Param = asset id), Show Bubble (Param = text, Param2 = `type\|seconds`), Hide Bubble — wired in `TriggerEventSystem` |

---

## 3. Scene System (Save/Load)

### 3.1 File Format

Scene files use `.ing` extension — **JSON** format with `.ing` extension.

```json
{
  "Scenes": [
    {
      "Name": "GameScene",
      "EditorObjects": [...],
      "BackgroundObjects": [...],
      "Elements": [...]
    }
  ],
  "EditorCameraPosition": [x, y, z],
  "EditorCameraYaw": 0.0,
  "EditorCameraPitch": 0.0
}
```

### 3.2 EditorObjectData Properties

```json
{
  "Name": "Box1",
  "PrimitiveType": "Box|Sphere|Plane|Camera|Light|Sky|GlbReference|Map2D|Player2D|Start2D",
  "Player2DSpriteSheet": "", "Player2DAnimationClip": "",
  "Player2DHeight": 2.0, "Player2DCapsuleRadius": 0.35, "Player2DCapsuleHeight": 1.8,
  "Player2DShowCapsule": true, "Player2DGravity": 25.0,
  "Sprite2DGlow": 0.0, "Player2DGlow": 0.0,
  "Sprite2DGlowColorX": 1.0, "Sprite2DGlowColorY": 1.0, "Sprite2DGlowColorZ": 1.0,
  "Player2DGlowColorX": 1.0, "Player2DGlowColorY": 1.0, "Player2DGlowColorZ": 1.0,
  "Sprite2DGlowFlicker": false, "Player2DGlowFlicker": false,
  "GlbFilePath": "",
  "PosX": 0, "PosY": 0, "PosZ": 0,
  "RotX": 0, "RotY": 0, "RotZ": 0,
  "ScaleX": 1, "ScaleY": 1, "ScaleZ": 1,
  "ColorR": 0.8, "ColorG": 0.8, "ColorB": 0.9,
  "CastShadow": true,
  "IsVisible": true,
  "CameraFov": 60, "CameraNear": 0.1, "CameraFar": 500,
  "PbrAlbedoPath": "", "PbrNormalPath": "", "PbrMetallicPath": "",
  "PbrRoughnessPath": "", "PbrAoPath": "", "PbrHeightPath": "",
  "PbrEmissionPath": "", "PbrTexTiling": 1.0,
  "TerrainPbrAlbedoBrightness": 1.0, "...": "..."
}
```

### 3.3 Terrain Properties (per EditorObject)

```json
{
  "TerrainHeightmapPath": "heightmap.png",
  "TerrainChunkSize": 32,
  "TerrainChunksPerSide": 8,
  "TerrainHeightScale": 100.0,
  "TerrainSlopeThreshold": 0.35,
  "TerrainTexTiling": 0.5,
  "TerrainSlopeTexTiling": 0.3,
  "TerrainParallaxScale": 0.15,
  "TerrainUseStochasticSampling": false,
  "TerrainTextureAirPath": "",
  "TerrainTextureDirtPath": "",
  "TerrainTextureGrassPath": "",
  "TerrainTextureSnowPath": "",
  "TerrainTextureSlopePath": "",
  "TerrainLayerAirTop": 0.18,
  "TerrainLayerDirtTop": 0.45,
  "TerrainLayerGrassTop": 0.75,
  "TerrainLayerSnowTop": 1.0,
  "TerrainPaintedData": "(base64 compressed heightmap modifications)",
  "TerrainSplatData": "(base64 compressed paint splat data)",
  "TerrainLayerList": [...],
  "TerrainSlopeLayer": {...},
  "TerrainSlopeEnabled": false,
  "TerrainBrushSize": 5,
  "TerrainBrushStrength": 0.125,
  "TerrainBrushSoftness": 1.0,
  "TerrainBrushFalloff": 1,
  "PaintLayerTextures": ["", "", "", ""],
  "PaintLayerTilingX": [0.5, 0.5, 0.5, 0.5],
  "PaintLayerTilingY": [0.5, 0.5, 0.5, 0.5],
  "PaintLayerStochastic": [false, false, false, false],
  "PaintLayerCount": 1,
  "TerrainLayerSettings": [...]
}
```

### 3.4 TerrainLayer (Dynamic Layer System)

Each layer in `TerrainLayerList`:
```json
{
  "Name": "Base",
  "Visible": true,
  "AlbedoPath": "Artifacts/Textures/default.jpg",
  "PbrPaths": [null, null, null, null, null, null],
  "TilingX": 0.5, "TilingY": 0.5,
  "HeightMin": 0.0, "HeightMax": 1.0,
  "BlendSharpness": 2.0,
  "StochasticSampling": false,
  "NormalStrength": 1.0, "NormalBlur": 0.0,
  "MetallicThreshold": 0.5, "MetallicSoftness": 0.1, "MetallicStrength": 1.0,
  "RoughnessStrength": 1.0, "RoughnessInvert": false,
  "AoStrength": 1.0, "AoBrightness": 0.0,
  "HeightStrength": 1.0, "HeightInvert": false, "HeightBlur": 0.0,
  "EmissionIntensity": 1.0,
  "AlbedoBrightness": 1.0, "AlbedoSaturation": 1.0, "AlbedoContrast": 1.0
}
```

### 3.5 Save/Load Behavior

- **Save All / Ctrl+S / F8**: Saves to current file; if no file active → opens Save As dialog. Also persists ALL per-project catalogs: items (`Assets/Items/items.json`), shops (`Assets/Shops/shops.json`), quests (`Assets/Quests/quests.json`), dialogues (`Assets/Dialogue/dialogues.json`) — not gated on scenes existing anymore
- **Item catalog auto-merge**: on project open, `LoadCatalog` reads `items.json` FIRST, then merges EVERY other top-level `*.json` in `Assets/Items/` as an external catalog (e.g. `gear-items.json`). Ids already present win (the main file is authoritative); files without an `Items` array or with invalid JSON are skipped (logged `[Inventory] Auto-load skip …`); with no `items.json` at all the external files still load. Saving writes the merged list back to `items.json`
- **Binary sidecar cache (warm load)**: big JSON loads (scene manifest, sheets, maps, catalogs) try the `.cache/<file>.bin` sidecar first; wire format **v2** = [magic][ver 2][root type][**layoutId** — hash of the DTO member layout][JSON stamp len/hash/mtime]. A sidecar with a wrong version OR a different member layout than the current build is rejected **silently** before decoding (JSON parses as usual, no console spam); the Sprite Editor then rewrites the sidecar (**self-heal**) so the next load is a binary hit. Stale v1 sidecars written by older builds (e.g. before the `PatternActions` field existed) are auto-rejected and healed — the `[BinCache] cache invalid … parsing JSON` log spam is gone
- **New Scene**: Clears active file (does NOT overwrite game.ing)
- **Save As**: Opens file dialog, saves JSON+ thumbnail PNG
- **Load**: Reads .ing JSON, reconstructs all objects, loads textures
- **Settings persistence**: `settings.json` for PostFX, Shadows, Fog, Quality, IDE fonts

---

## 3.6 Project System (.projing)

### 3.6.1 Project Structure

Every project is a self-contained folder with a `{Name}.projing` metadata file:

```json
{
  "projectName": "MyGame",
  "version": "1.0",
  "created": "2026-05-06T10:00:00",
  "lastOpened": "2026-09-03T14:00:00",
  "lastSaved": "2026-09-03T15:30:00",
  "scenes": ["game.ing", "Scenes/MainMenu.ing", "Scenes/Level1.ing"],
  "autoLoadGameIng": true
}
```

### 3.6.2 Per-Project Files

| File | Purpose |
|------|---------|
| `{Name}.projing`| Project metadata + scene inventory
 |
| `settings.json` | Engine settings (resolution, shadows, fog, post-FX, fonts) |
| `shadow_presets.json` | Saved shadow presets |
| `imgui.ini` | IDE panel layout (positions, sizes) |
| `recent_files.json` | Recently opened scene files |
| `game.ing` | Combined scene file (auto-loaded on project open) |
| `saves/` | Save game slots (slot_0 through slot_4) |
| `Assets/fonts/` | Project fonts (copied from exe on create) |
| `Assets/images/` | Image assets |
| `Assets/models/` | 3D model assets |
| `Assets/Maps/` | Heightmaps |
| `Scenes/` | Individual scene files (.ing) |

### 3.6.3 Project Lifecycle

1. **New Project** (File > New Project): Pick folder → enter name → creates structure+ copies fonts
2. **Open Project** (File > Open Project): Browse for `.projing` file → auto-loads `game.ing`
3. **Recent Projects**: File > Recent Projects submenu (persisted globally)
4. **Save All** (Ctrl+S): Saves scenes+ updates `.projing` (LastSaved+ scene inventory)+ ALL catalogs (item/shop/quest/dialogue)
5. **Close Project**: File > Close Project persists everything first (scenes+ sheets+ catalogs) then resets all editor state: scenes, selection, hierarchy, editor objects, Shop/Item Editor selection (via `OnProjectChanged`), Shadow preset cache
6. **Switching projects** (Recent Projects / Open Project): the outgoing project is persisted FIRST (`PersistEditorData`) — no data loss when switching without Close Project

### 3.6.4 Path Resolution

All asset paths are resolved via `PathHelpers.Resolve()`:
1. Check project root (`ProjectRoot/relativePath`)
2. Check project Assets (`ProjectRoot/Assets/relativePath`)
3. Fallback to exe directory (`BaseDirectory/relativePath`)

When saving, `PathHelpers.MakeRelative()` converts absolute paths to portable relative form.

### 3.6.5 File Menu Layout

```
File
├── New Project...
├── Open Project...       (file browser: *.projing)
├── Recent Projects       (submenu)
├── ── Current Project ──  (shows name+ path+ Close)
├── ── Separator ──
├── New Scene (Ctrl+N)
├── Open Scene... (Ctrl+O)
├── Open Recent Scene     (submenu)
├── Save (Ctrl+S)
├── Save As... (Ctrl+Shift+S)
└── Exit
```

---

## 4. Rendering Pipeline

### 4.1 Shader Programs

| Shader | Vertex | Fragment | Purpose |
|--------|--------|----------|---------|
| Editor Terrain | `pbrDisplace_vertex.glsl` | `objectPbr_fragment.glsl` | Terrain with PBR |
| Object PBR | `static_vertex.glsl` | `objectPbr_fragment.glsl` | GLB/PBR objects |
| GLTF | `gltf_vertex.glsl` | `gltf_fragment.glsl` | Standard glTF |
| Impostor | `impostor_vertex.glsl` | `impostor_fragment.glsl` | Billboard impostors |
| Sky | `sky_vertex.glsl` | `sky_fragment.glsl` | Procedural sky |
| Skybox | `skybox_vertex.glsl` | `skybox_fragment.glsl` | Cubemap skybox |
| Shadow | `shadow_static_vertex.glsl` | `shadow_static_alpha_fragment.glsl` | CSM depth pass |
| HUD | `hudVertex_shader.glsl` | `hudFragment_shader.glsl` | UI elements |
| Outline | `outline_vertex.glsl` | `outline_fragment.glsl` | Selection outline |
| PostFX Bright | `post_vertex.glsl` | `postFxBright_fragment.glsl` | Bloom bright pass |
| PostFX Downsample | `post_vertex.glsl` | `postFxBloomDownsample_fragment.glsl` | Reactive bloom 13-tap downsample/soften |
| PostFX Upsample | `post_vertex.glsl` | `postFxBloomUpsample_fragment.glsl` | Reactive bloom additive Catmull-Rom upsample |
| PostFX Composite | `post_vertex.glsl` | `postFxComposite_fragment.glsl` | Final composite |

### 4.2 Rendering Flow (per frame)

```
1. Shadow Pass
   ├── CSM: 3 cascade depth maps (sun directional)
   └── Local Shadows: Point (cube map)+ Spot (projective) per light

2. Scene Pass → MSAA FBO (multisampled)
   ├── Sky (procedural or cubemap)
   ├── Terrain (heightmap+ dynamic layers+ PBR)
   ├── Objects (primitives+ GLB references)
   ├── Selection highlight (outline shader)
   └── Grid overlay

3. Resolve MSAA → single-sample SceneColorTex

4. PostFX Chain (if enabled)
   ├── Auto-exposure (luminance measurement)
   ├── Reactive Bloom (bright extract → 5-mip chain → additive upsample → composite)
   ├── ACES Tonemapping
   └── Gamma correction → quad-draw copy back to scene texture

5. IDE Overlay
   ├── ImGui panels (Inspector, Hierarchy, etc.)
   ├── Viewport panel (renders SceneColorTex as ImGui image)
   └── Framebuffer viewer (debug)
```

### 4.3 Framebuffer Layout

| FBO | Format | Usage |
|-----|--------|-------|
| SceneFBO | RGBA16F MSAA | Main scene render |
| SceneColorTex | RGBA8 | Resolved scene (PostFX input/output) |
| ShadowMap0/1/2 | DEPTH24 | CSM cascade 0/1/2 |
| LocalShadowPoint[] | DEPTH24 | Per-point-light cube depth |
| LocalShadowSpot[] | DEPTH24 | Per-spotlight projective depth |
| BloomBright | RGBA8 half-res | Bloom bright extraction (mip 0 of the chain) |
| BloomMip0-4 | RGBA8 ½→1/32 | Reactive bloom mip chain (ping-pong soften scratch per level) |
| AutoExposureLuma | RGBA8 1/16 | Luminance measurement (mipmapped) |

---

## 5. PBR System

### 5.1 Cook-Torrance BRDF

Both terrain and objects use the same BRDF functions:
- **Distribution**: GGX/Trowbridge-Reitz
- **Geometry**: Smith's method with Schlick-GGX
- **Fresnel**: Schlick approximation

```glsl
float D = DistributionGGX(N, H, roughness);
float G = GeometrySmith(N, V, L, roughness);
vec3  F = FresnelSchlick(max(dot(H, V), 0.0), F0);
vec3 specular = D * G * F / (4.0 * NdotV * NdotL+ 0.0001);
vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
vec3 Lo = (kD * albedo / PI+ specular) * lightColor * NdotL;
```

### 5.2 Object PBR (objectPbr_fragment.glsl)

Per-object PBR with 7 texture slots:

| Slot | Type | Default |
|------|------|---------|
| Albedo / Base Color | RGB | Object color |
| Normal Map | RGB | (0.5, 0.5, 1.0) flat |
| Metallic Map | R | 0.0 (dielectric) |
| Roughness Map | R | 0.5 (mid) |
| AO Map | R | 1.0 (no occlusion) |
| Height Map | R | 0.5 (mid) |
| Emission Map | RGB | (0, 0, 0) none |

**Per-object tuning** (EditorObject properties):
- `TerrainPbrAlbedoBrightness/Saturation/Contrast` — Albedo adjustment
- `TerrainPbrNormalStrength/Blur` — Normal map intensity
- `TerrainPbrMetallicThreshold/Softness/Strength` — Metallic with smoothstep
- `TerrainPbrRoughnessStrength/Invert` — Roughness (+ smoothness invert)
- `TerrainPbrAoStrength/Brightness` — Ambient occlusion
- `TerrainPbrHeightStrength/Invert/Blur` — Parallax/height
- `TerrainPbrEmissionIntensity` — Self-illumination
- `PbrTexTiling` — Global texture tiling multiplier
- `PbrParallaxScale` — Parallax occlusion depth (steep POM, 0 = off)
- `PbrPomShadowStrength` — Relief self-shadowing strength (0 = off, 1 = hard)

**Height / Displacement (POM revamp)**:
- Steep Parallax Occlusion Mapping in the correct tangent basis (`transpose(TBN)·V`)
- Canonical height mapping: full 0..1 depth span, raw 0.5 = flat (invert/strength/blur tuning applied)
- Adaptive ray-march steps (8–48) by view obliqueness × depth scale; occlusion interpolation kills stair-stepping
- Displacement offset converted per map (`tiling_i / tiling_height`) so per-map tiling stays pixel-locked
- Relief self-shadowing: 8-tap march toward the sun in tangent space, slope-attenuated, distance-faded
- Crevice AO: 4-tap height-difference darkening of occluded valleys, blended with the AO map
- Marmoset-style height calibration (per-object, persisted):
  - `PbrHeightScaleCenter` — baseline gray treated as zero displacement depth ("Scale Center")
  - `PbrHeightContrast` / `PbrHeightContrastCenter` — exaggerate low/high separation around an adjustable mid
  - `PbrHeightOffset` — shifts the whole height field (raise valleys / tame crumpled spikes)
- Smear guards: physical tangent kept up to ~87° view elevation (no depth squashing at low angles),
  hard ±0.16 UV offset cap prevents cross-tile texture smearing, far-field fade 80–160u for stability
- **Vertex Displacement** (planes): TRUE geometric displacement — the plane mesh is tessellated
  (16..512 segments, `PbrVertexSegments`) and a dedicated vertex stage (`pbrDisplace_vertex.glsl`
 + the shared objectPbr fragment) pushes vertices along the displacement source via vertex
  texture fetch, re-deriving normals from the height gradient. Real silhouette, real parallax,
  real self-occlusion; view-ray POM auto-reduces (micro 0.3) so depth is not doubled.
  `PbrVertexDisplaceScale`/`PbrVertexOffset` are the LEGACY PBR fields (no UI on planes).
- **Terrain Geometry (heightmap → mesh)** — planes with a terrain elevation heightmap are
  ALWAYS displaced (forced, no toggle). Elevation is a SEPARATE source from the PBR height map:
  - `TerrainHeightPath` — dedicated grayscale heightmap (GPU unit 15, RAW 0..1, no Marmoset
    calibration); lazy texture load, dropped on path change, disposed with the object.
  - `TerrainHeightSourcePath` — effective source (plane-only legacy fallback to the PBR height
    slot); every elevation reader (dense-grid gate, displaced program gate, `HasPbrMaterial`,
    panel status) MUST use it, never the raw fields.
  - **Two height sliders from two separate images** (`displaceWorld(uvT, uvP)` in the shader):
    * `TerrainBaseHeight` — "Base Height (from heightmap)": how tall the raw elevation image
      stands (the SHAPE); uniform `u_terrainBaseHeight`.
    * `TerrainHeightScale` — "Displace Height (from PBR)": amplitude of the vertex-displacement
      DETAIL from the PBR height map (unit 5, POM-calibrated, its own per-map tiling); uniform
      `u_dispScale` on the terrain branch, gated by `u_pbrHeightDetail` (1 only when the PBR
      slot is real — the elevation fallback bind to unit 5 does NOT count as detail).
  - `TerrainHeightOffset` (−250..250) — shifts the whole displaced surface; `TerrainHeightStrength`
    (0..3) reshapes the raw elevation around mid-gray (0 flat, 1 as-authored, >1 steeper).
  - `TerrainHeightTilingX/Y` — the elevation's OWN tiling (uniform `u_terrainUvScale`), decoupled
    from PBR per-map tiling and global Map Tiling; slopes use two separate footprints (terrain
    tiling for base, POM tiling for detail) so gradients add correctly.
  - All uniforms upload every draw (live, no mesh rebuild); persist symmetric (SceneAsset+    save ×2+ load clamp, −1 sentinels migrate legacy scenes: BaseHeight inherits the old PBR
    peak, Displace starts 0 — shape unchanged).
  - Chunk AABB pad+ plane picking proxy use the real max: base+ detail+ |offset|
    (`TerrainDisplacementExtent`); elevation texture load failure flattens only dedicated-
    source planes (legacy fallback planes render through the unit-5 branch).
  - **Terrain Sculpt (viewport brush)** — brush sculpting that PAINTS the elevation heightmap.
    **ADDITIVE sculpt delta** — brush edits NEVER touch the authored base heightmap:
    `TerrainHeightfield` (Engine/Objects/TerrainHeightfield.cs) decodes the sculpt delta
    (stored bake, else a 0.5-neutral buffer) into a 512² CPU field; Raise/Lower/Smooth/
    Flatten/Noise/Terrace stamps mutate it (frame-batched via `SculptApply`; Noise paints
    an absolute fractal-value-noise field between the footprint's min/max elevation — one
    coherent pattern per stroke via a per-stroke seed, converging like Smooth; Terrace    quantizes the local 3×3 mean onto a shared N-step grid — per-texel Round() on a
    steep slope alternates levels (sawtooth teeth), so the MEAN is what snaps;
    `Steps` 2..64, and the grid stays shared across stamps); a dirty-rect GL_R8 texture binds at unit 16 (`u_sculptDeltaMap`) and the
    vertex stage composes elevation = base·BaseHeight+ (delta − 0.5)·2·`SculptAmp`+    POM detail. **Dirty-rect uploads serialize PACKED rows with 4-byte row padding**
    (`FlushTexture` in TerrainHeightfield.cs): `TexSubImage2D` reads rows at
    `UNPACK_ROW_LENGTH = 0`, so uploading straight from the row-pitched (512-byte) CPU
    mirror shifted every row after the first by (Res − rect width) texels — the LIVE
    sculpt surface rendered as jagged diagonal teeth while the CPU field and the TGA
    bake stayed smooth ("sculpt ≠ reload"; full-size 512-wide uploads coincidentally
    matched the stride, which is why freshly-loaded terrain always rendered correct).
    Same convention as `TerrainSplatField.FlushTexture`. — so "Sculpt Add Height" (TerrainSculptAmp, world units) scales the
    sculpted relief while the Base Height slider keeps driving the shaped terrain.
    Each finished stroke bakes the delta to `Artifacts/Terrain/<scene>/<object>_sculpt.tga`
    (`SculptDeltaPath`) — persistence without overwriting the base. CPU picking/rings and
    splat height bands read the COMBINED surface (base+ delta).
    Brush params:
    radius (world units), strength (height-range fraction per second at the core), hardness
    (falloff: 0 soft dome … 1 hard disc). Picking ray-marches the combined surface
    (`TryRaycastSculptSurface`); the viewport draws a surface-projected ring showing the TRUE
    world radius; while the session is ON, LMB paints instead of selecting/gizmo-dragging.
    UI: dedicated TERRAIN panel (menu "Terrain") — heightmap input, Terrain Geometry
    (size / Base Height / Displace Height / Offset / Strength / tiling / segments / chunks)
    and "Terrain Sculpt" brush controls+ "Sculpt Add Height" amplitude+ "Revert sculpt"
    (reset the delta layer to neutral — the base image is untouched). Session state:
    `EditorObject.SculptBrush`+    `IDEBridge.TerrainSculptObject` (null = off). SHIFT-TAP in the viewport cycles the
    brush mode (Raise → Lower → Smooth → Flatten → Noise → Terrace → Raise, per-stroke
    console log, synced back into the Terrain panel). Viewport affordances: the paintable-region
    boundary is drawn ON the displaced surface (96-point perimeter outline, live elevation per
    point) so the sculptable area is always visible; the brush ring hugs the terrain (rim
    samples lifted to live elevation) and its color encodes state — orange = cursor on the
    paintable surface, red = painting, dim gray = projected onto the footprint but off-surface
    (a click there will NOT sculpt).

### 5.3 Terrain rendering (displaced PBR plane — objectPbr pipeline)

Terrain IS a PBR Plane: the geometric-displacement vertex stage (pbrDisplace_vertex.glsl,
unit-15 elevation, base+ POM-detail decomposition — see 5.2) displaces the tessellated
grid while the SHARED objectPbr_fragment.glsl shades it (Cook-Torrance, POM, CSM, local
lights, fog). The old dedicated terrainEditor_fragment.glsl / sampler2DArray layer
system was stripped; terrain-specific behavior lives in the vertex stage+ CPU fields
+ the splat layer path below.

### 5.4 Local Light PBR

Local lights (point/spot) use Blinn-Phong for terrain, Cook-Torrance for objects:
```glsl
float spec = pow(max(dot(N, H), 0.0), 24.0) * 0.6;  // Terrain
vec3 specular = D * G * F / (4.0 * NdotV * NdotL);    // Objects
```

### 5.5 Terrain Splat — paintable texture layers driven by the sculpted height

4-layer texture blending INSIDE the shared objectPbr_fragment.glsl (no separate shader):
an RGBA8 weight map (one channel per layer, weights sum to 1 per texel) blends four
albedo/PBR layer sets. **Layer 0 IS the base PBR material** (the PBR-panel maps incl. the
ObjColor fallback), so an unpainted terrain renders identically to the plain path.

**Texture units** (splat block, per draw in `DrawPbrPrimitive`):

| Unit | Texture |
|------|---------|
| 0-6 | Base PBR maps (= splat layer 0) |
| 7/8/9 | CSM shadow cascades |
| 15 | Terrain elevation (vertex displacement) |
| 10 | Splat weight map — GL_RGBA8 256², live-updated (dirty-rect `TexSubImage2D`) |
| 11-14 | Layer albedo ×4 (layer 0 = base albedo map) |
| 21-24 / 31-34 / 41-44 / 51-54 / 61-64 | Layer normal / metallic / roughness / AO / POM-height ×4 |

Gate `u_splatActive` = a live weight field (paint session, band buffer) exists AND (note: the PBR render path itself is entered via `HasPbrMaterial`, which also counts splat state — field/band buffer/stored splat file/layer textures/bands-enabled — so a plain plane painted without any heightmap still renders through the splat pipeline)

**Splat renders through a TERRAIN_SPLAT program VARIANT** (`Shader.GetObjectPbrSplat(Displace)ShaderProgram` — the SAME `objectPbr_fragment.glsl` compiled with a `#define TERRAIN_SPLAT 1` prefix injected after `#version`). Reason: `GL_MAX_TEXTURE_IMAGE_UNITS` = 32 on the target GPU — the base objectPbr program uses 17 samplers (7 maps+ 3 CSM+ 7 local-light shadows) and the full splat set needs 25 more; one program cannot carry both, which is why the splat block was once stripped as "dead code" after the link started failing with "Number of sampler exceeds the limitation". The splat variant trades the 7 local-light SHADOW samplers for 21 splat samplers (weight map+ albedo/normal/metal/rough/AO per layer 0-3 — per-layer POM-height samplers dropped) = 31 ≤ 32. `DrawPbrPrimitive` picks the variant per-plane when splat state exists and falls back to the plain program if the variant failed to link (one-shot console warning). The `#version`-aware define-prefix loader lives in `ShaderHelpers.LoadShader(vertex, fragment, fragmentPrefix)`; `Tools/ShaderSmokeTest` covers all four objectPbr programs and prints `GL_MAX_TEXTURE_IMAGE_UNITS`.
(`SplatIsPainted` — any layer 1-3 map or paint — OR `SplatHeightBandsEnabled`). Uniform
vec4 `u_splatHasAlbedo/Normal/Metal/Rough/Ao/Height` gate per-slot presence — MUST be
`Uniform4f` (vec4 FLOAT): the old 4× `Uniform1i(loc+i)` scheme = a silent double bug —
`glUniform1i` on a vec4 location = GL_INVALID_OPERATION (the call is dropped → presence (0,0,0,0)
→ ASSIGNED layer textures never get sampled, the color tint always shows)
AND `loc+1..+3` on a non-array vec4 is not a vec4 component (a vec4 = ONE location) — the writes
land on other uniforms (a sampler on the wrong unit → random textures). Empty slots force weight
0 and the remainder renormalizes (never white/black). Layers without an albedo texture use
`u_splatTint[l]` (layer 0 = the object color). **Weight map sampling = RAW mesh UV**
(`TexCoord`, 1:1 with the 256² field — NOT the tiled uvAlbedo: sampling through the
tiled UV makes every paint stroke REPEAT once per tile, "tiled paint").
**Layer 0** samples the base maps through its own per-map tiling/offset+ the POM shift
(identical to the plain path). **Layers 1-3** sample ALL their maps through ONE per-layer
tiling `u_splatLayerTiling[l]` (`SplatLayerTiling`, a per-layer slider in the Terrain
panel → Layer textures) INDEPENDENT of the global Map Tiling — the mask paint stays
1:1 while the texture density is chosen per layer; layers 1-3 skip the POM shift
(its per-map conversion assumes a base tiling that no longer matches the
decoupled layer UVs).

**UNIVERSAL VERTEX DISPLACEMENT (PBR panel → "Vertex Displacement", every primitive).**
Checkbox `PbrVertexDisplaceEnabled` (default OFF — legacy behavior)+ `PbrPrimTessellation`
(1..64, subdivisions per Box face / slices·stacks Sphere; 1 = the legacy mesh):
- **Plane**: toggle ON adds a displaced path when ONLY a PBR height map is set (without
  terrain elevation / sculpt) — a plain plane with a PBR height never entered
  the displace program before. Terrain elevation/sculpt stays displaced without the toggle.
- **Box**: the mesh is subdivided into `tess×tess` quads per face (`Object3D.CreateBoxVertices(…, tess)`;
  the corner/UV layout is exactly the flat quad's, CCW winding kept) → the height map
  moves real vertices through `pbrDisplace_vertex.glsl` (the program is picked in
  `DrawPbrPrimitive`: `displaced = PbrVertexDisplaceEnabled && _pbrTex[5] != 0` for
  Box/Sphere; scale/offset = `PbrVertexDisplaceScale`/`PbrVertexOffset`).
- **Sphere**: slices = 12·tess, stacks = 8·tess.
- UI: a "Vertex Displacement" section in PbrPanel (Enable+ Height Scale+ Height Offset+  Tessellation for Box/Sphere+ a hint) — separate from the Terrain panel (which keeps
  terrain elevation/sculpt/chunks). Persist: `PbrVertexDisplaceEnabled`+  `PbrPrimTessellation` in SceneAsset+ save ×2+ load clamp+ Duplicate.
The key: displacement needs INTERIOR vertices — a 2-triangle-per-face mesh cannot move;
before raising tessellation, toggling ON on Box/Sphere produces nothing.
**Box/Sphere displaced ANTI-ARTIFACT**: (1) `u_dispEdgeFade` = the displacement FADES to 0
in a thin ring at the **FACE edge (raw mesh UV `uvD`, not the tiled height UV** —
a per-TILE ring via `fract(uvP)` made every height-map tile bulge separately and
crack across tiles mid-face) — edge vertices shared between neighboring faces sample
DIFFERENT heights (per-face UVs), without the face fade they TEAR (a strip stretched across faces);
(2) `u_dispRecomputeNormal = 0` for Box/Sphere — the gradient re-derivation in
`pbrDisplace_vertex.glsl` assumes a PLANE grid (cell world size from the model's X/Z axes); on
faces facing any direction it produces garbage normals (banded shading). Both are
uploaded from `DrawPbrPrimitive` (`perFaceUvDisp = Box || Sphere`); Plane keeps fade off
+ recompute on (terrain elevation must connect fully to the edge+ needs the normal slope).
**BUG "Box tess>1 still broken" (TWO causes, both fixed)**: (1) **mesh** — in
`Object3D.CreateBoxVertices`, the RIGHT face used `dv = p6−p1` = the face DIAGONAL
`(0,2hy,2hz)` (it must be `p2−p1`, pure+Y) → the corner walk landed at
`p5+p6−p1 = (hx,hy,3hz)`, half the depth OUTSIDE the box → the right face stretched into a slanted
parallelogram hanging out front (tess=1 hid it because it used the flat
quad; tess≥2 exploded) — the "stretched striped red" screenshot; (2) **shader** —
`u_dispOffset` was added OUTSIDE the edge-fade (`nw*(elev+offset)`): neighboring faces
have DIFFERENT normals → an unfaded offset pushes the edge vertices in different directions →
a TORN face (a gap between top and side) even with Height Scale 0 but an offset set —
now the whole `(elev+offset)*edgeFade`. Mesh rule: **the face basis MUST be a side edge vector
(one axis), not a diagonal**; the shader rule stands: the offset follows the fade.
**BUG "Box tessellation > 1 looks wrong" (fixed — terrain-path uniform leakage)**:
`terrainDriven = displaced` put Box/Sphere on the terrain path → `u_terrainDisplace`
+ `u_sculptAmp` were NEVER written for non-terrain, and the displace program is SHARED
across objects → a Box inherited `u_terrainDisplace=1`+ the sculpt amp from the last drawn terrain
plane, PLUS sampler units 15/16 still held the terrain/white textures → every
Box face got displaced by the terrain elevation (even Height Scale 0 hit it, because the
terrain branch uses BaseHeight/amp — and at tess=1 it was INVISIBLE because the edge fade = all
vertices sit in the fade ring; at tess=2 interior vertices appear → bulging). A three-layer fix:
(1) `terrainDriven = displaced && PrimitiveType == Plane`; (2) `u_sculptAmp`/unit-16
are ONLY terrain-driven, non-terrain = explicit 0; (3) the non-terrain else branch writes
`u_terrainDisplace = 0` EXPLICITLY — a shared uniform must be written on EVERY draw. Rule:
**a shared-program uniform gated per-object MUST be written on ALL branches** (even 0),
otherwise it leaks across objects.

**2D MERGE DOWN — REMOVED (revert).** The "⇓ Merge Down" button was briefly built then
taken out: the user does NOT need layer flatten/merge — 2D collision is deliberately SIMPLE
(player capsule vs tile-box overlap = a hit, on all layers at once, see
below). Layers are just draw order; physics reads all layers	ransparently. Do NOT add any union/merge tool for 2D collision again.

**SIMPLE 2D COLLISION: player capsule vs tile boxes (all layers).** The user's rule:
"capsule and box collide → collision hits", DONE — WITHOUT union/merge UI.
Implementation: `Player2DSystem` tests the capsule AABB against tile boxes on EVERY
layer that has `CollisionTileIds` (X sweep, ground/ceiling sweep,
`TriggerEventSystem.SnapFeetToGround` — all iterate `map.Layers`); a solid tile =
the tile exists AND its ID is collision-registered (`IsSolid`). The collision helper boxes in the
viewport (`EditorObject`, the `Map2dShowCollision` block) used to read only ONE layer
(the active one, fallback layer 0) → collision tiles on other layers weren't drawn (preview <
real collision). Now the boxes draw every collision-bearing layer (a `colLayers`
loop). Rule: a NEW collision reader MUST iterate every layer with
`CollisionTileIds.Count > 0` — collision is a per-layer-ID property, gameplay =
all layers at once, with no union/merge UI of any kind.

**2D MULTI-MAP COLLISION (every level map keeps hitting).** Physics (`Player2DSystem`)
and triggers (`TriggerEventSystem`) now evaluate EVERY tilemap shown through a
Map2D object in the scene (the ACTIVE map first, then the others — `EnumerateSceneMaps`),
each against all its collision-bearing layers. All maps bake at the world origin
(the Map2D object Position is ignored by the renderer) → the world↔grid mapping is IDENTICAL for all
maps, so a solid on any level map holds the capsule (the player on Map Level 1, a collision
box on Map Level 3 → still hits — the old bug: physics accepted only
`ActiveTilemap`, the player fell through other maps' collision). Key details: the X-push is
Y-aware (only the tile band overlapping the capsule's span holds — other maps don't
phantom-block from solid tiles far above the head); falling/rising across maps = the
HIGHEST (landing) / LOWEST (ceiling) surface candidate wins; pit-respawn snaps to the
highest ground across maps (`SnapFeetToGroundAny`), the pit threshold = the largest cell
across collision-bearing maps; camera follow keeps using the ACTIVE map
(`ComputeWorldBounds(activeMap)`). Per-map triggers are evaluated for all maps every
frame → `_activeMap` is re-pinned to the active map (`PinActiveMap`) so checkpoints/
save slots keep ground-snapping to the active map. Do NOT bring back the "physics only
sees ActiveTilemap" pattern.

**LEVEL MAP SWITCH = THE RUNTIME LEVEL SWITCHES TOO (the camera re-frames).** A portal with
a destination on another map now MOVES the runtime level: `TriggerEventSystem.RuntimeMap`
(starts = the active map at session start, reset on every Start2D spawn). The teleport handler
(`OnTeleportPlayer(target, z, destMap)`) — destMap != null → `RuntimeMap = destMap`
WITHOUT touching the camera (REVISION: the first version reset `CameraFollowInitialized`
→ the camera JUMPED/re-framed to the new level — the user rejected it: "the camera moved somewhere else,
it must stay unchanged"). Camera follow+ framing stay LOCKED to the session's active
map; a smooth follow carries the camera to the landing point like ordinary movement.
The ONE exception: the world-bound clamp uses `RuntimeMap ?? activeMap`
(the destination portal lives in the RUNTIME map's extent — clamping to the active map would fight the follow).
Save/Load Checkpoint ground-snaps to `RuntimeMap` (a checkpoint on any level
lands correctly). Change Map (file load, IDE.cs) sets `RuntimeMap = ActiveTilemap`
without resetting the camera flag. A portal within ONE map changes nothing.

**2D MAP SWITCH = THE EDITOR CAMERA IS NOT TOUCHED (FINAL, auto-focus revoked).** All paths
switching the active map (the combo selector, the post-Delete retarget, Add Tilemap, Load file,
restore .ing) do NOT touch the editor camera — `RetargetActiveTilemap`/
`LoadMapTilemap`/`CreateNewMap`/`LoadMapFromFile` without `FocusActiveTilemap()`;
framing is manual via the "Focus" button. History: auto-focus was built on request
("auto focus when switching maps"), then REVOKED once the user realized the camera also
reset on every map switch and that was annoying too ("for 2D, if possible don't reset the
camera on every map switch either") — and its first version (focus inside RetargetActiveTilemap)
proved to OVERRIDE the per-scene camera on scene switches/.ing restores. Do NOT turn it
back on; if ever, only on an explicit user action with a confirmation first.

**PER-SCENE CAMERA (save & load symmetric — double-checked).** EditorScene stores
`CameraPos/CameraYaw/CameraPitch+ CameraOrtho/CameraOrthoSize`: (1) the
`IDEBridge.SelectedEditorScene` setter STASHES the live camera to the outgoing scene before switching,
then RESTORES the incoming scene's camera (+`UpdateVectors`/`SyncSmoothVectors`); (2) loading an
`.ing` → `SceneAsset.EditorCamera*` → `EditorScene.CameraPos`+ `PendingCameraPos`
for the first scene (applied once by `SceneManager.ApplyPendingEditorCamera`);
a legacy manifest-level fallback for old files; (3) save ×2 snapshots the live camera
into the SELECTED scene only (other scenes keep their stored values). A scene rename moves
the camera along so the view doesn't reset. **"FORCED CAMERA" AUDIT (user decision:
"check everything that forces the camera position, don't force it")**: editor-camera
writers in edit mode = the USER ONLY (input, Focus Selection, the user-path map-editor
auto-focus)+ per-scene restore. `SyncLevelCamera` used to FORCE the ortho framing+ capture
`HasCameraStart` in edit mode every time a level showed → it overwrote the per-scene restore (the
"camera moves on its own when switching scenes" bug, the second culprit after the map-editor auto-focus);
now edit mode writes NO camera at all (just a lock state); framing/anchor
camera start happens ONLY during PLAY (preview/in-game/startup). **The third culprit (2D↔non-2D
scene switches): SyncLevelCamera's "restore previous view" branch (`level == null`)
threw a `_levelCameraSavedPos` snapshot taken ONCE while level-mode was active —
stale across scene switches; now gated by `_levelCameraMoved` (true ONLY when play-mode
has ever written the view) → edit mode never restores anything. The per-scene persist gained
`EditorCameraOrtho/OrthoSize` (previously only position/yaw/pitch — 2D zoom & projection
didn't come back). The camera reset-to-default in ClearState is only for "New Project". Do NOT
add other edit-mode camera writers.

**GOTCHA — VERTEX LAYOUT: `aTexCoord` MUST be location 3, NOT 2.** The primitive VAO
(`Object3D.SetupGPUResources`) binds loc 0 = pos, 1 = normal, **2 = COLOR vec3**,
**3 = UV vec2**. `vertex_shader.glsl` used to declare `aTexCoord` at location 2
→ every Box/Sphere drawn by objectPbr sampled its maps with `color.xy` as the
UV (constant per face) → **the texture never showed even though it loaded & bound fine**
(Plane passed because `pbrDisplace_vertex.glsl` was already location 3). Now it is location 3
(= `pbrDisplace_vertex.glsl`), the smoke test stays green. Rule: primitive shaders that
share the Object3D VAO MUST follow the loc2=color/loc3=uv layout; other VAOs (GLB skinned,
Map2D, ImGui) have their own layouts and are unaffected.

**GOTCHA — the legacy Inspector "Texture Path" on Box/Sphere/Plane = the PBR albedo.**
That slot used to load into `_textureID`, which NO render path ever bound
(the plain path forces `useTexture=0` = vertex color) and `HasPbrMaterial` didn't
count it → a Box textured via the Inspector showed FLAT GRAY (not about the vertex
count — the 36-vertex per-face UV 0..1 Box was already correct). Now `HasPbrMaterial` counts
TexturePath for Box/Sphere/Plane and `EnsurePbrTextures` bridges TexturePath →
the PBR albedo slot (the cache key uses the effective albedo source; PbrAlbedoPath always wins
when set).

**GOTCHA — GenTextures/TexImage2D WITHOUT BindTexture: the texture never gets data.**
`TerrainSplatField.EnsureTexture()` once called `TexImage2D`+ `TexParameter`
WITHOUT `GL.BindTexture` first: the upload landed on whatever texture was bound
and the weight texture itself never got storage → sampling = black → `splatSum=0`
→ the shader falls back to all-layer-0 → **the paint is invisible in 3D even though the CPU field+ TGA
are correct** (sculpt passed because `TerrainHeightfield.EnsureTexture` does bind). Rule:
every `GenTextures` MUST be immediately followed by `BindTexture` before `TexImage2D`/
`TexParameter`/`TexSubImage2D` — GL doesn't attach the new handle automatically. A related
case already fixed: `FromFile()` didn't mark a dirty rect, so an eager
`FlushTexture()` right after decode returned without creating the GPU texture (reloading the scene
→ the paint vanished); now `FromFile` calls `MarkWholeDirty()`.

**RANDOM TILING (anti-repetition, PBR panel → "Mapping → Random Tiling" — a CHECKBOX on/off; now `bool PbrRandomTiling`, uploaded 1.0/0.0, persisted as a float > 0.5 = on).**
`u_randomTiling`+ `randTiledUv()` in `objectPbr_fragment.glsl`: each tile's UV address
is hashed (fract-sin, deterministic — frozen across frames) → a 90° rotation (an index 0-3 from the
hash)+ a random offset; the warp is BLENDED back into the original tiling UV near tile edges
(a smoothstep 0..0.18 from the edge) so neighboring tiles always meet on identical
texel paths — seamless, no extra fetches/splits. Applied to the 6
albedo/normal/metal/rough/AO/emission maps and ALL splat layer samplers (each
through its own UV, the POM offset still subtracted afterwards). NOT applied to the
height map (unit 5) or the splat weight map (unit 10): the vertex stage displaces
through the RAW tiling in `pbrDisplace_vertex.glsl` — a per-tile warp there would
desync the POM march, relief shadows, and CPU geometry picking.
**UV ≤ 1 TILE GATE**: UVs spanning ≤ 1 tile (Box faces, Sphere — u_uvScale ≈ 1)
come back RAW without the warp — a single tile has no repetition to break up;
the warp would only rotate/shift the whole face AND rotate the normal map (the "PBR Box
went crazy" bug after the first random tiling; now Box/Sphere at tiling 1 is identical to the
plain path). **NORMAL CO-ROTATION**: the normal map rotates with the same tile angle
(`randTiledUv(..., out rot)` → a w-weighted tangent-space XY rotation) so
the bump lighting follows the rotated texture — also for splat layer normals
(`lrotS`). Property
`PbrRandomTiling` (default 0), upload `u.RandomTiling` di `DrawPbrPrimitive`,
persist `PbrRandomTiling` in SceneAsset+ save ×2+ load (clamped 0..1)+ Duplicate.
The "Random Tiling##object" slider in the PbrPanel mapping section+ included in "Reset tuning
to defaults".

**Three weight sources, max-blend (the brush always wins where it painted)**:
- **Manual paint** — `TerrainSplatField` (Engine/Objects/TerrainSplatField.cs, 256² RGBA
  CPU field): Paint/Erase/Smooth stamps on the selected layer via `SplatApply`, weights
  renormalized per texel so the sum stays 1; dirty-rect GL_RGBA8 upload; per-stroke undo
  (40 snapshots) via `SplatUndo/Redo`; each finished stroke bakes the field to
  `Artifacts/Terrain/<scene>/<object>_splat.tga` (32-bit RGBA, BGRA on disk) and points
  `SplatMapPath` at it — painted weights persist with the scene.
- **AUTO height bands** — `ComputeHeightBands` maps each texel's elevation (the SAME
  `TerrainHeightfield` the vertex stage displaces with, sampled through the terrain's
  OWN tiling ×BaseHeight+Offset) into per-layer smoothstep bands
  (`SplatHeightBands[l]` low/high, `SplatHeightLayerFeather` softness in world units,
  `SplatHeightLayerCount` 1-4 active layers). The strongest band owns the texel.
  Recompute is amortized (at most once per draw while `_splatBandsPending`); elevation/
  BaseHeight/Offset/tiling/band-param setters raise the flag. Bands never overwrite
  PAINTED state: `ComputeSplatBands` returns early when the field carries paint
  (`TerrainSplatField.HasPaint` — brush strokes, undo/redo restores, or a decoded
  splat file), and a band recompute over an unpainted field keeps that field
  unpainted (deterministic weights — the sticky `_bandsComputedPaint` lock prevents
  the recompute from ever clearing the flag).
- **AUTO slope layer (PBR)** — `TerrainSplatField.ComputeSlopeWeights` folds a
  STEEPNESS mask into ONE layer (1-3): weight = smoothstep(threshold, threshold+fade,
  1−N.Y) with N derived from the COMBINED displaced elevation (base×BaseHeight+  offset+ delta×amp — exactly the vertex stage's surface) via central differences
  in world units, through the terrain's own tiling. The fold is sum-preserving
  (target+= t·(1−target), others ×(1−t)) so paint/bands keep their share; painted
  fields are skipped (brush wins). Bands+ slope share ONE buffer
  (`_splatFieldForBands`): every rebuild rewrites the base first (bands or a neutral
  layer-0 fill) then re-folds the slope — the pass is NOT idempotent, never fold
  twice. UI: TerrainPanel "Auto layer from slope (PBR)" (Layer/Threshold/Blend).
  Persist ×5 site: SceneAsset `SplatSlope*`, save ×2+ load clamp, Duplicate.
  Plane-only (per-face Box/Sphere UVs carry no world slope meaning). CPU tests:
  SLOPE suite in `Tools/BrushLogicTest` (flat untouched / steep flank → 1 / sum=1).

**Viewport painting** (Terrain panel → "Terrain Paint"): brush session
(`EditorObject.SplatBrush`: mode Paint/Erase/Smooth, Layer 0-3, radius, strength,
hardness)+ `IDEBridge.TerrainSplatObject` — mutually exclusive with the sculpt session
(enabling one turns the other off). Same precedence as sculpt (gizmo/sun-handle first,
click-to-select blocked); the rubber-band MARQUEE is also suppressed while a terrain
sculpt/paint session is active (a terrain drag used to draw a stray selection
rectangle). Ring color green = paintable, red = painting, gray = projected.
Ctrl+Z / Ctrl+Y per-stroke undo. Painting works on FLAT planes too (exact local-space
ray∩plane fallback when no elevation field exists AND there is no sculpt delta — on
sculpted terrain the flat y=0 grid lies BELOW the surface, so a missed surface
ray-march must not fall back to it or the stamp lands through the rock at the wrong
spot; the brush ring just dims to the projection color instead).
Stroke end bakes ONLY when texels actually mutated: `TerrainSplatField.HasAnyEdits`
is set inside the stamp loop (a stamp that lands outside its texel window no longer
flips it), so a pick-miss stroke can never overwrite the previous splat bake with
band-only weights (`EndSplatStroke` guards on `HasPaint`+ logs
`stroke end: N texels mutated` — 0 means the stamp never landed).
Layer textures (albedo+ 5 optional PBR maps per layer 1-3, drag-drop `ASSET_IMAGE_PATH`)
plus per-layer tint live under "Layer textures"; "Auto layers from height" exposes the
bands; "Revert paint" reloads the stored splat file.

**Persistence** (SceneAsset+ save ×2+ load+ `EditorObjectManager.Duplicate`):
`SplatMapPath`, `SplatLayerAlbedo/Normal/Metallic/Roughness/Ao/Height` (layers 1-3),
`SplatLayerTints` (12 floats), `SplatHeightBandsEnabled/Count/Feather/Bands`.

---

## 6. Terrain System

### 6.1 Heightmap Terrain

- **Loading**: PNG/EXR heightmap → `MapLoader` → `TerrainChunk` grid
- **Chunked rendering**: Configurable `ChunksPerSide × ChunksPerSide` chunks
- **Height scale**: Multiplier for Y axis
- **Grid resolution**: Per-chunk configurable (default 32×32)

### 6.2 Dynamic Layer System

Height-based blending with configurable layers:

```
Layer 0: HeightMin=0.0, HeightMax=0.3  (Air)
Layer 1: HeightMin=0.2, HeightMax=0.5  (Dirt)
Layer 2: HeightMin=0.4, HeightMax=0.8  (Grass)
Layer 3: HeightMin=0.7, HeightMax=1.0  (Snow)
```

Each layer has:
- Albedo texture path+ PBR map paths (Normal, Metallic, Roughness, AO, Height, Emission)
- Tiling X/Y
- Height range (min/max)
- Blend sharpness (1=smooth, higher=sharper)
- Stochastic sampling toggle
- Full PBR tuning parameters

### 6.3 Slope Layer

Optional rock/cliff texture on steep surfaces:
- Enable/disable toggle
- Separate albedo+ PBR texture slots
- Slope threshold (0-1, default 0.35)
- Slope texture tiling

### 6.4 Terrain Painting

- **Brush tools**: Size, Strength, Softness, Falloff (Linear/Smooth/Sharp)
- **Paint layers**: Up to 4 custom paint layers with individual textures/tiling
- **Sculpting**: Height modification with brush
- **Smoothing**: Height averaging filter
- **Splat map**: Per-pixel layer weight painting
- **Heatmap visualization**: Layer weight overlay
- **Contour lines**: Topographic line overlay

### 6.5 Terrain Texturing

- **Triplanar mapping**: Projects textures from X, Y, Z planes (avoids UV stretching)
- **Stochastic sampling**: Random per-tile rotation to break repetition
- **Texture tiling**: Separate X/Y tiling per layer
- **Slope texture**: Separate tiling for cliff/rock surfaces
- **Parallax Occlusion Mapping**: Height-based surface detail

### 6.6 Texture Settings (per texture)

- Min/Mag filter: Nearest, Linear, Mipmap variants
- Wrap S/T: Repeat, Clamp, Mirrored Repeat
- Mipmap: Enable/disable
- Anisotropic filtering: 0-16x
- Auto-recommend button: Analyzes image and suggests optimal settings

---

## 7. Lighting & Shadows

### 7.1 Sun (Directional Light)

- Direction (Euler angles)
- Color (RGB picker)
- Intensity (brightness multiplier)

### 7.2 Local Lights (Point/Spot)

Up to `MAX_LOCAL_LIGHTS` per scene:

| Type | Properties |
|------|-----------|
| Point | Position, Color, Intensity, Range, Cast Shadow |
| Spot | Position, Direction, Color, Intensity, Range, Cone angle, Cast Shadow |

### 7.3 Cascaded Shadow Maps (CSM)

3 cascade levels for sun shadows:
- **Cascade 0**: Near (highest resolution)
- **Cascade 1**: Mid
- **Cascade 2**: Far (lowest resolution)
- **Blend zones**: Smooth transition between cascades
- **Bias**: Constant+ slope-scaled (prevents acne on slopes)
- **Shadow filtering**: Hard (PCF 1-tap) or Soft (Poisson 16-tap disk)
- **Debug overlay**: Colored cascade visualization (toggle with L key)

### 7.4 Local Light Shadows

| Type | Technique |
|------|-----------|
| Point | Cube map depth (6 faces), linear depth compare |
| Spot | Projective depth map, linear depth compare |
| **Bias** | Slope-scaled (`u_localShadowBias`, `u_localShadowPointBias`) |

### 7.5 Shadow Uniforms

Uploaded via `ShadowUniforms.UploadMain()`:
- `u_ConstantBias`, `u_SlopeBias`, `u_MinBias` — Sun shadow bias
- `shadowFilterMode` — Hard/Soft toggle
- `u_localShadowBias`, `u_localShadowPointBias` — Local light bias
- `u_CascadeOverlayAlpha` — Debug overlay strength

---

## 8. Post-Processing

### 8.1 Pipeline

```
SceneColorTex → Bright Extract → 5-Mip Reactive Chain (downsample → soften →
               additive upsample) → Bloom Composite → Auto-Exposure →
               ACES Tonemapping → Gamma → quad-draw copy back to scene texture
```

- **One shared processor**: `PostFxProcessor.Shared` serves both render paths
  (GameScene `PostProcessStack.RunStack` AND the editor shared-FBO viewport via
  `ApplyInPlace`) — auto-exposure keeps one continuous adaptation state.
- **Quad-draw copy-back**: the final result is copied with a passthrough quad draw,
  NEVER `glBlitFramebuffer` (the wrapper silently no-ops when the wgl pointer fails
  to load — this caused "effect works in debug panel but not in viewport").

### 8.2 Reactive Bloom

| Parameter | Default | Range |
|-----------|---------|-------|
| `BloomIntensity` | 1.0 | 0-2 |
| `BloomThreshold` | 0.5 | 0-2 |
| `BloomSoftKnee` | 0.15 | 0-0.5 |
| `BloomMips` (Radius) | 5 | 1-5 |

- Bright extraction: pixels above threshold+ soft knee (half res)
- **Mip chain**: 5 levels (½ → 1/32 res), each softened with 13-tap box blurs via
  ping-pong scratch targets (never sampling and writing the same texture)
- **Additive upsample**: Catmull-Rom 9-tap, blended ONE/ONE back down the chain —
  lower mips contribute tight hot cores, upper mips wide soft halos
- `BloomMips` truncates the chain: fewer mips = tight glow, more = cinematic halos

### 8.3 Auto-Exposure

| Parameter | Default | Range |
|-----------|---------|-------|
| `AutoExposure` | true | on/off |
| `AutoExposureMinExposure` | 0.2 | 0.05-8 |
| `AutoExposureMaxExposure` | 4.0 | 0.05-8 |
| `AutoExposureTargetLuminance` | 0.18 | 0.01-1 |
| `AutoExposureSpeed` | 0.6 | 0.01-10 |

- Measures scene average luminance via a dedicated 1/16-res luma texture (mip
  readback of its smallest level)
- Adapts exposure smoothly over time (exponential smoothing)
- Clamps to min/max range; live value shown in the Post FX panel

### 8.4 Tonemapping

- **ACES** filmic tonemapping (standard HDR → LDR)
- Applied after bloom composite

### 8.5 Gamma Correction

| Parameter | Default | Range |
|-----------|---------|-------|
| `Gamma` | 2.2 | 0.4-4 |
| `Exposure` | 1.0 | 0.1-4 (ignored while Auto Exposure is on) |

### 8.6 FX Debug Views

- **Viewport toolbar "FX Debug"** button cycles Scene → Composite → Output →
  Luma (auto-exposure input) → Bloom Mip 0-4, drawn in place of the scene texture
  with an amber stage badge (falls back to the scene when Post FX is off)
- **FrameBuffer Debug panel** shows live thumbnails of every intermediate target
  (composite, output, luma, all 5 bloom mips) plus a `Post FX chain:` liveness line
- All Post FX parameters persist symmetrically: panel changes → `settings.json`
  (project), project open → re-applied via `PostFxSettings.Apply`

---

## 9. Fog System

| Parameter | Default |
|-----------|---------|
| `useFog` | 0 (off) |
| `fogMode` | 3 (Exp2+ height blend) |
| `fogColor` | sky color |
| `fogDensity` | 0.001 |
| `fogStart` | 0 |
| `fogEnd` | 500 |
| `fogHeight` | 0 |
| `fogHeightRange` | 100 |

Modes: Linear (1), Exponential (2), Exp2+ height blend (3)

---

## 10. Sky System

### 10.1 Procedural Sky

- **Rayleigh scattering**: Sky color from sun angle
- **Turbulence**: Cloud-like wisps
- **Sun disk**: Size+ intensity control
- **Cloud layer**: Enable/disable, scale, speed, coverage

### 10.2 Skybox (Cubemap)

- 6-face cubemap loading
- HDR support

---

## 11. Camera System

### 11.1 Editor Camera

- **FPS mode**: WASD+ Mouse look
- **Speed**: Configurable move speed
- **Near/Far clip**: Configurable

### 11.2 Game Camera

- FOV, Near, Far (per-object properties)
- Position, Rotation stored in scene file

---

## 12. Input System

### 12.1 Keyboard Shortcuts

| Key | Action |
|-----|--------|
| F1 | Toggle wireframe |
| F5 | Preview mode |
| F8 | In-Game mode |
| ESC | Toggle menu (GameScene only — disabled for MainMenu/Loading) |
| H | Toggle selected object visibility |
| O | Toggle grid |
| J/K | Cycle through objects |
| L | Toggle cascade shadow debug overlay |
| M | Toggle local light shadow debug |
| , / . | Previous/Next scene |
| N | New scene |
| P | Toggle pause |
| 1/2/3 | Select translate/rotate/scale gizmo |
| Ctrl+1..6 | Add Plane/Box/Sphere/Camera/Light/Sky |
| Ctrl+Z/Y | Undo/Redo |
| Ctrl+C/X/V | Copy/Cut/Paste |
| Ctrl+D | Duplicate |
| Ctrl+S | Save All |
| Ctrl+Shift+S | Save As |
| Ctrl+N | New Scene |
| Ctrl+O | Open Scene |
| Ctrl+F1-F10 | Open Recent |

### 12.2 Mouse

- **Left click**: Select object
- **Right drag**: Camera look
- **Scroll**: Zoom
- **Shift+Scroll**: Move speed multiplier

---

## 13. Configuration Persistence

### 13.1 settings.json

```json
{
  "PostFxEnabled": true,
  "PostFxBloomIntensity": 1.0,
  "PostFxBloomThreshold": 0.5,
  "PostFxBloomSoftKnee": 0.15,
  "PostFxBloomMips": 5.0,
  "PostFxExposure": 1.0,
  "PostFxGamma": 2.2,
  "PostFxAutoExposure": true,
  "PostFxAutoExposureMin": 0.2,
  "PostFxAutoExposureMax": 4.0,
  "PostFxAutoExposureTarget": 0.18,
  "PostFxAutoExposureSpeed": 0.6,
  "FogEnabled": false,
  "FogMode": 3,
  "FogDensity": 0.001,
  "ShadowHardShadow": false,
  "CascadeOverlayAlpha": 0.15,
  "ConstantBias": 0.001,
  "SlopeBias": 0.005,
  "MinBias": 0.0005,
  "IDEFontSize": 14,
  "IDEFontPath": "",
  "ViewportFontSize": 14,
  "ViewportFontPath": "",
  "SelectionHighlightColor": [1.0, 0.8, 0.1],
  "EditorObjectHighlightColor": [0.1, 0.8, 1.0],
  "RecentFiles": []
}
```

### 13.2 Save Slots

- Save slots in `saves/slot_0/` through `saves/slot_9/`
- Each slot: `save.json`+ `thumbnail.png`
- Supports quick save/load

---

## 14. Texture Loading

### 14.1 Formats Supported

- PNG, JPG, BMP, TGA (via StbImageSharp)
- EXR (heightmaps)
- GLB/GLTF (3D models)

### 14.2 Texture Pipeline

```
Image file → StbImageSharp decode → RGBA byte array → OpenGL texture
```

### 14.3 Default Textures

| Name | File | Usage |
|------|------|-------|
| Fallback Albedo | `Artifacts/Textures/default.jpg` | Default object texture |
| Fallback Normal | Solid (0.5, 0.5, 1.0) | Flat normal map |
| Fallback Metallic | Solid (0, 0, 0) | Dielectric |
| Fallback Roughness | Solid (0.5, 0.5, 0.5) | Mid roughness |
| Fallback AO | Solid (1, 1, 1) | No occlusion |
| Fallback Height | Solid (0.5, 0.5, 0.5) | Mid height |
| Fallback Emission | Solid (0, 0, 0) | No emission |

---

## 15. UI System (Scene Elements)

Built-in UI elements for game HUD/menu:

| Type | Properties |
|------|-----------|
| Button | Text, Position, Size, Colors (normal/hover/active), Click behavior |
| Label | Text, Position, Font, Color, Alignment |
| SliderNumber | Min/Max/Step/Value, Track/Thumb colors |
| SliderText | Text options, Selected index |
| Checkbox | Checked state, Checkmark/Background colors |
| Dropdown | Options list, Selected index, Arrow colors |
| TextBox | Placeholder, Max length, Cursor color |
| Container | Children (nested elements) |
| Scene | Full-screen background element |

All elements support:
- Font selection (per-element font path+ size)
- Opacity (0-1)
- Alignment (Left/Center/Right)
- Hover effects (color transition)
- Auto-fill window / Auto-center
- Image background (Stretch/Zoom/Fill modes)


---

## 16. Container Overlay+ Bar Rendering (2D UI layering)

### 16.1 Container overlay frontmost rule (F8 fullscreen+ docked preview)

When a UI Container is opened in play/preview, it must render ABOVE the game HUD text and NPC bubbles and ABOVE other root-level non-container elements.

- F8 fullscreen path (`IDE.RenderInGameMode`): the authored UI container pass runs AFTER the HUD text mirror and dialogue overlay, so the open menu is unambiguously the top layer.
- Root-level element ordering: `RenderUIElements` partitions root children into non-containers-first and containers-last, so a game HUD Bar that shares a root with an open menu cannot draw on top of the menu even if authored after it in the hierarchy.
- Per-group bar sequence: each group runs its own full under→element→over pass (Background/Empty under, Progress in the element pass, ImagePath frame over) so the bar stays coherent with the menu z-order instead of punching through half the bar.
- Effect2DSystem and HUD Text Overlay draw with depth test off; draw order is the only layering signal.

### 16.2 Bar component render model

- `UIElementType.Bar` = 4 layers: `Background (-3)`, `Empty (-2)`, `Progress (-1)`, `ImagePath (0)`.
- The element pass draws only Progress; `DrawBarUnderLayers` draws Background+ Empty before the element pass; `DrawBarOverLayers` draws ImagePath after the element pass.
- A sprite-less Bar still renders via fallback colors (RGB `Vector3` 0..1) multiplied by Opacity — persisted as a float array in the scene file so old files load without change.
- Inspector "Colors (used when a layer has no image)" section sets per-layer fallback colors.

---

## 17. Performance Features

- **MSAA**: Configurable multi-sample anti-aliasing
- **Frustum culling**: Objects outside camera frustum skipped
- **Per-chunk frustum culling**: PBR plane chunk grids (Chunks per side > 1) culled
  individually from the view-projection matrix — flat multi-texture planes included
- **Per-chunk LOD**: PBR-plane terrain chunks pick ½/¼-segment meshes by camera distance
- **GPU occlusion culling**: `GL_ANY_SAMPLES_PASSED` per 2×2 chunk block on PBR planes
  (async result harvest, no pipeline stalls)
- **LOD**: Distance-based detail reduction
- **Shadow map caching**: Only rebuilds when light moves
- **Texture caching**: GPU texture IDs cached, no duplicate loads
- **Splat map caching**: Paint data uploaded once, not per-frame
- **Render timing panel**: Per-draw-call GPU timing

---

## 17. Build & Dependencies

### 17.1 .NET Dependencies

```xml
<PackageReference Include="ImGui.NET" Version="1.91.6.1" />
<PackageReference Include="StbImageSharp" Version="2.27.13" />
```

### 17.2 Native Libraries

- `cimgui.dll` / `cimgui.so` / `cimgui.dylib` — ImGui C bindings
- GLFW (loaded via `NativeLibrary`)
- OpenGL (loaded via `NativeLibrary`)

### 17.3 Build Command

```bash
dotnet build --nologo
```

---

## 18. Key Conventions

- **Face culling**: CCW (Counter-Clockwise)
- **Depth testing**: Always enabled
- **Blending**: Alpha blending for transparency
- **Coordinate system**: Right-handed, Y-up
- **Matrix order**: Column-major (OpenGL convention)
- **ImGui rendering**: After scene, before post-FX final blit
- **Shadow maps**: DEPTH24 format, separate FBO per cascade/light
- **Texture units**: Reserved allocation to avoid conflicts:
  - 0-7: Dynamic layer albedo
  - 6-8: CSM shadow maps
  - 9: Local point shadow (cube)
  - 10-13: Local spot shadow
  - 14-16: Point shadow maps
  - 20-23: Paint layer textures
  - 25-28: Legacy layer textures
  - 30-35: PBR texture arrays

---

*Generated for DarkEngine3D — OpenEngine3D game engine reference.*


> Scope note: this document describes a local OpenGL/C# desktop game engine. It is NOT a
> browser/web/Cloudflare Workers target. "Projects / Menu Scene Publishing" in this doc
> is about how the local IDE+ scene files+ catalogs are saved and organized — it does
> NOT describe hosting the engine as a web service.
