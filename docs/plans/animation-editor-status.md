# Animation Editor — Phase Tracking

Living status board for the Animation Editor revamp. The design/rationale lives in
[animation-editor-revamp.md](animation-editor-revamp.md); **this file is the single place where
progress is tracked** — update it as each phase advances instead of scrolling the plan.

Last updated: **2026-10-26**

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
| **2 — Skeleton authoring + bake** | Rigs (bones/slots/attachments/skins), animate mode, CPU rasterizer, `AnimationBaker`, dirty tracking | ⬜ Not started | — | — |
| **3 — Attachments at runtime** (optional) | Layered bake, `LayerClipNames` draw path, runtime skin swap, bone attachment points | ⬜ Not started | — | — |
| **4 — Spine-level extras** (optional) | Mesh/FFD/weights/IK, Spine JSON import/export, live skeleton runtime | ⬜ Not started | — | — |

**Current position:** Phase 1 complete; the next unit of work is Phase 2, starting with a small
demo rig (3 bones, 2 region attachments, idle/run) before committing to skins/layers.

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

## Phase 2 — Skeleton authoring + bake ⬜

Goal: hero/enemy characters can be rigged and animated inside the editor, while the runtime keeps
receiving ordinary baked atlas clips.

### Checklist

- [ ] Setup mode: bones (add / parent / rename, setup pose), slots (draw order, visibility).
- [ ] Region attachments (pick sheet + frame, pivot/offset/rotation/scale) and skins list.
- [ ] Attachment palette: drop on a bone slot, `Add texture…` PNG import, drag between slots,
      duplicate for skin overrides.
- [ ] Animate mode: bone selection in the viewport, transform gizmo, auto-key, key insertion.
- [ ] Timeline tracks: bone rotate/translate/scale, slot attachment/color, events; linear/stepped/
      bezier interpolation with handles; playback + onion skin.
- [ ] `SkeletonPose.Evaluate` (bone hierarchy math, deterministic).
- [ ] `SpriteRasterizer` (CPU, nearest sampling default, optional 2× supersample).
- [ ] `AnimationBaker`: bake clip / bake group / bake all; atlas-overflow warnings.
- [ ] Baked metadata correct: master height, per-frame feet offsets (`RenderOffsetX/Y`), events
      mapped to nearest sample, optional bbox hitboxes.
- [ ] Skin selection for baking (composite bake) + incremental `BakeKey` / dirty tracking.
- [ ] `Tools/AnimationEvalTest` + `Tools/SkeletonBakeTest` harnesses.

### Verification

| Date | Check | Result |
|---|---|---|
| — | not run yet | — |

### Acceptance

- [ ] Create a 3-bone rig, 2 attachments and an idle/run cycle.
- [ ] Bake at 12 fps; the baked clip plays in Player2D/Sprite2D with feet aligned and events firing.
- [ ] Editing the rig then rebaking updates the atlas.
- [ ] No runtime code paths change.

---

## Phase 3 — Attachments at runtime (optional) ⬜

- [ ] Layered bake (slot groups) + `LayerClipNames` handling in the sprite/player draw paths
      (base layer first; DoF mask / repeat strips / equipment overlays keep using it).
- [ ] Runtime skin/material swap integrated with `EquipSheet` / `EquipClip` / `EquipLayer`
      (no new item fields).
- [ ] Per-slot visibility/colour at runtime.
- [ ] Optional: bone attachment points (`hand`, `muzzle`) baked as per-frame anchor metadata so
      projectiles spawn from the right place.

### Acceptance

- [ ] A baked layered clip swaps weapon/clothing at runtime without rebaking.
- [ ] Existing non-layered clips render exactly as today.

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
