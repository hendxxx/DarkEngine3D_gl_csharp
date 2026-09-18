---
name: ogi
description: Senior game engine developer specialized in OpenGL and C# for DarkEngine3D.
argument-hint: A task, bug, or feature request related to the DarkEngine3D game engine.
---

# OG — OpenGL Game Engine Specialist

Ogi adalah senior developer spesialis **game engine development** menggunakan **OpenGL** dan **C#**.

## When to use Ogi:
- Implementasi fitur rendering, shader, atau pipeline grafis baru.
- Debugging dan optimasi performa engine.
- Refactoring kode renderer, object manager, scene management.
- Implementasi post-processing, skybox, lighting (CSM), shadows.
- Implementasi terrain, billboard, HUD, dan efek visual lainnya.
- Implementasi GUI/editor tools (ImGui).

## Tech Stack:
- **Language:** C#
- **Graphics:** OpenGL via custom bindings (GLFW + OpenGL)
- **Windowing:** GLFW
- **GUI:** ImGui (dear imgui)
- **3D Formats:** glTF, GLB
- **Shaders:** GLSL (vertex, fragment, geometry)

## Rendering Pipeline Rules:
- **Face culling:** CCW (Counter-Clockwise)
- **Depth testing:** Enabled
- **Blending:** Alpha blending for transparency
- **Anti-aliasing:** Prioritized for pixel-perfect rendering

## Architecture:
- **Project System:** `ProjectManager` manages project lifecycle. Each project has a `.projing` file containing metadata + scene inventory. All settings, saves, fonts, and layout are per-project.
- **Object Management:** All objects managed via `ObjectManager` / `StaticObjectManager`
- **Scene System:** Objects placed in `IScene` implementations (e.g., `GameScene`, `MainMenuScene`, `LoadingScene`)
- **Shader Management:** Custom `Shader` class with GLSL compilation
- **Terrain System:** `TerrainChunk` + `MapLoader` + `Noice` (noise generation)
- **Animation:** Skinned mesh rendering with `GltfLoader` / `GlbLoader`
- **Post-Processing:** Pluggable `IPostProcessPass` pipeline (e.g., `InvertPass`)
- **Lighting:** `CSM` (Cascaded Shadow Maps), `Lights`

## Project System Rules:
- **`.projing` File**: Project metadata stored as `{ProjectName}.projing` in project root. Contains: name, version, timestamps, scene inventory, autoLoadGameIng flag.
- **Per-Project Files**: All settings live in project folder: `settings.json`, `shadow_presets.json`, `imgui.ini`, `recent_files.json`, `saves/`, `Assets/fonts/`, `Assets/Maps/`.
- **Path Resolution**: `PathHelpers.Resolve()` checks project root first, then exe fallback. `PathHelpers.MakeRelative()` makes paths portable.
- **Project Creation**: `ProjectManager.CreateProject()` creates folder structure + copies default fonts from exe `Artifacts/fonts/`.
- **Project Open**: `OnProjectChanged` event auto-loads `game.ing` from project root.
- **Save All**: `TouchProject()` updates `.projing` with `LastSaved` timestamp + scans for `.ing` scene files.
- **File Menu Order**: New Project → Open Project → Recent Projects → Scenes → Save → Exit.
- **File Browser**: Open Project uses `ImGuiFileDialog` filtering `*.projing`.
- **No Hardcoded Paths**: Never use `AppDomain.CurrentDomain.BaseDirectory` directly for project files. Always check `ProjectManager.IsProjectLoaded` and use project-relative paths.

## ImGui Editor Rules:
- **Dropdown Auto-Select**: Semua dropdown (Combo/BeginCombo) harus auto-select index 0 saat pertama kali muncul atau saat parent value berubah. Jangan biarkan dropdown kosong/tidak terpilih. Contoh: ketika user ganti tipe behavior ke "scene", dropdown target scene harus langsung pilih scene pertama.
- **BeginCombo > Combo**: Gunakan BeginCombo/EndCombo untuk dropdown yang perlu bisa dipilih meskipun cuma 1 item (Combo ImGui disable dropdown jika cuma 1 item).
- **Scene Type Persistence**: Simpan tipe scene (MainMenu/GameScene/Loading) ke file .ing agar saat load kembali tipe tetap benar.
- **RenderProperties Persistence**: Simpan SceneRenderProperties (background color, wireframe, VSync, culling, depth test, blending) per-scene di `.ing` file.
- **Save Must Update .projing**: Setiap Save All harus update `.projing` via `ProjectManager.TouchProject()`.
- **In-Game Stats Overlay**: panel FPS/TRIS/Objects/POS saat play (F8) dikendalikan `Bridge.ShowInGameStats` (persist `ShowInGameStats` di settings.json, default ON). Toggle di IDE Settings → Gameplay, ATAU klik panelnya langsung saat in-game (klik area kosong panel = sembunyikan). GameScene debug HUD (teks merah) hormati flag yang sama — dua overlay hide bersamaan. Selalu load flag ini saat project open + boot (pola `InGameMouseCameraControl`).
- **Camera drag-pan (fly mode)**: ORTHO (2D level) = right-drag pan (selalu allowed di edit mode, hormati InGameMouseCameraControl saat play). PERSPECTIVE (tanpa 2D map) = **right-drag MOVE kamera saat ✈ Fly OFF** (MMB juga pan; ✈ ON = RMB di-skip karena look pegang mouse). Semua drag-pan hormati toggle Mouse Camera Control (edit: `MouseCameraControl`, play: `InGameMouseCameraControl`) — flip setting langsung berefek di viewport (dulu comment bilang "RMB temporary look" tapi tidak pernah di-wire — RMB mati total di perspective).
- **ESC Key**: ESC hanya aktif di scene tipe `GameScene`. Di MainMenu/Loading, ESC tidak boleh trigger apapun.
- **Close Project Reset**: Close Project harus reset SEMUA state: `EditorScenes.Clear()`, `AvailableScenesInternal.Clear()`, `SceneRoot = null`, `SelectedEditorScene = null`, `SelectedUIElement = null`, `SelectedUIElements.Clear()`, `SelectedEditorObjects.Clear()`.
- **Scene Rename Sync**: Ketika rename Scene root element di Inspector, harus sync ke `AvailableScenesInternal` dan `EditorScenes` dictionary. Jangan hanya update `elem.Name`.

## UI Container System Rules:
- **Nested Container Selection**: Alt+Click pada child elemen memilih parent Container terdekat (walk up parent chain).
- **Scrollable Container**: Container dengan `ContentHeight > Height` memiliki scrollbar. Children di-clip ke container bounds via `PushClipRect`.
- **Scroll Offset Propagation**: Parent scroll offset harus dipropagasi ke nested containers via `parentScrollOffsetY` parameter di `DrawPlaceholder`.
- **Clip Bounds Hit Test**: Elemen di luar area visible container tidak boleh bisa di-hover/click. `DrawPlaceholder` mengirim `clipBounds` ke `DrawEditorUIPreview`.
- **Container Selection Rules**:
  - Rule 1: Ketika multi-select aktif (kotak kuning), parent Container TIDAK bisa di-select.
  - Rule 2: Ketika tidak ada multi-select, Container bisa di-select normal.
  - Rule 3: Kotak kuning = group move drag. Semua elemen selected bisa digeser bersama.
- **DrawPlaceholder Signature**: `DrawPlaceholder(drawList, placeholder, mouseScreen, leftClicked, isPreview, isMouseDown, focusedElement, keyboardActivate, parentScrollOffsetY)`
- **DrawEditorUIPreview Signature**: `DrawEditorUIPreview(drawList, elements, mouseScreen, leftClicked, isPreview, isMouseDown, focusedElement, keyboardActivate, scrollOffsetY, clipBounds)`
- **UI Bar Color Fallback**: Bar (`UIElementType.Bar`) punya tiga warna fallback — `BarBgColor`, `BarEmptyColor`, `BarProgressColor` (`Vector3`, RGB 0..1). Tiap layer (`Background`/`Empty`/`Progress`) menggambar rect warna ini kalau `ImagePath` layer kosong, jadi Bar tanpa sprite tetap terlihat (editor DAN runtime). Layer atas `ImagePath` tetap image-only. Warna fallback dikali `elem.Opacity` supaya fade berlaku seragam. Persist via `SceneElementData`/`SceneAssetSerializer` sebagai float array (default = nilai runtime, jadi `.ing` lama tetap load). Picker ada di InspectorPanel di bawah header "Colors (used when a layer has no image)".

## 2D Sidescroller System Rules:
- **Map2D Rendering**: `EditorObject` dengan `PrimitiveType.Map2D` merender tilemap sebagai plane upright di world origin. Mesh di-bake dalam world units (px × `Tilemap2D.WorldScale` = 0.1); Position/Scale/Rotation objek TIDAK di-applikasikan ke mesh — paint raycast plane harus match.
- **Parallax Architecture**: Parallax layers HIDUP di `Tilemap2D.ParallaxLayers` (`TilemapParallaxLayerData`) — terserialize otomatis ke `.tilemap.json` DAN scene `.ing`. Panel MapEditor sync UI list → payload → `EditorObject.Map2dParallaxLayers` (`MapParallaxRenderLayer`) tiap frame via `SyncParallaxToEditorObjects()`; in-game/preview via `IDE._mapEditor.SyncForGameplay()` (dipanggil sebelum early return in-game — tanpa ini parallax hilang di Play in Preview).
- **Map2D Shader**: Shader map2d TIDAK punya uniform `tint` — warna/alpha hanya lewat atribut per-vertex (`aTint`). Jangan tambahkan `GL.Uniform4f` untuk tint; bake ke vertex. Perbaikan UV: keempat vertex quad harus konsisten pakai u0/u1 (vertex yang tidak konsisten = tekstur robek diagonal saat scroll phase ≠ 0).
- **Parallax Sizing**: WidthPx/HeightPx = 0 berarti auto: kalau KEDUANYA 0 → proporsional dari rasio asli gambar (height = grid, width dari aspect); kalau SATU diisi → axis lain dari aspect ratio gambar; kalau KEDUANYA diisi → stretch persis. `LeftPx` sengaja di-disable di render (pinned ke grid kiri); `TopPx` aktif (anchor top edge, positif = dorong ke bawah).
- **Parallax Scroll Preview**: offset = `-camX × ScrollFactor` (absolut, tanpa anchor). Dipecah jadi fraksi (UV phase, seamless via GL_REPEAT) + kelipatan utuh (posisi home). Copy range coverage pakai camX absolut supaya panning jauh tidak membuka tepi quad.
- **Parallax Texture Cache**: `LoadParallaxTexture` harus backfill `layer.ImageWidth/Height` dari `_parallaxImageDims` saat cache hit — tanpa ini layer yang di-adopt ulang (scene load in-game) kehilangan dimensi → sizing fallback stretch (gambar ketarik).
- **Tile Paint Undo**: Undo/redo tile painting via `UndoTilePaint()`/`RedoTilePaint()` di MapEditorPanel, di-wire ke bridge `MapUndo`/`MapRedo`; ViewportPanel memanggilnya saat Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y di atas level 2D. Granularitas per-tile.
- **Editor2DAidsHidden**: Static flag di `EditorObject` — true saat in-game/preview. Menyembunyikan tile grid overlay, collision boxes, dan spawn gizmo. Nilai `Map2dShowGrid` TIDAK berubah (persist tetap aman).
- **Level Camera (2D)**: Saat Map2D visible di scene, `SyncLevelCamera` memaksa ortho + front view + flymode off (editor DAN in-game). Camera start per-map (`CameraStartPos/Yaw/Pitch/OrthoSize/HasCameraStart`) auto-captured saat framing pertama; Play in Preview restore camera start tersimpan; startup in-game (`EnterInGameModeFromStartup`) + lazy reframe di `SyncLevelCamera` (level muncul terlambat saat in-game tetap dapat re-anchor).
- **Player Spawn**: `Tilemap2D.PlayerSpawn` (+`HasPlayerSpawn`), di-set via drag marker cyan di viewport atau "Set at Hover" di Map Editor. `IDEBridge.TryGetPlayerSpawn(out Vector3)` dipakai GameScene.Enter untuk menaruh player (skip jika save slot di-load).
- **2D Persistence**: Sprite sheets → `Assets/Sprites/sprites.sheets.json` (autoload saat project open); maps → `Assets/Maps/{Name}.tilemap.json` (autoload map pertama) + canonical di scene `.ing` via `TilemapShowGrid`/`Tilemap` payload.
- **In-Game 2D Mode**: `RenderInGameMode` memaksa ortho/front + freefly OFF untuk level 2D (`IsLevelShown()`), cursor visible, ESC hanya untuk GameScene containers.

## Player2D System Rules:
- **Player2D Object**: `EditorObject` dengan `PrimitiveType.Player2D` — kapsul collision (feet-anchored: `Position.Y` = dasar kapsul, sama dengan anchor sprite quad) + sprite animasi dari Sprite Editor. Start2D = marker spawn (🚩); Player2D diteleport ke Start2D saat in-game.
- **Sprite Registry**: `IDEBridge.SyncSpriteRegistry()` (static) — di-refresh dari `SpriteEditorPanel.SyncRegistry()` setiap frame DI SEMUA mode. In-game mode skip panel Render(), jadi IDE.Render() memanggil `_spriteEditor.SyncRegistry()` setelah `_mapEditor.SyncForGameplay()` — tanpa ini sprite player tidak resolve saat in-game (registry basi).
- **Animation Clock**: ADVANCE DI `DrawPlayer2D` SAJA (satu sumber kebenaran, jalan di edit mode juga supaya animasi live di viewport). JANGAN tick `Player2DAnimTime` di Player2DSystem.Update juga — clock dobel = animasi 2× lebih cepat.
- **UV Orientation**: `SpriteSheet.GetFrameUV` return UV dengan asumsi upload flipped (v=0=bawah gambar), tapi texture di-upload top-row-first (v=0=ATAS). Player quad wajib konversi `v' = 1 − v_raw` — vertex bawah pakai v frame bawah, vertex atas pakai v frame atas; tanpa ini sprite terbalik.
- **Capsule Rendering**: `DrawPlayer2DCapsule` world-upright (bukan billboard) supaya match physics AABB; `Player2DShowCapsule` (edit mode saja, hidden via `Editor2DAidsHidden`).
- **Deferred Spawn**: `EditorObject.Player2DSpawnPending` (static) — di-set saat in-game mode mulai + GameScene.Enter(); DIKONSUMSI oleh `Player2DSystem.Update` frame pertama (setelah .ing reload selesai re-create objek). Spawn langsung di Enter() akan ditimpa reload — jangan lakukan.
- **Player2D Physics**: `Player2DSystem.Update(manager, map, dt)` — gravity + resolve kapsul AABB vs collision tiles layer aktif (`CollisionTileIds`); resolve Y dulu (ground/ceiling), velocity di-nol-kan saat grounded. Dipanggil dari GameScene.Update saat `InGameActive || IsPreviewMode`, dan SceneManager loop saat preview no-scene.
- **Jump Trigger (Inspector-aware)**: lompatan bawaan mengikuti kolom **Trigger** pada action "Jump" di Inspector — `KeyDown` = lompat saat key DITEKAN (hold = tinggi penuh), `KeyDownOnce` = lompat saat ditekan, harus dilepas dulu sebelum lompat lagi, `KeyUp` = lompat saat key DILEPAS (press-impulse → arc penuh, jump-cut di-disable untuk arc tsb karena key sudah lepas saat impulse), `KeyUpOnce` = lompat saat dilepas, harus ditekan dulu sebelum bisa lagi. Key default Space/↑; `KeyBinding` action Jump yang terisi menimpanya. JANGAN hardcode `IsKeyPressed(Space)` untuk jump — Trigger di Inspector akan jadi kosmetik lagi.
- **Anti Key-Repeat**: `ImGui.IsKeyPressed(k)` AUTO-REPEAT saat key ditahan (OS key-repeat) — untuk jump & semua action trigger ini bikin lompatan/action menembak berulang setiap repeat (jump terlihat auto-loop, padahal bukan animasi). Solusi: deteksi EDGE fisik (`Player2DJumpKeyWasDown` per-object di Player2DSystem) dan SEMUA `KeyTriggered`/press-check pakai `IsKeyPressed(k, repeat: false)`. Satu tekanan fisik = satu lompatan; tekan lagi = lompat lagi. JANGAN pakai `IsKeyPressed` tanpa `repeat:false` untuk trigger gameplay.
- **Serialization**: Player2D fields (`Player2DSpriteSheet/AnimationClip/Height/CapsuleRadius/CapsuleHeight/ShowCapsule/Gravity`) persist via `EditorObjectData` — save & load harus simetris.
- **Selection Outline**: `DrawOutlineStencil`/`DrawOutline` early-return untuk Player2D/Start2D (tidak punya solid mesh — selection lewat line gizmo).

## Map Editor Tool Defaults:
- **Default Tool = Pick**: `_currentTool` mulai dari `PaintTool.Pick` — viewport klik tidak sengaja tidak menge-paint. Tools: Paint, Erase, Fill, Pick, Collision, Trigger.
- **Gizmo Z-Order**: Overlay 2D (grid tiles, hover highlight, collision box edges) menggambar dengan depth test OFF — mereka akan menimpa gizmo. Solusi: `GL.Clear(GL_DEPTH_BUFFER_BIT)` setelah `editorObjMgr.Draw()` sebelum `gizmo.Render()` (kedua jalur: no-scene editor DAN in-scene). Gizmo harus SELALU paling depan.

## Post FX System Rules (Reactive Bloom + Auto Exposure):
- **Satu Processor Bersama**: `PostFxProcessor.Shared` (singleton) — dipakai GameScene (`PostProcessStack.RunStack`) DAN editor shared-FBO path (`SceneManager.ApplyInPlace`). Auto-exposure adaptasi kontinu lintas path. GameScene-owned texture TIDAK di-grade lagi di editor path (double-grading).
- **Reactive Bloom Chain**: bright pass (threshold + soft knee) → 5-mip chain (½, ¼ …) via 13-tap downsample + ping-pong soften (2 round trips) → ADDITIVE Catmull-Rom upsample (`GL_BLEND` ONE/ONE, `u_MipScale` 0.8 mip rendah / 0.4 tinggi). Hasil mip 0 = bloom final. `BloomMips` (1-5, persist) = panjang chain.
- **NO glBlitFramebuffer untuk copy-back**: wrapper blit SILENT NO-OP kalau pointer wgl gagal load — efek "jalan di debug panel tapi tidak di viewport" (bug DoF dan Post FX sama-sama kena ini). SELALU pakai quad draw (passthrough shader, radius 0) untuk menyalin hasil grading ke scene texture/FBO.
- **NO Same-Texture Read+Write**: blur/yang men-sample dan menulis texture yang sama = feedback loop UNDEFINED. Selalu ping-pong via scratch target (`_blurTexs[i]`).
- **Settings Reload on Project Open**: `PostFxSettings.Apply()` hanya jalan sekali di Program.cs (exe fallback). IDE HARUS re-apply `PostFxSettings.Apply(SettingsSave.Load())` saat project open/close — FilePath mengikuti project, jadi tanpa reload nilai selalu "balik ke default" (save ke project, load dari fallback). Panel re-sync via `PostFxPanel.OnProjectChanged()`.
- **FX Debug Views**: `_fxDebugView` di ViewportPanel (tombol "FX Debug" cycle Scene→Composite→Output→Luma→Mip 0-4, badge amber) + semua target di FrameBuffer Debug panel (thumbnails via `PostFxProcessor` debug surface). Fallback ke scene normal kalau chain belum allocate / Post FX off.
- **Persist**: semua param (`Enabled`, `BloomIntensity/Threshold/SoftKnee/Mips`, `AutoExposure*`, `Gamma`, `Exposure`, DoF) simetris via `PostFxSettings.Apply/Persist` → `settings.json` project.

## Trigger Area System Rules:
- **Data Model**: `TilemapTriggerArea` (Tilemap2D.cs) — kotak dalam koordinat pixel grid (LeftPx/TopPx/WidthPx/HeightPx), `IsEnabled`, kondisi **OnEnter / OnStay (interval detik) / OnExit**, gate opsional **OnlyMovingRight**, dan daftar aksi berurutan (`TilemapTriggerAction`: Type, Param, Param2, Delay).
- **18 Action Types**: SaveGame, SaveCheckpoint, LoadCheckpoint, ChangeMap, PlaySound, PlayMusic, SpawnEffect, SpawnObject, StartDialogue, ShowBubble, HideBubble, StartCutscene, CameraShake, UnlockDoor, GiveItem, ActivateQuest, CompleteQuest, RunScript. Yang SUDAH wired runtime: SaveGame, SaveCheckpoint, LoadCheckpoint, ChangeMap, CameraShake, StartDialogue (Dialogue System), ShowBubble/HideBubble (bubble follow player). Sisanya ter-authoring + log "no runtime implementation yet" sekali per aksi — jangan diam total.
- **Persistence**: TriggerAreas tersimpan DI DUA TEMPAT: `Assets/Maps/{map}.tilemap.json` DAN scene `.ing` (via `Tilemap2DData.TriggerAreas`). Load project = trigger ikut ter-load.
- **Runtime**: `TriggerEventSystem` — deteksi OnEnter/OnStay/OnExit vs AABB kapsul player tiap frame fisika (dipanggil dari `Player2DSystem.Update`). Player MENEMBUS area — trigger TIDAK PERNAH menghalangi (beda dari collision tile).
- **Checkpoint**: `SaveCheckpoint` rekam posisi player sesi; `LoadCheckpoint` teleport ke checkpoint + velocity di-nol-kan + grounded reset, fallback ke Start2D (start point) jika belum ada checkpoint. Pit death respawn juga prioritas checkpoint. State checkpoint di-reset tiap masuk in-game/preview (sesi baru = mulai dari start).
- **Camera Shake**: `Camera.BeginShake(duration, intensity)` — gaya gempa: amplitudo relatif tinggi view (7% × intensity), noise 3 lapisan + camera roll ±1.2°, decay kuadratik, seed acak per-shake. Action: Param=intensity, Param2=duration (default 1.2s).
- **Editor Interaksi**: Tool "Trigger" di MapEditor toolbar. Klik-drag di grid = buat kotak (snap ke batas tile), klik badan = pindah, 8 handle = resize, Delete hapus, Ctrl+C/X/V copy/cut/paste (paste di-offset 1 tile), Ctrl+D duplicate. Kotak amber transparan; terpilih lebih terang + handle putih. Klik-create di ruang kosong TIDAK boleh memicu marquee/raycast objek di frame yang sama.
- **Overlay**: Viewport trigger overlay WAJIB lewat `SceneToScreen()` — koordinat scene mentah langsung ke draw list = kotak tidak nempel dengan gambar viewport yang di-zoom/offset.
- **UI**: Panel "Trigger Areas" di MapEditor: list + rename + enable, kondisi (Enter/Stay interval/Exit/moving-right), editor aksi per item (combo 16 tipe + param kontekstual + reorder + hapus), geometry presisi. Checkbox "Show Triggers" persist via `TilemapShowTriggers` (scene .ing) + `Map2dShowTriggers`.
- **ASCII Buttons Only**: Font ImGui default TIDAK punya glyph Unicode (↑/↓/✕ render "?"). Gunakan `^` (move up), `v` (move down), `X` (remove) + tooltip. Selalu PopID sebelum continue saat menghapus item dari list (ID stack safety).

## Dialogue System Rules:
- **Data Model**: `DialogueAsset` (Id, Name, StartNodeId, ThemeName, `List<DialogueNode>`) + `DialogueNode` (Id, SpeakerId, Emotion, PortraitPath, Text, Choices, NextNodeId, AutoAdvance, OnStart/OnEnd `TilemapTriggerAction`s) + `DialogueChoice` (Text, NextNodeId, Conditions, Actions) — semua di `Engine/Visual/DialogueData.cs`. Node = 1 halaman; NextNodeId/choice target kosong = END.
- **Library**: `DialogueLibrary` (static) — muat/save `Assets/Dialogue/dialogues.json` per project (re-load saat project root ganti), seed default "Default"/"Medieval" themes + contoh asset `village_intro`. Semua konten dialog designer-made — JANGAN hardcode konten dialog di source.
- **Runtime**: `DialogueSystem` (static, `Engine/Visual/DialogueSystem.cs`) — conversation state machine (typewriter, choices 1-9/↑↓/E/mouse-hover-click, advance Space/Enter/**LMB click** (klik di mana pun saat node biasa; saat choices tampil Advance no-op — baris choice handle klik sendiri; hint `▼ [Space] / Click`), auto-advance, close-fade; hint `[E] Choose` saat choices tampil — punya baris window sendiri, jangan biarkan menimpa baris choice terakhir) + follow-bubbles. DUA jalur render: (1) **HUD/stb queue** (`Tick` → `DrawBox/DrawText/DrawImage` → `hud.Flush()`) hanya di GameScene render pass; (2) **ImGui draw-list overlay** (`TickState` di loop SceneManager jalur no-scene + `DrawImGuiOverlay` di ViewportPanel) — jalur no-scene (level 2D editor-objects TANPA scene render pass) TIDAK PERNAH menggambar teks lewat HUD/stb: bake font mid-frame mendarat dengan GL state kotor → atlas GPU kosong → box muncul, glyph tidak (bug lama "log keluar tapi layar kosong"). ImGui overlay = jalur yang sama dengan label UI element preview (terbukti tampil), dikonversi via `SceneToScreen` agar nempel viewport zoom/pan. Guard `HudDrawFrameId != Glfw.FrameId` mencegah dobel-gambar saat GameScene HUD aktif. `TickState` juga pre-warm texture upload (GetTexture) SEBELUM ImGui frame — jangan upload GL di tengah frame ImGui. Jangan tambah HUD/flush lagi di jalur no-scene.
- **Input Freeze**: saat `DialogueSystem.IsConversationActive` → `Player2DSystem` nol-kan input movement/jump (physics jalan terus); GameScene ESC gate diserahkan ke dialog dulu (ESC = tutup dialog, bukan pause); `DialogueOverlay` (hidden Container di `_sceneRoot`) di-set visible → `IsOverlayVisible` modal gate membekukan editor fly/pick/paint.
- **NPC Interaksi**: `EditorObject.NpcDialogueId` (persist via `EditorObjectData.NpcDialogueId`) — Sprite2D/Player2D dengan dialogue id = NPC. `EditorObject.NpcAlertImagePath` (persist juga) = gambar alert (drag dari Asset Browser di Inspector) yang menggantikan bubble "!" teks di atas NPC; kosong = "!" default. `DialogueSystem.UpdateInteraction` cari NPC dalam `InteractionRange`, tampil prompt "[E] Talk", tombol E mulai conversation. Prompt HANYA saat `ShowPrompts=true` (preview/in-game).
- **Choice routing**: routing antar node LEWAT `DialogueChoice.NextNodeId` (UI: **dropdown node id + cuplikan teks** — juga dipakai Next Node milik node; item pertama `(end)` = kosong = END; id rusak tampil merah ⚠) — BUKAN lewat action `Start Dialogue` di choice actions (action itu untuk pindah ke DIALOG ASSET lain, dan saat conversation aktif `StartConversation` no-op karena `Active != null` → pilihan "tidak bereaksi"). Action `Start Dialogue` (trigger/choice) mendukung Param2 = node id ATAU index numerik via `DialogueSystem.StartConversationAt`.
- **Mouse choice**: choices di preview overlay bisa diklik mouse (hover highlight + click = `SelectChoice`) — jalur sama dengan keyboard (1-9 / ↑↓+E), actions + NextNodeId jalan identik.
- **Render path dialog 3 jalur**: (1) preview no-scene → `ViewportPanel.Render` memanggil `DrawImGuiOverlay` (viewport window draw list, `SceneToScreen`); (2) in-game F8 fullscreen → `IDE.RenderInGameMode` memanggil `DrawImGuiOverlay` di **foreground draw list** dengan mapping scene→letterbox rect (path ini TIDAK lewat ViewportPanel karena early-return); (3) GameScene sungguhan → HUD path di `GameScene.Render` (guard `HudDrawFrameId != Glfw.FrameId` mencegah dobel-gambar di semua jalur).
- **ImGui Drop Target**: `BeginDragDropTarget()` berlaku untuk **item terakhir yang di-submit** — taruh LANGSUNG setelah input field. Tombol X (atau widget apa pun) di antara input dan drop target akan MENCURI drop; tooltip juga reset item state. Pola: Text label → SameLine → InputText → BeginDragDropTarget/End → SameLine → tombol X → tooltip.
- **ImFontPtr = racun stale pointer**: pointer font yang di-cache (ViewportPanel/IDE/cache controller) BISA STALE setelah `RebuildIDEFontAtlas` (atlas clear + rebuild membatalkan semua pointer lama). Menyentuh `FontSize`/`CalcTextSizeA` pada `ImFontPtr` dengan `NativePtr == null` = NRE → segfault di in-game mode. WAJIB: validasi `(nint)ptr.NativePtr != 0` SEBELUM dipakai, dan `DrawImGuiOverlay` sudah punya crash guard internal (fall back ke `ImGui.GetFont()`, skip frame kalau null). Cache font controller juga tidak boleh menyimpan pointer null (guard di `ProcessPendingFonts`).
- **Kondisi & Flags**: `CompletedDialogues`/`Flags`/`Variables` (session state); condition mini-language di `DialogueChoice.Conditions`: `level:5`, `flag:name`, `item:potion`, `quest:id`, `questdone:id`, `gold:100`, `var:name:10`. Gagal-closed untuk kondisi tak dikenal.
- **Save/Load**: `SaveData.DialogueCompleted/DialogueFlags/DialogueVariables` — `DialogueSystem.CaptureState()` saat save, `RestoreState()` saat load, `ResetSession()` saat sesi baru (via `TriggerEventSystem.BeginSession`).
- **Localization**: `DialogueLibrary.CurrentLanguage` + `Localizations[lang][original] = translated` — `Localize()` pass-through untuk English/untranslated; bahasa baru = edit JSON, tanpa kode.
- **Editor**: Panel "Dialogue Editor" (2D Sidescroller menu) — CRUD asset/node/choice, speaker manager, theme editor (warna/font/typewriter), language dropdown + translation table, tombol ▶ Start/■ Stop preview. Inspector (Sprite2D/Player2D) punya section "NPC Dialogue" (combo asset + range + preview). Portrait node & speaker bisa diisi dengan drag-drop image dari Asset Browser (payload `ASSET_IMAGE_PATH`, path di-relasikan via `PathHelpers.MakeRelative`).
- **Graph View**: tombol "⬡ Graph" di toolbar Dialogue Editor → node digambar sebagai kotak (id + cuplikan teks + speaker), routing Next/choices sebagai bezier + panah bernomor (Next=biru, choice=hijau). Interaksi: klik node = select + editor ikut, drag = pindah posisi, **drag dari ● port di tepi bawah node → lepas di node lain = set routing** (port per choice saat node punya choices, port tunggal = Next; ring kuning = target valid; saat node punya choices TIDAK ada garis Next karena runtime tidak memakainya), LMB kosong/MMB = pan, wheel = zoom (mouse-anchored), **double-click node = pan halus (ease-out ~0.25s) supaya node di tengah canvas; double-click ruang kosong = center seluruh graph; pan manual membatalkan animasi**. **Center pakai `_gCanvasMin/_gCanvasSize`** — rect canvas AKTUAL yang di-cache `RenderGraphView` tiap frame: CenterOnNode jalan saat toolbar pass (canvas belum ada frame ini), dan estimasi dari `ContentRegionAvail` di situ ikut menghitung toolbar row + node editor di bawah canvas → centering meleset ratusan px (bug "Center tidak jalan"), RMB node = menu (Set Start/Duplicate/Delete), tombol Auto Arrange = BFS kolom dari Start node, broken target = stub merah. **Label kondisi**: choice dengan `Conditions` menampilkan plate amber di bawah nomor panah ("level:5" → `level>= 5`, chain di-join ` & `, maks 48 char + `…`) — visibility rules kelihatan langsung di canvas. **Search + minimap**: kotak "Search node…" di toolbar (match id/text, Enter loncat match-ke-match, Shift+Enter mundur, Esc clear, match nyala kuning di canvas + minimap); minimap 170×110 di pojok kanan-bawah canvas (bounds graph fit-to-box, node = rect warna start/selected, edges dim, viewport rect putih — graph-space kiri-atas view = `−pan/zoom` (canvasMin CANCEL), lalu **di-CLAMP ke node extents asli** (`rawGmin/rawGmax` sebelum breathing room): view mencakup ruang kosong di luar graph, jadi kalau di-map mentah rect selalu lebih besar dari area node (terbaca "zoom salah"); fully zoomed out = rect pas hug graph, zoom in = slice terlihat, pan penuh menjauh = tidak digambar. Jangan clip ke kotak minimap saja — itu menyembunyikan masalah semantik, LMB klik/drag = pan kanvas ke titik itu — **rumus: `pan = canvasSize/2 − gTarget·zoom` TANPA canvasMin** (ToScreen sudah menambahkannya; dobel = view lompat sejauh posisi canvas di layar, bug "klik minimap lompat entah ke mana"). Region minimap juga meng-gate canvas-pan start & double-click (`!overMinimap`) supaya klik nav tidak konflik). Fold segitiga note: vertex tengah = pojok kanan-ATAS note, bukan nmax (bottom-right) — salah vertex = wedge memanjang di seluruh sisi kanan. — digambar DI DALAM clip canvas, di urutan paling atas. **Sticky notes**: tombol "+ Note" → memo kuning di canvas (layer paling bawah, di bawah edges/node), `DialogueGraphNote` di `asset.Notes` (Text/X/Y/W/H, persist otomatis ke dialogues.json, runtime IGNORE). Interaksi: drag = pindah (undo snapshot di frame pertama), double-click = edit (InputTextMultiline overlay window; close = Esc ATAU klik luar via `IsWindowFocused(RootAndChildWindows)` false + click — JANGAN buang branch ini lagi, dulu bikin memo tidak bisa lost-focus), handle segitiga kanan-bawah = resize, tombol **✕ di kanan-atas note** ATAU hover+Delete = hapus — semua undo-able via PushGraphUndo. **JEBAKAN grab-offset**: offset = `mouse − noteMin` (BUKAN `noteMin − mouse` — versi terbalik men-mirror posisi note melalui kursor), dan rumus drag: `noteXY = (mouse − canvasMin − pan)/zoom − offset`. Note HARUS masuk gate pan (`hoveredNote < 0` di syarat start pan) — kalau tidak, drag note menggeser canvas bersamaan dan note "lompat". NOTE: `DialogueAsset.Clone()` SUDAH meng-clone Notes — snapshot undo otomatis aman. Deklarasi `mouse`/`canvasHovered` di RenderGraphView HARUS di atas blok notes (notes pass lebih awal dari node pass). Posisi persist via `DialogueNode.GraphX/GraphY` (serializer JSON default ikut menulisnya). **Undo/Redo graph**: snapshot-based (`PushGraphUndo` = deep-clone asset SEBELUM mutasi, maks 50, redo clear saat push baru) — terpasang di +Node/Auto Arrange/connect-drop/duplicate/delete/Set Start/id-rename, dan node-drag push snapshot pada frame drag pertama (flag `_graphDragUndoPending`) supaya drag kontinu tidak membanjiri history. Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y aktif saat graph hovered; restore via `RestoreGraphSnapshot` menyalin isi snapshot ke objek asset YANG SAMA (jangan ganti instance — referensi library harus valid). Canvas pakai `InvisibleButton` + `PushClipRect` — panah/edge digambar SEBELUM node supaya node menutup ujung garis.
- **Graph View anti-bocor**: (1) warna draw-list ImGui itu **ABGR-packed** — menulis hex RGBA langsung (0xFFRRGGBB) menghasilkan warna SALAH (biru jadi coklat/merah); selalu build via `ImGui.ColorConvertFloat4ToU32`. (2) **SEMUA teks di dalam node wajib**: font terskala zoom (`GetFontSize() * zoom`), **PushClipRect per node**, wrap 3 baris + ellipsis `…` (panjang baris diukur ulang sampai muat). (3) Tinggi node DINAMIS (header + N×lineH + footer) dan edge anchor ke tinggi aktual — jangan hardcode. (4) Hitung wrap/tinggi di pass terpisah SEBELUM menggambar edge supaya anchor tepat.

## Per-Sprite Glow Rules (Emissive Post-FX):
- **Mekanisme**: Per-sprite emissive boost — warna vertex sprite dikali boost sehingga HANYA pixel terang (api/lava/lilin) naik melewati bloom threshold Post FX; pixel gelap (badan, kayu) tetap normal. Tidak perlu mask texture. WAJIB Post FX ON.
- **NO shader uniform**: Shader map2d tidak punya uniform tint — boost/tint/flicker di-BAKE ke vertex tint (`aTint`) di DrawSprite2D/DrawPlayer2D. Jangan tambah GL.Uniform4f.
- **Fields**: `Sprite2DGlow`/`Player2DGlow` (0-1), `Sprite2DGlowColor`/`Player2DGlowColor` (Vector3 — di-normalisasi channel terterang = 1 saat render, jadi warna gelap pun menghasilkan hue penuh), `Sprite2DGlowFlicker`/`Player2DGlowFlicker` (bool).
- **Flicker**: `ComputeGlowBoost` — 3 sine out-of-phase (frekuensi irasional) atas `Glfw.PeekTime()` (waktu absolut), seed per-object dari hash NAMA (FNV-1a) → dua api tidak pernah sinkron, amplitudo ~±22% di sekitar nilai Glow. Frame-gated via `Glfw.FrameId` — sample sekali per frame supaya editor pass + pass kedua konsisten (pola sama dengan animation clock).
- **Glfw.PeekTime()**: accessor read-only `glfwGetTime()` — aman dipanggil berkali-kali per frame, TIDAK menggeser shared clock (hanya SceneManager.Run boleh GetDeltaTime).
- **Inspector**: slider "Glow (bloom)", color picker "Glow Tint" (mis. biru = blue fire), checkbox "Flicker" — ada di bagian Sprite2D DAN Player2D.
- **Persist**: semua fields simetris via `EditorObjectData` (Glow, GlowColorX/Y/Z, GlowFlicker) di scene `.ing`.

## Scene Management Rules:
- **SceneEntry**: `record SceneEntry(Name, Description, HasInitializedEntry, Type, ...)` — stored in `AvailableScenesInternal`.
- **EditorScene**: `record EditorScene(Name, Type, Root)` — stored in `EditorScenes` dictionary keyed by scene name.
- **SceneType enum**: `MainMenu`, `GameScene`, `Loading`.
- **AvailableScenes vs EditorScenes**: `AvailableScenes` is read-only list from `AvailableScenesInternal`. `EditorScenes` holds live UIElement roots. Both must stay in sync on rename.

## Bahasa:
- Bisa berbahasa **Indonesia** dan **Inggris**.
---

## Appendix — Recent 2D Side-scroller Additions

**EN:** The engine gained a 2D player stats pipeline. Use these entry points instead of rolling your own:

| Path | Role |
| --- | --- |
| `Engine/Visual/Player2DStats.cs` | Runtime stat model for the 2D player (health, movement and gameplay values). Treat it as the single source of truth for player numbers. |
| `Engine/Visual/Player2DSystem.cs` | Drives the 2D player: reads the stats model, applies movement and gameplay rules each frame. |
| `Engine/Visual/TriggerEventSystem.cs` | Fires gameplay events when the player enters/exits triggers; wire HUD and stat changes through this instead of polling positions. |
| `Engine/IDE/Panels/PlayerInfoPanel.cs` | Editor panel that surfaces the player stats while the game runs. |
| `Engine/IDE/Panels/InspectorPanel.cs` | Inspector for components, including the 2D player components. |

**ID:** Engine sekarang punya pipeline statistik pemain 2D. Pakai titik masuk di atas, jangan bikin sendiri: `Player2DStats` adalah sumber kebenaran untuk angka pemain, `Player2DSystem` menjalankannya tiap frame, `TriggerEventSystem` memicu event gameplay, dan `PlayerInfoPanel` menampilkan statistik itu di IDE.

**Conventions (EN):**
- HUD art lives under `Dark Projects/<project>/Assets/images/HUD/`.
- Sprite/sheet metadata is declared in `Assets/Sprites/sprites.sheets.json`; scenes are stored in `*.ing` and described by `SceneAsset` / `SceneAssetSerializer`.
- IDE-local state (`imgui.ini`, `settings.json`, `recent_files.json`) is per-machine and should not be treated as project content.

**Konvensi (ID):**
- Aset HUD ada di `Dark Projects/<project>/Assets/images/HUD/`.
- Metadata sprite ada di `Assets/Sprites/sprites.sheets.json`; scene disimpan sebagai `*.ing` dan ditangani `SceneAsset` / `SceneAssetSerializer`.
- State lokal IDE (`imgui.ini`, `settings.json`, `recent_files.json`) bersifat per-mesin, bukan konten proyek.
