# Bug Fix Plan — DarkEngine3D Main Menu Editor

## 🔴 CRITICAL
- [ ] #1 SceneManager.cs: Fix render-after-exit crash
- [ ] #2 MainMenuScene.cs: Call MapBehaviors after IDE edits
- [ ] #3 HUD.cs: Fix button mutation during iteration (defensive copy)
- [ ] #4 HUD.cs: Fix GPU memory leak (cache + delete scratch textures)

## 🟡 MODERATE
- [ ] #6 HUD.cs: Fix OpenGL state machine corruption in DrawBox
- [ ] #7 SceneManager.cs: Clean up FBO resources on incomplete status
- [ ] #8 SceneAssetSerializer.cs: Cache game.ing manifest to avoid full re-parse

## 🔵 MINOR
- [ ] #9 Program.cs + MainMenuScene.cs: Extract resolution constants to shared location
- [ ] #11 HierarchyPanel.cs: Guard undo against null parent / -1 index
- [ ] #12 OpenGL.cs: Fix face culling CW/CCW logic
- [ ] #13 HUD.cs: Dynamic VBO buffer sizing
- [ ] #14 IDEBridge.cs: Make AvailableScenes safer with IReadOnlyList exposure
