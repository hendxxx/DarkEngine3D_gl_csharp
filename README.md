# DarkEngine3D_gl_csharp

OpenGL/C# game engine with ImGui-based IDE editor.

## Quick Start

1. Pastikan `glfw3.dll` berada di folder yang sama dengan executable Anda.
2. Jika belum ada bisa didownload dari https://www.glfw.org/download.html
3. Pastikan folder `Artifacts` berada di folder yang sama dengan executable Anda.
4. Build: `dotnet build`
5. Run: `dotnet run`

## Features

- **3D Engine**: OpenGL 4.x, PBR rendering, cascaded shadow maps, post-processing
- **IDE Editor**: ImGui-based with Inspector, Hierarchy, Scene Manager, Asset Browser
- **UI System**: Buttons, Labels, Sliders, Checkboxes, Dropdowns, Containers with scroll
- **Project System**: `.projing` project files, per-project settings, auto-load scenes
- **Terrain**: Heightmap terrain with dynamic PBR layers, painting, sculpting
- **Scene Management**: Multiple scenes (MainMenu, GameScene, Loading), transitions
- **2D Game Support** (sidescroller, orthographic front view over the 3D viewport):
  - **Sprite Editor** — auto-detect/interactive sprite sheet slicing, animation clips with FPS + zoom, drag-drop import, saved to `Assets/Sprites/sprites.sheets.json`
  - **Map Editor** — tile painting (Paint/Erase/Fill/Pick + brush size) directly in the 3D viewport, multi-layers, marquee multi-select stamping, per-tile undo/redo (Ctrl+Z/Y)
  - **Tilemap in 3D** — rendered as an upright textured plane at world origin; camera auto-switches to ortho front view (editor and in-game)
  - **Parallax Backgrounds** — per-layer ScrollFactor/ZPosition/Alpha, aspect-preserving sizing, RepeatX/Y wrap, seamless scroll preview while panning, correct rendering in Play in Preview
  - **Collision Flags** — per-tile collision with translucent box preview in viewport
  - **Player Spawn** — draggable spawn marker; GameScene places the player at the map's spawn on scene enter
  - **Per-Map Camera Start** — saved view restored on Play in Preview; ortho/front locked for 2D levels
  - **Persistence** — maps saved to `Assets/Maps/*.tilemap.json` + scene `.ing`, autoloaded on project open; editor aids (grid/collision/spawn gizmos) auto-hidden in-game
- **IDE Settings Panel** — VSync, MSAA antialiasing, debug grid, font sizes with instant apply

## Tech Stack

- C# (.NET 9)
- OpenGL 4.x via raw bindings
- GLFW (windowing/input)
- ImGui.NET (editor UI)
- StbImageSharp (texture loading)
- System.Text.Json (serialization)

