# Sprite Sheets: Auto-Load on Project Open

## Summary

`Artifacts/Sprites/sprites.sheets.json` is now auto-loaded whenever a project is opened in the IDE, including any `*-sprite-anim.json` pattern exports found in the project tree. This matches the existing project-open wiring in `Engine/IDE/IDE.cs` → `SpriteEditorPanel.OnProjectChanged(projectRoot)`.

## What happens on project open

- The project manager raises `OnProjectChanged` with the new project root.
- The IDE calls `SpriteEditorPanel.OnProjectChanged(projectRoot)`.
- Sprite sheets from `sprites.sheets.json` are loaded.
- Any `*-sprite-anim.json` files in `Assets` are merged as sheets + animation clips.
- The sprite registry is synced so the player and Sprite2D objects can resolve clips in the viewport and in play/preview.

## What happens on project close

- Tracker sets for auto-loaded sheets/clips are cleared.
- Pattern-action defaults are reset.
- Session catalogs are cleared with the rest of the project state.

## Manual fallback

If auto-load is not wanted for a session, the Sprite Editor still supports the toolbar "Auto Load" button and the "Load" dialogs.
