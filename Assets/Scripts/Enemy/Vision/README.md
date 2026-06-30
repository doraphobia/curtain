# Curtain Enemy Vision

The vision system is split into three independent layers:

1. `VisionSensor2D` and `RadialVisionSampler2D` create gameplay geometry.
2. `VisionSnapshot` stores renderer-independent ray and polygon data.
3. `IVisionRenderer` consumes snapshots. The first backend is `ProceduralMeshVisionRenderer`.

Gameplay code must not access `MeshRenderer`, `Material`, shader properties, or the generated mesh.
Future shader, render-texture, decal, or renderer-feature backends should implement `IVisionRenderer`
and consume the existing snapshot and `VisionRenderParameters`.

`IVisionPortal`, `IVisionOccluder`, and `IVisionModifier` are extension contracts only. Recursive
window or linked-portal projection is deliberately not implemented in the first backend.

The sensor reuses its snapshot, sample lists, and raycast buffer. The mesh backend reuses a dynamic
mesh and vertex/index/UV/color lists to avoid routine per-frame garbage.
