# AE 到 Unity 一键自动化流程

这套工具的目标是把手动流程：

`AE 导出 .ae2shader -> 手动拖进 Unity -> 手动点 Generate Shader And Material`

变成：

`AE 里点 Send To Unity AEBridge -> Unity AEBridge 接收 job -> Unity 自动导入并生成 shader/material -> AE 可读取结果`

## 1. 安装 AE 面板

开发调试时可以直接运行：

1. 打开 After Effects 2026。
2. 打开 `File > Scripts > Run Script File...`。
3. 选择 `Packages/com.redhong01.ae2unity/Tools/AfterEffects/AE2Unity.jsx`。

长期使用时，把这个 JSX 文件复制到 AE 的 `ScriptUI Panels` 文件夹：

macOS:

```text
/Applications/Adobe After Effects 2026/Scripts/ScriptUI Panels/
```

Windows:

```text
C:\Program Files\Adobe\Adobe After Effects 2026\Support Files\Scripts\ScriptUI Panels\
```

重启 AE 后，从 `Window` 菜单打开 `AE2Unity`。

也可以使用包内安装脚本。

macOS:

```bash
cd Packages/com.redhong01.ae2unity/Tools/AfterEffects
./install-panel-macos.sh
```

Windows PowerShell:

```powershell
cd Packages\com.redhong01.ae2unity\Tools\AfterEffects
.\install-panel-windows.ps1
```

如果 AE 安装在非默认位置，把 AE 路径作为参数传进去。

## 2. 开启 AE 写文件权限

在 AE 里打开：

macOS:

```text
After Effects > Settings > Scripting & Expressions
```

Windows:

```text
Edit > Preferences > Scripting & Expressions
```

勾选：

```text
Allow Scripts To Write Files And Access Network
```

## 3. 在 AE 里绑定 Unity 项目

1. 打开 `AE2Unity` 面板。
2. 在 `Unity Project` 下拉列表里选择项目。这个列表会自动读取 Unity Hub 的项目名称和排序偏好。
3. 如果项目不在 Unity Hub 列表里，再点 `Choose` 手动选择项目根目录，也就是包含 `Assets` 和 `ProjectSettings` 的那个文件夹。
4. 在 `Composition` 里选择 `Current Active Comp` 或 `Specific Comp`。
5. 在 `Export Mode` 里选择导出方式。
6. `.ae2shader Folder` 默认是 `Assets/AE2Unity/Exports`，可以按项目需要改。
7. 如果导出模式包含 Media Encoder，设置 `Media Folder`、扩展名、Output Module Template，以及是否 `Start AME`。
8. 点 `Run Export`。

AE 会先把 payload 和 job 写到 Unity 项目根目录下：

```text
.ae2unitybridge/inbox/<JobId>.job
.ae2unitybridge/payloads/<JobId>/<CompName>.ae2shader
.ae2unitybridge/payloads/<JobId>/reference_frames/
```

## 4. Unity AEBridge 接收并实现

Unity 端默认开启 AEBridge Receiver。Unity 检测到 `.ae2unitybridge/inbox` 里的 job 后，会把 payload 导入到：

```text
Assets/AE2Unity/Exports/<CompName>.ae2shader
```

然后自动生成：

```text
Assets/AE2Unity/Exports/<CompName>.generated.shader
Assets/AE2Unity/Exports/<CompName>.generated.mat
```

最后 Unity 会把处理结果写回：

```text
.ae2unitybridge/outbox/<JobId>.result.json
```

AE 面板里点 `Check Last Bridge Result` 可以查看上一次 job 的状态。

如果选择 `AEBridge + Media Encoder video`，AE 会同时做两件事：

- 把 `.ae2shader` payload 发给 Unity AEBridge。
- 把选定 Composition 加入 AE Render Queue，并调用 `queueInAME()` 发送到 Adobe Media Encoder。媒体文件目标路径会落在 Unity 项目内的 `Media Folder`，默认是 `Assets/AE2Unity/Media`。

如果选择 `Media Encoder video only`，它只做 AME 视频/媒体输出，不发 `.ae2shader` bridge job。

设置位置：

```text
Project Settings > AE2Unity
```

可配置项：

- `Auto Generate On Import`：导入 `.ae2shader` 时自动生成 shader/material。
- `Overwrite Generated Assets`：重复从 AE 导出同名 comp 时覆盖已有生成文件。
- `AEBridge Receiver Enabled`：开启 Unity 侧接收器。
- `Bridge Output Path`：Bridge payload 最终导入到 Unity 的哪个 `Assets` 子目录。

Unity 菜单里也有手动工具：

```text
Tools > AE2Unity > Process AEBridge Inbox Now
Tools > AE2Unity > Open AEBridge Folder
```

## 5. Bridge 协议结构

Bridge 是文件队列协议，不开端口，不依赖系统防火墙或本地网络权限。

目录结构：

```text
.ae2unitybridge/
  inbox/       AE 写入待处理 job
  processing/  Unity 正在处理的 job
  done/        Unity 已完成的 job
  failed/      Unity 处理失败的 job
  outbox/      Unity 写回给 AE 的结果
  payloads/    AE 导出的 .ae2shader 和参考帧
```

job 命令：

```text
ImportAe2Shader
```

这个命令表示：Unity 读取 AE payload，导入为 `.ae2shader`，再执行当前生成器生成 shader/material。

## 6. 备用流程

如果只想绕过 Bridge，也可以在 AE 面板里点：

```text
Export Directly Into Assets
```

这会直接把 `.ae2shader` 写进 Unity 的 `Assets/AE2Unity/Exports`，再由普通 Asset Importer 自动生成。

## 7. 当前 MVP 限制

第一版重点是把自动化链路打通。复杂 AE 效果仍会被标记为 warning，不会伪装成已经 1:1 支持。

当前更适合验证：

- comp 基础信息
- layer 列表
- transform keyframes
- opacity keyframes
- 常见 blend mode 的元数据
- effect/mask 的能力分析
- Unity 端 shader/material 自动生成链路

后续真正的 shader 编译器会继续加 shape、gradient、blur、glow、mask、text fallback、precomp bake 等转换规则。
