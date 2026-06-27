# 工程依赖收集器 / Project Dependency Collector

工程依赖收集器的目标是把“同步一个文件夹”升级为“同步一个可打开的工程副本”。它会在不修改源工程的前提下，生成依赖 manifest、缺失引用报告和后续自动化任务计划。

The project dependency collector upgrades the workflow from “sync a folder” to “sync an openable project copy.” It generates a dependency manifest, missing-reference report, and follow-up automation plan without mutating the source project.

## 当前能力 / Current Capabilities

- 生成 `_CrossPlatformReport/dependency_manifest.json`。
- Generates `_CrossPlatformReport/dependency_manifest.json`.
- 识别 After Effects、Illustrator、Photoshop/PSB、Unity 工程相关文件。
- Detects After Effects, Illustrator, Photoshop/PSB, and Unity project-related files.
- 解析 Unity `.meta` GUID，并扫描 `.unity`、`.prefab`、`.mat`、`.asset` 等 YAML/text 文件中的 GUID 引用。
- Parses Unity `.meta` GUIDs and scans GUID references in `.unity`, `.prefab`, `.mat`, `.asset`, and related YAML/text files.
- 报告 Unity 缺失 GUID，判断工程文件夹是否只是 Unity 项目的子集。
- Reports missing Unity GUIDs to identify when the selected folder is only a subset of a Unity project.
- 扫描文本工程文件中的 macOS/Windows 绝对路径，标记内部源路径、规范化副本路径和外部绝对路径。
- Scans text project files for macOS/Windows absolute paths and classifies original-source paths, normalized-copy paths, and external absolute paths.
- 对小文件做内容 hash，报告“相同内容、不同路径/名称”的重复候选。
- Hashes small files and reports duplicate-content candidates with different paths or names.
- 对 AE `.aep` 生成宿主应用任务，配合 `ae_relink_collect.jsx` 在 After Effects 中收集和重连素材。
- Creates host-application tasks for AE `.aep` files, using `ae_relink_collect.jsx` inside After Effects to collect and relink footage.
- 对 Illustrator/Photoshop 文件只做检测和任务标记，暂不直接重写二进制/容器文件。
- Detects Illustrator/Photoshop files and marks follow-up tasks, but does not directly rewrite binary/container files yet.

## 非法路径处理规则 / Illegal Path Handling Rules

当前系统采用安全副本策略，而不是两端原地改名。

The current system uses a safe-copy strategy, not in-place renaming on both devices.

- 源工程永远不被直接重命名。
- The source project is never renamed directly.
- 系统先创建新的规范化副本，例如 `Motion_1_cross_platform_reviewed`。
- The system first creates a normalized copy, such as `Motion_1_cross_platform_reviewed`.
- Windows/macOS 不兼容文件名会在副本里重命名。
- Windows/macOS-incompatible filenames are renamed inside the copy.
- `rename_map.json` 和 `rename_map.csv` 记录每个旧路径到新路径的映射。
- `rename_map.json` and `rename_map.csv` record every old-path to new-path mapping.
- 大小写冲突会用 `__dup_<hash>` 追加后缀消解。
- Case-insensitive collisions are resolved with a `__dup_<hash>` suffix.
- 源工程旧文件保留不动，作为回滚和人工确认依据。
- Old files in the source project remain untouched for rollback and review.

## 重复内容策略 / Duplicate Content Policy

重复内容不会自动删除。

Duplicate content is never deleted automatically.

- 小文件会基于 SHA-256 生成重复候选组。
- Small files are grouped by SHA-256 when duplicate content is detected.
- 大文件默认不做全量 hash，避免扫描大型素材时拖慢工具。
- Large files are not fully hashed by default to avoid slowing down scans of media-heavy projects.
- 报告中的重复候选只用于人工确认、后续清理或未来的显式“一键去重”功能。
- Duplicate candidates are only for review, cleanup, or a future explicit one-click dedupe action.
- 在工程引用未重写并验证前，系统不应该删除任何同内容旧文件。
- The system should not delete any duplicate-content old file before project references are rewritten and verified.

## Adobe 自动化边界 / Adobe Automation Boundary

AE、AI、PSD 不是普通文本工程格式。安全重写引用需要宿主应用 API。

AE, AI, and PSD are not ordinary text project formats. Safe reference rewriting needs host-application APIs.

- AE `.aep`：当前通过生成 ExtendScript，在 After Effects 内打开副本、重连已复制素材、收集外部素材并保存副本工程。
- AE `.aep`: currently handled by generated ExtendScript inside After Effects, which opens copied projects, relinks copied media, collects external media, and saves the copied project.
- AE `.aepx`：可作为文本/XML 参与路径扫描。
- AE `.aepx`: can participate in text/XML path scanning.
- Illustrator/Photoshop：当前只检测文件并写入任务计划，下一阶段需要 Illustrator/Photoshop 脚本读取 linked/placed assets、执行 save-as 副本和 relink。
- Illustrator/Photoshop: currently detected and added to the task plan; the next phase needs Illustrator/Photoshop scripts to read linked/placed assets, perform save-as copies, and relink.

## 输出文件 / Output Files

- `_CrossPlatformReport/summary.json`：总体扫描摘要。
- `_CrossPlatformReport/summary.json`: overall scan summary.
- `_CrossPlatformReport/rename_map.json`：源路径到安全副本路径的完整映射。
- `_CrossPlatformReport/rename_map.json`: full mapping from source paths to safe-copy paths.
- `_CrossPlatformReport/collisions.json`：大小写/重名冲突消解记录。
- `_CrossPlatformReport/collisions.json`: case/name collision resolution records.
- `_CrossPlatformReport/dependency_manifest.json`：Unity、Adobe、文本路径、重复内容候选和自动化策略。
- `_CrossPlatformReport/dependency_manifest.json`: Unity, Adobe, text-path, duplicate-content, and automation policy data.
- `_CrossPlatformReport/ae_relink_collect.jsx`：After Effects 宿主应用重连和素材收集脚本。
- `_CrossPlatformReport/ae_relink_collect.jsx`: After Effects host script for relinking and footage collection.

## 下一阶段 / Next Phase

- 为 Illustrator 增加 linked/placed asset 收集脚本。
- Add an Illustrator linked/placed asset collection script.
- 为 Photoshop/PSB 增加 linked smart object 检测和收集脚本。
- Add Photoshop/PSB linked smart object detection and collection scripts.
- 在网页控制台中增加依赖 manifest 可视化视图。
- Add a dependency manifest view to the web dashboard.
- 在用户明确确认后，增加可审计的一键去重/归档动作。
- Add auditable one-click dedupe/archive actions only after explicit user confirmation.
