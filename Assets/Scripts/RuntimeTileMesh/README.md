# Runtime Tile Mesh

This folder contains a runtime pipeline for turning occupied grid cells into connected procedural meshes.

Pipeline:

`Tile Occupancy -> Connected Components -> Boundary Edges -> Ordered Loops -> Collinear Cleanup -> Triangulation -> Continuous UV -> Mesh`

Current project integration:

- Generic runtime tile coordinates are treated as `1x1` logical cells.
- `RuntimeTileMeshView.tileSize` scales those logical cells into Unity space.
- `TilePlacementGrid` can pass its current `1x5` room cell size into this system, so the project grid remains aligned while the mesh algorithm stays generic.
- `RuntimeTileMeshView` is only responsible for geometry and clipping. It should not own procedural-motion scale decisions.
- `RuntimeTileMeshProjectionRenderer` owns visual projection state and pushes it through `MaterialPropertyBlock`, so mesh rebuilds do not restart or rescale the pattern.

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

- The fallback triangulator is ear clipping.
- It supports simple no-hole concave polygons such as `L`, `T`, and `Z`.
- Hole loops are detected and reported, but not triangulated by the fallback path. Install or vendor `LibTessDotNet` later to support holes robustly.

Testing:

Use `Assets/Scenes/RuntimeTileMeshTest.unity` as the main manual test scene. It contains
an interactive fusion sandbox for dragging white connected planes on a `1x1`
test grid. Hover fades a block red, click selects it blue, movement snaps to
the grid, and clicking again places it. Placed blocks merge into one new block
when their occupied cells overlap or share an edge. Diagonal corner-only contact
stays separate. The blocks use `WorldTile` projection, so the procedural tile
continues in world space while the runtime mesh changes shape.

To rebuild that scene, use `Tools/Duo Curtain/Runtime Tile Mesh/Create Test Scene`.
To verify the mesh and fusion rules, use `Tools/Duo Curtain/Runtime Tile Mesh/Run Self Test`.

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
- `RingWithHole` logs a warning with the fallback triangulator instead of silently filling the hole.
- In the fusion sandbox, overlap and exact edge contact merge blocks; diagonal corner contact does not.
