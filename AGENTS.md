# Duo Curtain Development Rules

- Grid, tile, and room-block layout uses integer world coordinates only.
- One room grid cell is `1x5` Unity world units. Logical footprint, generated connected floor planes, and player-blocking checks must stay aligned to that grid.
- Preserve an asset's designed proportions when normalizing tile/window sizes; snap width to the nearest multiple of `1` and height to the nearest multiple of `5` rather than forcing every piece to one uniform size.
- `TilePlacementGrid.cellSize` must remain `1x5`, with its origin snapped to the same grid.
- Room visuals are generated as connected whole planes from adjacent registered room cells. Do not rebuild a visual-tile-prefab-per-cell system unless explicitly requested.
- `RuntimeTileMesh` uses generic `1x1` logical tile coordinates internally; project-specific world size is applied through `tileSize` (for current rooms, `1x5`).
- `Assets/Scenes/RedScene.unity` is the primary development and design reference scene. Do not use `LogicalCursorIntegration`, `SampleScene`, `newstuff`, or old room scenes as the default reference for new Fusion gameplay, topology map, camera, player, shop, footstep, or day-night work unless the user explicitly asks to compare legacy behavior.
- `TilePieceDefinition.cells` is the source of truth for a tile footprint. Keep cells explicit for production prefabs; do not let decorative children auto-generate gameplay cells.
- `Player` is the physical cursor/body and owns blocking, footsteps, and current-block checks.
- `Heading Point` is the interaction pointer. Hover-to-interact systems such as curtains, drawing zones, placement previews, and UI-like world boxes must query Heading Point, not Player.
- In Management Mode, selecting a block that currently contains `Player` carries `Player` with that block by preserving the player's local offset until the block is placed or the selection is cancelled.
- The Tab block-information overlay shows one screen-space label per Fusion Block at its logical top-right corner. Dimensions come from the `RuntimeTileMeshView.tiles` logical bounding box, and Type comes from `RuntimeTileMeshDraggableBlock.blockType` with `DEFAULT` fallback.
- Gameplay-readable visuals must use the shared `GameplayVisualRenderer` / `GameplayAdaptiveContrast.hlsl` pipeline instead of fixed black-or-white color switches. Keep adaptive contrast in rendering code; gameplay, AI, door, window, footprint, and interaction state must not depend on sampled background color.
