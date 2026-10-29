# 2D Fog (thick cloud masses) + Mist (thin small wisps)

Two independent atmosphere layers in `Effect2DSystem`, both drawn in front of the world.
Per the user's terminology: **fog = thick**, **mist = thin and small**.

They no longer share a texture. Fog stamps **lumpy cloud masses** (reference image 1: big
irregular grey cloud banks); mist draws **many short thin streaks** (reference image 2). Using
one radial glow for both was why the two layers kept looking swapped.

## Fog — thick cloud masses

| Field | Meaning |
|---|---|
| `FogEnabled` | master switch |
| `FogCoverage` | vertical extent of the bank as a view fraction (0.75 default) |
| `FogPuffs` | number of cloud **clusters** (12 default; each stamps 4 lobes) |
| `FogPuffSize` | cluster size multiplier (1.30 default) |
| `FogOpacity` | **per-cluster** alpha — clusters overlap, 0.25–0.40 is already thick |
| `FogBaseHaze` | flat haze under the clouds that fills the gaps (0 = clouds only) |
| `FogSpeed` | drift speed multiplier |
| `FogColor` | soft grey by default |
| `FogUseTint` | nudge the colour toward an active `Tint` |

Rendering: a base haze (flat body up to 72% of the coverage zone, then a fade to 0) plus
`FogPuffs` clusters via `EmitCloudMass` — one wide core quad plus three satellite lobes, which
is what produces the irregular cauliflower silhouette. `TexCloud` itself is a five-lobe
procedural texture with a jittered rim, so a single quad already reads as a cloud rather than a
glow. Size, height, alpha and travel phase come from `Hash01`, so the field is stable frame to
frame; horizontal travel is `(0.35 + |WindX|·0.22) · sign(WindX) · …` wrapped over a 3.2× view
span, so the bank drifts the same way as the wind streaks and rain slant. A slow sine bob keeps
the mass alive.

## Mist — thin low wisps

| Field | Meaning |
|---|---|
| `MistEnabled` | master switch |
| `MistWisps` | number of wisps (40 default, max 96) |
| `MistHeight` | height of the wisp zone as a view fraction (0.20 = ground level) |
| `MistLength` | wisp **half-length** as a fraction of half the view width (0.16 default) |
| `MistThickness` | wisp half-thickness as a fraction of the camera half-height |
| `MistOpacity` | wisp alpha (0.12–0.25 = thin) |
| `MistSpeed` | drift speed multiplier |
| `MistColor` | pale white-blue by default |
| `MistUseTint` | nudge the colour toward an active `Tint` |
| `MistWindDrift` | how strongly `WindX` changes wisp speed/direction |

Each wisp is one **short** `TexWisp` quad — a tapered ragged streak, not a band. Length,
thickness, y, alpha and phase come from `Hash01`; x travel wraps over a 2.2× span and follows
the wind direction. ~18% of the wisps (`h3 > 0.82`) are "strays" that float up to 2.6× the
zone height, matching the loose wisps above the dense row in the reference.

## Render order

```
world sprites → projectiles → particles → FOG cloud masses → MIST wisps
              → pixel FX → editor gizmos
```

Fog sits at z 0.0205–0.021, mist at 0.022 (in front). Both are
`SpriteLighting2D.ApplyUnlit` with the depth test off — atmosphere, never lit surfaces.

## Presets

- `Fog` → coverage 0.75, 12 clusters, cluster size 1.30, opacity 0.30, base haze 0.35, grey.
- `Mist` → 40 wisps, height 0.20, length 0.16, thickness 0.018, opacity 0.20.
- Rain/Storm/Snow set coverage/size/opacity (0.45/1.10/0.20, 0.75/1.35/0.34, 0.35/1.10/0.16).
- Every preset clears `MistEnabled` first; `ClearWeather()` and `ResetWeather()` clear both.
