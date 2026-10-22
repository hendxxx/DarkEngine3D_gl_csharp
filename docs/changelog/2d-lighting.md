# 2D Lighting: Direct / Point / Spotlight

## Summary

The 2D (sidescroller) render paths are now lit by the scene's existing **Light editor
objects**, using the normal atlases the Sprite Editor already bakes. No new object type,
no new serialization, no new asset requirement.

## Light sources

`SpriteLighting2D.Capture(light)` runs once per frame at the top of
`EditorObjectManager.Draw` (right after the 3D light upload) and snapshots:

- the **sun** (`Lights.SunDir` / `Lights.LightColor`) — driven by a Direct Light marker
  or the procedural day/night cycle;
- every **Point / Spotlight** marker collected into `Lights.LocalLights`, with position,
  aim direction, color, intensity, `LightPointRadius` and `LightConeAngle`.

`Apply(program)` uploads the snapshot to a shader once per program per frame (the dedupe
is per frame, so hundreds of sprites cost one upload). `ApplyUnlit(program)` switches a
pass back off.

## Shading model

- Direction to the sun/light is evaluated in the XY plane; a nominal +Z term keeps
  camera-facing quads lit while normal-mapped relief still varies.
- Point/Spot falloff uses the same `(1 − d²)²` curve as the 3D local lights.
- Spotlights are narrowed in XY by the marker's cone (`LightConeAngle`), aimed along
  `WorldLightDirection`.
- Ambient = `lightColor * 0.30 + 0.06`, so a daylit scene keeps its previous brightness
  while night and unlit corners still let lamp colors read.

## Surfaces

| Surface | Shader | Normal source |
|---|---|---|
| Map tiles | `EditorObject` map2d | flat |
| Parallax layers | map2d | flat |
| Player2D / Sprite2D | map2d | `<sheet>Normal.png` |
| Portals, equipment overlays | map2d | flat (overlay never samples the base sheet's atlas) |
| Projectiles | map2d | flat |
| World particles | `Effect2DSystem` | flat |

Collision boxes, trigger volumes, weather tint and the front fog pass call
`ApplyUnlit` — they are editor aids / atmosphere, not lit surfaces.

## Normal atlas loading

`SpriteLighting2D.TryGetNormalTexture(sheet)` looks for `<sheet name>Normal.png` (then
`<image name>Normal.png`) next to the color atlas, loads it lazily on the GL thread,
caches by the resolved image path and deletes on
`SpriteLighting2D.InvalidateNormalMaps()` — called from
`IDEBridge.NotifySpriteTextureReloaded` (re-bake / in-place reload) and
`SpriteEditorPanel.OnProjectChanged`.

The atlas is baked in image space (+y down), so the shader flips Y before lighting.
Sprites facing left mirror their UVs, which mirrors the normal's X with the art.

## Verification

- `dotnet build DarkEngine3D_gl_csharp.csproj` — 0 warnings, 0 errors.
- Both shader programs were extracted from the C# sources into a WebGL2 (GLSL ES 3.00)
  harness: compile + link clean. Pixel checks passed for ambient-only, direct sun,
  point falloff (bright center / zero outside range), spotlight cone (lit inside, zero
  outside), tilted-normal response (lit from the normal's side, zero from the other),
  and the unlit bypass (returns the raw albedo).
