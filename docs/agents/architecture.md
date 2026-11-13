# 架构与功能地图

用途：理解当前插件、定位改动位置。构建操作见[开发流程](development.md)，涉及游戏写入时继续读[安全约束](safety.md)。返回 [AGENTS.md](../../AGENTS.md)。

本页按 2026-11-14 的工作区源码整理；功能状态是实现范围，不代表本次已逐项完成游戏内验收。

## 运行与构建关系

```text
游戏 / BepInEx IL2CPP / Harmony
              |
              v
Plugin/  —— 直接编译链接 Core/*.cs ——> BeastSagaBetter.dll（net6.0）
                                 |
Core/（net6.0，独立项目）<—— ProjectReference —— Tests/（net8.0）
                       <—— ProjectReference —— tools/PanelPreview/（net8.0 Web）
```

插件 GUID 为 `scholarpei.beast-saga-better`，入口声明版本为 `0.1.0`，程序集名为 `BeastSagaBetter`。旧名称 `SafeDialogueTools` 仍用于兼容数据、迁移和标记文件，不能全仓盲目替换。

[`Plugin/Plugin.cs`](../../Plugin/Plugin.cs) 的 `Load()` 负责数据根目录选择、沙箱身份、配置、游戏指纹验证、检查点仓库、Harmony 挂钩和 `RuntimeDriver` 注册。指纹不匹配时提前返回，不安装游戏挂钩。

## 功能到源码的导航

| 功能与当前范围 | 纯逻辑 / 存储 | 游戏接入与显示 | 回归入口 |
| --- | --- | --- | --- |
| 选项判定、奖励消耗、任务和分支预测；未知条件与危险脚本处停止 | `Core/CheckPreview.cs`、`Core/FutureScriptPreview.cs` | `Plugin/Indicators.cs`、`Plugin/FuturePreviewPresentation.cs` | `Tests/Program.cs`、`Tests/IndicatorHarness.cs` |
| F5 手动检查点、对话前捕获、F6 读取、备注与保留策略 | `Core/CheckpointStore.cs`、`Core/LoadWorkStore.cs`、`Core/DataRootLock.cs`、`Core/LoadRequestPolicy.cs`、`Core/SafetyPolicy.cs` | `Plugin/Plugin.cs`、`Plugin/NativeSave.cs` | `Tests/Program.cs`、`Tests/CheckpointHistoryTests.cs` |
| Tab 面板：状态、存档库、修改作弊；快速保存、读取、地图传送 | `Core/CommandPanelState.cs`、`Core/PanelGeometry.cs`、`Core/PanelKeySuppression.cs` | `RuntimeDriver`、`Plugin/PanelKeyboardGate.cs`、`Plugin/PanelInputGate.cs` | `Tests/PanelRegressionTests.cs`、`tools/PanelPreview/` |
| 角色属性、武学与生活技能等级、好感、属性与特性点数 | `Core/CheatCharacters.cs`、`Core/CheatInput.cs`、`Core/CheatPanel.cs`、`Core/CheatBatch.cs`、`Core/CheatSession.cs` | `Plugin/CheatEditor.cs`、`Plugin/CheatCharacterSnapshot.cs`、`Plugin/CheatPanel.cs` | `Tests/CheatFeatureTests.cs`、`Tests/CheatSnapshotTests.cs` |
| 物品搜索、分类、背包筛选、原生图标、添加、移除具体装备实例 | `Core/CheatItems.cs`、`Core/ItemEditSession.cs`、`Core/ItemDisplayPolicyCache.cs`、`Core/ItemIconBudget.cs` | `Plugin/ItemEditor.cs`、`Plugin/ItemPanel.cs`、`Plugin/ItemIcons.cs` | `Tests/ItemFeatureTests.cs` |
| 当前地图选点、拖动和边缘移动相机、落点检查、退出恢复相机 | `Core/MapTeleportPolicy.cs`、`Core/MapTeleportCollision.cs` | `Plugin/MapTeleportController.cs`、输入 Gate | `Tests/MapTeleportTests.cs` |
| 钓鱼立即成功、当前场景移动速度调整 | `Core/CheatPolicy.cs`、`Core/CheatPanel.cs` | `Plugin/Plugin.cs` 的钓鱼 Patch、`Plugin/CheatEditor.cs`、`Plugin/CheatPanel.cs` | `Tests/CheatFeatureTests.cs` |
| 普通遭遇战中单人/全队恢复、二次确认击败当前敌方；战斗面板与独立权限 | `Core/CheatCombat.cs`、`Core/CombatPanelGeometry.cs` | `Plugin/CombatEditor.cs`、`Plugin/CombatFlowObserver.cs`、`Plugin/CombatPanel.cs`、输入 Gate | `Tests/CombatFeatureTests.cs` |
| 沙箱测试命令、状态快照、原生读取诊断 | `Core/LoadRequestPolicy.cs` | `Plugin/TestBridge.cs`、`Plugin/SandboxPrefs.cs` | `tools/start-test.ps1`、`tools/send-test.ps1` |

路径均相对仓库根目录。先读取相关行或方法，再扩大搜索范围；无需先通读体积较大的 `Plugin.cs`、`Indicators.cs` 或 `Tests/Program.cs`。

`CheatEditor` 的快照模型、快照组装和好感查找位于 `CheatCharacterSnapshot.cs` partial 中，测试直接链接这份实现。已认识角色的好感记录不依赖已招募队友对象；原生六维、武学和生活值读取仍留在 `CheatEditor.cs`。

## 配置与默认值

配置注册于 `Plugin.Load()`，配置文件为游戏目录下 `BepInEx/config/scholarpei.beast-saga-better.cfg`。

| 分区 | 键 | 默认值与含义 |
| --- | --- | --- |
| `General` | `EnableCheckpoints` | `false`；正式环境还需授权标记，见[授权规则](safety.md#启动指纹与授权) |
| `General` | `EnableIndicators` | `true`；只读判定与未来提示 |
| `Cheats` | `FishingInstantSuccess` | `false` |
| `Checkpoints` | `MaxManualCheckpoints` | `50`；已备注手动检查点额外保留 |
| `Checkpoints` | `MaxPreDialogueCheckpoints` | `10` |
| `Checkpoints` | `CaptureThumbnails` | `true`；截图宽 1280，高度随屏幕比例计算且至少 720 |
| `Keys` | `QuickSave` / `QuickLoad` | `F5` / `F6` |
| `Keys` | `Panel` / `ClosePanel` | `Tab` / `Escape` |
| `Keys` | `PanelKeyMigrated` | `false`；首次加载将旧 Home 面板键迁移为 Tab 并更新内部标记 |

## 数据与目录职责

- 正式游戏根目录默认是仓库父目录；插件项目通过 `GameDir` 属性引用其中的 `BepInEx/core` 和 `BepInEx/interop`。
- 数据默认写入游戏根目录 `BeastSagaBetterData/`。仅当新目录不存在而 `SafeDialogueToolsData/` 存在时沿用旧目录；不是合并两个数据目录。
- `checkpoints/` 存放各代检查点及选择、备注索引；`load-work/` 存放读档时复制出的工作数据。结构和一致性规则见[检查点流程](safety.md#检查点与读档流程)。
- `sandbox/Game/` 是游戏验证副本；`backups/` 是本地备份，`verification/` 是本地验证记录，`dist/` 是部署脚本写出的 DLL。
- `assets/images/` 是 README 展示图片；`docs/agents/` 是共享维护资料。
- 本地 `docs/superpowers/` 中的设计与计划未纳入版本控制，只能作为历史线索，不能作为当前实现或验收证据。

## 当前限制与已知描述差异

以下是查阅源码时发现的边界，修改相关功能时先核对，不能据此自行扩充需求：

1. **战斗适配范围有限。** 已开放普通场景遭遇战的手动操作；特殊目标、对话、倒计时、援军及未知状态禁用。原生流程观察器安装失败时保持禁用。单人遭遇战运行证据不能证明多队员、所有异常状态或剧情战斗已验收。
2. **角色范围有限制。** 已认识但未入队的角色只开放好感；生活点按技能等级推导且只读；不能把未经确认的最终悟性或脚程显示成已知值。
3. **各修改功能的检查点要求不同。** 物品增删强制要求 `CheckpointsActive`；角色/点数的 `EnsurePreEditCheckpoint()` 在未获建档 reservation 时会返回允许。不要描述为“所有修改都强制成功建档”，详见[写入保护](safety.md#游戏状态写入)。
4. **配置帮助文本滞后。** `EnableCheckpoints` 的说明仍写“仅允许隔离副本”，实际 `SafetyPolicy.CanOperate()` 已支持“配置开启 + 正式授权标记”。以策略及调用为准。
5. **传送仅覆盖当前地图。** `MapTeleportController.Teleport()` 设置玩家位置，再调用 `map.UpdateMap()` 和 `map.LoadCheck()`，同时恢复相机相关状态；README 已按直接移动玩家并更新当前地图加载描述。不能推断已覆盖跨地图加载或所有剧情触发器。
6. **网页只是布局预览。** `PanelPreview` 的状态字段部分为演示值，选择保存在服务内存，备注编辑不写回检查点；网页效果不能替代游戏 IMGUI 验证。
7. **测试数量会变化。** `Tests/Program.cs` 汇总所有控制台测试，README 徽标中的数量属于维护时的快照；报告本次实际 `RESULT`。

## 维护本页

新增功能时更新对应导航行；配置变化时更新配置表；解决上述差异后删除或重写相应条目。不要把待办、过程日志、完整反编译内容或本机验证输出堆到根入口。
