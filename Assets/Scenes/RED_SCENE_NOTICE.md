# RedScene Is The Primary Fusion Reference

`Assets/Scenes/RedScene.unity` is the primary development scene for current Fusion gameplay.

Use RedScene as the default reference for:

- RuntimeTileMesh fusion blocks and generated room planes
- Player / Heading Point control
- Management mode, shop, buying, dragging, snapping, and merging blocks
- Topology map / minimap behavior
- Door and wall debug visuals
- Concrete footstep tuning
- Fusion day-night environment behavior

Legacy scenes such as `LogicalCursorIntegration`, `SampleScene`, and `newstuff` are historical references only. Do not base new Fusion implementation details on them unless a task explicitly asks for a legacy comparison.
