# Model Editor - Viewport Integration TODO

## Step 1: IDEBridge.cs - Add EditorObjectManager and selected object
- [ ] Add `EditorObjectManager? EditorObjectManager` property
- [ ] Add `EditorObject? SelectedEditorObject` property (distinct from GltfObject selection)

## Step 2: IDE.cs - Instantiate EditorObjectManager, wire to bridge
- [ ] Create `EditorObjectManager` instance
- [ ] Assign to bridge.EditorObjectManager
- [ ] Add keyboard shortcuts for gizmo mode (W=Translate, E=Rotate, R=Scale)
- [ ] Add "Model" menu with "Add Primitive" submenu
- [ ] Add EditorObjectManager Draw/RenderShadow integration in rendering pipeline

## Step 3: ViewportPanel.cs - Gizmo, click-to-select, toolbar
- [ ] Add gizmo rendering when EditorObject is selected
- [ ] Add click-to-select via raycast from viewport click
- [ ] Add gizmo axis hit testing and drag interaction
- [ ] Add toolbar buttons for gizmo mode + primitive creation
- [ ] Add keyboard shortcuts W/E/R for gizmo mode

## Step 4: InspectorPanel.cs - EditorObject properties
- [ ] Add `RenderEditorObjectInspector()` method
- [ ] Show Name, Position, Rotation, Scale, Color, Texture, CastShadow
- [ ] Wire up in Render() to detect SelectedEditorObject

## Step 5: GameScene.cs - Integrate EditorObjectManager rendering
- [ ] Call EditorObjectManager.Draw() after main object rendering
- [ ] Call EditorObjectManager.RenderShadow() in shadow pass
- [ ] Wire up bridge data for editor objects

