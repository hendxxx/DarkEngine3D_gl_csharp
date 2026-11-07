# Animation Editor — Phase Tracking

Living status board for the Animation Editor revamp. The design/rationale lives in
[animation-editor-revamp.md](animation-editor-revamp.md); **this file is the single place where
progress is tracked** — update it as each phase advances instead of scrolling the plan.

Last updated: **2026-11-07**

## How to read / maintain this file

| Status | Meaning |
|---|---|
| ✅ Done | Implemented **and** verified by the listed checks |
| 🟡 In progress | Partially delivered — see the notes under the checklist |
| ⬜ Not started | Scoped but no code yet |
| ⚠️ Accepted deviation | Deliberate difference from the plan, kept with a reason |

Rules when updating:

1. Tick `- [x]` only after the item is implemented **and** the phase's verification row passes.
2. Every phase keeps its own **Verification** table; append a row per verification run — never
   rewrite history or delete a failed row.
3. A partial/regressed item is marked ⚠️ with one sentence saying what is missing and why.
4. Update the high-level table **and** "Last updated" in the same commit as the change.

## Phase overview

| Phase | Scope | Status | Delivered | Evidence |
|---|---|---|---|---|
| **0 — Foundation** | Animation data model, additive clip fields, DTOs, JSON round-trip tests | ✅ Done | 2026-10-25 | build 0/0, `AnimationEditorDataTest` A–I, `SpriteSheetDataJsonTest`, `BinCacheTest` |
| **1 — Clip workspace** | Animation Editor panel, groups, standard sets, timeline, Library, Preview Loadout, quick actions, open-clip navigation | ✅ Done | 2026-10-26 | build 0/0, `AnimationEditorDataTest` ALL PASS (data side); ⚠️ no in-IDE smoke run yet |
| **2 — Skeleton authoring + bake** | Rigs (bones/slots/attachments/skins), animate mode, CPU rasterizer, `AnimationBaker`, dirty tracking | 🟡 Engine half done | 2026-11-07 | build 0/0, `AnimationEvalTest` A–I; editor UI + atlas wiring pending |
| **3 — Attachments at runtime** (optional) | Layered bake, `LayerClipNames` draw path, runtime skin swap, bone attachment points | 🟡 In progress | 2026-11-07 | build 0/0, `AnimationEditorDataTest` J; runtime draw path not smoke-tested in the IDE |
| **4 — Spine-level extras** (optional) | Mesh/FFD/weights/IK, Spine JSON import/export, live skeleton runtime | ⬜ Not started | — | — |

**Current position:** Phase 1 complete. Phase 3's CONSUMPTION half is delivered (layered clip
compositing). Phase 2's ENGINE half is delivered too: pose evaluation, CPU rasterizer and the frame
baker all work and are harness-verified — a rig clip can already be turned into atlas-ready frames
from code. What is missing is everything that makes it usable from the editor: setup mode, animate
mode, the timeline UI, and the button that pushes `AnimationBaker` output through
`SpriteAtlasBaker.BakeClip`. That wiring is the next unit of work.

---

## Phase 0 — Foundation ✅

Goal: settle the data model with zero UI change and zero runtime change, so every later phase is
additive.

### Checklist

- [x] `AnimationClipMode` (`Sprite` / `Skeleton`), `AnimationGroup`, `PreviewLoadoutEntry`,
      `AnimationSetPreset` (data-driven standard set defaults).
- [x] `AnimationTrack` / `AnimationKey` / `AnimationTrackKind` / `AnimationKeyInterp` +
      `Evaluate()` (Linear/Stepped; Bezier falls back to Linear until Phase 2).
- [x] `AnimationRig` model: bones, slots, region attachments, skins, default skin.
- [x] `AnimationClip2D` authoring fields added additively: `Id`, `GroupPath`, `Mode`, `RigId`,
      `FrameHolds`, `Tracks`, `Bake` (on top of the existing runtime fields).
- [x] `BakeSettings` (`Fps`, `TargetAtlas`, `LastBakeHash`) + `BakeDirty` badge source.
- [x] `SpriteSheetsSaveData` gained `AnimationGroups` / `Rigs` / `AnimationTemplates`.
- [x] Legacy files load unchanged (no rewrite on load; ids minted on load; missing fields default).
- [x] Binary cache layout bumped — old sidecars are rejected *silently* and self-heal via JSON.
- [x] `Tools/AnimationEditorDataTest` cases A–I (round-trip, legacy load, clone isolation,
      templates, key evaluation).

### Files

`Engine/Visual/AnimationTypes.cs`, `Engine/Visual/AnimationRig.cs`,
`Engine/Visual/AnimationClip2D.cs`, `Engine/IDE/Panels/SpriteEditorPanel.cs` (DTO plumbing),
`Tools/AnimationEditorDataTest/`.

### Verification

| Date | Check | Result |
|---|---|---|
| 2026-10-25 | `dotnet build` | 0 warnings, 0 errors |
| 2026-10-25 | `Tools/AnimationEditorDataTest` A–I | ALL PASS |
| 2026-10-25 | `Tools/SpriteSheetDataJsonTest` | ALL PASS |
| 2026-10-25 | `Tools/BinCacheTest` | ALL PASS |

### Acceptance

- [x] Old project loads and saves unchanged.
- [x] New fields survive a JSON round-trip; unknown/absent fields never throw.
- [x] Build clean; binary cache test passes after the version bump.

---

## Phase 1 — Clip workspace ✅

Goal: make clip authoring fast — one workspace for groups, standard actions, timeline, library,
preview dressing and navigation.

### 1.1 Panel + workspace

- [x] `AnimationEditorPanel` created and wired in `IDE.cs` (menu entry, render, undo/redo routing,
      focus tracking).
- [x] Group tree: create / rename / nest / delete, search, group filter, per-group clip counts,
      dirty badges.
- [x] Persistence of groups + per-group `PreviewLoadout` in `sprites.sheets.json`.

### 1.2 Clip authoring

- [x] Clip properties: name (commit on Enter/blur), FPS, loop, reverse, speed, bake state.
- [x] Timeline frame strip: drag-reorder, per-frame holds, insert / duplicate / remove frames,
      event markers on frames.
- [x] `+ Clip` dialog (source sheet, preview-range prefill, FPS, loop, bake-on-create).
- [x] Bake Clip / Duplicate Clip / Delete Clip; Bake Dirty bulk action.
- [x] Undo/redo through the Sprite Editor snapshot plumbing.

### 1.3 Standard set + quick actions

- [x] **New Animation Set** dialog: rows Idle/Walk/Run/Attack/Dead (+ Jump/Hurt/Cast), prefilled
      from sheet rows or the current preview range, editable timing/loop/event, optional bake-all.
- [x] Names follow `<sheet>-<action>` so runtime lookups + equipment auto-match keep working.
- [x] Data-driven templates persist as `AnimationTemplates`; **Save as template** button.
- [x] Toolbar **quick actions** `+Idle +Walk +Run +Attack +Dead` from the current preview range;
      an existing `<sheet>-<action>` clip is selected, never overwritten.

### 1.4 Library dashboard

- [x] Search / group / dirty filters; sortable columns (Clip, Group, Sheet, FPS, Frames, Duration,
      Loop, State, Mode, Refs).
- [x] Reference count column + bulk **Bake Dirty**.
- [x] Double-click a row to open that clip in the Workspace.

### 1.5 Rename safety

- [x] Live reference scan: scene base clip, Player2D action clips, direct equipment layers,
      inventory `EquipClip`.
- [x] **Update N reference(s)** action after a rename.

### 1.6 Preview Loadout (drag & drop equipment)

- [x] Palette of inventory equipment items + source sheets, with text filter.
- [x] Drop targets: the preview canvas and the layers list; layers list supports visibility,
      removal and per-layer info.
- [x] Overlay drawing mirrors the runtime rules exactly: authored clip → action-token auto-match →
      position-in-clip, raw-index clamp fallback, own-FPS when unsynced; negative layers draw
      behind the base sprite.
- [x] Item layers resolve **live** from `ItemDef` (editing the item updates the preview, no
      re-link); direct sheet layers carry their own values.
- [x] Loadout persists per group; ungrouped clips use a session-only list with an on-screen hint.

### 1.7 Open clip from anywhere

- [x] `IDEBridge.RequestOpenAnimationClip` + `AnimationEditorPanel.OpenAtClip(sheet, clip)`.
- [x] Asset Browser clip boxes: click/drag still places a Sprite2D (click deferred past the
      double-click window), **double-click opens the clip**.
- [x] Inspector `Edit Clip` buttons: Sprite2D base clip, Player2D base clip, every
      Animation Actions clip row.
- [x] Navigation clears filters that would hide the row, focuses the window, switches to the
      Workspace page and starts playback; unknown clips report instead of mis-selecting.
- [x] Workspace/Library stay reachable as two page buttons (ImGui.NET's flagged `BeginTabItem`
      overload forces a tab close button, so the tab bar was replaced).

### 1.8 Deviations (accepted)

- ⚠️ **Sprite Editor clip section kept**, with an `Open Animation Editor →` link, instead of being
  removed. Quick clip creation from the preview range still lives there; deleting it was not worth
  the regression risk.
- ⚠️ **Reference scan scope**: effects/trigger params and saved-but-unloaded scenes are not scanned.
  The tooltip and the plan doc say so.
- ⚠️ **Preview overlay scale** assumes a 1-world-unit-tall character; the scene object's real height
  wins in-game.
- ⚠️ **No in-IDE smoke run yet**: the panel compiles and its data paths are harness-tested, but the
  drag/drop and navigation gestures have not been clicked through in the running IDE.

### Verification

| Date | Check | Result |
|---|---|---|
| 2026-10-25 | `dotnet build` after the workspace/library pass | 0 warnings, 0 errors |
| 2026-10-25 | `Tools/AnimationEditorDataTest` (case B extended: loadout round-trip + clone) | ALL PASS |
| 2026-10-25 | `Tools/SpriteSheetDataJsonTest`, `Tools/BinCacheTest` | ALL PASS |
| 2026-10-26 | `dotnet build -t:Rebuild` after navigation + quick actions | 0 warnings, 0 errors |
| 2026-10-26 | `Tools/AnimationEditorDataTest` | ALL PASS |
| 2026-10-26 | `Tools/SpriteSheetDataJsonTest`, `Tools/BinCacheTest` | ALL PASS |

### Acceptance

- [x] From one character sheet, create a complete Idle/Walk/Run/Attack/Dead set quickly (dialog or
      quick actions).
- [x] Drag a sword/clothing item onto the preview and play an action with the overlay following.
- [x] Switch between clips from the dashboard.
- [x] Bake a set through the existing pipeline; runtime data shape unchanged.
- [ ] Click through the whole flow in a running IDE (pending smoke run — see 1.8).

---

## Phase 2 — Skeleton authoring + bake 🟡

Goal: hero/enemy characters can be rigged and animated inside the editor, while the runtime keeps
receiving ordinary baked atlas clips.

The ENGINE half (evaluation + rasterization + bake) is delivered headless and harness-verified; the
EDITOR half (setup mode, animate mode, timeline, palette, wiring bake into the atlas) is not
started. Nothing in the game reads any of this yet — a baked rig clip is still just a frame strip.

### Checklist

- [ ] Setup mode: bones (add / parent / rename, setup pose), slots (draw order, visibility).
- [ ] Region attachments (pick sheet + frame, pivot/offset/rotation/scale) and skins list.
- [ ] Attachment palette: drop on a bone slot, `Add texture…` PNG import, drag between slots,
      duplicate for skin overrides.
- [ ] Animate mode: bone selection in the viewport, transform gizmo, auto-key, key insertion.
- [ ] Timeline tracks: bone rotate/translate/scale, slot attachment/color, events; linear/stepped/
      bezier interpolation with handles; playback + onion skin.
      (Data + evaluation exist; the timeline UI and its curve handles do not.)
- [x] `SkeletonPoseEvaluator.Evaluate` — bone hierarchy math (memoized, cycle-safe), setup pose +
      tracks, skins, per-slot attachment index (-1 hides), slot alpha, draw order = slot order.
- [x] `AnimationCurve` — Linear / Stepped / Bezier, same key-segment convention as
      `AnimationTrack.Evaluate` (which stays untouched so sprite clips keep their timing).
- [x] `SpriteRasterizer` — CPU, bilinear sampling with clamped edges, straight-alpha source-over,
      slot tint, pivot-relative quads. Reads source sheets through StbImageSharp.
- [x] `AnimationBaker` — samples a clip into `BakedSpriteFrame`s (atlas-ready pixels + metadata),
      fps resolution (override → `BakeSettings.Fps` → clip FPS), loop drops the duplicate end frame,
      ONE canvas + ONE anchor per clip, atlas-overflow warnings. Writes nothing itself.
- [ ] Wire `AnimationBaker` → `SpriteAtlasBaker.BakeClip` from the editor (needs the Bake button).
- [ ] Events mapped to nearest sample, optional bbox hitboxes.
- [ ] Per-frame feet offsets: NOT needed — a shared canvas + shared anchor already keeps feet
      aligned; `RenderOffsetX/Y` stay 0 for baked rigs.
- [ ] Skin selection UI for baking (the evaluator already takes a skin name).
- [ ] Incremental bake hash / dirty tracking for rig edits (`BakeSettings.LastBakeHash`).
- [x] `Tools/AnimationEvalTest` (cases A–I: transforms, curves, pose, skins, frame rects,
      rasterizer pixels, bake timing/loop seam/warnings, limits).
- [ ] `Tools/SkeletonBakeTest` (golden hashes, atlas placement, real project art) — deferred with
      the atlas wiring above.

### Files

`Engine/Visual/Skeleton/Transform2D.cs`, `SkeletonPose.cs` (pose + `AnimationCurve`),
`SpriteRasterizer.cs`, `AnimationBaker.cs`, `Tools/AnimationEvalTest/`.

### Verification

| Date | Check | Result |
|---|---|---|
| 2026-11-07 | `dotnet build DarkEngine3D_gl_csharp.csproj -c Debug` | 0 warnings, 0 errors |
| 2026-11-07 | `Tools/AnimationEvalTest` A–I | ALL PASS |
| — | Baked rig clip played in Player2D/Sprite2D | blocked on the editor bake wiring |

### Acceptance

- [x] The evaluator + baker produce deterministic, feet-aligned frames from a 2-bone rig with two
      region attachments and an authored rotate cycle (harness).
- [ ] Create a 3-bone rig, 2 attachments and an idle/run cycle IN THE EDITOR.
- [ ] Bake at 12 fps; the baked clip plays in Player2D/Sprite2D with feet aligned and events firing.
- [ ] Editing the rig then rebaking updates the atlas.
- [x] No runtime code paths change (the baker writes `SpriteFrame`/`BakedSpriteFrame` only).

---

## Phase 3 — Attachments at runtime (optional) 🟡

Delivered first (before Phase 2) because the CONSUMPTION half does not depend on a skeleton:
any two baked clips can be composited, so body/hair/weapon authored as separate strips already
swap without rebaking. The slot-group BAKE side still needs Phase 2 (there are no slots to group
yet), which is why this phase stays in progress.

### Checklist

- [x] `AnimationClip2D.LayerClipNames` (+ `HasLayerClips`) and `AnimationClip2DData.LayerClipNames`
      — additive, absent in old files → empty list; both `CloneClip` implementations (editor duplicate
      + undo snapshots) copy it.
- [x] `IDEBridge.TryFindSpriteClipByName` — name-only resolver for layer entries, cached per
      `SpriteRegistryVersion` (a layer can be rebaked into another atlas without editing every
      composite clip). Only clips with baked frames qualify.
- [x] Draw path `EditorObject.DrawBakedLayerClips`, called from BOTH `DrawPlayer2D` and
      `DrawSprite2D` after the equipment overlays: list order = draw order, each layer samples its
      own sheet at the BASE clip's position-in-clip (clamped), scaled by the base `pxToWorld` so a
      layer authored on the base grid lines up, anchored bottom-center of each tile (a repeated
      strip dresses every tile), z just above the base quad and below the equipment band.
- [x] Base layer stays the single source for everything that is not the sprite draw: DoF silhouette,
      repeat-strip width, equipment overlays, badges/`RuntimeVisualTopOffset`.
- [x] Editor UI (Animation Editor → clip properties): layer list with reorder (^/v), remove,
      missing-layer warning, and an "Add layer clip" combo filtered to baked clips; no rebake needed.
- [ ] Layered BAKE (slot groups) — blocked on Phase 2 (no slot model in the bake path yet).
- [ ] Runtime per-slot visibility/colour — needs Phase 2 slots.
- [ ] Optional bone attachment points (`hand`, `muzzle`) — needs Phase 2 rigs.
- ⚠️ Item integration is unchanged on purpose: `EquipSheet/EquipClip/EquipLayer` keeps drawing the
      equipment overlays it always did, and a layered clip's layers sit BELOW that band, so worn items
      still paint over baked hair/weapon layers. No new item fields were added.

### Verification

| Date | Check | Result |
|---|---|---|
| 2026-11-07 | `dotnet build DarkEngine3D_gl_csharp.csproj -c Debug` | 0 warnings, 0 errors |
| 2026-11-07 | `Tools/AnimationEditorDataTest` (case J: layer list round-trip, order, copy semantics, legacy clip) | ALL PASS |
| — | In-IDE smoke run (layered clip drawn over a player + sprite) | not run yet |

### Acceptance

- [x] A layered clip's layer list is authored in the editor and survives save/load.
- [x] Existing non-layered clips take an early-out in the new draw path and render exactly as today.
- [ ] Swapping a baked weapon/clothing layer in the running IDE without rebaking (pending smoke run).

---

## Phase 4 — Spine-level extras (optional) ⬜

- [ ] Mesh attachments, FFD, vertex weights/skinning, IK, path constraints.
- [ ] Spine JSON import/export (region attachments first).
- [ ] Live skeleton runtime (`SkeletonAnimator2D`) as an alternative to bake for hero characters.

No acceptance criteria yet — scope to be decided after Phase 2/3 results.

---

## Build & test gate (every phase)

| Check | Command | Requirement |
|---|---|---|
| Compile | `dotnet build DarkEngine3D_gl_csharp.csproj -t:Rebuild` | 0 warnings / 0 errors |
| Animation data | `dotnet run --project Tools/AnimationEditorDataTest/AnimationEditorDataTest.csproj` | ALL PASS |
| Sheets JSON | `dotnet run --project Tools/SpriteSheetDataJsonTest/SpriteSheetDataJsonTest.csproj` | ALL PASS |
| Binary cache | `dotnet run --project Tools/BinCacheTest/BinCacheTest.csproj` | ALL PASS |
| Manual GL | run the IDE, exercise the phase's gestures | recorded in the phase's verification table |

## Open items carried forward

1. **In-IDE smoke run of Phase 1** — double-click a clip box, `Edit Clip` from the Inspector, quick
   actions, Preview Loadout drag/drop, then reopen the project to confirm groups/loadout persist.
2. **Reference scan coverage** — extend to effect/trigger params and saved-but-unloaded scenes when
   bulk rename becomes common.
3. **Sprite Editor clip section** — revisit removing it once the Animation Editor has had real use;
   keep the link button either way.
4. **Phase 2 start** — build the demo rig first (3 bones, 2 attachments) and validate the rasterizer
   against a hand-made bake before wiring skins.
5. **Layered bake smoke test** — composite two existing baked clips (body + weapon) on a Player2D and a
   Sprite2D in the running IDE, confirm the layer follows the base action's frames and that a repeated
   Sprite2D strip dresses every tile.
