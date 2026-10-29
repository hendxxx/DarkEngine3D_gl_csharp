# Pixel atmosphere FX — lightning, god rays, heat haze, frost, wind streaks

Five new overlay effects in `Effect2DSystem`, all **procedural** (no sprite assets, no
post-processing pass) and drawn with hard-edged quads/segments so they sit naturally in a
pixel-art scene. Every one is an independent toggle in **Effects → Pixel FX**.

They render as one final pass before the editor gizmos:

```
world sprites → projectiles → particles → fog bands → mist → PIXEL FX → gizmos
```

## Lightning

- `LightningEnabled`, `LightningInterval` (s, 0 = manual), `LightningThickness`
  (fraction of the camera half-height), `LightningFlash` (0–1), `LightningColor`,
  `LightningGlowColor`.
- `StrikeLightning()` makes the next tick fire immediately (panel button **Strike now**).
- A strike builds one 16-segment jagged polyline from above the view down to
  `GroundYAtAny`, with 2–4 short forks. Stored in world space so it does not slide if the
  camera moves during the 0.45 s lifetime.
- Drawn as: full-view flash wash → wide dim halo per segment → bright core → forks →
  soft ground-hit glow. Alpha is strobed (`|sin(k·22)|`) so it reads as 2–3 pulses.

## God rays

- `GodRaysEnabled`, `GodRaysCount` (1–24), `GodRaysStrength`, `GodRaysAngle` (deg, 236 =
  down-left), `GodRaysSpread` (deg), `GodRaysLength` (× half-diagonal), `GodRaysColor`.
- A fan of long rotated quads from a source above the view (25% right, 10% above the top),
  each with a stable hash-driven width, length and shimmer phase, plus a bloom at the
  source so the beams read as one light.

## Wind streaks

- `WindStreaksEnabled`, `WindStreakAmount` (× the 28-streak base), `WindStreakSpeed`,
  `WindStreakColor`.
- Horizontal dashes drifting with `WindX` direction; per-streak height/length/phase comes
  from a deterministic hash (never flickers), positions wrap across a 2.4× view span.

## Heat haze

- `HeatHazeEnabled`, `HeatHazeStrength`, `HeatHazeHeight` (fraction of the view from the
  bottom), `HeatHazeSpeed`, `HeatHazeColor`.
- Wobbling warm shimmer lines (3 per row × up to 40 rows) with independent sine phases —
  the pixel-art shorthand for rising hot air. This is *not* a UV-distortion pass.

## Frost

- `FrostEnabled`, `FrostStrength`, `FrostDensity` (× base), `FrostColor`,
  `ReshuffleFrost()` (panel button **New Frost Pattern**).
- Procedural ice ferns anchored on the four view edges: a spine of 4–7 segments with
  alternating side branches, drawn as a white-blue stroke batch, plus a soft frosted border
  wash on the top/bottom edges.
- Pattern is **cached** in normalized view coordinates and regenerated only when the seed
  or density changes, so it stays static like real window frost (seeded `System.Random`).

## Batching + shared helpers

- `Stroke(x0, y0, x1, y1, thickness, z, col)` appends a rotated quad to `_strokeBuf`;
  `FlushStrokes()` uploads and draws the whole batch once. Frost alone is ~1000 segments —
  one draw call per segment would be a driver bottleneck.
- `OverlayQuadRot(...)` draws one rotated quad (god rays).
- `BeginOverlay/EndOverlay` hold the shared pass state (matrices, blend, unlit, VAO/VBO,
  solid texture) for the pixel FX renderers.
- `Hash01(int)` gives stable 0..1 values (using `Random` per frame would flicker).
- `RenderPixelFxFront(camera)` is the single entry point; it early-returns when all five
  toggles are off.

## Tick + reset

- `UpdateLightning(cam, dt)` runs from **both** `Tick` (gameplay) and `TickEditorWeather`
  (editor), so bolts also fire while editing; the editor early-return now includes the five
  new flags.
- `ResetWeather()` turns all five off and clears the live bolt.

## Safety notes

- Frost/rays/wind are screen-space overlays that follow the camera; lightning is world
  space on purpose.
- All passes stay `SpriteLighting2D.ApplyUnlit` and draw with the depth test disabled —
  they are atmosphere, not lit surfaces, and order decides compositing.
