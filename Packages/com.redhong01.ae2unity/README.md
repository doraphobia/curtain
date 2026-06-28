# AE2Unity

## English

`AE2Unity` is an early automation scaffold for moving constrained Adobe After Effects 2026 compositions into Unity 6 as shader/material assets and realtime motion-data runtime assets.

The current workflow combines two sides:

- An After Effects ScriptUI panel that exports composition metadata, optional reference frames, optional Media Encoder output, and bridge jobs.
- A Unity package that receives `.ae2shader` or `.ae2motion` payloads, imports them into the project, and generates Unity shader/material/runtime prefab assets.

This is not a full one-click replacement for every After Effects feature yet. The package is designed to make supported AE motion graphics reproducible inside Unity, while clearly warning when a comp uses features that should be baked, exported as media, or handled by future compiler passes.

### Requirements

- Adobe After Effects 2026.
- Unity 6.
- macOS or Windows.
- Optional: Adobe Media Encoder when using media export modes.

The package uses C# editor/runtime code, ShaderLab/HLSL, and ExtendScript (`.jsx`). It does not require native DLLs.

### Documentation Rule

Repository files mentioned in this README are written as clickable Markdown links, such as [Tools/AfterEffects/AE2Unity.jsx](Tools/AfterEffects/AE2Unity.jsx). Local Unity project files, Unity Hub files, and generated export paths remain code paths because they do not exist inside this package repository.

### Install From Git

In Unity 6, install this package from the Package Manager:

1. Open `Window > Package Manager`.
2. Click `+`.
3. Choose `Install package from git URL...`.
4. Enter:

```text
https://github.com/RedHong01/AE2Unity.git
```

You can also add it directly to `Packages/manifest.json`:

```json
"com.redhong01.ae2unity": "https://github.com/RedHong01/AE2Unity.git"
```

To pin a release, use a tag:

```json
"com.redhong01.ae2unity": "https://github.com/RedHong01/AE2Unity.git#v0.6.1"
```

If the repository is private, Unity must have GitHub access through your local Git credentials.

### AEBridge Workflow

1. Install [Tools/AfterEffects/AE2Unity.jsx](Tools/AfterEffects/AE2Unity.jsx) into the After Effects `Scripts/ScriptUI Panels` folder.
2. Restart After Effects.
3. Open `Window > AE2Unity.jsx`.
4. Select a Unity project from the Unity Hub project dropdown.
5. Choose the active composition or a specific composition.
6. Choose an export mode, such as `AEBridge: .ae2shader -> Unity shader/material`.
7. Click `Run Export`.
8. The AE panel writes a bridge job into `.ae2unitybridge/inbox`.
9. Unity's AEBridge receiver imports the payload into `Assets/AE2Unity/Exports/<CompName>.ae2shader`.
10. Unity generates `<CompName>.generated.shader` and `<CompName>.generated.mat`.
11. Unity writes the result to `.ae2unitybridge/outbox`, which the AE panel can read with `Check Last Bridge Result`.

Direct export and manual generation are still available through `Direct .ae2shader into Unity Assets`, `Manual folder .ae2shader export`, and `Assets > AE2Unity > Generate Shader From AE2Shader`.

### Motion Runtime Workflow

The `.ae2motion` path treats After Effects as the authoring tool and Unity as the realtime renderer:

```text
AE Shape Layer + Transform/Trim Keyframes
-> .ae2motion Motion Data
-> AE2MotionData asset
-> AE2MotionPlayer
-> MaterialPropertyBlock
-> AE2Unity/Procedural/Circle, Rect, or Stroke Unlit
```

Use `Bridge: Motion runtime`, `Direct motion to Assets`, or `Manual motion folder` in [Tools/AfterEffects/AE2Unity.jsx](Tools/AfterEffects/AE2Unity.jsx). Unity imports the file with [Editor/Motion/AE2MotionImporter.cs](Editor/Motion/AE2MotionImporter.cs), stores it as [Runtime/Motion/AE2MotionData.cs](Runtime/Motion/AE2MotionData.cs), evaluates keyframes with [Runtime/Motion/AE2MotionEvaluator.cs](Runtime/Motion/AE2MotionEvaluator.cs), and binds values to shader properties through [Runtime/Motion/AE2ShaderPropertyBinder.cs](Runtime/Motion/AE2ShaderPropertyBinder.cs).

The realtime renderer currently supports the first runtime-renderable shape layer in a motion document. Supported renderer hints are procedural circle, procedural rounded rectangle, and procedural open-path stroke with trim start/end. Expressions, effects, masks, complex paths, animated path vertices, and non-shape layers are preserved as data/warnings so future compiler passes can support them without changing the bridge.

### After Effects Panel

The AE panel reads Unity Hub's local `projects-v1.json` and `projectSortPreferences.json`, so the Unity project dropdown follows the same project names and sorting preference used by Unity Hub. `Choose` remains available as a fallback for projects that are not listed in Unity Hub.

Panels opened from the `Window` menu can be dragged by their panel tab and docked into AE sidebars or saved workspaces. If you run the JSX through `File > Scripts > Run Script File...`, After Effects opens it as a standalone palette, which is useful for testing but cannot be docked into the workspace.

The panel switches to a compact paged layout when docked into a narrow sidebar. Compact mode groups controls into Export, Result, Paths, and Media sections. Use the compact `Up`/`Down` controls, arrow keys, Page Up/Page Down, the compact scrollbar, or mouse wheel when AE forwards wheel events to browse sections. The compact header also includes `Run` on the Export page and `Full`, which opens a complete standalone AE2Unity window without removing the docked panel. Each compact page uses explicit narrow-panel sizing so Export, Result, Paths, and Media controls remain visible in small sidebars. Export actions automatically switch compact mode to the Result page before writing progress or final status text. The compact Result page uses a fixed narrow-panel result box so status text remains visible in small sidebars.

Standalone windows include `Compact` and `Full Size` buttons. You can also use `Ctrl/Cmd+Shift+C` to switch a standalone window into compact mode and `Ctrl/Cmd+Shift+F` to restore the full layout. Wider floating or docked layouts show the full set of path, media, refresh, reference frame, and generation options at once.

### Export Modes

- `AEBridge: .ae2shader -> Unity shader/material`: sends composition metadata to Unity and asks Unity to generate shader/material assets.
- `AEBridge + Media Encoder`: sends metadata and can also queue a video/media export.
- `Media Encoder only`: exports media without generating Unity shader/material assets.
- `Direct .ae2shader into Unity Assets`: writes an `.ae2shader` file directly into the selected Unity project's `Assets` folder.
- `Manual folder .ae2shader export`: writes an `.ae2shader` file to a user-selected folder.
- `Bridge: Motion runtime`: sends `.ae2motion` data to Unity and asks Unity to generate runtime material/prefab assets.
- `Direct motion to Assets`: writes `.ae2motion` directly into the selected Unity project's `Assets` folder.
- `Manual motion folder`: writes `.ae2motion` to a user-selected folder.

### Supported MVP Surface

- Composition metadata: width, height, frame rate, duration, color/bit-depth hints.
- Layers: name, index, type, enabled flag, in/out points, blend mode, track matte hint.
- Transform keyframes: anchor, position, scale, rotation, opacity.
- Source footage path references.
- Basic effect/mask metadata for capability analysis.
- Unity material preview through `AE2Unity/Composite Unlit`.
- `.ae2motion` motion data: composition timing, layer hierarchy, transform keyframes, ellipse/rectangle/open-path shape parameters, rectangle roundness, fill/stroke/trim metadata, renderer hints, and explicit warnings.
- Runtime playback through [Runtime/Motion/AE2MotionPlayer.cs](Runtime/Motion/AE2MotionPlayer.cs), [Runtime/Motion/AE2MotionEvaluator.cs](Runtime/Motion/AE2MotionEvaluator.cs), and [Runtime/Motion/AE2ShaderPropertyBinder.cs](Runtime/Motion/AE2ShaderPropertyBinder.cs).
- Procedural runtime shaders: [Runtime/Shaders/Procedural/AE2UnityProceduralCircle.shader](Runtime/Shaders/Procedural/AE2UnityProceduralCircle.shader), [Runtime/Shaders/Procedural/AE2UnityProceduralRect.shader](Runtime/Shaders/Procedural/AE2UnityProceduralRect.shader), and [Runtime/Shaders/Procedural/AE2UnityProceduralStroke.shader](Runtime/Shaders/Procedural/AE2UnityProceduralStroke.shader).
- Motion schema reference: [Documentation~/ae2motion.schema.json](Documentation~/ae2motion.schema.json).
- Motion samples: [Samples~/ProceduralCircleMotion/ProceduralCircle.ae2motion](Samples~/ProceduralCircleMotion/ProceduralCircle.ae2motion), [Samples~/ProceduralShapeMotion/ProceduralRect.ae2motion](Samples~/ProceduralShapeMotion/ProceduralRect.ae2motion), and [Samples~/ProceduralShapeMotion/ProceduralStroke.ae2motion](Samples~/ProceduralShapeMotion/ProceduralStroke.ae2motion).

### Known Fallbacks

The importer marks complex features as warnings rather than silently pretending they are shader-identical. Third-party effects, 3D layers, cameras, lights, complex text animation, particles, and unsupported expressions should be baked or handled by later compiler passes.

### Unity Settings

Open `Project Settings > AE2Unity`.

- `Auto Generate On Import`: when enabled, Unity generates shader/material assets whenever an `.ae2shader` file is imported.
- `Overwrite Generated Assets`: when enabled, repeat exports update the same `.generated.shader` and `.generated.mat` files.
- `AEBridge Receiver Enabled`: when enabled, Unity polls `.ae2unitybridge/inbox` for AE jobs.
- `Bridge Output Path`: default Unity asset folder for bridge imports.

Bridge utilities are also available in `Tools > AE2Unity`.

### License

This project is released under the MIT License. Community users may use, modify, and redistribute it, including in commercial projects, as long as the copyright and license notice are preserved. See [LICENSE](LICENSE) for the full text.

## 中文

`AE2Unity` 是一个早期自动化工具框架，用来把受控范围内的 Adobe After Effects 2026 合成转换到 Unity 6 中，并生成 shader/material 资产和 realtime motion-data runtime 资产。

目前这套工作流由两部分组成：

- After Effects 侧的 ScriptUI 面板，负责导出合成 metadata、可选参考帧、可选 Media Encoder 输出，以及 AEBridge 任务。
- Unity 侧的 package，负责接收 `.ae2shader` 或 `.ae2motion` payload，将它导入 Unity 项目，并生成 shader/material/runtime prefab 资产。

它目前还不是“所有 AE 功能都能一键完整复刻”的最终编译器。它的目标是让支持范围内的 AE motion graphics 可以在 Unity 中复现，同时在遇到复杂功能时明确给出 warning，提示哪些内容需要 bake、导出为媒体文件，或者等待后续 compiler pass 支持。

### 环境需求

- Adobe After Effects 2026。
- Unity 6。
- macOS 或 Windows。
- 可选：当使用媒体导出模式时，需要 Adobe Media Encoder。

这个 package 使用 C# editor/runtime 代码、ShaderLab/HLSL 和 ExtendScript (`.jsx`)。它不依赖 native DLL。

### 文档规则

这个 README 中提到的仓库内文件会写成可点击的 Markdown 链接，例如 [Tools/AfterEffects/AE2Unity.jsx](Tools/AfterEffects/AE2Unity.jsx)。本地 Unity 项目文件、Unity Hub 文件，以及导出生成路径会保留为代码路径，因为它们不在这个 package repo 内。

### 通过 Git 安装

在 Unity 6 中，通过 Package Manager 安装：

1. 打开 `Window > Package Manager`。
2. 点击 `+`。
3. 选择 `Install package from git URL...`。
4. 输入：

```text
https://github.com/RedHong01/AE2Unity.git
```

你也可以直接写入 `Packages/manifest.json`：

```json
"com.redhong01.ae2unity": "https://github.com/RedHong01/AE2Unity.git"
```

如果希望锁定某个 release，可以使用 tag：

```json
"com.redhong01.ae2unity": "https://github.com/RedHong01/AE2Unity.git#v0.6.1"
```

如果 repo 是 private，Unity 需要能够通过你本地的 Git 凭据访问 GitHub。

### AEBridge 工作流程

1. 把 [Tools/AfterEffects/AE2Unity.jsx](Tools/AfterEffects/AE2Unity.jsx) 安装到 After Effects 的 `Scripts/ScriptUI Panels` 文件夹。
2. 重启 After Effects。
3. 从 `Window > AE2Unity.jsx` 打开面板。
4. 在 AE 面板中从 Unity Hub 项目列表选择 Unity project。
5. 选择当前正在编辑的 composition，或指定某一个 composition。
6. 选择导出模式，例如 `AEBridge: .ae2shader -> Unity shader/material`。
7. 点击 `Run Export`。
8. AE 面板会把 bridge job 写入 `.ae2unitybridge/inbox`。
9. Unity 侧 AEBridge receiver 会把 payload 导入到 `Assets/AE2Unity/Exports/<CompName>.ae2shader`。
10. Unity 生成 `<CompName>.generated.shader` 和 `<CompName>.generated.mat`。
11. Unity 把结果写入 `.ae2unitybridge/outbox`，AE 面板可以通过 `Check Last Bridge Result` 读取结果。

你也仍然可以使用直接导出和手动生成流程：`Direct .ae2shader into Unity Assets`、`Manual folder .ae2shader export`，以及 Unity 中的 `Assets > AE2Unity > Generate Shader From AE2Shader`。

### Motion Runtime 工作流程

`.ae2motion` 路径会把 After Effects 作为 motion authoring tool，把 Unity 作为 realtime renderer：

```text
AE Shape Layer + Transform/Trim Keyframes
-> .ae2motion Motion Data
-> AE2MotionData asset
-> AE2MotionPlayer
-> MaterialPropertyBlock
-> AE2Unity/Procedural/Circle, Rect, or Stroke Unlit
```

在 [Tools/AfterEffects/AE2Unity.jsx](Tools/AfterEffects/AE2Unity.jsx) 中选择 `Bridge: Motion runtime`、`Direct motion to Assets` 或 `Manual motion folder`。Unity 会用 [Editor/Motion/AE2MotionImporter.cs](Editor/Motion/AE2MotionImporter.cs) 导入文件，保存为 [Runtime/Motion/AE2MotionData.cs](Runtime/Motion/AE2MotionData.cs)，通过 [Runtime/Motion/AE2MotionEvaluator.cs](Runtime/Motion/AE2MotionEvaluator.cs) evaluate keyframes，并由 [Runtime/Motion/AE2ShaderPropertyBinder.cs](Runtime/Motion/AE2ShaderPropertyBinder.cs) 把结果写入 shader properties。

当前 realtime renderer 会渲染 motion document 中第一个可运行时渲染的 shape layer。已经支持 procedural circle、procedural rounded rectangle，以及带 trim start/end 的 procedural open-path stroke。Expressions、effects、masks、复杂 path、animated path vertices 和非 shape layers 会作为数据/warnings 保留下来，方便后续 compiler pass 扩展，而不需要重做 bridge。

### After Effects 面板

AE 面板会读取 Unity Hub 本地的 `projects-v1.json` 和 `projectSortPreferences.json`，所以 Unity Project 下拉列表会尽量沿用 Unity Hub 中的项目名称和排序。`Choose` 按钮仍然保留，用来选择没有出现在 Unity Hub 列表中的项目。

从 `Window` 菜单打开的面板可以拖拽 tab，并吸附到 AE 的侧边栏或保存进 workspace。如果通过 `File > Scripts > Run Script File...` 运行 JSX，AE 会把它作为独立 palette 打开，这适合测试，但不能吸附到 AE workspace 中。

当面板被吸附到较窄侧边栏时，会切换到紧凑分页布局。紧凑模式会把控件分成 Export、Result、Paths 和 Media 几个区域。你可以用紧凑模式里的 `Up`/`Down`、方向键、Page Up/Page Down、小 scrollbar，或者在 AE 把滚轮事件传给 ScriptUI 时直接用鼠标滚轮切换区域。紧凑标题栏会在 Export 页显示 `Run` 按钮，也有 `Full` 按钮，可以在不移除停靠面板的情况下打开一个完整的独立 AE2Unity 窗口。每个紧凑分页都有明确的小画幅尺寸，让 Export、Result、Paths 和 Media 控件在窄侧栏中都能保持可见。执行导出时，紧凑模式会先自动跳到 Result 页，再写入进度或最终状态。紧凑 Result 页会使用固定的小画幅结果框，确保状态文本在窄侧栏中仍然可见。

独立窗口里有 `Compact` 和 `Full Size` 按钮。你也可以用 `Ctrl/Cmd+Shift+C` 把独立窗口切到紧凑模式，用 `Ctrl/Cmd+Shift+F` 恢复完整布局。较宽的浮窗或停靠布局会一次性显示完整的 path、media、refresh、reference frame 和 generation 选项。

### 导出模式

- `AEBridge: .ae2shader -> Unity shader/material`：发送 composition metadata 到 Unity，并让 Unity 生成 shader/material 资产。
- `AEBridge + Media Encoder`：发送 metadata，同时可选地排队视频/媒体导出。
- `Media Encoder only`：只导出媒体文件，不生成 Unity shader/material。
- `Direct .ae2shader into Unity Assets`：把 `.ae2shader` 直接写入所选 Unity 项目的 `Assets` 文件夹。
- `Manual folder .ae2shader export`：把 `.ae2shader` 写入用户手动选择的文件夹。
- `Bridge: Motion runtime`：把 `.ae2motion` 数据发送到 Unity，并让 Unity 生成 runtime material/prefab 资产。
- `Direct motion to Assets`：把 `.ae2motion` 直接写入所选 Unity 项目的 `Assets` 文件夹。
- `Manual motion folder`：把 `.ae2motion` 写入用户手动选择的文件夹。

### 当前 MVP 支持范围

- Composition metadata：宽度、高度、帧率、时长、色彩/bit-depth 提示。
- Layers：名称、index、类型、enabled 状态、in/out points、blend mode、track matte hint。
- Transform keyframes：anchor、position、scale、rotation、opacity。
- Source footage 路径引用。
- 用于 capability analysis 的基础 effect/mask metadata。
- 通过 `AE2Unity/Composite Unlit` 进行 Unity material preview。
- `.ae2motion` motion data：composition timing、layer hierarchy、transform keyframes、ellipse/rectangle/open-path shape parameters、rectangle roundness、fill/stroke/trim metadata、renderer hints 和明确 warnings。
- 通过 [Runtime/Motion/AE2MotionPlayer.cs](Runtime/Motion/AE2MotionPlayer.cs)、[Runtime/Motion/AE2MotionEvaluator.cs](Runtime/Motion/AE2MotionEvaluator.cs) 和 [Runtime/Motion/AE2ShaderPropertyBinder.cs](Runtime/Motion/AE2ShaderPropertyBinder.cs) 进行 runtime playback。
- Procedural runtime shaders：[Runtime/Shaders/Procedural/AE2UnityProceduralCircle.shader](Runtime/Shaders/Procedural/AE2UnityProceduralCircle.shader)、[Runtime/Shaders/Procedural/AE2UnityProceduralRect.shader](Runtime/Shaders/Procedural/AE2UnityProceduralRect.shader) 和 [Runtime/Shaders/Procedural/AE2UnityProceduralStroke.shader](Runtime/Shaders/Procedural/AE2UnityProceduralStroke.shader)。
- Motion schema 参考：[Documentation~/ae2motion.schema.json](Documentation~/ae2motion.schema.json)。
- Motion samples：[Samples~/ProceduralCircleMotion/ProceduralCircle.ae2motion](Samples~/ProceduralCircleMotion/ProceduralCircle.ae2motion)、[Samples~/ProceduralShapeMotion/ProceduralRect.ae2motion](Samples~/ProceduralShapeMotion/ProceduralRect.ae2motion) 和 [Samples~/ProceduralShapeMotion/ProceduralStroke.ae2motion](Samples~/ProceduralShapeMotion/ProceduralStroke.ae2motion)。

### 已知 fallback

Importer 会把复杂功能标记为 warning，而不是假装它们已经被 shader 完全等价还原。第三方 effects、3D layers、cameras、lights、复杂 text animation、particles，以及不支持的 expressions，建议先 bake、导出为媒体，或者等待后续 compiler pass 支持。

### Unity 设置

打开 `Project Settings > AE2Unity`。

- `Auto Generate On Import`：启用后，Unity 每次导入 `.ae2shader` 都会自动生成 shader/material。
- `Overwrite Generated Assets`：启用后，重复导出会更新同名 `.generated.shader` 和 `.generated.mat`。
- `AEBridge Receiver Enabled`：启用后，Unity 会轮询 `.ae2unitybridge/inbox` 中的 AE jobs。
- `Bridge Output Path`：bridge import 的默认 Unity asset 文件夹。

Bridge 工具也可以从 `Tools > AE2Unity` 使用。

### 开源许可证

本项目使用 MIT License 发布。社区用户可以使用、修改和再分发，也可以用于商业项目，只需要保留版权和许可证声明。完整条款见 [LICENSE](LICENSE)。
