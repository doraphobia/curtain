# Curtain Enemy Vision

The vision system is split into three independent layers:

1. `VisionSensor2D` and `RadialVisionSampler2D` create gameplay geometry.
2. `VisionSnapshot` stores renderer-independent ray and polygon data.
3. `IVisionRenderer` consumes snapshots. The first backend is `ProceduralMeshVisionRenderer`.

Gameplay code must not access `MeshRenderer`, `Material`, shader properties, or the generated mesh.
Future shader, render-texture, decal, or renderer-feature backends should implement `IVisionRenderer`
and consume the existing snapshot and `VisionRenderParameters`.

`IVisionPortal`, `IVisionOccluder`, and `IVisionModifier` are extension contracts. Window projection
is implemented as a one-depth bidirectional vision portal: the primary polygon stops at the open
window segment, then a secondary polygon starts just past the window and is clipped by the same
`VisibilityWorld` segments. Recursive or linked-portal projection is deliberately left for later.

The sensor reuses its snapshot, sample lists, and raycast buffer. The mesh backend reuses a dynamic
mesh and vertex/index/UV/color lists to avoid routine per-frame garbage.

## Visibility inputs

`VisionSensor2D` now samples through `VisibilityWorld` by default. `VisibilityWorld` asks every
`IVisibilitySegmentSource` for world-space line segments, then `RadialVisionSampler2D` builds a real
visibility polygon against those segments. The old `Physics2D.Raycast` path remains as a fallback
only when no visibility segments are registered.

Runtime/fusion room blocks are not expected to rely on colliders or layers for enemy sight. The
primary source is `RuntimeTileMeshFusionSandbox`, which converts every merged block's outer occupied
cell boundary into `Wall` segments. `RuntimeTileMeshFusionDoor` contributes closed/open door and
safe-wall segments. `FusionWallAttachment` contributes closed/open window segments and configures
the attached `WindowPortal` aperture.

This matters because generated room meshes and gameplay triggers are not guaranteed to be complete
vision occluders. A block can be visible and walkable while still missing a collider or being on a
layer ignored by `Physics2D.Raycast`. The visibility solver should therefore treat the block/grid
topology as the source of truth, not selected-room state or renderer/collider side effects.

## Window portal rule

An open window is not transparent wall and does not reveal a whole room. It is a narrow aperture.

- `ClosedWindow` blocks vision and movement.
- `OpenWindow` blocks primary vision as a boundary hit, blocks movement, and creates a portal
  continuation polygon.
- Walls around the window remain opaque.
- Player detection must use `VisionSnapshot.TryGetDetectionInfo` so gameplay matches the rendered
  primary and portal polygons.

Tune projection on `VisionSensor2D` or `FusionNightFootprintEnemy`:

- `allowWindowPortals`
- `portalExitOffset`
- `portalContinuationDistance`
- `portalSpreadAngle`
- `maxPortalDepth` (currently intended to stay at `1`)

Use `VisionDebugView2D` to show world segments, primary rays, portal rays, and portal polygons.
