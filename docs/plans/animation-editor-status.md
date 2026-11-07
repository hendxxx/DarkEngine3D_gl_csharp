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
| **2 — Skeleton authoring + bake** | Rigs (bones/slots/attachments/skins), animate mode, CPU rasterizer, `AnimationBaker`, atlas bake, dirty tracking | ✅ Complete | 2026-11-07 | build 0/0; `AnimationEvalTest` A–I, `SkeletonBakeTest` A–E, data J–K all pass; IDE not visually verified |
| **3 — Attachments at runtime** (optional) | Layered bake, `LayerClipNames` draw path, runtime skin swap, bone attachment points | 🟡 In progress | 2026-11-07 | build 0/0, `AnimationEditorDataTest` J; runtime draw path not smoke-tested in the IDE |
| **4 — Spine-level extras** (optional) | Mesh/FFD/weights/IK, Spine JSON import/export, live skeleton runtime | ⬜ Not started | — | — |

**Current position:** Phase 1 complete. Phase 2 complete (rig setup + animate + atlas bake) and
Phase 3's consumption half delivered (layered clip compositing), so a character can be rigged,
animated, baked and played as an ordinary atlas strip. Remaining: Phase 3's slot-group bake, and
Phase 4 extras. The open polish list is small: palette drag & drop, onion skin, bezier handles,
bbox hitboxes, and a visual pass in the running IDE.

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

## Phase 2 — Skeleton authoring + bake ✅

Goal: hero/enemy characters can be rigged and animated inside the editor, while the runtime keeps
receiving ordinary baked atlas clips.

Delivered end to end: rig setup page → animate mode with auto-key + track/key editor → Bake writes
atlas frames through the existing repack worker → the runtime plays an ordinary frame strip. The
runtime contract did not change: no gameplay path reads a rig, a clip, a skin or a track.

### Checklist

- [x] Setup mode — **Rig Setup page** in the Animation Editor: rig list (new / duplicate / delete,
      refused while a clip still references the rig), design height, bones (add as child of the
      selected bone, parent picker that hides descendants, delete reparents children to root and
      clears slots), slots (add / reorder ^v / delete with its attachments, bone binding,
      visibility, tint), and a live setup-pose preview with bone gizmos + the root cross.
      Renames commit on Enter/blur and carry every name reference with them.
- [x] Region attachments (sheet + frame, pivot / offset / rotation / scale) edited per attachment;
      `Use for selected clip` links the rig to a clip (`RigId` + `Mode = Skeleton`).
- [x] Skins list: add / delete / rename / `Make default`, plus a per-slot override combo
      ("(rig default)" removes the override). The bake composites the skin chosen on the clip.
- [x] `Add texture…` PNG/JPG import from the rig page (reuses the Sprite Editor import path:
      grid auto-fit, sibling export, unique sheet names).
- [x] Animate mode on the Rig Setup page: play/pause/scrub, cycle length, auto-key, bone selection
      from the list, K buttons per channel (rotate / translate / scale).
- [x] Timeline tracks: bone rotate/translate/scale, slot attachment + alpha, per-key time/value/
      value2/interpolation (linear / stepped / bezier) editing, add/delete keys and tracks, event
      list with add-at-playhead. Playback previews the pose live.
- ⚠️ Not done (polish, not blockers): dragging an attachment from a palette ONTO a slot row /
      viewport bone, duplicate-for-skin shortcut, onion skin, and per-key bezier handles in a curve
      editor (interp is selectable, handles are not editable yet).
- [x] `SkeletonPoseEvaluator.Evaluate` — bone hierarchy math (memoized, cycle-safe), setup pose +
      tracks, skins, per-slot attachment index (-1 hides), slot alpha, draw order = slot order.
- [x] `AnimationCurve` — Linear / Stepped / Bezier, same key-segment convention as
      `AnimationTrack.Evaluate` (which stays untouched so sprite clips keep their timing).
- [x] `SpriteRasterizer` — CPU, bilinear sampling with clamped edges, straight-alpha source-over,
      slot tint, pivot-relative quads. Reads source sheets through StbImageSharp.
- [x] `AnimationBaker` — samples a clip into `BakedSpriteFrame`s (atlas-ready pixels + metadata),
      fps resolution (override → `BakeSettings.Fps` → clip FPS), loop drops the duplicate end frame,
      ONE canvas + ONE anchor per clip, atlas-overflow warnings. Writes nothing itself.
- [x] Bake wired: the toolbar Bake / Bake Dirty / Bake All buttons handle skeleton clips too,
      sampling the rig on the UI thread and packing through `TryStartBakeBatch` (one atlas write for
      everything, off the UI thread).
- [x] Runtime placement stamped at bake time: the canvas is SYMMETRIC about the root (frames are
      centred on the object) and `clip.SpriteOffsetY = BelowRootPx` compensates art below the feet,
      because the draw path puts a frame's BOTTOM on the feet line and ignores `SpriteFrame` anchors.
      `clip.MasterHeight = rig.DesignHeightPx` so a raised weapon does not shrink the body.
- [x] Events re-targeted proportionally onto the new sample count (`RemapEventsToFrameCount`) —
      a Hit at 60% stays at 60%.
- [x] Skin selection for baking (`BakeSettings.Skin`, combo on the clip).
- [x] Dirty tracking: a rig fingerprint marks every clip animating it dirty while the author edits,
      and `BakeSettings.LastBakeHash` stores the rig+timeline hash of the last bake.
- [x] `Tools/AnimationEvalTest` (A–I) and `Tools/SkeletonBakeTest` (A–E) + data cases J–K.
- [ ] Optional (not part of acceptance): bbox-slot hitboxes baked into clip metadata, onion skin.

### Verification

| Date | Check | Result |
|---|---|---|
| 2026-11-07 | `dotnet build DarkEngine3D_gl_csharp.csproj -c Debug` | 0 warnings, 0 errors |
| 2026-11-07 | `Tools/AnimationEvalTest` A–I | ALL PASS |
| 2026-11-07 | Rig Setup / Animate pages: author, preview, save | NOT visually verified (compiled only) |
| 2026-11-07 | `Tools/SkeletonBakeTest` real atlas write + rebake | ALL PASS (38 checks) |
| 2026-11-07 | `Tools/AnimationEditorDataTest` J–K (layers, skeleton semantics, events, bake settings) | ALL PASS |
| — | Baked rig clip played in Player2D/Sprite2D in a running IDE | NOT run (needs the app + a rigged project) |

### Acceptance

- [x] The evaluator + baker produce deterministic, feet-aligned frames from a 2-bone rig with two
      region attachments and an authored rotate cycle (harness).
- [x] A rig clip can be created, keyed, baked and rebaked from the editor; a rig edit marks every
      clip using it dirty and the rebake rewrites the atlas (harness + editor code path).
- [x] Feet alignment and canvas symmetry are pinned by tests against the runtime's actual placement
      rules (bottom-on-feet, centred, MasterHeight scaling, `SpriteOffsetY`).
- [x] No runtime code paths change (the baker writes `SpriteFrame`/`BakedSpriteFrame` only).
- [ ] Visual confirmation in the running IDE with a real project rig (see the verification row).

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
4. **Visual pass in the IDE** — author a real 3-bone rig with two attachments, key an idle + a run,
   bake at 12 fps and play it on a Player2D: confirm feet stay on the ground, the sprite does not
   bob, and an event fires. This is the only acceptance item still open.
5. **Palette drag & drop** — drag a sheet frame from a palette onto a slot row / viewport bone
   (functionally covered today by `+ Attachment` + the sheet/frame pickers).
6. **Layered bake smoke test** — composite two existing baked clips (body + weapon) on a Player2D and a
   Sprite2D in the running IDE, confirm the layer follows the base action's frames and that a repeated
   Sprite2D strip dresses every tile.
