# Model Editor Integration - Implementation Progress

## Step A: Fix EditorObject.cs API Mismatches
- [x] Add constructor `EditorObject(EditorPrimitiveType type, string? name = null)`
- [x] Add `InitGPU()` as alias for `EnsureResources()`
- [x] Add uniform-location Draw overload matching EditorObjectManager's call
- [x] Add `GetWorldAABB()` method
- [x] Fix shadow method signature

## Step B: Create ModelEditorPanel.cs
- [x] Create new file Engine/IDE/Panels/ModelEditorPanel.cs
- [x] Object list with click-to-select
- [x] Create buttons (Plane, Box, Sphere)
- [x] Delete selected button
- [x] Gizmo mode toggle buttons
- [x] Properties section

## Step C: Update IDE.cs
- [x] Create EditorObjectManager instance
- [x] Assign to bridge.EditorObjectManager
- [x] Add "Model" menu with "Add Primitive" submenu
- [x] Add W/E/R keyboard shortcuts for gizmo modes
- [x] Instantiate ModelEditorPanel
- [ ] Wire EditorObjectManager Draw/RenderShadow in rendering pipeline

## Step D: Update ViewportPanel.cs
- [ ] Render gizmo when editor object is selected
- [ ] Handle mouse click for raycast selection
- [ ] Handle gizmo drag interaction
- [ ] Add toolbar buttons for gizmo mode + primitive creation

## Step E: Update InspectorPanel.cs
- [ ] Add RenderEditorObjectInspector() method
- [ ] Show Name, Position, Rotation, Scale, Color, Texture, CastShadow
- [ ] Wire up in Render() to detect SelectedEditorObject

## Step F: Update GameScene.cs
- [ ] Call EditorObjectManager.Draw() after main object rendering
- [ ] Call EditorObjectManager.RenderShadow() in shadow pass
