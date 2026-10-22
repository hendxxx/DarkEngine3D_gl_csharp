# 2D sky: sun & moon on a visible arc (2D only)

## Problem
The procedural sky positions the sun/moon from the camera's real view direction. In a
2D level the camera looks straight ahead with a 60° FOV, so at 10:00 the sun (≈56°
elevation) is outside the visible sky and the level never shows it — while a
perspective view of the same scene does.

## Fix
`SkyRenderer` now draws its own sun/moon discs when the camera is **orthographic**
(2D), and nothing else changes for 3D:

- Position comes from the scene's real sun direction (`Lights.RealSunDir`, so the sky
  time-of-day and Sun pitch/yaw overrides all apply):
  - `x = −dir.x` — the sun crosses the sky left → right between 06:00 and 18:00.
  - The sun rides the upper arc (`SunArcBottom … SunArcTop`), the moon a lower arc
    (`MoonArcBottom … MoonArcTop`) so day and night read as separate bands.
  - The sun shows while it is above the horizon, the moon while it is below. The moon
    uses `Lights.RealSunDir`, **not** `Lights.SunDir` — the latter is the active shading
    light and flips to the moon at night.
- Drawn as screen-space quads (`sky2d_celestial_*`), blended, depth test off: the sun is
  a warm core with a soft halo using `SkySettings.Sun.Color`/`Size`, the moon samples
  `SkySettings.Moon.TexturePath` (grey moon texture) with `Moon.TintColor`/`Size`.
- The procedural sky's own sun/moon/rays/eclipses are suppressed in ortho through the
  new `hideCelestial` uniform (clouds and stars stay), so 2D shows exactly one sun or
  one moon instead of a duplicate.

## Scope
Only orthographic (2D) cameras. Perspective cameras keep the original sky unchanged.
