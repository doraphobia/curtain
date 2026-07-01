# Curtain Enemy Vision

The vision system is split into three independent layers:

1. `VisionSensor2D` and `RadialVisionSampler2D` create gameplay geometry.
2. `VisionSnapshot` stores renderer-independent ray and polygon data.
3. `IVisionRenderer` consumes snapshots. The first backend is `ProceduralMeshVisionRenderer`.

Gameplay code must not access `MeshRenderer`, `Material`, shader properties, or the generated mesh.
Future shader, render-texture, decal, or renderer-feature backends should implement `IVisionRenderer`
and consume the existing snapshot and `VisionRenderParameters`.

`IVisibilitySegmentSource`, `IVisibilityOpeningSource`, `IVisionOccluder`, and `IVisionModifier`
are extension contracts. Vision no longer treats windows as a special portal that emits a second
cone. Open windows, open doors, vents, broken walls, and future glass walls all register generic
`VisibilityOpening` geometry. Each incoming ray either stops on an occluder or continues through an
opening aperture in the same direction.

The sensor reuses its snapshot, sample lists, and raycast buffer. The mesh backend reuses a dynamic
mesh and vertex/index/UV/color lists to avoid routine per-frame garbage.

## Visibility inputs

`VisionSensor2D` samples through `VisibilityWorld` by default. `VisibilityWorld` asks every
`IVisibilitySegmentSource` for occluding world-space line segments and every
`IVisibilityOpeningSource` for aperture geometry, then `RadialVisionSampler2D` builds a visibility
polygon against those two lists. The old `Physics2D.Raycast` path remains as a fallback only when no
visibility world geometry is registered.

Runtime/fusion room blocks are not expected to rely on colliders or layers for enemy sight. The
primary source is `RuntimeTileMeshFusionSandbox`, which converts every merged block's outer occupied
cell boundary into `Wall` segments. `RuntimeTileMeshFusionDoor` contributes closed/open door and
safe-wall segments plus an opening only when its doorway is passable. `FusionWallAttachment`
contributes closed window occluders and open window openings. Adjacent open windows merge into a
single larger opening before the enemy vision sampler sees them.

This matters because generated room meshes and gameplay triggers are not guaranteed to be complete
vision occluders. A block can be visible and walkable while still missing a collider or being on a
layer ignored by `Physics2D.Raycast`. The visibility solver should therefore treat the block/grid
topology as the source of truth, not selected-room state or renderer/collider side effects.

## Opening Rule

An opening is not a second eye and does not reveal a whole room. It is a local aperture in an
otherwise blocking boundary.

- Closed doors/windows/walls are occluders.
- Open doors/windows are openings.
- Rays that hit an occluder outside an opening stop.
- Rays that hit an occluder through an opening continue in the same direction.
- The continuation width comes from opening geometry, not a hardcoded spread angle.
- Walls around the opening remain opaque.
- Player detection must use `VisionSnapshot.TryGetDetectionInfo` so gameplay matches the rendered
  polygon.

Runtime World is expected to evolve as:

`Geometry -> Occluders -> Openings -> Navigation -> Visibility -> Sound -> Lighting`.

Use `VisionDebugView2D` to show world segments, rays, hit points, and visibility polygons.
