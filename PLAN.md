# Model Editor Integration Plan

## Current API Mismatches

### EditorObject.cs vs EditorObjectManager.cs
1. **Constructor**: `new EditorObject(type)` doesn't exist (only static factory `CreateDefault()`)
2. **GPU Init**: `InitGPU()` method doesn't exist (only `EnsureResources()`)
3. **Draw API**: Manager calls `obj.Draw(modelLoc, viewLoc, projLoc, ..., camera, light)` — EditorObject has `Draw(float dt, nint window, float moveSpeed)`
4. **Shadow API**: Manager calls `obj.RenderShadow(shadowModelLoc, camera, csm, cascadeIndex)` — EditorObject has `RenderShadow(Camera, CSM, int, uint, int)`
5. **AABB API**: Manager calls `obj.GetWorldAABB()` — EditorObject has property `WorldAABB`

## Implementation Steps

### Step 1: Fix EditorObject.cs
- Add constructor `EditorObject(EditorPrimitiveType type, string? name = null)`
- Add `InitGPU()` as alias for `EnsureResources()` + additional setup
- Add uniform-location Draw overload matching Manager's call
- Add `GetWorldAABB()` method
- Fix shadow method signature
- Keep existing APIs

### Step 2: Add EditorObject fields to IDEBridge.cs
- `EditorObjectManager? EditorObjectManager` property
- `GizmoMode GizmoMode` property (translate/rotate/scale)
- Selected editor object tracking

### Step 3: Create ModelEditorPanel.cs
- New ImGui panel with list of editor objects
- Create/delete buttons
- Property editing (transform, color, texture, shadow)
- Gizmo mode selector

### Step 4: Update IDE.cs
- Add Model menu with Create Primitive submenu
- W/E/R keyboard shortcuts for gizmo modes
- Wire EditorObjectManager lifecycle
- Instantiate ModelEditorPanel

### Step 5: Update ViewportPanel.cs
- Toolbar: Gizmo mode buttons (W/E/R) + Create buttons
- Gizmo rendering for selected editor object
- Viewport click → raycast editor objects (when no UI element hit)
- Selection wireframe for editor objects

### Step 6: Update InspectorPanel.cs
- When SelectedEditorObject != null, show editor properties
- Transform editing (position, rotation, scale)
- Color picker, texture path, shadow toggle

### Step 7: Wire rendering in GameScene.cs
- Call `EditorObjectManager.Draw()` after object rendering
- Call `EditorObjectManager.RenderShadow()` in shadow pass

