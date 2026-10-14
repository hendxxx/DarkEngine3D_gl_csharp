# Copilot Instructions

## Project Guidelines
- For this Sprite Editor task, generated normal maps should contain per-pixel directional normals/volume like the provided colorful sprite-map reference, not a flat neutral normal texture.
- When modifying Sprite Editor atlas packing/repacking, preserve the existing optimized sprite animation behavior, clip metadata, frame ordering, atlas rendering, and normal maps; validate mappings before replacing assets and use failure-safe commits.