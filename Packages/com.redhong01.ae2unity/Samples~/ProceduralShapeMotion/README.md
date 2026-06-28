# Procedural Shape Motion Sample

## English

Import this sample through Unity Package Manager's `Samples` section, then select [ProceduralRect.ae2motion](ProceduralRect.ae2motion) or [ProceduralStroke.ae2motion](ProceduralStroke.ae2motion). The Inspector can generate runtime material/prefab assets for each file.

- [ProceduralRect.ae2motion](ProceduralRect.ae2motion) uses `AE2Unity/Procedural/Rect Unlit` with animated transform, size, and rounded corners.
- [ProceduralStroke.ae2motion](ProceduralStroke.ae2motion) uses `AE2Unity/Procedural/Stroke Unlit` with a simple open path and animated trim.

These samples contain no PNG, video, or spritesheet frames. Unity evaluates the motion data at runtime and writes the values into procedural shaders through `AE2MotionPlayer`.

## 中文

在 Unity Package Manager 的 `Samples` 区域导入这个示例，然后选中 [ProceduralRect.ae2motion](ProceduralRect.ae2motion) 或 [ProceduralStroke.ae2motion](ProceduralStroke.ae2motion)。Inspector 可以为每个文件生成 runtime material/prefab 资产。

- [ProceduralRect.ae2motion](ProceduralRect.ae2motion) 使用 `AE2Unity/Procedural/Rect Unlit`，包含 transform、size 和圆角动画。
- [ProceduralStroke.ae2motion](ProceduralStroke.ae2motion) 使用 `AE2Unity/Procedural/Stroke Unlit`，包含简单 open path 和 trim 动画。

这些 sample 不包含 PNG、视频或 spritesheet。Unity 会在运行时 evaluate motion data，并通过 `AE2MotionPlayer` 把数值写入 procedural shaders。
