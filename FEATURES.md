# DarkEngine3D — Functional Report

> **Purpose**: Complete technical reference for reproducing this OpenGL/C# game engine.
> **Tech Stack**: C# (.NET 9), OpenGL 4.x via raw bindings, GLFW, ImGui (dear imgui), StbImageSharp, System.Text.Json
> **3D Formats**: glTF 2.0 (.gltf/.glb)

---

## 1. Architecture Overview

```
DarkEngine3D_gl_csharp/
├── Engine/
│   ├── Config/          — Settings persistence (PostFX, Shadows, Fog, Quality)
│   ├── Helpers/         — Path resolution, JSON converters
│   ├── IDE/             — ImGui editor (panels, menus, shortcuts)
│   │   └── Panels/      — Inspector, Hierarchy, SceneView, AssetBrowser, etc.
│   ├── Inputs/          — GLFW keyboard/mouse input wrappers
│   ├── Libs/            — OpenGL loader, GLFW bindings, Shader compiler, Texture utils
│   ├── Objects/          — EditorObject, Object3D, TerrainMesh, GLB/GLTF loaders
│   ├── Project/         — ProjectManager, .projing file handling
│   ├── Scene/            — Scene management, Save/Load (.ing JSON format)
│   ├── Terrains/         — TerrainChunk, MapLoader, Noise generation
│   └── Visual/           — Camera, CSM, Lights, Skybox, PostFX, Billboard, HUD
├── Artifacts/
│   ├── shaders/          — 40 GLSL shaders (vertex, fragment, geometry)
│   ├── Textures/         — Default textures
│   └── fonts/            — IDE fonts (copied to project on create)
└── bin/Debug/net9.0/     — Build output (exe runs from here)

Project Folder (user-created):
ProjectRoot/
├── {Name}.projing         — Project metadata + scene inventory
├── settings.json          — Per-project engine settings
├── shadow_presets.json    — Per-project shadow presets
├── imgui.ini              — Per-project IDE layout
├── recent_files.json      — Per-project recent scenes
├── game.ing               — Combined scene file (auto-loaded on open)
├── saves/                 — Save game slots (per-project)
│   └── slot_N/
├── Assets/
│   ├── fonts/             — Copied from exe on project create
│   ├── images/
│   ├── models/
│   └── Maps/              — Heightmaps
└── Scenes/                — Individual scene files (.ing)
```

### Core Classes

| Class | File | Purpose |
|-------|------|---------|
| `IDE` | `IDE.cs` | Main editor loop, ImGui rendering, menu bar |
| `GameScene` | `GameScene.cs` | Runtime scene rendering |
| `EditorObject` | `EditorObject.cs` | All editor objects (Box, Sphere, Plane, Camera, Light, Sky, GLB) |
| `EditorTerrainMesh` | `EditorTerrainMesh.cs` | Terrain rendering (heightmap, layers, PBR, painting) |
| `Object3D` | `Object3D.cs` | GPU mesh representation (VAO/VBO/EBO) |
| `SceneManager` | `SceneManager.cs` | Scene loading/switching |
| `PostProcessStack` | `PostProcessStack.cs` | MSAA + PostFX pipeline |
| `PostFxProcessor` | `PostFxProcessor.cs` | Bloom + Auto-exposure + Tonemapping |
| `CSM` | `CSM.cs` | Cascaded Shadow Maps (3 cascades) |
| `LocalLightShadow` | `LocalLightShadow.cs` | Point (cube map) + Spot (projective) shadows |
| `Camera` | `Camera.cs` | Editor + game camera (FPS/Orbit) |
| `Lights` | `Lights.cs` | Sun + local point/spot lights |

---

## 2. IDE Features (ImGui)

### 2.1 Menu Bar

| Menu | Items | Shortcuts |
|------|-------|-----------|
| **File** | New Scene, Open Scene, Open Recent (Ctrl+F1-F10), Save All, Save As, Exit | Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S |
| **Model** | Add Plane/Box/Sphere/Camera/Light/Sky, Add GLB Reference, Gizmo modes | Ctrl+1..6 |
| **Edit** | Undo/Redo, Cut/Copy/Paste, Duplicate, Delete | Ctrl+Z/Y/X/C/V/D, Del |
| **View** | IDE Mode, In-Game Mode, Preview Mode, Snap to Grid, Viewport Shadows, IDE Font, Viewport Font | F5, F8 |
| **Help** | About | — |

### 2.2 Panels

| Panel | Purpose | Key Features |
|-------|---------|--------------|
| **SceneView** | 3D viewport | Gizmo translate/rotate/scale, selection highlight, grid |
| **Hierarchy** | Object tree | Add/delete/rename objects, drag reorder |
| **Inspector** | Property editor | Context-sensitive per object type (see §2.3) |
| **AssetBrowser** | File browser | Drag-drop images/models to Inspector |
| **Console** | Log output | Stdout/stderr capture |
| **SceneManager** | Scene list | Add/remove/switch scenes, save/load, scene rename sync |
| **ShadowSettings** | Shadow config | CSM bias/blend, local light shadows |
| **PostFxPanel** | Post-processing | Bloom, tonemapping, auto-exposure, gamma |
| **TerrainBrush** | Terrain tools | Brush size/strength/softness, paint layers, sculpt |
| **PbrPanel** | PBR material | Per-object texture slots + tuning (see §5) |
| **RenderTime** | Performance | FPS, frame time, GPU timing |
| **FramebufferViewer** | Debug | View any FBO texture |

### 2.2.1 UI Editor — Container & Selection Features

| Feature | Description |
|---------|-------------|
| **Nested Container Selection** | Alt+Click on any child to select nearest parent Container |
| **Scrollable Containers** | Container clips children to bounds, scrollbar when content exceeds height |
| **Nested Scroll Propagation** | Parent scroll offset propagates to nested containers correctly |
| **Clip Bounds Hit Test** | Elements outside container visible area cannot be hovered/clicked |
| **Multi-Select Group Drag** | Ctrl+Click to multi-select, drag yellow bounding box to move all together |
| **Container Selection Rules** | When multi-select active, parent Container cannot be selected (preserves group) |
| **Marquee Selection** | Rubber-band selection for multiple UI elements |
| **GroupMove with Undo** | Group drag records undo per-element for single Ctrl+Z restore |

### 2.3 Inspector — Object Types

**Box/Sphere/Plane**:
- Transform: Position (XYZ), Rotation (Euler XYZ), Scale (XYZ)
- Pivot Position
- Color (RGB picker)
- Cast Shadow checkbox
- Texture slot (albedo) + Texture Settings (filter, wrap, mip, anisotropy)
- PBR Maps: Normal, Metallic, Roughness, AO, Height, Emission texture slots
- PBR Tuning: All parameters (see §5)

**Camera**:
- FOV, Near Clip, Far Clip

**Light**:
- Type: Directional / Point / Spot
- Direction (Euler), Color, Intensity, Range
- Spot: Cone angle (inner/outer)
- Cast Shadow toggle

**Sky**:
- Turbulence, Sun Size/Intensity
- Sun Direction (Euler)
- Cloud layer (enable, scale, speed, coverage)

**GLB Reference**:
- Model path, Position, Rotation, Scale
- Cast Shadow toggle
- PBR Maps + Tuning (same as primitives)

---

## 3. Scene System (Save/Load)

### 3.1 File Format

Scene files use `.ing` extension — **JSON** format with `.ing` extension.

```json
{
  "Scenes": [
    {
      "Name": "GameScene",
      "EditorObjects": [...],
      "BackgroundObjects": [...],
      "Elements": [...]
    }
  ],
  "EditorCameraPosition": [x, y, z],
  "EditorCameraYaw": 0.0,
  "EditorCameraPitch": 0.0
}
```

### 3.2 EditorObjectData Properties

```json
{
  "Name": "Box1",
  "PrimitiveType": "Box|Sphere|Plane|Camera|Light|Sky|GlbReference",
  "GlbFilePath": "",
  "PosX": 0, "PosY": 0, "PosZ": 0,
  "RotX": 0, "RotY": 0, "RotZ": 0,
  "ScaleX": 1, "ScaleY": 1, "ScaleZ": 1,
  "ColorR": 0.8, "ColorG": 0.8, "ColorB": 0.9,
  "CastShadow": true,
  "IsVisible": true,
  "CameraFov": 60, "CameraNear": 0.1, "CameraFar": 500,
  "PbrAlbedoPath": "", "PbrNormalPath": "", "PbrMetallicPath": "",
  "PbrRoughnessPath": "", "PbrAoPath": "", "PbrHeightPath": "",
  "PbrEmissionPath": "", "PbrTexTiling": 1.0,
  "TerrainPbrAlbedoBrightness": 1.0, "...": "..."
}
```

### 3.3 Terrain Properties (per EditorObject)

```json
{
  "TerrainHeightmapPath": "heightmap.png",
  "TerrainChunkSize": 32,
  "TerrainChunksPerSide": 8,
  "TerrainHeightScale": 100.0,
  "TerrainSlopeThreshold": 0.35,
  "TerrainTexTiling": 0.5,
  "TerrainSlopeTexTiling": 0.3,
  "TerrainParallaxScale": 0.15,
  "TerrainUseStochasticSampling": false,
  "TerrainTextureAirPath": "",
  "TerrainTextureDirtPath": "",
  "TerrainTextureGrassPath": "",
  "TerrainTextureSnowPath": "",
  "TerrainTextureSlopePath": "",
  "TerrainLayerAirTop": 0.18,
  "TerrainLayerDirtTop": 0.45,
  "TerrainLayerGrassTop": 0.75,
  "TerrainLayerSnowTop": 1.0,
  "TerrainPaintedData": "(base64 compressed heightmap modifications)",
  "TerrainSplatData": "(base64 compressed paint splat data)",
  "TerrainLayerList": [...],
  "TerrainSlopeLayer": {...},
  "TerrainSlopeEnabled": false,
  "TerrainBrushSize": 5,
  "TerrainBrushStrength": 0.125,
  "TerrainBrushSoftness": 1.0,
  "TerrainBrushFalloff": 1,
  "PaintLayerTextures": ["", "", "", ""],
  "PaintLayerTilingX": [0.5, 0.5, 0.5, 0.5],
  "PaintLayerTilingY": [0.5, 0.5, 0.5, 0.5],
  "PaintLayerStochastic": [false, false, false, false],
  "PaintLayerCount": 1,
  "TerrainLayerSettings": [...]
}
```

### 3.4 TerrainLayer (Dynamic Layer System)

Each layer in `TerrainLayerList`:
```json
{
  "Name": "Base",
  "Visible": true,
  "AlbedoPath": "Artifacts/Textures/default.jpg",
  "PbrPaths": [null, null, null, null, null, null],
  "TilingX": 0.5, "TilingY": 0.5,
  "HeightMin": 0.0, "HeightMax": 1.0,
  "BlendSharpness": 2.0,
  "StochasticSampling": false,
  "NormalStrength": 1.0, "NormalBlur": 0.0,
  "MetallicThreshold": 0.5, "MetallicSoftness": 0.1, "MetallicStrength": 1.0,
  "RoughnessStrength": 1.0, "RoughnessInvert": false,
  "AoStrength": 1.0, "AoBrightness": 0.0,
  "HeightStrength": 1.0, "HeightInvert": false, "HeightBlur": 0.0,
  "EmissionIntensity": 1.0,
  "AlbedoBrightness": 1.0, "AlbedoSaturation": 1.0, "AlbedoContrast": 1.0
}
```

### 3.5 Save/Load Behavior

- **Save All / Ctrl+S / F8**: Saves to current file; if no file active → opens Save As dialog
- **New Scene**: Clears active file (does NOT overwrite game.ing)
- **Save As**: Opens file dialog, saves JSON + thumbnail PNG
- **Load**: Reads .ing JSON, reconstructs all objects, loads textures
- **Settings persistence**: `settings.json` for PostFX, Shadows, Fog, Quality, IDE fonts

---

## 3.6 Project System (.projing)

### 3.6.1 Project Structure

Every project is a self-contained folder with a `{Name}.projing` metadata file:

```json
{
  "projectName": "MyGame",
  "version": "1.0",
  "created": "2026-05-06T10:00:00",
  "lastOpened": "2026-09-03T14:00:00",
  "lastSaved": "2026-09-03T15:30:00",
  "scenes": ["game.ing", "Scenes/MainMenu.ing", "Scenes/Level1.ing"],
  "autoLoadGameIng": true
}
```

### 3.6.2 Per-Project Files

| File | Purpose |
|------|---------|
| `{Name}.projing` | Project metadata + scene inventory |
| `settings.json` | Engine settings (resolution, shadows, fog, post-FX, fonts) |
| `shadow_presets.json` | Saved shadow presets |
| `imgui.ini` | IDE panel layout (positions, sizes) |
| `recent_files.json` | Recently opened scene files |
| `game.ing` | Combined scene file (auto-loaded on project open) |
| `saves/` | Save game slots (slot_0 through slot_4) |
| `Assets/fonts/` | Project fonts (copied from exe on create) |
| `Assets/images/` | Image assets |
| `Assets/models/` | 3D model assets |
| `Assets/Maps/` | Heightmaps |
| `Scenes/` | Individual scene files (.ing) |

### 3.6.3 Project Lifecycle

1. **New Project** (File > New Project): Pick folder → enter name → creates structure + copies fonts
2. **Open Project** (File > Open Project): Browse for `.projing` file → auto-loads `game.ing`
3. **Recent Projects**: File > Recent Projects submenu (persisted globally)
4. **Save All** (Ctrl+S): Saves scenes + updates `.projing` (LastSaved + scene inventory)
5. **Close Project**: File > Close Project (resets all editor state: scenes, selection, hierarchy, editor objects)

### 3.6.4 Path Resolution

All asset paths are resolved via `PathHelpers.Resolve()`:
1. Check project root (`ProjectRoot/relativePath`)
2. Check project Assets (`ProjectRoot/Assets/relativePath`)
3. Fallback to exe directory (`BaseDirectory/relativePath`)

When saving, `PathHelpers.MakeRelative()` converts absolute paths to portable relative form.

### 3.6.5 File Menu Layout

```
File
├── New Project...
├── Open Project...       (file browser: *.projing)
├── Recent Projects       (submenu)
├── ── Current Project ──  (shows name + path + Close)
├── ── Separator ──
├── New Scene (Ctrl+N)
├── Open Scene... (Ctrl+O)
├── Open Recent Scene     (submenu)
├── Save (Ctrl+S)
├── Save As... (Ctrl+Shift+S)
└── Exit
```

---

## 4. Rendering Pipeline

### 4.1 Shader Programs

| Shader | Vertex | Fragment | Purpose |
|--------|--------|----------|---------|
| Editor Terrain | `vertex_shader.glsl` | `terrainEditor_fragment.glsl` | Terrain with PBR |
| Object PBR | `static_vertex.glsl` | `objectPbr_fragment.glsl` | GLB/PBR objects |
| GLTF | `gltf_vertex.glsl` | `gltf_fragment.glsl` | Standard glTF |
| Impostor | `impostor_vertex.glsl` | `impostor_fragment.glsl` | Billboard impostors |
| Sky | `sky_vertex.glsl` | `sky_fragment.glsl` | Procedural sky |
| Skybox | `skybox_vertex.glsl` | `skybox_fragment.glsl` | Cubemap skybox |
| Shadow | `shadow_static_vertex.glsl` | `shadow_static_alpha_fragment.glsl` | CSM depth pass |
| HUD | `hudVertex_shader.glsl` | `hudFragment_shader.glsl` | UI elements |
| Outline | `outline_vertex.glsl` | `outline_fragment.glsl` | Selection outline |
| PostFX Bright | `post_vertex.glsl` | `postFxBright_fragment.glsl` | Bloom bright pass |
| PostFX Blur | `post_vertex.glsl` | `postFxBlur_fragment.glsl` | Bloom gaussian blur |
| PostFX Composite | `post_vertex.glsl` | `postFxComposite_fragment.glsl` | Final composite |

### 4.2 Rendering Flow (per frame)

```
1. Shadow Pass
   ├── CSM: 3 cascade depth maps (sun directional)
   └── Local Shadows: Point (cube map) + Spot (projective) per light

2. Scene Pass → MSAA FBO (multisampled)
   ├── Sky (procedural or cubemap)
   ├── Terrain (heightmap + dynamic layers + PBR)
   ├── Objects (primitives + GLB references)
   ├── Selection highlight (outline shader)
   └── Grid overlay

3. Resolve MSAA → single-sample SceneColorTex

4. PostFX Chain (if enabled)
   ├── Auto-exposure (luminance measurement)
   ├── Bloom (bright extract → blur → composite)
   ├── ACES Tonemapping
   └── Gamma correction

5. IDE Overlay
   ├── ImGui panels (Inspector, Hierarchy, etc.)
   ├── Viewport panel (renders SceneColorTex as ImGui image)
   └── Framebuffer viewer (debug)
```

### 4.3 Framebuffer Layout

| FBO | Format | Usage |
|-----|--------|-------|
| SceneFBO | RGBA16F MSAA | Main scene render |
| SceneColorTex | RGBA8 | Resolved scene (PostFX input/output) |
| ShadowMap0/1/2 | DEPTH24 | CSM cascade 0/1/2 |
| LocalShadowPoint[] | DEPTH24 | Per-point-light cube depth |
| LocalShadowSpot[] | DEPTH24 | Per-spotlight projective depth |
| BloomBright | RGBA16F | Bloom bright extraction |
| BloomBlurA/B | RGBA16F | Ping-pong gaussian blur |
| AutoExposureLuma | R16F | Luminance measurement |

---

## 5. PBR System

### 5.1 Cook-Torrance BRDF

Both terrain and objects use the same BRDF functions:
- **Distribution**: GGX/Trowbridge-Reitz
- **Geometry**: Smith's method with Schlick-GGX
- **Fresnel**: Schlick approximation

```glsl
float D = DistributionGGX(N, H, roughness);
float G = GeometrySmith(N, V, L, roughness);
vec3  F = FresnelSchlick(max(dot(H, V), 0.0), F0);
vec3 specular = D * G * F / (4.0 * NdotV * NdotL + 0.0001);
vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
vec3 Lo = (kD * albedo / PI + specular) * lightColor * NdotL;
```

### 5.2 Object PBR (objectPbr_fragment.glsl)

Per-object PBR with 7 texture slots:

| Slot | Type | Default |
|------|------|---------|
| Albedo / Base Color | RGB | Object color |
| Normal Map | RGB | (0.5, 0.5, 1.0) flat |
| Metallic Map | R | 0.0 (dielectric) |
| Roughness Map | R | 0.5 (mid) |
| AO Map | R | 1.0 (no occlusion) |
| Height Map | R | 0.5 (mid) |
| Emission Map | RGB | (0, 0, 0) none |

**Per-object tuning** (EditorObject properties):
- `TerrainPbrAlbedoBrightness/Saturation/Contrast` — Albedo adjustment
- `TerrainPbrNormalStrength/Blur` — Normal map intensity
- `TerrainPbrMetallicThreshold/Softness/Strength` — Metallic with smoothstep
- `TerrainPbrRoughnessStrength/Invert` — Roughness (+ smoothness invert)
- `TerrainPbrAoStrength/Brightness` — Ambient occlusion
- `TerrainPbrHeightStrength/Invert/Blur` — Parallax/height
- `TerrainPbrEmissionIntensity` — Self-illumination
- `PbrTexTiling` — Global texture tiling multiplier
- `PbrParallaxScale` — Parallax occlusion depth

### 5.3 Terrain PBR (terrainEditor_fragment.glsl)

Same BRDF as objects, with dynamic layer blending:

**Uniforms** (driven by first layer's PBR Tuning):
- `terrainPbrMetallic` — Global metallic (0-1)
- `terrainPbrRoughness` — Global roughness (0-1)
- `terrainPbrMetallicThreshold/Softness` — Smoothstep for texture
- `terrainPbrNormalStr` — Normal map strength (0-2)
- `terrainPbrAoStr/Brightness` — AO strength + brightness
- `terrainPbrHeightStr` — Height strength
- `terrainPbrEmissionIntensity` — Emission intensity
- `terrainPbrAlbedoBright/Sat/Contrast` — Albedo adjustment

**PBR Texture Arrays** (optional, per-layer):
- `sampler2DArray pbrNormalMap/MetallicMap/RoughnessMap/AoMap/HeightMap/EmissionMap`
- 6 texture units (30-35), 8 layers max
- Fallback to uniforms when no texture assigned (texture returns 0)

### 5.4 Local Light PBR

Local lights (point/spot) use Blinn-Phong for terrain, Cook-Torrance for objects:
```glsl
float spec = pow(max(dot(N, H), 0.0), 24.0) * 0.6;  // Terrain
vec3 specular = D * G * F / (4.0 * NdotV * NdotL);    // Objects
```

---

## 6. Terrain System

### 6.1 Heightmap Terrain

- **Loading**: PNG/EXR heightmap → `MapLoader` → `TerrainChunk` grid
- **Chunked rendering**: Configurable `ChunksPerSide × ChunksPerSide` chunks
- **Height scale**: Multiplier for Y axis
- **Grid resolution**: Per-chunk configurable (default 32×32)

### 6.2 Dynamic Layer System

Height-based blending with configurable layers:

```
Layer 0: HeightMin=0.0, HeightMax=0.3  (Air)
Layer 1: HeightMin=0.2, HeightMax=0.5  (Dirt)
Layer 2: HeightMin=0.4, HeightMax=0.8  (Grass)
Layer 3: HeightMin=0.7, HeightMax=1.0  (Snow)
```

Each layer has:
- Albedo texture path + PBR map paths (Normal, Metallic, Roughness, AO, Height, Emission)
- Tiling X/Y
- Height range (min/max)
- Blend sharpness (1=smooth, higher=sharper)
- Stochastic sampling toggle
- Full PBR tuning parameters

### 6.3 Slope Layer

Optional rock/cliff texture on steep surfaces:
- Enable/disable toggle
- Separate albedo + PBR texture slots
- Slope threshold (0-1, default 0.35)
- Slope texture tiling

### 6.4 Terrain Painting

- **Brush tools**: Size, Strength, Softness, Falloff (Linear/Smooth/Sharp)
- **Paint layers**: Up to 4 custom paint layers with individual textures/tiling
- **Sculpting**: Height modification with brush
- **Smoothing**: Height averaging filter
- **Splat map**: Per-pixel layer weight painting
- **Heatmap visualization**: Layer weight overlay
- **Contour lines**: Topographic line overlay

### 6.5 Terrain Texturing

- **Triplanar mapping**: Projects textures from X, Y, Z planes (avoids UV stretching)
- **Stochastic sampling**: Random per-tile rotation to break repetition
- **Texture tiling**: Separate X/Y tiling per layer
- **Slope texture**: Separate tiling for cliff/rock surfaces
- **Parallax Occlusion Mapping**: Height-based surface detail

### 6.6 Texture Settings (per texture)

- Min/Mag filter: Nearest, Linear, Mipmap variants
- Wrap S/T: Repeat, Clamp, Mirrored Repeat
- Mipmap: Enable/disable
- Anisotropic filtering: 0-16x
- Auto-recommend button: Analyzes image and suggests optimal settings

---

## 7. Lighting & Shadows

### 7.1 Sun (Directional Light)

- Direction (Euler angles)
- Color (RGB picker)
- Intensity (brightness multiplier)

### 7.2 Local Lights (Point/Spot)

Up to `MAX_LOCAL_LIGHTS` per scene:

| Type | Properties |
|------|-----------|
| Point | Position, Color, Intensity, Range, Cast Shadow |
| Spot | Position, Direction, Color, Intensity, Range, Cone angle, Cast Shadow |

### 7.3 Cascaded Shadow Maps (CSM)

3 cascade levels for sun shadows:
- **Cascade 0**: Near (highest resolution)
- **Cascade 1**: Mid
- **Cascade 2**: Far (lowest resolution)
- **Blend zones**: Smooth transition between cascades
- **Bias**: Constant + slope-scaled (prevents acne on slopes)
- **Shadow filtering**: Hard (PCF 1-tap) or Soft (Poisson 16-tap disk)
- **Debug overlay**: Colored cascade visualization (toggle with L key)

### 7.4 Local Light Shadows

| Type | Technique |
|------|-----------|
| Point | Cube map depth (6 faces), linear depth compare |
| Spot | Projective depth map, linear depth compare |
| **Bias** | Slope-scaled (`u_localShadowBias`, `u_localShadowPointBias`) |

### 7.5 Shadow Uniforms

Uploaded via `ShadowUniforms.UploadMain()`:
- `u_ConstantBias`, `u_SlopeBias`, `u_MinBias` — Sun shadow bias
- `shadowFilterMode` — Hard/Soft toggle
- `u_localShadowBias`, `u_localShadowPointBias` — Local light bias
- `u_CascadeOverlayAlpha` — Debug overlay strength

---

## 8. Post-Processing

### 8.1 Pipeline

```
SceneColorTex → Bright Extract → Gaussian Blur (ping-pong) → Bloom Composite
             → Auto-Exposure → ACES Tonemapping → Gamma Correction
```

### 8.2 Bloom

| Parameter | Default | Range |
|-----------|---------|-------|
| `BloomIntensity` | 1.0 | 0-4 |
| `BloomThreshold` | 0.5 | 0-2 |
| `BloomSoftKnee` | 0.15 | 0-1 |

- Bright extraction: pixels above threshold + soft knee
- Gaussian blur: configurable blur radius
- Composite: additive blend with scene

### 8.3 Auto-Exposure

| Parameter | Default | Range |
|-----------|---------|-------|
| `AutoExposure` | true | on/off |
| `AutoExposureMinExposure` | 0.2 | 0.05-8 |
| `AutoExposureMaxExposure` | 4.0 | 0.05-8 |
| `AutoExposureTargetLuminance` | 0.18 | 0.01-1 |
| `AutoExposureSpeed` | 0.6 | 0.01-10 |

- Measures scene average luminance
- Adapts exposure smoothly over time
- Clamps to min/max range

### 8.4 Tonemapping

- **ACES** filmic tonemapping (standard HDR → LDR)
- Applied after bloom composite

### 8.5 Gamma Correction

| Parameter | Default | Range |
|-----------|---------|-------|
| `Gamma` | 2.2 | 0.4-4 |
| `Exposure` | 1.0 | 0.1-8 |

---

## 9. Fog System

| Parameter | Default |
|-----------|---------|
| `useFog` | 0 (off) |
| `fogMode` | 3 (Exp2 + height blend) |
| `fogColor` | sky color |
| `fogDensity` | 0.001 |
| `fogStart` | 0 |
| `fogEnd` | 500 |
| `fogHeight` | 0 |
| `fogHeightRange` | 100 |

Modes: Linear (1), Exponential (2), Exp2 + height blend (3)

---

## 10. Sky System

### 10.1 Procedural Sky

- **Rayleigh scattering**: Sky color from sun angle
- **Turbulence**: Cloud-like wisps
- **Sun disk**: Size + intensity control
- **Cloud layer**: Enable/disable, scale, speed, coverage

### 10.2 Skybox (Cubemap)

- 6-face cubemap loading
- HDR support

---

## 11. Camera System

### 11.1 Editor Camera

- **FPS mode**: WASD + Mouse look
- **Speed**: Configurable move speed
- **Near/Far clip**: Configurable

### 11.2 Game Camera

- FOV, Near, Far (per-object properties)
- Position, Rotation stored in scene file

---

## 12. Input System

### 12.1 Keyboard Shortcuts

| Key | Action |
|-----|--------|
| F1 | Toggle wireframe |
| F5 | Preview mode |
| F8 | In-Game mode |
| ESC | Toggle menu (GameScene only — disabled for MainMenu/Loading) |
| H | Toggle selected object visibility |
| O | Toggle grid |
| J/K | Cycle through objects |
| L | Toggle cascade shadow debug overlay |
| M | Toggle local light shadow debug |
| , / . | Previous/Next scene |
| N | New scene |
| P | Toggle pause |
| 1/2/3 | Select translate/rotate/scale gizmo |
| Ctrl+1..6 | Add Plane/Box/Sphere/Camera/Light/Sky |
| Ctrl+Z/Y | Undo/Redo |
| Ctrl+C/X/V | Copy/Cut/Paste |
| Ctrl+D | Duplicate |
| Ctrl+S | Save All |
| Ctrl+Shift+S | Save As |
| Ctrl+N | New Scene |
| Ctrl+O | Open Scene |
| Ctrl+F1-F10 | Open Recent |

### 12.2 Mouse

- **Left click**: Select object
- **Right drag**: Camera look
- **Scroll**: Zoom
- **Shift+Scroll**: Move speed multiplier

---

## 13. Configuration Persistence

### 13.1 settings.json

```json
{
  "PostFxEnabled": true,
  "PostFxBloomIntensity": 1.0,
  "PostFxBloomThreshold": 0.5,
  "PostFxBloomSoftKnee": 0.15,
  "PostFxExposure": 1.0,
  "PostFxGamma": 2.2,
  "PostFxAutoExposure": true,
  "PostFxAutoExposureMin": 0.2,
  "PostFxAutoExposureMax": 4.0,
  "PostFxAutoExposureTarget": 0.18,
  "PostFxAutoExposureSpeed": 0.6,
  "FogEnabled": false,
  "FogMode": 3,
  "FogDensity": 0.001,
  "ShadowHardShadow": false,
  "CascadeOverlayAlpha": 0.15,
  "ConstantBias": 0.001,
  "SlopeBias": 0.005,
  "MinBias": 0.0005,
  "IDEFontSize": 14,
  "IDEFontPath": "",
  "ViewportFontSize": 14,
  "ViewportFontPath": "",
  "SelectionHighlightColor": [1.0, 0.8, 0.1],
  "EditorObjectHighlightColor": [0.1, 0.8, 1.0],
  "RecentFiles": []
}
```

### 13.2 Save Slots

- Save slots in `saves/slot_0/` through `saves/slot_9/`
- Each slot: `save.json` + `thumbnail.png`
- Supports quick save/load

---

## 14. Texture Loading

### 14.1 Formats Supported

- PNG, JPG, BMP, TGA (via StbImageSharp)
- EXR (heightmaps)
- GLB/GLTF (3D models)

### 14.2 Texture Pipeline

```
Image file → StbImageSharp decode → RGBA byte array → OpenGL texture
```

### 14.3 Default Textures

| Name | File | Usage |
|------|------|-------|
| Fallback Albedo | `Artifacts/Textures/default.jpg` | Default object texture |
| Fallback Normal | Solid (0.5, 0.5, 1.0) | Flat normal map |
| Fallback Metallic | Solid (0, 0, 0) | Dielectric |
| Fallback Roughness | Solid (0.5, 0.5, 0.5) | Mid roughness |
| Fallback AO | Solid (1, 1, 1) | No occlusion |
| Fallback Height | Solid (0.5, 0.5, 0.5) | Mid height |
| Fallback Emission | Solid (0, 0, 0) | No emission |

---

## 15. UI System (Scene Elements)

Built-in UI elements for game HUD/menu:

| Type | Properties |
|------|-----------|
| Button | Text, Position, Size, Colors (normal/hover/active), Click behavior |
| Label | Text, Position, Font, Color, Alignment |
| SliderNumber | Min/Max/Step/Value, Track/Thumb colors |
| SliderText | Text options, Selected index |
| Checkbox | Checked state, Checkmark/Background colors |
| Dropdown | Options list, Selected index, Arrow colors |
| TextBox | Placeholder, Max length, Cursor color |
| Container | Children (nested elements) |
| Scene | Full-screen background element |

All elements support:
- Font selection (per-element font path + size)
- Opacity (0-1)
- Alignment (Left/Center/Right)
- Hover effects (color transition)
- Auto-fill window / Auto-center
- Image background (Stretch/Zoom/Fill modes)

---

## 16. Performance Features

- **MSAA**: Configurable multi-sample anti-aliasing
- **Frustum culling**: Objects outside camera frustum skipped
- **LOD**: Distance-based detail reduction
- **Shadow map caching**: Only rebuilds when light moves
- **Texture caching**: GPU texture IDs cached, no duplicate loads
- **Splat map caching**: Paint data uploaded once, not per-frame
- **Render timing panel**: Per-draw-call GPU timing

---

## 17. Build & Dependencies

### 17.1 .NET Dependencies

```xml
<PackageReference Include="ImGui.NET" Version="1.91.6.1" />
<PackageReference Include="StbImageSharp" Version="2.27.13" />
```

### 17.2 Native Libraries

- `cimgui.dll` / `cimgui.so` / `cimgui.dylib` — ImGui C bindings
- GLFW (loaded via `NativeLibrary`)
- OpenGL (loaded via `NativeLibrary`)

### 17.3 Build Command

```bash
dotnet build --nologo
```

---

## 18. Key Conventions

- **Face culling**: CCW (Counter-Clockwise)
- **Depth testing**: Always enabled
- **Blending**: Alpha blending for transparency
- **Coordinate system**: Right-handed, Y-up
- **Matrix order**: Column-major (OpenGL convention)
- **ImGui rendering**: After scene, before post-FX final blit
- **Shadow maps**: DEPTH24 format, separate FBO per cascade/light
- **Texture units**: Reserved allocation to avoid conflicts:
  - 0-7: Dynamic layer albedo
  - 6-8: CSM shadow maps
  - 9: Local point shadow (cube)
  - 10-13: Local spot shadow
  - 14-16: Point shadow maps
  - 20-23: Paint layer textures
  - 25-28: Legacy layer textures
  - 30-35: PBR texture arrays

---

*Generated for DarkEngine3D — OpenEngine3D game engine reference.*
