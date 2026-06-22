# Deep Research Prompt: AE 到 Unity Shader Bridge 插件研究

请你作为一名技术产品研究员、Unity 技术美术、After Effects 插件生态研究员，围绕下面这个插件项目做一次系统性 Deep Research。研究目标不是泛泛介绍 AE 或 Unity，而是帮助我判断：现有市场上有没有类似工具，它们各自覆盖哪些工作流，我正在做的插件可以如何差异化和革新。

## 当前项目背景

我正在开发一个跨平台插件系统，暂名为 `AE2Unity / AEBridge`。

目标用户是：希望把 After Effects 里的 2D motion graphics、UI 动效、视觉设计、特效设计，尽可能自动化地转换到 Unity 6 项目中的技术美术、游戏设计师、交互设计师、独立开发者。

目标不是简单导出视频，也不是只把 AE 动画变成 Lottie，而是建立一套 AE 与 Unity 之间的桥接工具链：

- AE 侧插件负责读取当前 Composition、Layer、Transform、Opacity、Blend Mode、Mask、Effect metadata、关键帧、表达式文本、素材引用、参考帧。
- AE 侧把这些内容导出为 `.ae2shader` JSON payload。
- Unity 侧插件 `AEBridge` 负责接收 AE 发来的 job，导入 `.ae2shader`，分析能力范围，生成 Unity 可用的 shader、material、preview asset。
- 当前 Unity 目标环境是 Unity 6 / URP，shader 方向包括 ShaderLab、HLSL、Shader Graph Custom Function。
- 当前 AE 目标环境是 After Effects 2026。
- 跨平台目标是 Windows 和 macOS。

当前 MVP 已经做了一个文件队列型 bridge：

```text
AE Plugin
  -> .ae2unitybridge/inbox/<JobId>.job
  -> .ae2unitybridge/payloads/<JobId>/<CompName>.ae2shader

Unity AEBridge Receiver
  -> polls inbox
  -> imports payload into Assets/AE2Unity/Exports
  -> generates .generated.shader and .generated.mat
  -> writes .ae2unitybridge/outbox/<JobId>.result.json
```

当前插件支持的是自动化链路和基础 metadata 转换，还没有完成真正完整的 AE effect 到 shader 的一比一编译。当前功能范围包括：

- 导出 AE comp 基础信息：width、height、frame rate、duration、work area、color space、bit depth。
- 导出 layer 基础信息：name、index、type、enabled、in/out point、blend mode、track matte。
- 导出 transform keyframes：anchor、position、scale、rotation、opacity。
- 导出 effect/mask metadata，用于能力分析。
- 导出 reference frames，用于未来 Unity 渲染结果像素差对比。
- Unity 导入 `.ae2shader` 为 ScriptableObject。
- Unity 生成 preview shader/material。
- Unity Inspector 显示 unsupported feature warnings。
- Unity AEBridge Receiver 接收 AE job 并自动 implementation。

长期目标是把 AE 里的设计尽可能转换成 Unity 中可实时运行、可调参、可复用的 shader/material/prefab/animation clip，而不是只生成静态贴图或视频。

## 请研究的问题

请覆盖以下研究问题，并尽量使用官方文档、产品页、插件市场页、GitHub、开发者论坛、技术博客、用户案例作为来源。

1. 现有是否有类似“After Effects 到 Unity”的插件或工具？
   - 它们叫什么？
   - 是商业工具、开源项目、官方工具，还是社区脚本？
   - 是否仍在维护？
   - 支持哪些 AE 版本、Unity 版本、平台？
   - 它们是导出视频、序列帧、Lottie、JSON、SVG、Spine、Rive、Unity Timeline、Prefab、Shader，还是其他格式？

2. 现有是否有类似“AE 到代码 / AE 到实时引擎 / AE 到 shader”的工具？
   - 例如 AE 到 Web、AE 到 Lottie、AE 到 SVG animation、AE 到 WebGL、AE 到 game engine、AE 到 Unity UI、AE 到 Unreal 的工具。
   - 它们能否保留 layer structure、keyframes、blend modes、masks、text、shape、expressions、effects？
   - 哪些东西会 fallback 成图片、video、sprite sheet、texture atlas？

3. Unity 生态里有没有解决相近问题的插件？
   - Unity Lottie/Rive/Spine/DragonBones/Live2D/Anima2D/Sprite animation/Timeline/VFX Graph/Shader Graph 相关工作流。
   - Unity Asset Store 或 GitHub 上是否有 AE importer、motion graphics importer、vector animation importer、shader generator、flipbook baker、timeline bridge。
   - 它们覆盖的场景是 UI 动效、角色动画、粒子特效、2D cutout、motion graphics、shader effects，还是游戏内 cinematic？

4. 主流工作流是什么？
   - AE 设计师通常如何把动效交付给 Unity 开发者？
   - 常见路径是否包括：MP4/WebM、PNG sequence、sprite sheet、JSON/Lottie、SVG、Spine/Rive、手动重建、Shader Graph 手工复刻、Timeline/Animator 手工实现？
   - 每种工作流的优缺点是什么？
   - 在游戏开发、交互装置、UI/UX prototype、AR/VR、real-time motion graphics 中分别有什么不同？

5. 现有工具的能力边界和痛点是什么？
   - 哪些 AE 特性最难转换：expressions、third-party effects、precomp、3D layers、camera/lights、text animator、masks、track mattes、blur/glow/noise、color management？
   - 为什么这些特性难以一比一转换到 Unity？
   - 现有工具通常怎么处理这些问题？
   - 用户最常抱怨什么：不稳定、格式限制、手动步骤多、视觉不一致、性能差、不可调参、无法实时、跨平台问题？

6. 我的插件可以如何革新？
   - 如果目标是 AE 侧发送、Unity 侧接收并 implementation，这个 architecture 相比“单次导出文件”有什么优势？
   - 文件队列 bridge、HTTP/WebSocket bridge、Unity Editor receiver、AE panel sender，各自的优缺点是什么？
   - “shader-first conversion” 相比 “video/sprite-sheet export” 的核心价值是什么？
   - 如何设计 fallback 策略：shader 可编译、shader + baked assets、full baked fallback？
   - 如何用 reference frames 做 pixel diff validation？
   - 如何设计能力报告，让设计师知道哪些 AE 层被实时转换、哪些被 bake、哪些需要手动重建？

7. 请给出竞品/替代方案矩阵。
   - 工具名称
   - 官网或来源链接
   - 类型：AE plugin / Unity plugin / standalone / web tool / open source
   - 输入格式
   - 输出格式
   - 是否支持 Unity
   - 是否支持 shader 或实时材质
   - 支持的 AE 特性
   - fallback 策略
   - 价格/授权
   - 平台
   - 维护状态
   - 主要优点
   - 主要限制

8. 请给出主流工作流地图。
   - 从 AE 到 Unity 的常见路径有哪些？
   - 哪些适合 UI？
   - 哪些适合 2D 特效？
   - 哪些适合游戏内实时材质？
   - 哪些适合视觉保真但不需要互动？
   - 哪些适合需要 runtime parameter control 的情况？

9. 请给出我的插件的差异化产品定位。
   - 它不应该只是“另一个导出器”，它应该主打什么？
   - 目标用户是谁？
   - 最小可用版本应该支持哪些 AE 特性？
   - 哪些功能最能证明它有价值？
   - 哪些功能应该避免过早承诺？

10. 请给出技术路线建议。
    - AE 侧应该继续用 ExtendScript/ScriptUI，还是应该研究 UXP / C++ SDK / CEP？
    - Unity 侧应该优先使用 ScriptedImporter、AssetPostprocessor、EditorWindow、SettingsProvider、ShaderLab/HLSL、Shader Graph、Timeline、Animator、RenderTexture、URP Renderer Feature 中的哪些？
    - 如何设计 IR / schema？
    - 如何组织 conversion passes？
    - 如何处理 cross-platform Windows/macOS？
    - 如何处理第三方 AE 插件？
    - 如何处理色彩管理和 gamma/linear？
    - 如何处理性能预算？

11. 请给出未来 3 个版本的路线图。
    - v0.1：自动化 bridge + metadata import
    - v0.2：基础 shape/gradient/transform/opacity/blend mode 转 shader
    - v0.3：mask/blur/glow/noise + reference frame diff
    - 或者请根据研究结果提出更合理路线。

12. 请给出风险清单。
    - 技术风险
    - 产品风险
    - 用户体验风险
    - 跨平台风险
    - 法务/授权风险，例如字体、第三方素材、AE 插件效果、Adobe/Unity API 限制
    - 性能风险

## 研究输出格式

请按以下结构输出：

1. Executive Summary
2. Existing Tools and Competitors
3. Workflow Landscape: AE to Unity / Real-Time Engines
4. Capability Matrix
5. Gaps and Pain Points
6. Innovation Opportunities for AEBridge
7. Recommended MVP Scope
8. Technical Architecture Recommendations
9. Roadmap
10. Risks and Unknowns
11. Source List

请尽量给出表格，尤其是竞品矩阵和工作流矩阵。

## 来源要求

- 请优先使用官方文档、产品官网、Unity Asset Store、Adobe Exchange、GitHub、开发者论坛、技术博客。
- 请标注每个工具的来源链接。
- 如果信息可能已经过时，请标注“需要验证”。
- 请特别关注 2024-2026 年仍然活跃或仍然被使用的工具。
- 不要只列工具名字，要说明它们实际解决的问题、覆盖范围、限制和与我这个 AEBridge 插件的区别。

## 重要判断标准

请始终围绕一个核心问题分析：

“如果我的目标是让 AE 设计能够尽可能自动化地进入 Unity，并且尽可能生成实时 shader/material/prefab，而不是只导出视频，那么现有工具有什么缺口？我的 AEBridge 插件最有价值的创新点在哪里？”
