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
├── {Name}.projing         — Project metadata + scene inventory
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
| **Persistence** | All sheets + clips saved to `Assets/Sprites/sprites.sheets.json`, auto-loaded on project open |
| **Play Integration** | Play in Preview loads the selected animation clip |

### 2.5 Map Editor (2D Tilemap)

Tilemap editor rendering into the 3D viewport as an upright textured plane (`EditorObject` type `Map2D`). The grid appears at world origin; camera auto-switches to orthographic front view.

| Feature | Description |
|---------|-------------|
| **New/Resize Map** | Grid of empty tiles shown immediately in viewport; GameScene type enforced (warning otherwise) |
| **Tile Palette** | Auto-detected cols/rows from tileset image (read-only); multi-select (marquee) preserves block shape when stamping |
| **Tools** | Paint, Erase (with brush size), Fill (flood), Pick (default), Collision, Trigger — paint directly in the 3D viewport |
| **Layers** | Multiple tile layers, visibility/lock per layer, all visible layers render (stacked in depth, tiny lift per layer) |
| **Undo/Redo** | Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y over the 2D level (per-tile granularity) |
| **Collision Flags** | Per-tile-id collision toggles; dedicated Collision paint tool (click/drag toggles, hover preview shows add vs remove); full 3D translucent boxes with bright edges CENTERED on the tile; per-layer `CollisionTileIds` persist in the tilemap json; Show Collision checkbox persisted in settings |
| **Parallax Layers** | Image layers behind/in front of the grid: ScrollFactor, ZPosition, Alpha (per-vertex tint), WidthPx/HeightPx (0 = proportional to image aspect), RepeatX/Y (GL_REPEAT), TopPx offset, TileHorizontal wrapping |
| **Parallax Preview** | Panning the editor camera slides each layer by `-camX × ScrollFactor` (fractional UV phase = seamless wrap) |
| **Grid Overlay** | Show/hide tile grid + grid color, real-time; auto-hidden in preview/in-game |
| **Camera Start** | Per-map saved view; "Set Current View"/"Reset" in Grid Settings; Play in Preview restores it; auto-captured on first framing |
| **Player Spawn** | Draggable cyan cross marker in viewport (or "Set at Hover"); GameScene places the player there on Enter (unless a save slot loads) |
| **Save/Load** | `Assets/Maps/{Name}.tilemap.json` (carries tiles + parallax + spawn + camera start) and canonical scene `.ing`; autoload first map on project open |
| **Trigger Areas** | Dedicated Trigger tool: click-drag on the grid draws a snap-to-tile box; drag body to move, 8 handles to resize; Delete / Ctrl+C/X/V / Ctrl+D supported; amber translucent boxes (selected = brighter + white handles); edited in the Trigger Areas panel (see §2.9) |
| **In-Game Parity** | Parallax layers/textures sync every frame in preview mode; startup `-load=` in-game re-anchors camera (lazy reframe when tilemap adopts late) |

### 2.6 Player2D System (2D Character)

Player character object with capsule collider + animated sprite, spawned from a Start marker.

| Feature | Description |
|---------|-------------|
| **Player 2D Object** | `EditorPrimitiveType.Player2D` — added from the Hierarchy toolbar or Add-element dropdown (🏃 icon) |
| **Capsule Collider** | Feet-anchored capsule (Position.Y = capsule bottom), radius/height/visibility tunable in Inspector; rendered as a world-upright blue outline in edit mode (hidden in-game via `Editor2DAidsHidden`) |
| **Animated Sprite** | Sprite Sheet + Animation Clip pickers (from the Sprite Editor via the static `IDEBridge` registry, refreshed every frame in all modes); clip FPS/loop/reverse/speed respected; animation previews live in edit mode |
| **Sprite UV** | Frame UVs converted (`v' = 1 − v_raw`) for the top-row-first texture upload — sprite stands upright |
| **Start Object** | `EditorPrimitiveType.Start2D` (🚩 arrow marker) — the spawn point; Player2D teleports there (velocity + anim clock reset) when in-game mode begins |
| **Deferred Spawn** | `EditorObject.Player2DSpawnPending` static flag set on in-game entry; consumed by `Player2DSystem.Update` on the first frame AFTER the .ing reload re-creates objects |
| **Physics** | `Player2DSystem` — gravity (tunable per-player) + capsule-AABB vs collision-tile resolution (ground/ceiling on the active layer); runs only in preview/in-game |
| **Persistence** | Sheet/clip/height/capsule/gravity saved in the scene `.ing` via `EditorObjectData` |

### 2.7 Gizmo Z-Order

The transform gizmo is ALWAYS frontmost: 2D overlays (tile grid, hover highlight, collision box edges) draw with depth test disabled and would paint over an earlier gizmo, so the depth buffer is cleared after `EditorObjectManager.Draw()` and before `gizmo.Render()` in both the editor (no-scene) and in-scene render paths.

### 2.8 Inspector — Object Types

**Box/Sphere/Plane**:
- Transform: Position (XYZ), Rotation (Euler XYZ), Scale (XYZ)
- Pivot Position
- Color (RGB picker)
- Cast Shadow checkbox
- Texture slot (albedo) + Texture Settings (filter, wrap, mip, anisotropy)
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
- PBR Maps + Tuning (same as primitives)

**Map2D (2D Level)**:
- Tilemap binding (auto-created from Map Editor "New Map" / scene `.ing` restore)
- Tileset cols/rows + FlipV (read-only, auto-detected)
- Show Grid + Grid Color, Show Collision + Collision Color
- Grid Settings: camera start capture/reset, spawn info
- Rendered as upright world-space plane; object transform intentionally not applied

**Player 2D**:
- Sprite Sheet + Animation Clip combos (auto-select first item; clip summary shown when resolvable)
- Sprite Height, Capsule Radius/Height, Show Capsule, Gravity
- Glow (bloom) slider + Glow Tint picker + Flicker checkbox (see §2.10)
- Selection shows via line gizmos (no stencil outline — no solid mesh)

### 2.9 Trigger Area / Event System

Non-blocking event volumes on the tilemap: the player passes through them; entering/staying/exiting fires an ordered list of actions.

| Feature | Description |
|---------|-------------|
| **Trigger Area** | Rectangle in grid pixel coords (LeftPx/TopPx/WidthPx/HeightPx) on the tilemap; no physical collision — detection only |
| **Conditions** | On Enter / On Stay (with repeat interval in seconds) / On Exit; optional gate "only when moving right" |
| **Actions (18 types)** | Save Game, Save Checkpoint, Load Checkpoint, Change Map, Play Sound, Play Music, Spawn Effect, Spawn Object, Start Dialogue, Show Bubble, Hide Bubble, Start Cutscene, Camera Shake, Unlock Door, Give Item, Activate Quest, Complete Quest, Run Script — each with Param/Param2/Delay |
| **Wired Runtime** | Save Game (next empty slot), Save Checkpoint (records player position), Load Checkpoint (teleport to checkpoint, fallback = start point), Change Map (loads `Assets/Maps/{name}.tilemap.json` in place), Camera Shake (earthquake-style, intensity × duration), Start Dialogue (opens the Dialogue System conversation), Show/Hide Bubble (player-following speech bubble; Param2 = `type\|duration`) |
| **Checkpoint Chain** | Save Checkpoint → player position stored for the session; Load Checkpoint zeroed velocity + grounded reset; pit-death respawn prefers the checkpoint; checkpoint state resets on each new play session |
| **Camera Shake** | View-height-relative amplitude (7% × intensity), 3-layer noise + ±1.2° camera roll, quadratic decay, random seed per shake |
| **Visual Editor** | Trigger tool: drag-create (snaps to tile bounds), move by dragging the body, resize via 8 handles, Delete/Ctrl+C/X/V/Ctrl+D; "Show Triggers" checkbox (persisted); Trigger Areas panel: list + rename + enable, conditions, per-action editor with contextual params + reorder, precise geometry |
| **Rendering** | Amber translucent boxes drawn in edit mode only (hidden in-game via `Editor2DAidsHidden`); overlay projected via `SceneToScreen` so it sticks to the viewport image |
| **Persistence** | `Assets/Maps/{map}.tilemap.json` AND scene `.ing` (`Tilemap2DData.TriggerAreas`) — triggers load with the project |

### 2.10 Per-Sprite Glow (Emissive Post-FX)

Per-sprite emissive boost so only bright sprite pixels (fire, lava, candles) cross the bloom threshold — the rest of the sprite and scene stay normal. Requires Post FX enabled.

| Feature | Description |
|---------|-------------|
| **Glow (bloom)** | `Sprite2DGlow` / `Player2DGlow` (0-1) — vertex color multiplied ×1..×4; bright pixels bloom, dark pixels untouched |
| **Glow Tint** | `Sprite2DGlowColor` / `Player2DGlowColor` — colors the bloom (e.g. blue fire); brightest channel normalized to 1 at render, so any brightness of the hue works; white = natural colors |
| **Flicker** | `Sprite2DGlowFlicker` / `Player2DGlowFlicker` — organic fire breathing: 3 out-of-phase sine layers over absolute engine time (±22% around the Glow value), per-object seed from the name hash (two fires never sync), frame-gated via `Glfw.FrameId` for multi-pass consistency |
| **Baked to Vertices** | Boost/tint/flicker multiply the per-vertex tint (`aTint`) — the map2d shader has no tint uniform by design |
| **Inspector** | "Glow (bloom)" slider + "Glow Tint" picker + "Flicker" checkbox in both the Sprite2D and Player2D sections |
| **Persistence** | Saved in the scene `.ing` via `EditorObjectData` (Glow + GlowColorX/Y/Z + Flicker), symmetric save/load |

### 2.11 Dialogue System (Conversation + Bubble)

Data-driven dialogue: RPG conversation window + world-space speech bubbles, authored entirely in the editor.

| Feature | Description |
|---------|-------------|
| **Dialogue Assets** | `DialogueAsset` (Id, Name, StartNodeId, ThemeName) with `DialogueNode` pages — text, speaker, portrait, emotion, choices, auto-advance, per-node start/end actions; branching via NextNodeId / choice targets (empty = end). Stored in `Assets/Dialogue/dialogues.json` |
| **Dialogue Editor** | Panel (2D Sidescroller menu): asset list + add/duplicate/delete, node editor (speaker/emotion/portrait/text/next/choices/actions), speaker manager, theme editor (colors, fonts, typewriter speed), language dropdown + translation table, ▶ Start/■ Stop preview |
| **Conversation Runtime** | `DialogueSystem` — bottom window with portrait frame, colored speaker name, typewriter text, numbered choices (1-9 / arrows + E / Space next / Esc close); closes with a fade; freezes player input + editor camera via the `DialogueOverlay` modal gate |
| **Speakers** | Reusable `SpeakerData` (Id, Name, portrait, name color); portraits support emotion variants (`<name>_Happy.png` tried before the base) |
| **Themes** | `DialogueThemeData` with optional parent inheritance — window/border/name/text/choice/bubble colors, fonts, typewriter speed, optional background image; built-in Default + Medieval |
| **Bubbles** | `ShowBubble/HideBubble` follow any object (player/NPC) with fade in/out, auto-hide on duration/distance, type-tinted borders (Speech/Thought/Quest/Warning); also fired from trigger actions |
| **NPC Interaction** | `EditorObject.NpcDialogueId` (Inspector: NPC Dialogue section) — Sprite2D/Player2D with a dialogue id shows an "[E] Talk" prompt in range and starts the conversation on E |
| **Conditions** | Choice conditions: `level:5`, `flag:name`, `item:potion`, `quest:id`, `questdone:id`, `gold:100`, `var:name:10` — unknown conditions fail closed |
| **Actions** | Choices/nodes execute trigger-catalog actions (Give Item, Activate Quest, Change Map, Play Sound, Camera Shake…) — no new action types needed |
| **Localization** | `DialogueLibrary.CurrentLanguage` + per-language override tables (English/Indonesia/Japanese/Chinese/Korean/Thai/Vietnamese); untranslated text passes through |
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

- **Save All / Ctrl+S / F8**: Saves to current file; if no file active → opens Save As dialog
- **New Scene**: Clears active file (does NOT overwrite game.ing)
- **Save As**: Opens file dialog, saves JSON + thumbnail PNG
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
| `{Name}.projing` | Project metadata + scene inventory |
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

1. **New Project** (File > New Project): Pick folder → enter name → creates structure + copies fonts
2. **Open Project** (File > Open Project): Browse for `.projing` file → auto-loads `game.ing`
3. **Recent Projects**: File > Recent Projects submenu (persisted globally)
4. **Save All** (Ctrl+S): Saves scenes + updates `.projing` (LastSaved + scene inventory)
5. **Close Project**: File > Close Project (resets all editor state: scenes, selection, hierarchy, editor objects)

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
├── ── Current Project ──  (shows name + path + Close)
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
| Editor Terrain | `vertex_shader.glsl` | `terrainEditor_fragment.glsl` | Terrain with PBR |
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
   └── Local Shadows: Point (cube map) + Spot (projective) per light

2. Scene Pass → MSAA FBO (multisampled)
   ├── Sky (procedural or cubemap)
   ├── Terrain (heightmap + dynamic layers + PBR)
   ├── Objects (primitives + GLB references)
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
vec3 specular = D * G * F / (4.0 * NdotV * NdotL + 0.0001);
vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
vec3 Lo = (kD * albedo / PI + specular) * lightColor * NdotL;
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
  - All uniforms upload every draw (live, no mesh rebuild); persist symmetric (SceneAsset +
    save ×2 + load clamp, −1 sentinels migrate legacy scenes: BaseHeight inherits the old PBR
    peak, Displace starts 0 — shape unchanged).
  - Chunk AABB pad + plane picking proxy use the real max: base + detail + |offset|
    (`TerrainDisplacementExtent`); elevation texture load failure flattens only dedicated-
    source planes (legacy fallback planes render through the unit-5 branch).

### 5.3 Terrain PBR (terrainEditor_fragment.glsl)

Same BRDF as objects, with dynamic layer blending:

**Uniforms** (driven by first layer's PBR Tuning):
- `terrainPbrMetallic` — Global metallic (0-1)
- `terrainPbrRoughness` — Global roughness (0-1)
- `terrainPbrMetallicThreshold/Softness` — Smoothstep for texture
- `terrainPbrNormalStr` — Normal map strength (0-2)
- `terrainPbrAoStr/Brightness` — AO strength + brightness
- `terrainPbrHeightStr` — Height strength
- `terrainPbrEmissionIntensity` — Emission intensity
- `terrainPbrAlbedoBright/Sat/Contrast` — Albedo adjustment

**PBR Texture Arrays** (optional, per-layer):
- `sampler2DArray pbrNormalMap/MetallicMap/RoughnessMap/AoMap/HeightMap/EmissionMap`
- 6 texture units (30-35), 8 layers max
- Fallback to uniforms when no texture assigned (texture returns 0)

### 5.4 Local Light PBR

Local lights (point/spot) use Blinn-Phong for terrain, Cook-Torrance for objects:
```glsl
float spec = pow(max(dot(N, H), 0.0), 24.0) * 0.6;  // Terrain
vec3 specular = D * G * F / (4.0 * NdotV * NdotL);    // Objects
```

### 5.5 PBR Splat Terrain (paintable multi-texture plane)

An add-on layer over the objectPbr pipeline (never a replacement): an RGBA splat map
blends up to **4 albedo layers** while normal/metallic/roughness/AO/POM/CSM come from the
unchanged objectPbr shader. A plane with no paint and height layers off renders pixel-
identical to the standard look (zero regression).

**Shader**: `objectPbrSplat_fragment.glsl` (flat + displaced variants; programs
`GetObjectPbrSplatShaderProgram` / `GetObjectPbrSplatDisplaceShaderProgram`).

**Texture units**:

| Unit | Texture |
|------|---------|
| 0-6 | Standard PBR maps (albedo/normal/metallic/roughness/AO/height/emission) |
| 7/8/9 | CSM shadow cascades |
| 10 | Splat map — `GL_TEXTURE_3D` 16×16×16 RGBA, dynamic |
| 11-14 | Splat layer albedo × 4 |

**Layer painting** (PBR Material panel → "PBR Splat Terrain"):
- 4 layer slots: albedo texture (drag-drop from Asset Browser / browse / clear) + tint (ColorEdit3)
- `SplatTiling` — per-layer world tiling; `SplatPaintStrength` — brush strength
- Layer 0 without its own texture falls back to the object's albedo MAP (never blank white)
- Empty layer slots contribute zero weight (uniform `u_splatHasAlbedo`)

**Height Layers (auto-terrain by elevation)**:
- `SplatHeightLayersEnabled` — layer weights are auto-assigned by height-map elevation:
  layer 1 = valleys … layer N = peaks (soft smoothstep bands, `SplatHeightLayerCount`
  active bands 1-4, `SplatHeightLayerFeather` transition softness)
- Manual brush paint overrides locally (max blend), so the base terrain look is free
- Works without any paint; needs a height map (or sculpt buffer) for elevation data

**Slope Auto-Paint (rock on steep terrain)**:
- `SplatSlopeEnabled` — one splat layer (`SplatSlopeLayer`, 0-3) auto-blends onto STEEP
  geometry with no brush painting: slope = 1 − |N·up| computed from the displaced
  surface normal, so sculpted cliffs and hillsides get rocky automatically
- `SplatSlopeThreshold` (0-1, default 0.35 — 0 = any incline, 1 = vertical walls) and
  `SplatSlopeFeather` transition softness (smoothstep above the threshold)
- Overrides manual paint via max-blend AND attenuates the other layers (a full cliff
  renders fully rocky, not a muddy mix); evaluated AFTER height bands so rock wins on
  steep faces regardless of elevation
- An empty rock-layer slot never fires (`u_splatHasAlbedo` gate — no white cliffs);
  gate `splatActive` includes the slope toggle, so it works with zero paint
- **Slope mask heatmap** ("Show slope mask" in the PBR panel) — viewport overlay for
  visual threshold tuning: blue = flat, green = approaching the threshold, green→red =
  the feather blend zone, full red = full rock; works even with no rock albedo assigned
  (pure geometry preview). Editor aid only — transient, never persisted

**Triplanar sampling** (`SplatTriplanar`):
- Opt-in world-space X/Y/Z projection for all 4 splat layers — cliff walls get the rock
  texture's SIDE projection instead of the top texture stretched vertically; tiling
  becomes repetitions per world unit (shares the `SplatTiling` slider)
- Axis weights pow(|N|,4) (tight transitions, renormalized); overhangs (folded normal,
  `g.y < 0`) fall back to planar UV so the texture never swims while orbiting
- Layer-0 albedo-MAP fallback stays planar (the map owns its own authored UVs)
- Works for painted splats, height bands and slope auto-rock alike (replaces the layer
  sampling inside the same blend)

**Height sculpting (user-drawable)**:
- Runtime R8 buffer 512² (`EnsureSculptBuffer` decodes the authored height map once —
  sculpt SMOOTHS the authored terrain, it does not replace it)
- Dynamic texture (`GL_R8` + `TexSubImage2D`) replaces the height unit while sculpted →
  displacement + POM see the live surface with no shader changes
- Viewport brush modes: **Sculpt** (drag raise, Ctrl lower), **Paint layer** (Ctrl erase),
  **Smooth**, **Flatten** — Shift = soft, Ctrl+scroll = brush size; 3D ring follows the
  displaced surface (CPU raycast mirrors the shader height calibration)
- Per-stroke undo via bridge events (`OnPbrSplatPainted` / `OnPbrSculpted`)

**Optimizations (all per-plane, Inspector/PbrPanel toggles)**:
- **Per-chunk frustum culling** — chunk grid builds whenever `Chunks per side > 1`
  (flat splat planes included); Gribb–Hartmann AABB test from the view-projection matrix,
  analytic chunk bounds + Y padding so displaced peaks never pop at screen edges
- **Per-chunk LOD** (`PbrLodEnabled`) — LOD1/LOD2 meshes at ½/¼ segments; each chunk picks
  its level per frame from camera distance (`PbrLodDistance`, `PbrLodDistance2`)
- **GPU occlusion culling** (`PbrOcclusionEnabled`) — `GL_ANY_SAMPLES_PASSED` queries per
  2×2 chunk block, drawn invisible with color+depth writes off; results applied next frame
  (never stalls), miss = 2 force-draw frames
- Live info: chunks/cull counts in the Inspector ("Frustum cull: N/M skipped"),
  "LOD drawn" and "Occluded" lines in the PBR panel

**Height map input**: PBR panel slot (browse / auto-detect) **or** Inspector
"Displaced Plane Grid" → drag-drop a PNG straight from the Asset Browser (path setter
drops the CPU height caches so raycast/sculpt always see the new map)

**Persistence** (symmetric across SceneAsset, SceneManagerPanel save/load ×3 sites, object clone):
- `PbrSplatLayers` (albedo path + tint per layer)
- `SplatPaintedData` / `PbrSculptData` — base64, empty = never painted (no size cost)
- `PbrLodEnabled` / `PbrLodDistance` / `PbrLodDistance2` / `PbrOcclusionEnabled`
- `SplatHeightLayersEnabled` / `SplatHeightLayerCount` / `SplatHeightLayerFeather`
- `SplatSlopeEnabled` / `SplatSlopeLayer` / `SplatSlopeThreshold` / `SplatSlopeFeather`
- `SplatTriplanar`

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
- Albedo texture path + PBR map paths (Normal, Metallic, Roughness, AO, Height, Emission)
- Tiling X/Y
- Height range (min/max)
- Blend sharpness (1=smooth, higher=sharper)
- Stochastic sampling toggle
- Full PBR tuning parameters

### 6.3 Slope Layer

Optional rock/cliff texture on steep surfaces:
- Enable/disable toggle
- Separate albedo + PBR texture slots
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
- **Bias**: Constant + slope-scaled (prevents acne on slopes)
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

- Bright extraction: pixels above threshold + soft knee (half res)
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
| `fogMode` | 3 (Exp2 + height blend) |
| `fogColor` | sky color |
| `fogDensity` | 0.001 |
| `fogStart` | 0 |
| `fogEnd` | 500 |
| `fogHeight` | 0 |
| `fogHeightRange` | 100 |

Modes: Linear (1), Exponential (2), Exp2 + height blend (3)

---

## 10. Sky System

### 10.1 Procedural Sky

- **Rayleigh scattering**: Sky color from sun angle
- **Turbulence**: Cloud-like wisps
- **Sun disk**: Size + intensity control
- **Cloud layer**: Enable/disable, scale, speed, coverage

### 10.2 Skybox (Cubemap)

- 6-face cubemap loading
- HDR support

---

## 11. Camera System

### 11.1 Editor Camera

- **FPS mode**: WASD + Mouse look
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
- Each slot: `save.json` + `thumbnail.png`
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
- Font selection (per-element font path + size)
- Opacity (0-1)
- Alignment (Left/Center/Right)
- Hover effects (color transition)
- Auto-fill window / Auto-center
- Image background (Stretch/Zoom/Fill modes)

---

## 16. Performance Features

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
