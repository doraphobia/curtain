# Runtime Tile Mesh

This folder contains a runtime pipeline for turning occupied grid cells into connected procedural meshes.

Pipeline:

`Tile Occupancy -> Connected Components -> Boundary Edges -> Ordered Loops -> Collinear Cleanup -> Triangulation -> Continuous UV -> Mesh`

Current project integration:

- Generic runtime tile coordinates are treated as `1x1` logical cells.
- `RuntimeTileMeshView.tileSize` scales those logical cells into Unity space.
- `TilePlacementGrid` can pass its current `1x5` room cell size into this system, so the project grid remains aligned while the mesh algorithm stays generic.

UV modes:

- `Bounds`: maps the entire connected mesh bounds to `0-1`.
- `ObjectSpace`: uses vertex `x/y` positions times `uvTilingScale`, plus `uvOffset`.

Triangulation:

- The fallback triangulator is ear clipping.
- It supports simple no-hole concave polygons such as `L`, `T`, and `Z`.
- Hole loops are detected and reported, but not triangulated by the fallback path. Install or vendor `LibTessDotNet` later to support holes robustly.

Testing:

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
