# Duo Curtain Development Rules

- Grid, tile, and room-block layout uses integer world coordinates only.
- One tile grid unit is `5` Unity world units. Tile visual size, logical footprint, and player-blocking area must stay aligned to multiples of `5`.
- `TilePlacementGrid.cellSize` must remain `5x5`, with an integer origin snapped to the same grid.
- `TilePieceDefinition.cells` is the source of truth for a tile footprint. Keep cells explicit for production prefabs; do not let decorative children auto-generate gameplay cells.
- `Player` is the physical cursor/body and owns blocking, footsteps, and current-block checks.
- `Heading Point` is the interaction pointer. Hover-to-interact systems such as curtains, drawing zones, placement previews, and UI-like world boxes must query Heading Point, not Player.
