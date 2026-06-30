# Fusion 设计状态与待确认项

这份文档用来分开“已确认规则”、“当前临时实现”和“尚待确认的设计”，避免之后把已落地行为误当成最终设计。

## 已确认并落地

- 项目房间 cell 使用整数坐标，一个 cell 映射为 `1x5` Unity 世界单位。
- `RuntimeTileMesh` 内部使用通用 `1x1` logical cell，通过 `tileSize` 映射到项目世界尺寸。
- 连接的 cell 生成一个整体平面，不使用每格一个视觉 prefab 的方案。
- `Player` 是物理位置，负责移动阻挡、当前房间检测和脚步声。
- `Heading Point` 负责 hover、互动、世界选择、放置和管理 UI 指针。
- Enter 在 Player Mode 与 Management Mode 之间切换。管理模式暂停玩家移动，并解除 Heading Point 的玩家半径限制。
- 玩家只能在管理模式选中已有 Block。Hover 缓慢变红，选中缓慢变蓝，移动吸附 logical grid。
- Block 放置后，与重叠或共享正交边的 Block 融合。只接触对角不融合。
- 门只在融合瞬间生成，且共享边必须刚好是连续三个 logical cell。
- 共享边中间 cell 是门洞，两侧安全 cell 是墙并阻挡穿越。
- 临时门是黑色 `0.25x1` 面板，默认开门角度为 90 度，并可在 Inspector 通过 sandbox 的 `doorOpenAngleDegrees` 或生成门的 `openAngleDegrees` 调整。
- 门可由 Heading Point 点击门板切换，也会根据玩家移动方向在碰到当前门板位置时切换状态；打开后的门板再次被玩家撞到时关闭。
- 玩家从门的某一段撞门时，距离撞击点更远的墙体连接端会成为门轴；门打开到玩家移动方向对应的一侧。
- Build Settings 中启用的可玩场景通过 `FusionSceneBootstrapper` 运行时自动接入融合 sandbox、PlayerControl、PauseManager 和相关引用；纯 UI 场景没有 gameplay 表面时不会强行创建融合对象。
- 暂停键统一为空格键。暂停时只渲染并高斯模糊世界相机画面，暂停 UI 和其他 Screen Space Overlay UI 保持在模糊层之上。
- 没有生成门的共享边在融合后可以自由通行。
- 商店使用两次点击确认。购买成功的 Block 立即附着在 Heading Point 上进行吸附放置。
- 管理模式选中当前包含 Player 的 Block 时，Player 保持在 Block 内原有局部位置并跟随 Block 移动。
- Tab 可切换 Block 信息层，每个 Fusion Block 在逻辑包围盒右上角显示一份尺寸与 Type，并使用 Bayon 的 Figma typography 规格。该信息层会自动跟随当前启用的 Fusion camera，并带有可调描边/半透明底以避免黑字在深色区域不可见。
- RedScene 当前有 `FusionBackgroundShaderController` 背景 shader 入口。它挂在 Player Camera 与 Management Camera 上，从 `StageCycleController` 读取 DayTop/DayBottom/BeforeNight/Night 并驱动背景渐变、网格和轻微漂移。
- 右上角 topology/project thumbnail 使用正方形 UI 根尺寸，默认 `220x220`，避免继续按旧长方形比例设计缩略图。

## 当前只是临时实现

- RedScene 的可编辑 Block 使用 `RuntimeTileMesh` 的 `1x1` logical cell。正式房间 cell 仍是项目规则里的 `1x5` 世界单位，二者必须通过 `tileSize`/adapter 映射，不能混成同一个概念。
- Management Camera 当前只会自动包围所有 Block，没有手动平移、缩放、聚焦选中和地图边界控制。
- 商店 UI 是运行时生成的，还不是可视化设计的 prefab。
- 货币暂时复用 `TimeCounterUI` 作为数字钱包，没有持久化。
- 融合后会丢失原始 Block 的独立身份，当前没有拆分、撤销或编辑历史。
- 门和墙是临时生成几何。当前只有墙视觉提供 prefab 替换接口；运行时接入器会补齐场景级 sandbox，但正式可视化 prefab 还未完成。
- 背景 shader 当前只是 RedScene 的环境层接口，不代表最终美术。正式版本可以替换 material、shader 或改成手绘/程序化背景资产。
- 现有四个 Block prefab 是测试预设。还没有完成 Inspector 形状画板，也没有“把当前融合结果保存为新 Block Type”的正式流程。

## 旧窗户/阳光/金钱管线现状

- `HoverScrollColorLerp2D` 是旧窗户/窗帘核心。它需要 `Collider2D` 与 `SpriteRenderer`，hover 由 Heading Point 位置查询触发，滚轮改变开合进度 `ColorProgress`。
- `IsAtColorB` 代表窗户/窗帘达到打开状态；如果绑定了 `WindowPortal`，敌人视野会把这个状态当作是否能从窗户看到玩家的开关。
- 阳光/金钱目前不是独立 sunlight resource，而是 `HoverScrollColorLerp2D.GenerateCurrency()` 直接写入 `TimeCounterUI`。
- 旧规则：DayTop 时 Right 窗口每秒 +2，Left/None 每秒 +1；DayBottom 时 Left 每秒 +2，Right/None 每秒 +1；BeforeNight 和 Night 不产出。
- `FusionGameModeController` 可以绑定 `TimeCounterUI currencySource`，所以 RedScene 未来可以把窗户收益直接作为当前商店 money 的来源。
- `NightWindowEventController` 与 `NightWindowVisitorEventController` 会查找带窗户/窗帘逻辑的对象，并在夜晚事件中影响暂停、UI、sanity、货币或访客事件。它们还没有和 Fusion Block 自动生成窗口绑定。

## 旧敌人攻击管线现状

- 当前敌人系统不是射击管线。它是外部生成、窗户侦测、锁定房间、破门、入室追击、近距离攻击的威胁管线。
- `EnemyController` 状态流为：`SpawnOutside -> SearchOutside -> DetectPlayer -> MoveToExteriorDoor -> BreakingDoor -> EnterRoom -> ChasePlayer -> AttackPlayer`，并带有 `LostPlayer` 和 `SearchLastKnownRoom`。
- `EnemyVision` 负责锥形视野、Line of Sight、窗口采样点检测；`WindowPortal` 决定窗口是否打开、属于哪个 `Room`。
- `Room` / `RoomManager` 是旧房间归属判断入口；`BreakableExteriorDoor` 是敌人破门入口；`DoorBreakProgressBar` 是破门 UI。
- `EnemyFootprintTrace` 与 footprint renderer 负责用脚印表现敌人存在。敌人本体可隐藏，脚印按状态和移动距离生成。
- RedScene/Fusion 目前还没有把生成的 Fusion Block 自动注册成 `Room`，也没有自动从 Fusion 门/墙生成 `WindowPortal` 或 `BreakableExteriorDoor`。因此旧敌人逻辑无法完整理解当前融合房间拓扑。

## 尚待确认的设计

1. 正式 Fusion Block 使用 `1x1` 世界 tile、`1x5` 世界 tile，还是两者都支持并由每张地图的 `tileSize` 决定？
2. “Player 位于 Block 内”是只检查 Player 中心点，还是要求整个玩家碰撞圆都在 Block 里？当前跟随逻辑使用中心点。
3. 管理模式搬运房间时，是否允许 Player 随房间穿过本来的阻挡区？还是放置后 Player 不在最终可行走区时要拒绝放置？
4. Player 所在 Block 融入另一个 Block 后，Player 应保留最终世界位置、保留相对被移动源 Block 的位置，还是移到新整体中心？当前保留搬运后的世界位置。
5. 融合后的 Block 是永久原子化，还是允许拆分或撤销回已购买的原始 Block？
6. 已经带门的融合 Block 再次移动和融合时，旧门是永久保留、重新校验，还是根据当前所有接缝完全重建？
7. 一次融合中如果存在多条刚好三格的共享边，是否可以同时生成多扇门？
8. 正式 Fusion Scene Services 是否还需要做成可拖拽 prefab，还是保留当前运行时自动接入器作为唯一入口？
9. 门的开关状态是否需要跨存档保存？读档后默认打开还是关闭？
10. 正式 Management Camera 需要哪些控制：拖拽平移、边缘平移、滚轮缩放、键盘移动、聚焦选中、地图边界？
11. 摄像机模式切换动画在玩家暂停时应使用 scaled time 还是 unscaled time？
12. 商店第一次点击的确认是否会过期？点击空白处是否取消？还是一直保留到选择另一个商品？
13. 购买但未放置的 Block 是立即扣钱还是放置后扣钱？取消放置是否退款？
14. 价格和库存是全局、每关独立，还是直接保存在每个 Block Type asset 上？
15. 自定义 Block Type 只保存 footprint 和价格，还是也保存 material、动画 tile、门、墙、音频地面类型和 gameplay tag？
16. 自定义 Block 的编辑流程需要在 Edit Mode、Play Mode，还是两者都支持？
17. 保存自定义 Block 时应禁止哪些形状：不连通岛、洞、仅对角连接、重复 cell、或超过尺寸上限？
18. 白色 fallback 是否必须始终存在，还是允许某些具有游戏逻辑的 Block 故意不可见？
19. Fusion 窗户应如何生成：自动放在每个外墙边、通过商店购买 window block、还是手动在 Block prefab/自定义 Block Type 上放置 anchor？
20. Fusion 窗户尺寸是否仍按旧项目 sprite/collider，还是按 logical cell 边缘生成一个标准窗口组件？
21. 阳光作为 money 接口时，是否保留旧的 DayTop/DayBottom 左右方向收益规则，还是改成每个 Window Type 在 Inspector 中配置收益曲线？
22. RedScene 中 `TimeCounterUI` 是否继续作为 money 的唯一来源？还是要拆出正式 `FusionCurrencyWallet`，由窗户、商店和 UI 共同引用？
23. 窗户打开状态是否仍由滚轮开合控制，还是未来要改成 Heading Point 点击/拖拽/自动开关？
24. 敌人生成点应基于 Fusion Block 外部边缘、地图整体外包围、还是专门的 spawn anchor？
25. 敌人要进入 Fusion 房间时，外部门应使用现有 `BreakableExteriorDoor`，还是复用/扩展 Fusion Door？
26. Fusion Room 是否需要一个 adapter，把每个连通 floor 组件注册成旧 `Room`，从而让 Enemy/Window 先兼容旧管线？
27. 背景 shader 未来要表达日夜天气、外部空间、威胁窗口，还是只作为地图编辑模式的视觉背景？
