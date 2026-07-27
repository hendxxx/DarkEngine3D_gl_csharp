# TODO: Model Editor - Add Plane, Box, Sphere, glb Object

## Step 1: Create EditorObject.cs - Editor primitive 3D objects
- [x] Class EditorObject with enum EditorPrimitiveType { Plane, Box, Sphere, GlbReference }
- [x] Properties: Position, RotationEuler, Scale, Color, TexturePath, CastShadow
- [x] Vertex generation for Plane (using Object3D.CreatePlaneVertices)
- [x] Reference to glb file for GlbReference type
- [x] GPU resources (VAO, VBO) for primitive rendering

## Step 2: Add Plane vertices generation to Object3D.cs
- [x] CreatePlaneVertices() method
- [x] CreatePlaneObject() factory method

## Step 3: Create EditorObjectManager.cs
- [x] List<EditorObject> storage
- [x] Add/Remove/Clear operations
- [x] Draw() - render all editor objects
- [x] RenderShadow() - shadow pass
- [x] Selection via raycast

## Step 4: Create TransformGizmo.cs
- [x] Translation arrows (X=red, Y=green, Z=blue) with arrowhead
- [x] Rotation circles (wireframe rings)
- [x] Scale handles (cubes at axis ends)
- [x] Mouse ray intersection for drag
- [x] Active axis highlighting

## Step 5: Modify IDEBridge.cs
- [x] Add EditorObjectManager reference
- [x] Add SelectedEditorObject property
- [x] Add EditorObjects list

## Step 6: Modify IDE.cs - Add Create menu
- [x] Menu bar: "Create > Plane / Box / Sphere / glb Object"
- [x] Menu bar: "Edit > Delete Selected Object"
- [x] Wire up create actions

## Step 7: Modify InspectorPanel.cs
- [x] Show EditorObject properties (position, rotation, scale, color)
- [x] Color picker for object tint
- [x] Texture path selection (with drag-drop from Asset Browser)
- [x] Cast shadow toggle
- [x] Primitive type selector

## Step 8: Modify ViewportPanel.cs
- [x] Render 3D gizmo for selected editor object
- [x] Mouse ray intersection for selection
- [x] Handle gizmo drag interaction

## Step 9: Integrate with GameScene / IDE rendering
- [x] Initialize EditorObjectManager in IDE
- [x] Call Draw() and RenderShadow() in rendering pipeline
- [x] Proper cleanup on scene exit

## Step 10: Test and verify
- [x] Build and run
- [x] Test create Plane, Box, Sphere
- [x] Test create glb object from Asset Browser
- [x] Test properties editing in Inspector
- [x] Test gizmo interaction
- [x] Test shadow rendering

