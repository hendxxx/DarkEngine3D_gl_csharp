# TODO: Scene Render Properties + Gizmo Fix

## Step 1: Add RenderProperties to IDEBridge.EditorScene
- [x] Add `SceneRenderProperties RenderProperties` to the `EditorScene` record in `IDEBridge.cs`
- [x] Initialize with default values

## Step 2: Show Render Properties in InspectorPanel
- [x] In `InspectorPanel.RenderEditorSceneInfo()`, add a collapsible "Render Properties" section
- [x] Show controls for: BackgroundColor, VSync, FaceCulling, FrontFaceWinding, WireframeMode, DepthTest, Blending
- [x] When properties change, apply via `renderProps.Apply()`

## Step 3: Fix Gizmo Real-Time Update
- [x] Move the gizmo 3D rendering to AFTER the IDE overlay render in the SceneManager loop
- [x] Ensure the gizmo renders into the correct FBO before rendering

## Step 4: Build and Test
- [x] Build the project and verify no errors
