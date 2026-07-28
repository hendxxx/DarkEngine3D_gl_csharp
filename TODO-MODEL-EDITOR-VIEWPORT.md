# Model Editor - Viewport Integration TODO

## Completed: ViewportPanel.cs - Gizmo, click-to-select, toolbar
- [x] TransformGizmo field synchronized with IDEBridge.GizmoMode (shared via Bridge.EditorGizmo)
- [x] Render gizmo at selected editor object's world position (in GameScene.Render)
- [x] Hit test gizmo on mouse click (in GameScene.UpdateBridgeData)
- [x] Handle gizmo drag to update object transform (in GameScene.UpdateBridgeData)
- [x] Add toolbar buttons for gizmo mode (W/E/R) + primitive creation (Plane/Box/Sphere)
  
## Notes
- Gizmo rendering happens in GameScene.Render() using OpenGL line drawing
- Gizmo interaction (hit test, drag) happens in GameScene.UpdateBridgeData()
- ViewportPanel provides toolbar buttons and synchronizes gizmo mode via Bridge
- Shared TransformGizmo instance via Bridge.EditorGizmo (set in IDE.cs)
