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

## 1.0 No absolute paths in project JSON (drive-letter-free stores)

Every path stored in project data files — `*.tilemap.json`, `*.ing`, `*.projing`, `*.sheets.json`, `*-sprite-anim.json` (tilemsets, parallax layers, sprite sheets, animation exports) — MUST be **relative** (project-relative, e.g. `Artifacts\\images\\Tiles\\Tiles.png`), never an absolute `C:\...` path. Stores are shared between machines and the exe folder can move.

- **Write side:** file pickers return absolute paths; convert BEFORE storing with `PathHelpers.MakeProjectRelative(path)` (`Engine/Helpers/PathHelpers.cs`) — project-relative when a project is loaded, exe-relative fallback otherwise. All Map Editor tileset/parallax set-points and Sprite Editor import points (dialog, drag-and-drop, Save As Pattern) already do this.
- **Read side:** resolve with `PathHelpers.ResolveProjectAsset(path)` — tries legacy absolute as-is → project root → exe folder → seeded by filename under the project's `Artifacts/`. Used by tilemap load, tileset texture load (panel + `EditorObject.EnsureMap2DTilesetTexture`), parallax cache (`GetParallaxTexture` — resolve BEFORE cache lookups so keys match), sprite preview textures, and weather/effect sheet-pixel profiler (`Effect2DSystem.GetSheetPixels`).
- **Auto-migration:** `Tilemap2D.FromData` / `SpriteSheet.FromData` call `MakeDataPathRelative` on every stored path at load — old saves that still carry `C:\` are normalized when opened, and the next save writes them clean. Json stores must NEVER be edited to keep absolute paths.
- **Cache-key rule:** any texture/path cache must resolve the path FIRST and use the RESOLVED path as the key consistently in store AND lookup (`LoadParallaxTexture`/`LoadPreviewTexture` pattern) — otherwise relative-stored paths miss the cache forever (symptom: art silently missing). `IDEBridge.SyncSpriteRegistry` resolves too, for the in-game Player2D registry.

## 1.0.1 Sprite grid detection + Column Offset (per-sheet leading columns)

`SpriteGridDetector.Detect(pixels, w, h)` reads a sheet's grid from pixel evidence (standard 8/16/32/64 cell sizes preferred) and returns `SpriteGridFit` (Cols/Rows/FrameW/FrameH/OffX/OffY/PadX/PadY + `ColOffset`).

- **`SpriteSheet.ColOffset`** (in cells): every row is shifted right by `ColOffset × (FrameWidth + PaddingX)` on top of `OffsetX` before the first sliced frame — for art whose first inked column is not the grid's first cell. Applied in `GetFrameUV`, `BakeUniformFrames`, the Sprite Editor grid overlay, the frame preview UV, the atlas re-bake, `IsFrameEmpty` and the pattern-export row scan; a site that misses it deslices that sheet.
- **Persistence**: `SpriteSheetData.ColOffset` is `int?`, written only when set (`JsonIgnoreCondition.WhenWritingNull`). NULL = the file predates the field → the loader PREDICTS from the image; an explicit value (including 0) is honoured verbatim.
- **Prediction**: the detector predicts `ColOffset` = whole empty cells in front of the first inked column (only when the grid resolved to origin 0, at least one full cell, never more cells than columns). A detected lead-in that became `OffX` predicts 0, so a sheet is never double-shifted.
- **Resolution order** (import, auto-fit, load, pattern bake): the value in the sheet's own `<image>-sprite-anim.json` wins; prediction from the pixels is the fallback.
- **`Engine/IDE/Panels/SpritePatternExport.cs`** (pure file/image work — no ImGui, no GL, so a harness can exercise it) owns export I/O: `ExportPathFor`, `TryLoadExport` / `TryLoadColOffset`, `PredictColOffset`, `SaveGridToExport`, `CreateExportIfMissing`. It finds the exact `<image>-sprite-anim.json` first, then legacy sibling exports matched by the sheet entry's image path or name.
- **Grid edits write the export**: changing any grid field in Sheet Settings, or pressing Auto Detect, calls `SaveGridToExport` — the edited grid (incl. ColOffset) lands in the export that describes that image, re-baking its uniform frames when the grid changed. An existing file is updated, never duplicated.
- **Create when missing (user request)**: a grid edit / Auto Detect / plain image import writes `<image name>-sprite-anim.json` next to the image when none exists — one sheet entry, no clips (an export is a bake input), project-relative `ImagePath` (drive-letter-free), NEVER overwriting an existing file and creating nothing at all when the image is absent. Import reads that sibling export back (grid, frames, master box, flips + its clips, baked via `BakeSiblingExportClips` → `SaveAllSheets()`), so exported art re-imports exactly as authored.
- **Auto Load All Files keeps what already exists (user request "jika clipnya sudah ada di skip saja")**: the folder sweep loads `sprites.sheets.json` first, then every clip whose name is already present AND whose baked atlas PNG is still on disk is KEPT — never rebaked (`HasUsableBakedClip`). Only clips that are missing, unbaked, or pointing at a deleted atlas are created. The targeted Save As Pattern merge passes `skipExistingClips: false`, so the pattern it just wrote still refreshes. Status/log report `N clip(s) baked, M existing kept`.

## 1.1 Recent editor/gameplay additions (session notes)

- **Disable Zoom**: `Camera.OrthoZoomDisabled` (per project, settings.json `OrthoZoomDisabled`). True = scroll-wheel ortho zoom inert + viewport "Ortho Zoom" slider greyed; `ApplyZoomLimits` forces max = min so every clamp path holds one height. Checkbox in IDE Settings → Camera Zoom (Min/Max sliders + Reset disable with it); stored Min/Max are never overwritten — unchecking restores the previous range. Flag loads at boot / project open (before ApplyZoomLimits) / project close.
- **F1 = stats overlay toggle** (two-way): F1 hides or re-shows the in-game FPS/TRIS/Objects/POS panel (`Bridge.ShowInGameStats`, persisted). F8 remains in-game mode only. The panel stays click-to-hide; F1 is the only way back without the settings menu.
- **Backtick `` ` `` removed from mapping** — no key conflict.
- **Scene View editor stats**: GLTF counters legitimately read 0/0 in pure 2D scenes; new bridge counters (`EditorObjectTotal`, `EditorObjectTotal2D`, `EditorObjectDrawn2D`, stamped from `EditorObject.Cull2DTotal/Culled` — no extra render cost) feed an "Editor Objects" row in SceneViewPanel and replace the misleading "Animated: 0 / Static: 0" when a scene has no GLTF agents.
- **Animation Clip follow-through**: switching the Player 2D "Animation Clip" combo updates every locomotion action whose clip follows the old base clip (empty or equal, same sheet) — the always-on Idle action previously kept the old clip, so the change was invisible. Actions with their own clip are untouched.
- **Pit-death respawn ground-robust**: respawn first checks the spawn column's topmost ground (`ColumnGroundTop`, all collision maps, ramp-aware); a hole redirects to the nearest column with ground (±40 cells, closest side), then the highest Box top. One fatal fall = exactly one respawn; gravity untouched (the fall itself is free-fall to the threshold).
- **Frame Profiler sparse spikes**: the "Sprite registry sync" row is always drawn (never conditional on the instantaneous value — it reads 0.00 on 7 of 8 throttled frames = flicker); it shows `FrameProfiler.MaxRecent(HistSpriteRegistry, 8, current)` = the peak over the throttle window, stable between bursts. Diagnose regressions via the history average.
- **Item / Shop Editor behave like every other panel** (user: "tidak tersimpan di imgui, seharusnya setiap dibuka bisa langsung terbuka"): both default `_visible = true` — they were the only two panels closed by default, so their windows never reached `Begin()` and never registered in `imgui.ini` (menu entry without ✓, nothing on screen at startup). The menu toggle now also requests focus on the closed→open transition only (`_focusPending` → `ImGui.SetNextWindowFocus()` before `Begin`, never every frame) so a window restored into the same floating rect as another panel comes to the front instead of hiding underneath it — the saved layout had Item and Shop Editor at the exact same `Pos`/`Size`. Both call `IDE.PanelFocus.Notify("Item Editor" / "Shop Editor")` after `Begin`, so the last-focused restore (startup + return from F8) tracks them too.
- **Recent Projects switching** (project A → B via the File menu): PersistEditorData → ProjectManager.CloseProject → OpenProject(B) — exactly one OpenProject, never two (a double call re-saves the loaded B over the old A's data).
- **Terrain "Revert Paint" actually fires now (the "it never triggers" bug)**: the confirm modal was opened with `BeginPopupModal("Revert paint?", ref _confirmRevertSplat)` where that SAME flag had just been set to `false` by the `ImGui.OpenPopup(...)` line above — ImGui 1.91 CLOSES a modal whose `p_open` is false, so the modal body never ran, `RevertSplat()` was never called, and the `.tga` kept every stroke (the button looked completely dead). The panel now passes a LOCAL `bool revertModalOpen = true` (the pattern `MapEditorPanel` uses). Behind the dialog, two blockers in `EditorObject` were removed: (1) the bake-file owner registry is keyed by the RESOLVED ABSOLUTE path (the scene stores an absolute `SplatMapPath`, the bake a relative one — the same file must land on ONE key, or a duplicate's shared bake gets cleaned while the duplicate still points at it) and a disposed object RELEASES its claim (a scene reload / delete used to leave the file looking "shared" forever, so the revert skipped the clean silently); a delete undo re-claims it via `EditorObjectManager.Insert` → `ReclaimSplatBakePath`. (2) `RevertSplat` now also cleans the object's OWN bake file `Artifacts/Terrain/<scene>/<object>_splat.tga` when the scene stores no path (the bake path is written only when the scene is saved, so an unsaved paint lived on disk with nothing referencing it) — the strokes really leave the file, and the one-level undo restores them. `OwnSplatBakeHasPaint()` lets the dialog tell the user the file on disk still carries paint.

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

## 1.8 2D lighting (Direct / Point / Spotlight)

The sidescroller passes are **lit by the existing Light editor objects** — no new object type, no extra serialization. `SpriteLighting2D.Capture(light)` runs once per frame in `EditorObjectManager.Draw` (right after the 3D light upload) and snapshots the sun + every collected Point/Spot marker; `Apply(program)` uploads that snapshot once per program per frame.

- **Direct** = the scene sun (`Lights.SunDir` / `LightColor`, i.e. the Direct marker or the procedural day/night). Only its XY part drives the 2D shading, with a nominal +Z so flat quads stay lit.
- **Point** = marker position, `LightPointRadius` falloff (`(1−d²)²`, same curve as the 3D local lights). **Spotlight** = the same falloff narrowed by `LightConeAngle` in XY, aimed along the marker's `WorldLightDirection`.
- **Surfaces**: map tiles, parallax layers, Player2D/Sprite2D, portals, projectiles and world particles all share `SpriteLighting2D.GlslLighting` in their fragment shader (sprite + particle shaders draw it from one const, so they cannot drift apart). Editor aids (collision boxes, trigger volumes) and weather overlays (rain/snow tint, front fog) call `ApplyUnlit` and keep their authored colors.
- **Normal maps**: sprites sample the baked `<sheet>Normal.png` atlas (`SpriteAtlasBaker` already wrote them) on texture unit 1; `SpriteLighting2D.TryGetNormalTexture` loads it lazily per sheet, caches by resolved image path and re-reads it after a re-bake (`IDEBridge.NotifySpriteTextureReloaded`) or a project switch. Sheets without a normal atlas (tilesets, parallax) shade with a flat (0,0,1) normal. The atlas is baked in image space, so the shader flips Y when converting to world space. A single flag, `SpriteLighting2D.NormalMapsEnabled` (IDE Settings → Rendering → **"2D Normal Maps (sprites)"**, persisted per project as `SpriteNormalMaps2D`), turns the normal textures off: every 2D surface then shades with a flat (0,0,1) normal and no `<sheet>Normal.png` is loaded — lighting/falloff keep working. Re-applied at boot, project open and project close.

## 1.9 2D sky: sun & moon on a visible arc (2D only)

The procedural sky positions the sun/moon from the camera's real view direction, so a level camera (looking straight ahead, 60° FOV) keeps them outside the visible sky — a perspective view of the same scene showed them, the 2D view never did. `SkyRenderer` now draws its own sun/moon discs when the camera is **orthographic**, and nothing changes for perspective (3D):

- **Position** comes from the scene's real sun direction (`Lights.RealSunDir`, so time-of-day AND the Sun pitch/yaw overrides apply): `x = −dir.x` — the sun crosses left → right between 06:00 and 18:00. The sun rides an upper arc (`SunArcBottom … SunArcTop`), the moon a lower arc (`MoonArcBottom … MoonArcTop`), so day and night read as separate bands. The sun shows above the horizon, the moon below it. The moon uses `RealSunDir` and NOT `SunDir` (that one is the active shading light and flips to the moon at night).
- **Draw**: screen-space quads via `sky2d_celestial_vertex/fragment.glsl`, alpha-blended, depth test off — a warm core + soft halo from `SkySettings.Sun.Color/Size`, and the moon samples `SkySettings.Moon.TexturePath` (the grey moon texture) tinted by `Moon.TintColor` at `Moon.Size`.
- **No duplicate sun**: the procedural sky's own sun/moon/rays/eclipses are suppressed in ortho through the `hideCelestial` uniform (`realtime_fragment.glsl`) — clouds and stars stay.

## 1.10 2D object placement always lands on Z = 0

In 2D (orthographic camera) every newly added object spawns on the Z = 0 world plane: `IDEBridge.GetGridSpawnPosition` forces `Z = 0` when the camera is orthographic (X/Y still follow the camera so the object appears where the user is looking). The ortho camera parks far on +Z looking back at the level, so "spawn at the camera position" used to put new objects at Z ≈ 60 — in front of the tilemap, off the 2D plane. This covers every creation path (Model menu, viewport +Box/+Sphere/… buttons, Hierarchy quick-add, GLB drop); perspective (3D) spawning is untouched. Sprite2D placement and Map2D creation already used Z = 0.

## 1.11 Scene game type (2D SideScroller / 2D Top Down / 2.5D / 3D)

The Add New Scene / Edit Scene dialogs carry a **Game Type** combo for Game Scene entries (`IDEBridge.GameType`, labels `2D SideScroller`, `2D Top Down`, `2.5D`, `3D`). Menus and loading screens have no game type (the combo is disabled for them). Scene Manager shows the short badge in its new **Game** column, and the choice persists per scene in `SceneAsset.GameType` using stable keys (`2d_sidescroller`, `2d_topdown`, `2.5d`, `3d`).

- **New-scene camera**: a 2D type creates the scene with the ortho level-editor front view (position `(0,0,300)`, yaw `180`, pitch `0`, ortho half-height `32`); 2.5D/3D keep the current pose but force perspective. Only creation is configured — selecting an existing scene always restores its own saved camera, and editing the type of an existing scene never moves the camera.
- **Legacy files**: a missing key is inferred from the saved editor camera (ortho → 2D SideScroller, otherwise 3D), so old `.ing` files keep their meaning without re-saving. Every save path (Save All / Save As, and the Hierarchy single-scene save) writes the key.
- **Metadata today**: the type labels/persists the scene; it does not change gameplay systems yet — 2D Top Down still runs the existing sidescroller player physics.

## 1.12 Image slicing (9-slice) + hover/clicked state art

UI elements can draw a sliced image instead of a stretched bitmap. `UIElement.SliceEnabled` + the source window (`SliceX/Y/Width/Height`, in texture px, clamped to the bitmap and offset into the atlas crop when `UseAtlasCoordinates` is on) and four border insets (`SliceBorderLeft/Right/Top/Bottom`) define a 3×3 grid in the Inspector. Every value persists through `SceneElementData` / `SceneAssetSerializer` and `DeepCloneFrom`.

- **The rule is the classic 3×3 grid**: `A C G I` are fixed corners, `B H` stretch along X only, `D F` stretch along Y only, `E` stretches on both axes. Corners are fixed in shape but scale with the element through one uniform factor (`min(elemW / srcW, elemH / srcH)`), so a 100×100 element keeps thin borders while a 500×500 one gets proportionally thicker borders — per-axis scaling is deliberately NOT used (a tall, narrow button inflated just the top/bottom band into huge blocks). When the element is smaller than its own borders the bands shrink proportionally and keep a 1 px middle.
- **Hover / Clicked / Selected art**: `HoverImagePath` / `ClickedImagePath` / `SelectedImagePath` (+ optional `*UseAtlasCoordinates`, `*AtlasImagePath`, `*AtlasX/Y/Width/Height`) override the normal bitmap per state; a state with nothing set falls back to `ImagePath`. Resolution order in `GetStateImage(pressed, hovered, selected)` is pressed (clicked) → selected → hovered. **Selected** is the keyboard-focus state: it is driven by arrow/Tab navigation (MainMenuScene's menu selection and the IDE in-game keyboard focus), so an image-only menu gets a real focused look — author it exactly like Hover (same picker section in the Inspector). In atlas mode a blank path or blank atlas image falls back to the element's default atlas image and, when the state crop is empty, to the element's own crop, so a hover frame can live in the same atlas sheet as the idle frame.
- **Auto-detect borders**: `Engine/Visual/NineSliceDetector.cs` samples the selected window's pixels (column/row colour profiles, ignoring the outer quarter so corner decoration cannot skew the opposite axis) and fills the four insets; the Inspector also fills the source window when it is still 0×0.
- **Visual preview**: the Inspector draws the source window with the green cut lines, `A`–`I` labels, a hover rule tooltip and draggable handles on each inset (drag a line/handle to write that border in source px); the handles are hit-tested so a drag keeps tracking outside the box.
- **Selection outline is a global editor preference**: IDE Settings → Interface → **Use Border When Selected** (`SettingsData.ShowSelectionBorder`, also on `IDEBridge`). OFF hides the cyan wireframe/tint/corner brackets for every UI element while the resize handles stay usable; it is per-project in `settings.json` and restored by the settings Reset.

## 1.13 Main menu owns the keyboard

Whenever a `MainMenuScene` is visible and no overlay (Settings / Load Game / confirm) is open, the menu takes ALL keyboard input; nothing else in the scene reacts to keys.

- **Arrow navigation is the default**: Up/Down (also W/S and Left/Right) move a selection over the clickable elements registered with the HUD, wrapping around at both ends. The selection starts on the first entry and is highlighted through `HUD.DrawButtons(keyboardSelected)` (glow + sidebar pulse). Moving or clicking the mouse hands the highlight back to hover; the next arrow/Enter press takes it back.
- **Enter = click**: Enter or Space invokes the selected element's `OnClick` — the same delegate the mouse uses, so every behavior string (`startgame`, `scene:Name`, `loadSlot:N`, `cycleSetting:N`, `timer:…`) works unchanged. Elements with no click behavior are selectable but do nothing.
- **Esc toggles the menu**: with no overlay open, Esc hides the menu UI (buttons + images; the animated background keeps running) and Esc again brings it back. Overlays still consume Esc first — Settings, Load Game and the confirm dialogs close instead of hiding the menu, and the mouse click pass is skipped while the menu is hidden so invisible buttons cannot be clicked.
- **Edge-detected only**: every key is tracked with a was-down flag, so a held arrow steps once and a held Esc toggles once instead of repeating every frame.
- **Editor preview unchanged**: keyboard selection only applies while `InGameActive` is true; the IDE viewport preview keeps pure mouse-hover highlighting.
- **Image menus are navigable too**: the selection walks every VISIBLE element that has a click behavior and art — text buttons (HUD-rendered) and image/atlas elements (drawn by the scene). A selected image element draws its hover art and a pulsing accent frame placed just OUTSIDE its rect (`HUD.Flush` draws images after boxes, so an inside frame would be hidden).
- **The list follows visual order**: `RebuildMenuNav` walks the hierarchy and reproduces the HUD button index with the same conditions the HUD registration uses (visible, text non-empty, no `ImagePath`). Invisible elements are skipped for selection but their subtree is still walked, otherwise the HUD index mapping desyncs.
- **Image hover / clicked art now applies in-game**: the menu renderer resolves `GetStateImage(pressed, hovered)` for every clickable image element (keyboard selection counts as hovered), crops atlas sub-rects through `HUD.DrawImageUV`, and mouse presses invoke `OnClick` — previously this only worked in the editor preview.
- **No other key consumers in the menu path**: the main menu scene is the only reader — there is no pause, gameplay, dialogue or container-keyboard handling in it, so navigating the menu is the only thing the keyboard does while it is on screen.
- **Per-container input capture**: any `UIElementType.Container` has **Capture Input While Visible** in the Inspector (`UIElement.CaptureInputWhenVisible`, persisted in `SceneElementData`). While the container is visible it owns the focus: `UIElement.HasVisibleInputCapture()` walks the tree (a hidden element never captures, and its children are ignored too — except a `Scene` root, whose `IsVisible = false` is a hierarchy convention and must not hide the menu underneath), `SceneManager` folds it into `IDEBridge.MenuOwnsInput` — checked against the active scene root and the selected editor scene root — and both `Player2DSystem` (movement, run, jump, bound action keys, portal interact) and `GameScene` (camera look / key update) stop responding. Menu elements inside the container keep working; the mouse still drives the menu itself because only gameplay action paths are gated. `SceneManager` logs `[SceneManager] menu input capture: ON/off (menuScene=…, captureContainer=…)` whenever the state flips, which is the quickest way to confirm the flag reached the runtime.
- **2D side-scroller behaves like 3D while a menu is up**: `SceneManager` refreshes `IDEBridge.MenuOwnsInput` from the active scene type (`MainMenuScene` / `LoadingScene`) every frame before the 2D systems run, and `Player2DSystem` ORs it into `inputFrozen`. In 3D there is no player to react, so a menu left the world static; in 2D the character used to keep walking/jumping behind the scene because `Player2DSystem.Update` runs whenever `InGameActive`/`IsPreviewMode` is set, independent of the active scene. The freeze covers A/D + arrows, Shift-run, jump, every Inspector-bound action key, and the portal interact key (the portal probe is nulled while frozen). Physics, gravity and animation stay live, exactly like the dialogue freeze.

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
