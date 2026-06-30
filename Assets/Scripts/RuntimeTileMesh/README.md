# Runtime Tile Mesh

This folder contains a runtime pipeline for turning occupied grid cells into connected procedural meshes.

Pipeline:

`Tile Occupancy -> Connected Components -> Per-Tile Grid Quads -> UV -> Mesh`

Boundary extraction is still used for collider/debug outlines, but render mesh is built directly from occupied grid cells so merged 1x1 (or any fixed `tileSize`) floors never lose concave detail.

Current project integration:

- Generic runtime tile coordinates are treated as `1x1` logical cells.
- `RuntimeTileMeshView.tileSize` scales those logical cells into Unity space.
- `TilePlacementGrid` can pass its current `1x5` room cell size into this system, so the project grid remains aligned while the mesh algorithm stays generic.
- `RuntimeTileMeshView` is only responsible for geometry and clipping. It should not own procedural-motion scale decisions.
- `RuntimeTileMeshProjectionRenderer` owns visual projection state and pushes it through `MaterialPropertyBlock`, so mesh rebuilds do not restart or rescale the pattern.
- Projection is optional. If no projection/animation material is assigned, `RuntimeTileMeshView` must keep the old plain white fallback visual.

UV modes:

- `Bounds`: maps the entire connected mesh bounds to `0-1`.
- `ObjectSpace`: uses vertex `x/y` positions times `uvTilingScale`, plus `uvOffset`.
- Bounds UV is only appropriate for deliberate stretch effects such as logo reveals. Procedural motion should use a projection shader instead.

Projection modes:

- `StretchToBounds`: uses generated mesh UV and stretches to the current mesh bounds.
- `ObjectSpace`: repeats in local object coordinates.
- `WorldTile`: repeats in stable world coordinates. This is the default for merged room/block motion.
- `AnchoredTile`: repeats from an explicit world-space anchor for object-owned pattern variants.

For AE-style reusable motion tiles, author one tile such as `3x3`, set
`motionTileSize` to that cell span, and let the shader use `frac(globalPatternCoordinate / motionTileSize)`.
Merging should reveal more of the same infinite pattern rather than changing UV scale.

Triangulation:

- Render mesh uses one quad (2 triangles) per occupied logical tile.
- This preserves every concave corner and every Z/T/L detail after fusion.
- Boundary loops are still extracted for collider paths and gizmo debug.
- Ear clipping remains available for future non-grid shapes, but grid floors no longer depend on it.

Testing:

Use `Assets/Scenes/RedScene.unity` as the main development and manual test scene.
It contains an interactive fusion sandbox for dragging white connected planes on a
`1x1` test grid. Hover fades a block red, click selects it blue, movement snaps to
the grid, and clicking again places it. Placed blocks merge into one new block when
their occupied cells overlap or share an edge. Diagonal corner-only contact stays
separate. The blocks use `WorldTile` projection, so the procedural tile continues
in world space while the runtime mesh changes shape.

Do not use legacy room scenes such as `LogicalCursorIntegration`, `SampleScene`, or
`newstuff` as the default reference for Fusion gameplay, topology map, camera,
player, shop, footsteps, or day-night work unless a task explicitly asks for a
legacy comparison.

To rebuild that scene, use `Tools/Duo Curtain/Runtime Tile Mesh/Create RedScene`.
To verify the mesh and fusion rules, use `Tools/Duo Curtain/Runtime Tile Mesh/Run Self Test`.

Fusion integrity monitoring:

- Add `RuntimeTileMeshFusionIntegrityMonitor` to the Fusion Sandbox object, or regenerate `RedScene`.
- Open `Tools/Duo Curtain/Runtime Tile Mesh/Fusion Integrity Monitor` during Play Mode.
- Every merge group records before/after tile snapshots, mesh counts, boundary stats, and issue codes.
- Issues log missing tiles, mesh coverage gaps, unconsumed boundary edges, and merge tile loss/gain.
- Use `Run Audit` for a full-scene check at any time, and `Export Log` to save the report history.

To make a new manual test object:

1. Create an empty GameObject.
2. Add `RuntimeTileMeshView`.
3. Add `RuntimeTileMeshDemo`.
4. Choose `Single`, `OneByThree`, `L`, `T`, `Z`, `RingWithHole`, or `DiagonalTouch`.
5. Assign a material on `RuntimeTileMeshView.material`.
6. Switch `UV Mode` between `Bounds` and `ObjectSpace`.

Expected behavior:

- `OneByThree` becomes one rectangle mesh, not three repeated-UV quads.
- `L`, `T`, and `Z` triangulate as one concave mesh per connected component.
- `DiagonalTouch` creates two separate component meshes because corner contact is not a four-neighbor connection.
- `RingWithHole` renders only occupied tiles, leaving the empty center unmeshed.
- In the fusion sandbox, overlap and exact edge contact merge blocks; diagonal corner contact does not.
