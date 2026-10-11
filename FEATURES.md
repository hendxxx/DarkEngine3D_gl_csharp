# DarkEngine3D — Functional Report

> **Purpose**: Complete technical reference for reproducing this OpenGL/C# game engine.
> **Tech Stack**: C# (.NET 9), OpenGL 3.3 via raw bindings, GLFW, ImGui (dear imgui), StbImageSharp, System.Text.Json, Numbers (G + H autocorrelation)
> **2D Physics**: feet-anchored capsule vs tilebox (multi-map, slope-aware), Player2D subsystem
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
│   └── Visual/           — Camera, CSM, Lights, Skybox, PostFX, Billboard, HUD,
│                          Player2D system, 2D physics, Tilemap2D, TriggerEventSystem,
│                          Inventory/Shop/Quest/Dialogue/Weather FX, glow
├── Artifacts/
│   ├── shaders/          — GLSL shaders (vertex, fragment, geometry)
│   └── fonts/            — IDE fonts (copied to project on create)
└── bin/Debug/net9.0/     — Build output (exe runs from here)
```

Project Folder (user-created): `ProjectRoot/` + `Artifacts/Sprites/sprites.sheets.json` + `Assets/Maps/{name}.tilemap.json` + `Artifacts/Items/`, `Artifacts/Shops/`, `Artifacts/Quests/`, `Artifacts/Dialogue/` + `*.ing` scene files + `*.projing`.

## 1.1 Capsule-anchored coordinate model (2D)

Player2D physics is **feet-anchored**: `Position.Y` is the object's origin, but the capsule's **feet** sit at `Position.Y + capOffY` and the body center (capX) at `Position.X + capOffX`. All probes/resolves use the capsule's horizontal center + vertical span [yBot, yBot+height]. Results are converted back to `Position.X/Y` at the end. The sprite anchor follows via rendering — the collider lands on the marker regardless of the capsule nudge.

## 1.2 Capsule vs tilebox (simple collision, multi-map)

QSV's rule: **capsule + box collide → collision lands, DONE**. There is no union/merge UI. Collision lives in `CollisionTileIds` per tilemap layer; physics iterates ALL layers and ALL visible maps in the scene (active map first). Every map bakes at world origin → a shared world↔grid mapping, so a solid tile painted on any level stops the capsule wherever it stands.

**Grid mapping (renderer convention):** grid row `ty` occupies world Y in `[(H-1-ty)*cell, (H-ty)*cell]`, row 0 = top of the map. World column `tx` occupies `[(tx+1)*cell]`. World origin (0,0) = bottom-left of the map. Row-to-world flip is consistent across renderer bake, collision preview boxes, and the physics probes.

**Per-tile top-edge override (kiri/kanan terpisah — collision box slope):** each collision tile normally spans its full cell (top = 100%); a per-tile L/R top override makes the box a **ramp/wedge** (e.g. 0.3 left, 1.0 right = a slope the player walks up). Key = tile index `y*Width+x`; value = `(LeftFrac, RightFrac)` of the cell height, 1.0 = full. Tiles without a key stay full-height (backward compat). Stored in `TileLayer.TileTopOverrides`.

**Physics step (per capsule, per frame):**
1. **X**: swept AABB move + X-push against walls (Y-aware: only tiles whose band overlaps [yBot,yTop] block). Ramp-aware: the leading edge's SIDE of the tile blocks only if feet are **below** the tile's top edge at that X (walking onto a lowered/wedge top = no wall hit; the step-up + landing logic handle the stride instead).
2. **Step-up** (optional, when grounded + moving + StepHeight>0): probe the column **0.05 ahead** of the leading edge, read the interpolated top. If that top is **within stepH of the feet** AND above the old feet, lift the feet onto it (yBot/yTop raised). Kept as a floor so gravity doesn't re-pull through it; a step-up frame counts as grounded.
3. **Y**: gravity → velY; climb/descend sweep-head/fall feet probe across the rows the feet cross (topmost solid wins per map; across maps the HIGHEST candidate lands, LOWEST ceiling hits); position updated to `newY`.
4. **Step-up carry + landing**: the lift persists as `stepFloor` (newY clamped below). The landing probe uses the **resolved X (newX)** and the **lifted yBot**, not the frame-start position — otherwise a step-up frame reads as "came from below" and never lands → stuck.
5. **Boxes** as solid AABB colliders (same swept logic, land on top / bump ceiling).
6. **Pit death**: feet well below 0 → respawn at checkpoint/start.
7. Step-up only fires while grounded + moving; the X-push is Y-aware via the interpolated edge top; the wall push uses `dir * r` (right wall vs left wall) correctly.

**Step-up feature** (user: "tanjakan sedikit langsung naik saja"): walking INTO a collision tile whose (possibly interpolated) top is only ≤ StepHeight above the feet LIFTS the feet onto that top instead of stopping the character; the Y-resolve keeps grounding. Only while grounded and moving — a jump passes over anything taller anyway.

## 1.3 Two-pointer controls (GUI file authors separate)

- **Sprite Editor** (2D sprite sheets + animation clips): cropping/picking from a sheet. Tilemap painting uses a **separate Map Editor pick** (painting palette), not the Sprite Editor picker.

## 1.4 2D pixel collision (PixelSprite/box2D)

`PixelSprite` = grid of pixels with per-cell collision (pick all rows of that pixel). Use the **text rendering filter rule** (any unicode line wider than area = break text) for context.

## 1.5 Raycast of collision tiles (raygrid)

`RaycastTileGrid` — query tile collisions by grid coordinates, returning per-pixel collision status. Used by multi-map sweep + character collider logic.

## 1.6 Internal per-tile node cleaners

-`SlabClean()` (Verts3D) — re-derives rig counts from vertex/floor/triangle blob.
-`TileGridToVerts` (generate .l + material + store)+.`TriangleSlab` → store Verts3D entries in per-triangle color/t for fast reconstruction.

## 1.7 Scene briefly separate (no persistence)

`SceneArtifact` is auto-cleaned (WIP); it's a transient 2D pixel collision node, not stored.

## 2. Project reference

DarkEngine3D_gl_csharp/Engine. Prose references use relative paths from the repo root.

---

## 2. Game Engine Rules

### 2.1 Math constants (NOT mathematical geometry).

`Math.PI` / `MathF.PI`, `Math.Sin` etc. — angle in radians; **angle from** = heading to forward positions (not from). Vertex level: enabled by recommended Creative Coliseum workflow. By default, for "original C++/DX9 shader calling convention" — see documentation page.

#### 2.1.1{Back to top} `Angle.cpp` — `C52` clamp; `Math.PI`, `MathF.PI`; `sin`, `cos`, `tan`, `System.Math` API-s.

### 2.2 Build targets & distro folders

`x64` folder in `Artifacts/shaders/` — load via `SafeLoad(Artifacts/shaders/...)`. Copy-to-output: `<None Update="..."><CopyToOutputDirectory>Always</CopyToOutputDirectory></None>` in csproj. **Otherwise file not copied → shader program ==0 → silent skip → objects disappear without error.**

### 2.3 Shader convention

**CCW culling** default; enabled Z test; alpha blending; see `T` modifier incorporated in standard shader chain in renderer for float,Z<4; alpha component requirement explicit. **Clip coordinate convention**: normalized device coordinate standard (PC/3DQP: −1..1).

### 2.4 Standalone architecture

Separate `Saving/Loading` (EditorObject → Registered on `ObjectObserverService`) vs `Add(TilemapCollisionLayer)` vs runtime scene tag. Persist: original 1.0 vs duplicate layouts. `ChangeSceneAfterPlayerEvent` / `ChangeSceneAtLoadRelease` persist scene-specific parameters; on drag-proxy event, update `SceneObject` rule + scene focus-owner.

Edited by **onic engineer**: Section bits, stack sizes, traps documented from CAP 00 5*DI**-G 'tool' can execute `PD Top` operand metadata while flags remain.
