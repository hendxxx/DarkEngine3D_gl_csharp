# Model Editor Integration - Implementation Plan

## Phase 1: Fix API Mismatches (EditorObject.cs ↔ EditorObjectManager.cs)
- [x] Fix EditorObject.cs constructors to support `new EditorObject(type)`
- [x] Add `InitGPU()` method
- [x] Add uniform-location Draw overload
- [x] Fix WorldAABB / GetWorldAABB naming

## Phase 2: Create ModelEditorPanel.cs
- [x] New ImGui panel with object list, create/delete, properties
- [x] Add to IDE.cs instantiation

## Phase 3: Update IDE.cs
- [x] Add "Model" menu with Create Primitive sub-menu  
- [x] Add W/E/R keyboard shortcuts for gizmo modes
- [x] Wire EditorObjectManager lifecycle

## Phase 4: Update ViewportPanel.cs
- [x] Render gizmo for selected editor object
- [x] Handle viewport click → raycast editor objects
- [x] Add toolbar gizmo mode buttons + primitive creation buttons

## Phase 5: Update InspectorPanel.cs
- [x] Add EditorObject property editing when selected

## Phase 6: Integrate rendering into game pipeline
- [x] Wire EditorObjectManager.Draw() and RenderShadow() in GameScene

