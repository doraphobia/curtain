## Curtain Gameplay Dashboard（第一阶段）

### 目标
- 将关键玩法调参集中到一个入口：`Tools/Curtain/Gameplay Dashboard`
- 将“设计配置”从分散的 MonoBehaviour Inspector 中逐步迁移到 ScriptableObject
- 保持轻量：不做注册表/扫描器/迁移系统/反射编辑器
- 支持 Play Mode Live Tuning：Dashboard 改值后，已接入 Settings 的系统会立即读取并生效

---

### 文件夹结构（第一阶段）
- `Assets/Curtain/Settings/`
  - 各系统 Settings ScriptableObject（只存配置，不存运行时状态）
- `Assets/Curtain/Editor/Dashboard/`
  - Dashboard 编辑器窗口（只在 Editor 可用）
- `Assets/Curtain/Docs/`
  - 工具与架构说明文档（本文件）

---

### Settings 资产（第一阶段）
Dashboard 会在首次打开时自动创建下列资产（路径：`Assets/Curtain/Settings/*.asset`）：
- `EnemySettings.asset`
- `VisionSettings.asset`
- `DoorSettings.asset`
- `CameraSettings.asset`
- `FootprintSettings.asset`
- `SanitySettings.asset`
- `EconomySettings.asset`
- `AccessibilitySettings.asset`
- `LocalizationSettings.asset`（占位页）
- `DebugSettings.asset`

这些 asset 的脚本定义位于：
- `Assets/Curtain/Settings/*.cs`

---

### Runtime 系统如何读取 Settings（第一阶段策略）
策略是“可选接入”：
- 组件新增一个 `settings` 引用字段（可空）
- 若 `settings != null`：每帧（或关键入口）从 Settings 复制配置到现有字段
- 若为空：保持现有 Inspector 行为不变

这样可以：
- 不破坏 prefab/scene 的既有序列化数据
- 允许逐系统渐进迁移

已接入（第一阶段）：
- `Assets/Scripts/Enemy/EnemyController.cs` → `Curtain.Settings.EnemySettings`
- `Assets/Scripts/Enemy/EnemyVision.cs` → `Curtain.Settings.VisionSettings`
- `Assets/Scripts/RuntimeTileMesh/RuntimeTileMeshFusionDoor.cs` → `Curtain.Settings.DoorSettings`
- `Assets/Scripts/RuntimeTileMesh/FusionModeCameraRig.cs` → `Curtain.Settings.CameraSettings`
- `Assets/Scripts/RuntimeTileMesh/FusionSanityController.cs` → `Curtain.Settings.SanitySettings`
- `Assets/Scripts/RuntimeTileMesh/FusionNightEnemySpawner.cs`（只接入 debug logging）→ `Curtain.Settings.DebugSettings`

---

### 如何添加一个新页面
1. 在 `CurtainGameplayDashboardWindow.Page` 增加枚举项
2. 在 `DrawLeftNav()` 增加导航按钮
3. 在 `switch (currentPage)` 中添加 `DrawXxxPage()` 的分支
4. 用 `DrawCard(title, contents)` 组织分组展示

---

### 如何添加一个新的可调参数（推荐流程）
1. 在对应的 `Settings/*.cs` ScriptableObject 添加字段（仅配置）
2. 在 Dashboard 页 `DrawXxxPage()` 中用 `DrawProp(SerializedObject, "fieldName")` 暴露
3. 在 Runtime 组件中新增“可选接入”：
   - 添加 `public XxxSettings settings;`
   - 在 `ApplySettingsIfPresent()` 里从 asset 拷贝到运行时字段

---

### 注意事项
- Settings 只保存“设计配置”，不要放运行时状态（HP、当前目标、计时器等）
- EditorWindow 代码必须在 `Assets/**/Editor/**` 下
- Runtime 代码不得引用 `UnityEditor`

