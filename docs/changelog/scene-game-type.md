# Scene game type: 2D SideScroller / 2D Top Down / 2.5D / 3D

## What
The Add New Scene dialog (and Edit Scene) now asks for the **Game Type** of a
Game Scene entry:

| Combo label | Enum | Saved key | Camera on creation |
|---|---|---|---|
| 2D SideScroller | `GameType.SideScroller2D` | `2d_sidescroller` | ortho, front view |
| 2D Top Down | `GameType.TopDown2D` | `2d_topdown` | ortho, front view |
| 2.5D | `GameType.Isometric25D` | `2.5d` | perspective |
| 3D | `GameType.ThreeD` | `3d` | perspective |

The combo is only enabled for **Scene Type = Game Scene** (menus/loading have no
gameplay perspective). Scene Manager gained a **Game** column showing the short
badge for every Game Scene.

## Camera setup
Only a NEWLY created scene is configured; existing views are never overwritten.

- 2D types: `CameraOrtho = true`, `CameraOrthoSize = 32` (default zoom) and the
  level-editor front view — position `(0, 0, 300)`, yaw `180`, pitch `0`. Selecting
  the new scene restores that view through the normal per-scene camera stash.
- 2.5D / 3D: keep the CURRENT camera pose but force perspective
  (`CameraOrtho = false`), so creating a 3D scene while authoring in a 2D ortho
  view does not leave the new scene zoomed like a level.

Editing the type of an existing scene only changes the label/persistence — the
scene keeps its saved camera.

## Persistence
`SceneAsset.GameType` stores the stable key in every `.ing` save path
(Scene Manager Save All / Save As + the Hierarchy single-scene save). On load,
`IDEBridge.ParseGameType(key, asset.EditorCameraOrtho)` reads it; a missing key
(older files) falls back to the saved camera: orthographic → 2D SideScroller,
otherwise 3D. `GameType` is metadata today — gameplay systems are unchanged, so
2D Top Down still runs the existing sidescroller player physics until top-down
movement is wired.
