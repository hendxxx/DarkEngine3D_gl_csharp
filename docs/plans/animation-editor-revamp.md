# Plan: Animation Editor revamp (Godot/Spine-style, bake-first)

Status: **Phase 0 ✅ + Phase 1 ✅ complete (2026-10-26)** — Phase 2 (skeleton authoring +
bake) not started
Tracking: **[animation-editor-status.md](animation-editor-status.md)** — phase checklists,
verification log and open items (keep that file updated as work progresses).
Reference: <https://en.esotericsoftware.com/spine-in-depth>
Target: replace the flat "Animation Clips" list with a real animation workspace that supports
both the current frame-strip clips and skeleton-based clips with attachments — while the
runtime keeps consuming **baked atlases** exactly like today.

---

## 1. Goals

1. Every animation is easy to edit: open a clip, scrub a timeline, change keys/frames, see the
   result live, bake, done.
2. Animations are organised in **groups** (tree/folders, e.g. `Player/Hero/Locomotion`,
   `Enemies/Goblin/Combat`), with group-level actions (bake group, play group, export).
3. Two clip modes in one editor:
   - **Sprite clip** — the current frame-strip animation (frame indices, FPS, events, master box).
   - **Skeleton clip** — bones + slots + attachment skins + keyframed timelines (Spine-style),
     with region attachments in v1.
4. **Bake stays the runtime contract.** Authored rigs/timelines are editor-only source data.
   The game still loads a `SpriteSheet` (atlas PNG + frame rects) and an `AnimationClip2D`
   (frame indices + timing + events). No new runtime dependency, no FPS cost.
5. Backward compatible: every existing `sprites.sheets.json` / `*-sprite-anim.json` /
   `.ing` reference keeps working without migration steps.
6. Creating the common set — **Idle, Walk, Run, Attack, Dead, Jump, Hurt, Cast** — is one
   dialog / one click with sensible defaults (loop, FPS, hit event, hold-last-frame)
   instead of hand-authoring every clip.
7. Attachments (weapon, clothes, hair, item art) are added by **drag & drop** from an
   inventory-style palette onto a clip or a bone slot — no manual sheet/clip hunting.
8. One **Animation Library** window manages every clip: group tree, search/filter,
   dashboard, open-from-anywhere — no more "clip kesana kemari".
9. The existing sheet settings (grid, padding, offsets, master box, flip, resolution) stay
   exactly as they are; the animation editor consumes them.

## 2. Non-goals (v1)

- Mesh deformation, FFD, vertex weights/skinning, IK, path constraints, physics.
- Live skeleton evaluation at runtime (that would bypass the bake system).
- Spine JSON/Binary import or export (designed to be addable later).
- Animation blending/mixing layers at runtime beyond what the existing equipment layers do.
- Multi-atlas clips (a clip's frames must still fit one atlas — existing constraint).

---

## 3. Current system audit (what we build on)

| Area | Today | File |
|---|---|---|
| Clip runtime data | `AnimationClip2D`: Name, SpriteSheetName (baked atlas), `FrameIndices`, FPS, Loop, Reverse, Speed, `Events`, master-box snapshot | `Engine/Visual/AnimationClip2D.cs` |
| Clip authoring | Flat list in Sprite Editor; create from preview range, rename/FPS/loop/speed, play preview; no timeline, no keys | `Engine/IDE/Panels/SpriteEditorPanel.cs` (`RenderAnimationClips`, ~L1490) |
| Bake | `SpriteAtlasBaker.BakeClip` copies source frame pixels into 4096² atlases (+ normal atlas), returns the baked clip with atlas frame indices; `RepackAllAsync` defragments | `Engine/Visual/SpriteAtlasBaker.cs` |
| Persistence | `SpriteSheetsSaveData { Sheets, AnimationClips, PatternActions, Preview* }` in `Artifacts/Sprites/sprites.sheets.json` + per-file `*-sprite-anim.json`; binary-cache sidecar | `SpriteEditorPanel.cs` (L3418), `BinaryObjectCache` |
| Registry | `(sheet, clip)` → clip dictionary, per-sheet sorted clip names, equipment auto-match by name/token | `Engine/IDE/IDEBridge.cs` (`SyncSpriteRegistry`, `FindAnimationClipForBase`) |
| Rendering | Every 2D pass draws one atlas frame by UV; per-frame `RenderOffsetX/Y` + clip master box handle pivots/scaling | `Engine/Objects/EditorObject.cs` (`DrawSprite2D`, `TryGetSprite2DTileData`), `Player2DSystem`, effects/projectiles |
| Groups today | Sheets have `GroupName` + category sections; **clips have no grouping** | `SpriteSheet.cs`, `SpriteEditorPanel.cs` |

Pain points this revamp fixes:

- Clips are only "first frame → last frame"; per-frame timing, holds and keys are impossible.
- No timeline/dopesheet, no keyframe editing, no curve control.
- Flat clip list; assets with 100+ clips are hard to navigate.
- Every authored change requires a full/partial rebake with no source retained → re-editing
  means re-authoring.
- No rig/skeleton, so cutout animation and attachment swapping need external tools and
  hand-made sheets.

---

## 4. Architecture decisions

### 4.1 Bake-first, authoring-source retained

```
Authoring (editor only)                    Runtime (unchanged)
────────────────────────                   ───────────────────
AnimationRig (bones/slots/skins)  ──┐
AnimationClip2D.Tracks (keys)      ─┼─►  AnimationBaker ──►  PNG atlas + AnimationClip2D
Sprite clip FrameIndices           ─┘        (CPU)          (FrameIndices, FPS, events,
                                                              master box, per-frame offsets)
```

- The rig + tracks are stored in `sprites.sheets.json` (source of truth), never loaded by
  gameplay classes.
- Bake writes exactly the artifacts the engine already understands: frames into
  `mainspriteN.png` (+ normal atlas) and a clip whose `SpriteSheetName` is that atlas.
- Re-bake is deterministic; a content hash stored on the clip lets us skip clean clips.

### 4.2 Two modes, one clip class

`AnimationClip2D` keeps its current runtime fields and gains authoring fields:

```csharp
enum AnimationClipMode { Sprite, Skeleton }

class AnimationClip2D {
    // existing runtime fields
    string Name; string SpriteSheetName; List<int> FrameIndices;
    float FPS; bool Loop, Reverse; float SpeedMultiplier;
    Dictionary<int,string> Events;
    float MasterWidth, MasterHeight, SpriteOffsetX, SpriteOffsetY;

    // new — authoring/identity (ignored by renderers)
    string Id;                  // stable id (guid) for linking layers/rebake
    string GroupPath;           // "" = ungrouped, else "Player/Hero/Locomotion"
    AnimationClipMode Mode;     // Sprite (default) | Skeleton
    string? RigId;              // Skeleton only
    List<FrameHold>? FrameHolds;        // Sprite mode: per-frame duration multiplier
    List<AnimationTrack>? Tracks;       // Skeleton mode (and optional Frame track)
    BakeSettings? Bake;                 // fps, target atlas, last hash/time, layer mode
    List<string>? LayerClipNames;       // baked attachment layers (later phase)
}
```

A legacy clip loads as `Mode = Sprite`, `GroupPath = ""`, `Tracks = null`; the timeline
synthesizes a Frame track from `FrameIndices` + `FPS`. **Nothing is rewritten on load.**

### 4.3 Groups are display/organisation only

`AnimationGroup { Path, SortOrder, DefaultRigId?, Color?, PreviewItemIds? }` — a tree path,
not an owner. `PreviewItemIds` is the editor's drag-and-drop loadout (inventory item ids shown
on the preview character); it never reaches the runtime.
Clips reference it by `GroupPath`. This keeps `(sheet, clip)` lookup untouched, allows drag &
drop between groups, and survives old files (missing = root).

### 4.4 Rig model (Spine-inspired, region attachments first)

```csharp
class AnimationRig {
    string Id; string Name;
    float DesignHeightPx;              // canonical frame height → clip.MasterHeight
    List<Bone2D> Bones;                // parent by name, index 0 = root at feet (0,0)
    List<Slot2D> Slots;                // list order = draw order
    List<Attachment2D> Attachments;    // Region only in v1
    List<Skin2D> Skins;                // slot → attachment overrides
    string DefaultSkin;
}

class Bone2D {
    string Name; string? Parent;
    float Length;
    float X, Y, Rotation, ScaleX, ScaleY, ShearX, ShearY;   // setup pose (local)
}

class Slot2D {
    string Name; string Bone; string? Attachment;
    float R,G,B,A;
    bool Visible;
}

class Attachment2D {
    string Name; string Slot;
    string SheetName; int FrameIndex;    // source sprite region
    float OffsetX, OffsetY, Rotation, ScaleX, ScaleY;
    float PivotX, PivotY;                // 0..1 inside the region (0.5, 1 = feet/bottom-center)
}

class Skin2D { string Name; Dictionary<string,string> AttachmentBySlot; }
```

`AttachmentKind` is an enum so Mesh/BoundingBox can be added without a schema break.

### 4.5 Timeline model

- Time in **seconds** (float), not frame numbers — skeleton animation must be
  frame-rate independent; the sprite mode converts via FPS for display.
- Track types: `SpriteFrame`, `BoneRotate`, `BoneTranslate`, `BoneScale`, `SlotAttachment`,
  `SlotColor`, `DrawOrder`, `Event`.
- Key: `{ Time, X, Y, Value, Interp (Linear|Stepped|Bezier), BezierIn, BezierOut }`.
- Evaluator: `SkeletonPose.Evaluate(rig, clip, time)` → world bone matrices + ordered slots.
  Pure math, no GL, testable headless.
- Sprite clips continue to evaluate exactly as `AnimationClip2D.GetFrameAtTime` does today;
  the timeline is just a visual editor over `FrameIndices`.

### 4.6 Attachment support (staged)

- **Phase 2 (v1 skeleton):** select a skin; bake the composed visible slots into ONE frame
  per sample. Attachment swapping at runtime becomes "play a different baked clip"
  (`hero-run-knight` vs `hero-run-mage`), which is how many baked pipelines ship skins.
- **Phase 3 (layered bake, optional):** bake per slot-group layers (`Body`, `Hair`, `Weapon`)
  into separate clips that share timing/pivot; a clip stores `LayerClipNames` and the
  renderer draws them in order at the same time — this is what enables true runtime
  attachment swap (equip a sword without a new body bake). Reuses the existing
  equipment-layer drawing order and `FindAnimationClipForBase` conventions.

---

## 5. Editor UX

New dockable panel **Animation Editor** (separate from Sprite Editor; Sprite Editor keeps
sheet slicing + bake plumbing and links here). Layout:

```
┌───────────────┬─────────────────────────────┬──────────────────┐
│ Groups/Clips  │  Viewport preview           │  Inspector       │
│ (tree+search) │  (bones, gizmo, onion skin) │  clip/rig/bake   │
├───────────────┴─────────────────────────────┴──────────────────┤
│  Timeline / Dopesheet (tracks, keys, playhead, curve editor)   │
└────────────────────────────────────────────────────────────────┘
```

- **Left tree:** groups (folders) + clips; search; context menu: New Group, New Clip
  (Sprite/Skeleton), Duplicate, Rename, Move to group…, Bake, Delete. Drag & drop to regroup.
  Show badges: `baked/dirty`, mode, frame count, duration, rig name.
- **Toolbar:** play/pause/step, loop, speed, `Auto-Key` toggle, `Setup | Animate` mode
  (Spine), onion skin, zoom.
- **Viewport:** draw the live pose (not the baked atlas) using the CPU/GL preview; select
  bone (click), rotate/translate/scale with the existing gizmo math; attachment preview;
  foot/root marker; optional grid.
- **Inspector:** clip properties (name, group, FPS/bake fps, loop, events), bake settings
  (target atlas, fps, padding, layer mode), rig properties (design height, skin), attachment
  properties (sheet/frame, pivot, offset, rotation/scale).
- **Timeline:** track tree on the left (Bones → Rotate/Translate/Scale, Slots → Attachment/
  Color, Events), keys as diamonds; drag keys, right-click interpolation, copy/paste,
  multi-select; playhead scrub updates the viewport immediately. A "Frame strip" row is the
  Sprite-mode equivalent (thumbnails, drag to reorder, hold multipliers).
- **Sprite clip editing:** keep the current workflow (set preview range → Create Clip) as a
  one-click action in the new panel; the strip row then allows reordering/trimming, adding
  frames, per-frame holds and event markers — no rebake until Save/Bake.

### 5.1 Create the standard animation set in one go

The clip is the unit — nothing new is introduced at runtime. The editor just makes creating
and editing the usual actions fast:

- **"New Animation Set" dialog:** a table of actions with editable start/end frame, FPS,
  loop, and event per row. Prefilled with project defaults:

  | Action | Default | Notes |
  |---|---|---|
  | Idle | loop, 8–12 fps | first action of a character |
  | Walk | loop, 8–12 fps | |
  | Run | loop, 12–15 fps | |
  | Attack | once, 12–15 fps | `Hit` event at ~60% of the clip |
  | Dead | once + hold last | no loop; last frame stays |
  | Jump Start / Jump End | once | optional split of the jump |
  | Hurt | once | |
  | Cast | once | optional |

- Ranges can be prefilled from the **current preview selection** or from the sheet's **row
  pattern** — the same row→action convention `Save As Pattern` already uses (`idle|walk|run|…`).
- Names follow the existing convention `<sheet>-<action>` (`hero-idle`, `hero-run`), so all
  current lookups keep working: `GetClipNames`, action tokens, equipment auto-match
  (`FindAnimationClipForBase`).
- Master box is snapshotted from the sheet exactly like today's `Create Clip`; the group
  (`<sheet>` by default) is created automatically if missing.
- One click on **Create Set** creates every checked clip and optionally bakes the whole set;
  clips that are not baked stay visibly dirty for review.
- **Toolbar quick actions:** `+Idle +Walk +Run +Attack +Dead` create a clip from the current
  frame selection with the default timing, then rename inline. `Duplicate as…` clones a clip
  to a new action name (`Attack` → `Attack2`).
- Templates live in data (`AnimationTemplates`), not in code, so the list can grow
  (project-specific actions, per-game defaults) without a rebuild.

### 5.2 Attachments & textures by drag and drop

Two attachment paths — both keep the **clip** as the target and reuse the existing equipment
art rather than inventing a second layer system:

**A. Sprite clips (frame animations)** — runtime attachments are inventory equipment today
(`InventorySystem.ItemDef.EquipSheet/EquipClip/EquipLayer/EquipSyncFrame`). The editor adds a
**Preview Loadout** palette:

- Sources: inventory items (icon + name + equip layer), sheets in the `Base Equipment`,
  `Items`, `Hair`, `FX` categories, and raw sheets.
- Drag an item/sheet onto the preview character or the **Layers** list → the layer is added
  using the item's equip art (or a direct layer when there is no item), synced by frame:
  the matching clip on the overlay sheet is found by action token (`FindAnimationClipForBase`),
  falling back to the raw frame index — the exact rule the runtime already uses.
- Drag to reorder = draw order; eye = visibility; click = offset/height/sync; `X` = remove.
- Per group the loadout can be saved as the **Preview Loadout** (editor convenience only — the
  runtime still equips items from the inventory/scene, so no duplicate data). The designer
  always sees the character with clothes/weapon while animating.
- Editing the item later still works: the palette reads `EquipSheet/EquipClip/EquipLayer` live;
  no re-link needed.

**B. Skeleton clips** — the same palette drags onto a **bone slot**:

- Drop = create a region attachment in the current skin (pivot auto-set from the sheet's
  master box/anchor, draw order follows the slot list). Drag between slots to move it;
  `Duplicate for skin` creates a skin override instead of a new attachment.
- **`Add texture…`** accepts a PNG drop: it imports the file as a sheet (existing import +
  auto-grid) and immediately links it as the attachment, so hair/weapon/item textures need
  no trip to the Sprite Editor.
- Attachments are part of the rig/skin (baked into the clip); the runtime still receives only
  baked frames.

### 5.3 One place to manage every clip (Animation Library)

- **Dashboard table** beside the tree: Group, Name, Mode, Sheet/Rig, FPS, Frames, Duration,
  Loop, Baked/Dirty, References. Sortable columns, multi-select, bulk bake/move/delete.
- Filters: group, mode, sheet, rig, dirty-only, text search; per-group counts.
- **References count** from the scene/action scan (below) so rename/delete is safe.
- **Open clip from anywhere:** double-clicking a clip in the Asset Browser, the Inspector's
  clip combo, an action row, or a clip box opens the Animation Editor at that clip. One
  navigation entry point instead of hunting through panels.
- **Rename safety:** renaming a clip that is referenced by scenes/actions/effects shows
  `N references` and offers "Update references" (scan loaded `EditorObject`s and action
  lists; persists on next scene save). Names stay the public reference in v1.
- Group actions: New Clip/Set here, Bake Group, play loops of the whole group, move group.

### 5.4 Sheet settings stay as-is

No change to grid/padding/offset/`ColOffset`/master box/FlipX/FlipY/normal version. The
animation editor only **reads** them; new clips snapshot the master box exactly like the
current `Create Clip`, and attachment auto-pivot uses the same sheet values. This keeps the
current art workflow untouched — only clip management changes.

### 5.5 Typical workflow (the target experience)

```
1. Animation Editor → group "Player/Hero" → New Animation Set
2. Tick Idle / Walk / Run / Attack / Dead  (ranges prefilled from the sheet rows)
   → Create Set → Bake Set                       ~30 seconds
3. Drag the "Sword" item from the palette onto the preview
   → Layers: body | sword (+1)                    ~5 seconds
4. Play Attack in the timeline; the sword overlay follows the same action
   (token match), hit event fires at the authored frame
5. Adjust frame holds / event frame in the timeline → Bake
6. Scene keeps working: Player2D already points at "hero-run" etc. — names unchanged
```

No sheet-editor detour, no manual clip creation per action, no hunting for which sheet a
clip belongs to — everything starts and ends in the clip.

### 5.6 Editor implementation slices

- `AnimationEditorPanel` (main window + wiring) — new file.
- `AnimationTimeline` (track tree, keys, zoom/pan, selection, drag) — new file.
- `AnimationPreview` (pose evaluation + gizmo hit-test + draw) — new file.
- `AnimationLookup`/`AnimationLibrary` (groups, filters, reference scan) — new file.
- `AnimationAssetPalette` (inventory/sheet drag source, texture import) — new file.
- `AnimationInspector` (clip/rig/attachment/bake UI) — new file.
- `SpriteEditorPanel` — remove the old clip section, add an "Open in Animation Editor"
  button; keep sheet/grid/master-box/preview-range features.

---

## 6. Bake pipeline

### 6.1 Steps for a skeleton clip

1. `samples = ceil(duration * bakeFps)` (loop: exclude the duplicate last sample when it
   equals t=0; non-loop: include the final pose).
2. For each sample `t`: evaluate pose → for each visible slot (draw order): transform the
   region attachment quad through the bone world matrix → rasterize into an RGBA buffer.
3. Compute the union AABB of all sample frames; pad 1–2 px; verify `W,H ≤ 4094`.
4. Per sample: crop to the frame's own AABB, then write per-frame metadata:
   - `RenderOffsetX/Y` = offset from the rig root (feet anchor) to the frame's bottom-center,
     so runtime draws feet at `Position` exactly like a normal clip;
   - `AnchorX/Y` = 0.5 / 1 (feet);
   - `Hitbox*` = optional bounding-box slot (later) or full frame.
5. Pack frames with `SpriteAtlasBaker` into a 4096² atlas (reuse `BakeClip`/repack so the
   normal atlas and occupied-rect logic stay identical).
6. Emit the baked `AnimationClip2D`: atlas name, frame indices, FPS = bakeFps, events mapped
   to nearest sample, `MasterHeight = rig.DesignHeightPx`, master box snapshot, `Bake`
   metadata (hash of rig + tracks + settings).
7. `SpriteAtlasManager.InvalidateAll()` + reload preview texture (existing pattern).

### 6.2 Rasterizer

- New `SpriteRasterizer` (CPU, `StbImageSharp` pixels already available):
  - destination-scan inverse mapping; nearest sampling for pixel art (default), optional
    bilinear + 2× supersample for rotated edges;
  - premultiplied alpha blend, clamped edge sampling;
  - deterministic byte-exact output (testable).
- Why CPU: no GL context needed for tests/headless tools; deterministic hashes; matches the
  existing baker's pixel-copy architecture. A single 64×64×~12-frame clip is cheap; a bone
  fan-out with hundreds of samples runs in the same background-task pattern as
  `RepackAllAsync`.

### 6.3 Incremental bake / dirty tracking

- `BakeKey = hash(rig setup + tracks + bake settings + source frame ids)`.
- "Bake" per clip, "Bake Group", "Bake All"; optional "Auto-bake on Save" per project.
- Dirty badge when `BakeKey` differs from the stored `LastBakeHash`; skip clean clips in bulk
  bakes.
- Baked source frames stay in `sprites.sheets.json`; nothing is lost after a rebake.

### 6.4 Atlas limits

- One clip still must fit one atlas (existing `AnimationClip2D.SpriteSheetName` contract).
- Pre-bake estimate `samples × maxFrameArea` + warning when a clip needs > 1 atlas; suggest
  lowering bake fps or splitting the clip. Multi-atlas clips are a later schema change
  (per-frame atlas refs) and explicitly out of v1.

---

## 7. Persistence & migration

New/extended JSON in `Artifacts/Sprites/sprites.sheets.json` (and pattern files):

```jsonc
{
  "FormatVersion": 2,
  "Sheets": [ ...unchanged... ],
  "AnimationGroups": [
    { "Path": "Player/Hero", "SortOrder": 0, "DefaultRigId": "rig_hero",
      "PreviewItemIds": ["sword_iron", "shirt_leather"] }
  ],
  "AnimationTemplates": [
    { "Action": "Attack", "Fps": 12, "Loop": false, "HoldLast": false,
      "Event": "Hit", "EventAtPercent": 60 },
    { "Action": "Dead", "Fps": 10, "Loop": false, "HoldLast": true }
  ],
  "Rigs": [
    {
      "Id": "rig_hero", "Name": "Hero", "DesignHeightPx": 64,
      "Bones": [ { "Name": "root", "Parent": null, "X": 0, "Y": 0 } ],
      "Slots": [ { "Name": "body", "Bone": "root", "Attachment": "body" } ],
      "Attachments": [ { "Name": "body", "Slot": "body", "SheetName": "hero", "FrameIndex": 0 } ],
      "Skins": [ { "Name": "default", "AttachmentBySlot": {} } ],
      "DefaultSkin": "default"
    }
  ],
  "AnimationClips": [
    {
      "Name": "run", "SpriteSheetName": "mainsprite1",
      "FrameIndices": [12,13,14,15], "FPS": 12, "Loop": true,
      "Id": "clip_ab12", "GroupPath": "Player/Hero/Locomotion",
      "Mode": "Skeleton", "RigId": "rig_hero",
      "Tracks": [ { "Kind": "BoneRotate", "Target": "thigh_l", "Keys": [ ... ] } ],
      "Bake": { "Fps": 12, "TargetAtlas": 1, "LastBakeHash": "..." }
    }
  ]
}
```

- `FormatVersion` defaults to 1 when absent. Old file → new editor reads as Sprite-mode clips at
  the root group; save writes v2 only when something actually changed (avoid churn).
- New fields are additive; the older engine ignores unknown JSON properties.
- `BinaryObjectCache` layout/version must be bumped (schema change) and `BinCacheTest`
  updated; stale sidecars self-heal via the existing fallback path.
- `*-sprite-anim.json` pattern exports write the same schema subset (sheet + clips); groups/rigs
  stay in the canonical file to avoid duplicating rigs per export.

---

## 8. Runtime impact

- **Phases 0–2:** zero renderer changes. A baked skeleton clip is an ordinary atlas clip.
  The Preview Loadout (drag-drop clothes/weapon in the editor) is editor-only; gameplay keeps
  equipping items from the inventory/scene exactly as today.
- Per-frame `RenderOffsetX/Y` + `MasterHeight` already handle feet anchoring and scale
  (`EditorObject.DrawSprite2D`, `TryGetSprite2DTileData`); the baker must compute them right.
- Events: baked into the clip's existing `Events` dictionary (frame index → name), so
  `CheckEvent` keeps firing exactly as today.
- **Phase 3 (layered bake only):** add `LayerClipNames` handling in the sprite/player draw
  paths (draw each layer clip at the same time, same anchor, back→front). Everything else
  (DoF mask data, repeat strips, equipment overlays) uses the first/base layer.
- No new runtime allocations per frame; clip lookup stays `(sheet, name)`.

---

## 9. Phased delivery

### Phase 0 — Foundation (1–2 days)
- New animation model files (groups, rigs, tracks, keys, bake settings).
- Extend `AnimationClip2D` + DTOs + `SpriteSheetsSaveData` with backward-compatible fields.
- JSON round-trip + legacy-load tests (`SpriteSheetDataJsonTest` extension).
- Registry/persistence compile-through; no UI change yet.

**Acceptance:** old project loads unchanged; save/load round-trips new fields; 0 warnings;
binary cache test passes with bumped version.

**✅ Status (2026-10-25): DONE.** `Engine/Visual/AnimationTypes.cs` (groups, set presets,
tracks/keys with `Evaluate`, `BakeSettings`) + `Engine/Visual/AnimationRig.cs` (rig model:
bones/slots/attachments/skins). `AnimationClip2D` gained `Id`/`RigId`/`Tracks`/`Bake` on top of
the authoring fields — all additive; pre-Phase-0 files load unchanged (ids are minted on load).
`SpriteSheetsSaveData` gained `AnimationGroups`/`Rigs`/`AnimationTemplates`. Harnesses:
`Tools/AnimationEditorDataTest` (cases A–I) ALL PASS, `SpriteSheetDataJsonTest` ALL PASS,
`BinCacheTest` ALL PASS (the DTO layout changed → `layoutId` changed → old sidecars are
rejected silently and self-heal through the JSON path). Build: 0 warnings / 0 errors.

### Phase 1 — Clip workspace + standard set + library (5–7 days)
- New Animation Editor panel (groups tree, clip list, timeline/dopesheet, preview).
- Sprite-mode editing: reorder/trim/add frames, per-frame holds, events, FPS/loop/speed;
  "Create Clip from range" kept.
- **New Animation Set** dialog + quick actions (`+Idle +Walk +Run +Attack +Dead`) +
  data-driven templates; auto-bake option; names follow `<sheet>-<action>`.
- **Animation Library** dashboard (filters, sortable columns, bulk bake, reference counts);
  group create/rename/nest/drag; open-clip from Asset Browser/Inspector/actions.
- **Preview Loadout** palette: drag inventory items/equipment sheets onto the preview to see
  clothes/weapon on the animated character (uses the existing equipment layer rules).
- Rename reference scan + update action; old clip section removed from Sprite Editor
  (button links to the new panel).

**Acceptance:** from one character sheet, create a complete Idle/Walk/Run/Attack/Dead set in
under two minutes, drag a sword item onto the preview and play it, switch between all clips
from the dashboard, and bake a set with output byte-identical to today's pipeline; runtime
untouched.

**✅ Status (2026-10-26): DONE.** `Engine/IDE/Panels/AnimationEditorPanel.cs`
(wired in `IDE.cs`): group tree (new/rename/nest/delete, search, filter), clip rows with dirty
badges, preview + playback, timeline frame strip (drag-reorder, per-frame holds, events,
insert/duplicate/remove), clip properties with Bake/Duplicate/Delete, `+ Clip` dialog, **New
Animation Set** dialog (rows prefilled from sheet rows or preview range, optional bake-all,
names `<sheet>-<action>`), and the **Library** dashboard (search/group/dirty filters,
click-to-sort columns, **Refs** column, Bake Dirty bulk bake). Data-driven templates persist:
the dialog seeds from `AnimationTemplates` (fallback: built-in presets) with a
**Save as template** button. Rename safety: live reference scan (scene base clip + action
clips + direct equipment layers + inventory `EquipClip`) with **Update N reference(s)**.
The toolbar also carries the plan's **quick actions** (`+Idle +Walk +Run +Attack +Dead`): one
click builds that action from the current preview range with the preset timing/event on the
selected source sheet, named `<sheet>-<action>`; an existing clip of that name is SELECTED,
never overwritten, so a second click cannot destroy authored frames/holds/events.
**✅ Preview Loadout added (2026-10-25, second pass).** Palette (inventory equipment
items + source sheets, text filter) with drag sources → drop on the preview canvas or the
layers list; layers list with visibility / remove / per-layer info; overlays drawn
back→front using the runtime's exact frame rules (authored clip → action-token auto-match →
position-in-clip, raw index clamp fallback, own-FPS when unsynced; negative layers paint
behind the base sprite). The loadout persists per group (`AnimationGroup.PreviewLoadout`,
editor-only data); item layers resolve LIVE from `ItemDef`, direct sheet layers carry their
own values. Preview overlay scale assumes a 1-world-unit-tall character — the scene
object's real height wins in-game.

**✅ Open clip from anywhere added (2026-10-26, third pass).** One navigation entry point:
`IDEBridge.RequestOpenAnimationClip` (wired in `IDE.cs`) + `AnimationEditorPanel.OpenAtClip`
(sheet, clip). **Asset Browser** clip boxes: click/drag still places a Sprite2D (the click is
deferred past the double-click window so the gesture never double-fires), **double-click opens
the clip**; the tiles are hand-drawn over an `InvisibleButton` for exactly that reason. The
**Inspector** gained `Edit Clip` buttons next to the Sprite2D and Player2D base-clip combos
and next to every **Animation Actions** clip combo, passing the action's own sheet (falling
back to the player sheet when it has none). A **double-click on a Library dashboard row**
also jumps to the Workspace. Navigation clears search/group/dirty filters when they would hide
the row, switches to the Workspace page, focuses the window and starts playback; an unknown
clip reports in the status line instead of silently selecting another.
The Library dashboard and the Workspace are now two page buttons (ImGui.NET's only flagged
`BeginTabItem` overload forces a tab close button — the tab bar was replaced, not extended).

**Known deviations (deliberate):** the Sprite Editor's old clip section was **kept** (with an
`Open Animation Editor →` button) instead of removed — quick clip creation from the preview
range still lives there, and deleting it was not worth the regression risk; the reference scan
ignores effects/trigger params and saved-but-unloaded scenes; no in-app smoke run yet —
verification is compile (0 warnings) + the data harnesses only, so the new gestures have not
been clicked through in the running IDE.

### Phase 2 — Skeleton authoring + bake (7–10 days)
- Rig setup mode: bones (add/parent/rename, setup pose), slots (draw order, visibility),
  region attachments (pick sheet+frame, pivot/offset/rotation/scale), skins list.
- Attachment palette drop onto a bone slot (region attachment in the current skin),
  `Add texture…` PNG import, drag between slots, duplicate for skin overrides.
- Animate mode: select bone in viewport, transform gizmo, auto-key, key insertion; tracks
  (rotate/translate/scale, slot attachment/color, event), interpolation (linear/stepped/
  bezier with handles); playback + onion skin.
- `SkeletonPose.Evaluate` + `SpriteRasterizer` + `AnimationBaker` (CPU); bake single clip,
  bake group, bake all; warnings for atlas overflow.
- Baked clip metadata: master height, per-frame feet offsets, events, hitboxes (from a
  designated bbox slot later).
- Skin selection for baking (composite bake).

**Acceptance:** create a 3-bone rig, 2 attachments and an idle/run cycle; bake at 12 fps;
the baked clip plays in Player2D/Sprite2D with feet aligned and events firing; editing the
rig then rebaking updates the atlas; no runtime code paths change.

### Phase 3 — Attachments at runtime (4–6 days, optional)
- Layered bake (slot groups) + `LayerClipNames` draw path.
- Skin/material swap at runtime (equipment layers integration), per-slot visibility/colour.
- Inventory items equip onto the layered bake through their existing `EquipSheet/EquipClip/
  EquipLayer` fields (no new item data), so drag-drop authoring carries over to gameplay.
- Optional: bone attachment points for effects (`hand`, `muzzle`) baked as clip metadata
  (per-frame anchor points) so projectiles spawn from the right place.

### Phase 4 — Spine-level extras (optional, iterative)
- Mesh attachments, FFD, weights/skinning, IK, path constraints.
- Spine JSON import/export (region-only first).
- Live skeleton runtime (`SkeletonAnimator2D`) as an alternative to bake for hero characters.

---

## 10. Testing

- `Tools/AnimationEvalTest` (new console harness): bone hierarchy math, curve evaluation
  (linear/stepped/bezier), pose determinism, slot draw order.
- `Tools/SkeletonBakeTest` (new): golden hashes for a fixed rig/clip; pivot/feet alignment
  (dot at root lands at the expected `RenderOffset`); loop seam (last≠first for loop);
  event mapping; max-size rejection.
- `Tools/SpriteSheetDataJsonTest` (extend): v1→v2 load, groups/rigs/tracks round-trip,
  unknown fields tolerated, binary-cache fallback.
- `Tools/BinCacheTest`: layout version bump, old sidecar ignored.
- `Tools/AnimationSetTest` (new, pure data): standard-set defaults (name `<sheet>-<action>`,
  loop/FPS/Hit event/hold-last), template overrides, duplicate-as naming, group auto-create.
- Attachment palette tests: item `EquipSheet/EquipClip/EquipLayer` → layer mapping, action-token
  clip match fallback, direct-layer creation, drag reorder produces draw order, and loadout
  data is editor-only (never serialized into runtime clip fields).
- Library tests: filters (group/mode/sheet/dirty/text), reference counts, bulk bake selection.
- Manual GL smoke: new panel opens; timeline scrub; standard set + bake; Player2D preview;
  drag a weapon item onto the preview; DoF mask follows the baked clip; equipment overlays
  still sync; project reopen restores groups/rigs/loadout.
- Build gate: `dotnet build` 0 warnings / 0 errors after each phase.

---

## 11. Risks & mitigations

| Risk | Mitigation |
|---|---|
| CPU rasterization slow for long/high-fps clips | background bake (existing `Task.Run` pattern), incremental `BakeKey`, per-clip frame budget warning |
| Pixel-art rotation looks jagged | nearest sampling by default, optional 2× supersample + downscale |
| 4096 atlas overflow | pre-bake estimate, "split clip/lower fps" guidance, per-clip atlas assignment |
| Clip rename breaks scene references | reference scanner + "Update references" action; names stay the public key |
| Schema churn / binary cache | explicit `FormatVersion`, additive fields only, cache version bump + self-heal |
| Editor scope creep | separate panel files, phase gates; mesh/IK/import deferred |
| Runtime regressions | phases 0–2 add no renderer code; existing clip tests must stay green |

---

## 12. Decision points for the user

1. **Skeleton v1 scope** — bones + slots + region attachments only (recommended), or include
   mesh/FFD/weights immediately? (The latter roughly doubles Phase 2.)
2. **Attachment strategy** — composite bake per skin (recommended for Phase 2) vs layered bake
   from day one (needed for runtime weapon swap; Phase 3).
3. **Panel placement** — separate Animation workspace panel (recommended) vs a tab inside the
   Sprite Editor.
4. **Spine interop** — defer JSON import (recommended) or require it in v1.
5. **Runtime skeleton** — bake-only forever (recommended for consistency/FPS) vs plan a live
   skeleton renderer for hero characters later.
6. **Standard action list** — is `Idle/Walk/Run/Attack/Dead/Jump Start/Jump End/Hurt/Cast`
   enough as the default set (recommended; editable templates), or do you have a fixed list
   for this project?
7. **Attachment source of truth** — items keep owning their equip art (recommended; works
   today), with the Animation Editor only previewing them, or clips may store per-clip
   attachment overrides as well?

Recommended starting point: Phase 0 + Phase 1 (clip workspace, standard set, library,
preview loadout), then evaluate Phase 2 with a small demo rig before committing to
attachments/layers.
