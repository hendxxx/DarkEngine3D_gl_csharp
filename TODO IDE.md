# IDE Cleanup & Dead Code Removal - TODO

## Bug Kritis
- [x] **Fix #1**: Panggil `InvalidateGameIngCache()` di `SaveGameIng()` dan `SaveAllRegisteredScenes()` — `Engine/Scene/SceneAssetSerializer.cs`

## Cleanup / Rapihin
- [x] **Fix #2**: Hapus handler `"canceleexit"` duplikat di `HandlePreviewBehavior()` — `Engine/IDE/Panels/ViewportPanel.cs`
- [x] **Fix #3**: Perbaiki komentar korup `///NaN` di IDE.cs
- [x] **Fix #4**: Perbaiki `seNaN` korup di IDEBridge.cs (SceneTextureWidth)
- [x] **Fix #5**: Hapus dead code `Matrix3x3` struct di Helpers.cs
- [x] **Fix #6**: Perbaiki dropdown 1 item tidak bisa dipilih
- [x] **Fix #7**: Rapihin komentar tidak konsisten

## File yang TIDAK Disentuh
- `Engine/Scene/MainMenuScene.cs` ❌
- `Engine/Scene/LoadingScene.cs` ❌
- `Engine/Scene/GameScene.cs` ❌
