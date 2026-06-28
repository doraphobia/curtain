# Procedural Circle Motion Sample

## English

Import this sample through Unity Package Manager's `Samples` section, then select [ProceduralCircle.ae2motion](ProceduralCircle.ae2motion). The Inspector shows the imported `AE2MotionData` and offers:

- `Generate Runtime Assets`: creates a `.motion.mat` and `.motion.prefab` using `AE2Unity/Procedural/Circle Unlit`.
- `Create Preview GameObject`: creates a scene quad with `AE2MotionPlayer`, a runtime material, and MaterialPropertyBlock binding.

The sample contains no PNG, video, or spritesheet frames. Unity evaluates position, scale, and opacity keyframes at runtime and sends the result into the procedural circle shader.

## 中文

在 Unity Package Manager 的 `Samples` 区域导入这个示例，然后选中 [ProceduralCircle.ae2motion](ProceduralCircle.ae2motion)。Inspector 会显示导入后的 `AE2MotionData`，并提供：

- `Generate Runtime Assets`：使用 `AE2Unity/Procedural/Circle Unlit` 生成 `.motion.mat` 和 `.motion.prefab`。
- `Create Preview GameObject`：在场景里创建一个带 `AE2MotionPlayer` 的 quad，并通过 MaterialPropertyBlock 把运动参数传给 shader。

这个 sample 不包含 PNG、视频或 spritesheet。Unity 会在运行时 evaluate position、scale 和 opacity keyframes，然后把结果传给 procedural circle shader 实时绘制圆形。
