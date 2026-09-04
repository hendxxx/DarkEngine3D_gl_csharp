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

## Tech Stack

- C# (.NET 9)
- OpenGL 4.x via raw bindings
- GLFW (windowing/input)
- ImGui.NET (editor UI)
- StbImageSharp (texture loading)
- System.Text.Json (serialization)

