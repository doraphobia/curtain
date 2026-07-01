# Gameplay Visual Accessibility Framework

Gameplay rendering samples URP's camera opaque texture per pixel and continuously adapts contrast. Gameplay code does not choose black or white based on room state.

Use `GameplayVisualRenderer.Ensure(...)` for generated visuals, or add `GameplayVisualRenderer` in the Inspector. A `GameplayVisualProfile` can share tuning across prefabs. Custom future shaders can include `GameplayAdaptiveContrast.hlsl` and call `DuoCurtainAdaptiveContrast` without replacing their animation logic.

Priorities are semantic tuning weights, not sorting orders. Global and per-visual debug modes expose background luminance, contrast map, adaptive blend, and priority.
