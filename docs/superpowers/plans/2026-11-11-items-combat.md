# Items and Combat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在既有作弊面板实现可显示原生图标的安全物品增删，以及有检查点保障的战斗恢复和原生敌人死亡处理。

**Architecture:** 保留 IMGUI，Core 承担保护策略/会话/搜索，Plugin 适配游戏原生数据、图标借用及战斗接口。原生加载器保持唯一资源所有权；所有有副作用的提交均在主线程 Update 执行，并在实际调用前重新验证状态。物品和战斗按序独立实现，最后统一隔离运行验收。

**Tech Stack:** C# / Core net8.0 / Plugin net6.0 / BepInEx IL2CPP / Unity IMGUI / 游戏原生 FairyGUI 图标加载器。

**Spec:** `docs/superpowers/specs/2026-11-11-items-combat-design.md`

## Global Constraints

- 不使用插件自己的 `AssetBundle.LoadFromFile`、Bundle 缓存、全局加载拦截；不释放游戏拥有的纹理或 Bundle。
- 任务类 `EquipType.任务 == 4`、信类 `ItemType.信 == 5` 只读；装备删除必须选定未穿戴且未锁定熔解的背包实例。
- 物品修改前成功创建并验证本轮检查点，失败不修改；关闭/读档/场景变化使会话失效。
- 战斗必须已有明确选定且完整验证的战前检查点；不在战斗中存读档；不用 `SkillCure(-1f)` 或直接发奖拼凑获胜。
- 数量正整数，普通物品一次最多 999，装备新增一次最多 10；溢出、未知数据和状态全部拒绝。
- 图标只按可见范围加载：每帧最多 2 个新请求，约 2 ms 后不开始下一项，最多 64 个借用；搜索输入约 100 ms 合并。值为初始工程限制，冷加载和 p95 必须实测。
- 所有 Python 命令先设置 `PYTHON_BASIC_REPL=1`，路径使用 `/`；禁止修改正式存档或提前部署正式 DLL。

## Review Focus

- 检查点创建失败后标记误留：用失败→重试→成功→关闭的真实会话测试覆盖。
- 同名装备/不同词条实例误删：用稳定实例键、穿戴与锁定保护及失效选择测试覆盖。
- 搜索/滚动迟到图标串行或漏引用：用世代失效及租借容量/请求预算状态测试覆盖，另在游戏回归。
- 战斗确认后出现动画、援军或下一场战斗：用战斗世代及阶段变化拒绝提交测试覆盖。
- 原生死亡重复奖励：用一次提交状态测试覆盖，并在隔离运行比对经验/掉落和后续剧情。

## Files and ownership

- Core 新增 `CheatItems.cs`、`ItemEditSession.cs`：目录、过滤、数量及特殊物品/装备删除保护、检查点会话。
- Plugin 新增 `ItemEditor.cs`、`ItemIcons.cs`、`ItemPanel.cs`：原生物品适配、图标借用、物品页。
- Core 新增 `CheatCombat.cs`；Plugin 新增 `CombatEditor.cs`、`CombatPanel.cs`：独立战斗安全策略、世代/确认/提交、原生操作与 UI。
- 修改 `Plugin.cs`、`CheatPanel.cs`、`Core/CheatPanel.cs`、`TestBridge.cs`：最少必要的可见策略、Update 调度、分类入口及仅隔离测试诊断。
- Tests 新增独立测试文件，沿用 `Program.cs` 末尾的 Run 注册方式；不拆改既有测试体系。

### Task 1: Core item protection and session contracts

**Files:** Create `Core/CheatItems.cs`, `Core/ItemEditSession.cs`, `Tests/ItemFeatureTests.cs`; modify `Tests/Program.cs` 注册测试。

**Interfaces:** Produce `CheatItemEntry`（稳定 Key、Name、Description、Category、ItemType、Rank、IsEquipment）；`CheatItemCatalog.Filter(...)` 返回稳定目录结果；`ItemEditPolicy.BlockReason(...)` 覆盖种类、操作、数量、拥有数、穿戴与锁定；`ItemEditSession` 仅在真实成功验证 generation 后进入 Protected，支持 Reset 和提交锁。具体 record 的参数用命名参数，Plugin 后续按实际定义调用。

- [ ] 写行为测试：任务/信的添加移除均拒绝但可浏览；删除不超过拥有数、排除穿戴锁定；数量边界及溢出；同名不同键搜索不会混淆。
- [ ] 写会话测试：创建失败后仍未保护且可重试；成功后复用同 generation；重置丢弃旧保护；执行中拒绝重复提交。
- [ ] 运行 `./tools/sdk/dotnet.exe run --project Tests/SafeDialogueTools.Tests.csproj -c Release --nologo`，确认新增断言在实现前失败（缺少新类型时可编译失败，但不得把拼写/命令错误当作 RED）。
- [ ] 最小实现上述类型及行为，再运行整个测试套件；要求所有测试通过。
- [ ] 只提交该任务文件；记录测试数、RED/GREEN 与接口至任务报告。

### Task 2: Native inventory, borrowed icons and item panel

**Files:** Create `Plugin/ItemEditor.cs`, `Plugin/ItemIcons.cs`, `Plugin/ItemPanel.cs`; modify `Plugin/Plugin.cs`, `Plugin/CheatPanel.cs`, `Core/CheatPanel.cs`, `Plugin/TestBridge.cs`; extend `Tests/ItemFeatureTests.cs`，必要时新增纯逻辑图标队列文件及测试。

**Consumes:** Task 1 Core 类型及真实检查点 `Store.Validate` / `NativeSave.Validate`；现有缩放、GUIStyle 与输入隔断。

**Produces:** `ItemEditor.Tick/Reset`（Update 主线程调度，生命周期失效）、`ItemEditor.Request...`（请求而非 OnGUI 内写入）；`RuntimeDriver.DrawItemsPage(Rect)`；`ItemIcons` 的可见项借用/释放及 Tick，运行时诊断记录目录/拥有数/原生图标/执行结果。

- [ ] 先读实际 interop/原生方法，确认普通物品新增/移除和装备独立实例/ID/词条创建、实例删除接口；从 `MyGLoader` 验证构造、url、texture、Dispose 路径。将证据写入任务报告，不猜参数。
- [ ] 增加缺失的边界行为测试并看 RED：过期世代请求不能绑定新目标；超过容量/每帧请求预算不创建新借用；失败检查点不执行原生提交回调。
- [ ] 实现数据目录和背包快照、原生操作、成功后对比实际数量与实例、异常不重试；物品页仅发送请求。
- [ ] 图标通过原生 MyGLoader 生命周期借用，不自己改 NTexture 引用计数；绘制原生 UV/旋转并查 alpha 支持，闭页清理租借。无法正确支持的图像不能以整图集代替。
- [ ] 接入物品分类，展示列表/详情/具体装备实例与禁用原因；关闭/读档/场景/玩家变化清理会话。
- [ ] 扩展只在 TestMode 启用的目录/图标/物品状态诊断和增删测试命令，不开放正式远程入口。
- [ ] 运行完整测试及 `./tools/sdk/dotnet.exe build Plugin/SafeDialogueTools.csproj -c Release --nologo`，要求 0 errors、0 warnings；提交任务文件并报告。

### Regression gate before Task 3: status/archive scaling and scrollbar failure

User supplied 2560×1440 screenshots after manual item testing: status/archive labels overlap, and a repeated native GUI.Box null exception originates in DrawCheatScrollbar. Repair mixed scaled fonts/unscaled geometry, allocate text by measured height and scroll long bodies, rebuild complete styles on resolution/lifetime invalidation, replace thin scrollbar Box calls with bounded rectangle drawing, and contain a draw failure once rather than flood logs. Preserve working cheat-page style and all save/edit safeguards. Detailed test-first brief and evidence live in `.superpowers/sdd/2026-11-11-items-combat/ui-regression-brief.md`; run the complete suite and Release build, review the fixed diff, then coordinate a sandbox restart with the user. Do not replace their running DLL during manual testing.

### Task 3: Guarded native combat operations and panel

**Files:** Create `Core/CheatCombat.cs`, `Tests/CombatFeatureTests.cs`, `Plugin/CombatEditor.cs`, `Plugin/CombatPanel.cs`; modify `Plugin/Plugin.cs`, `Plugin/CheatPanel.cs`, `Core/CheatPanel.cs`, `Plugin/TestBridge.cs`, `Tests/Program.cs`。

**Consumes:** 原有安全存读档入口（不得放宽）、显式读取目标及完整校验；现有面板缩放/输入；Task 2 的 Update 调度模式。

**Produces:** 独立面板可见和战斗可执行策略、`CombatEditor.Tick/Reset/Request...`、`RuntimeDriver.DrawCombatPage(Rect)`、只在隔离版的状态/操作诊断。

- [ ] 先核实原生稳定战斗阶段和 `EnemyHurt(...)` 到死亡结算的真实契约；不要直接调用 `EnemyDies/DieAward/BattleEndUI`。支持类型必须有证据；无法核实就是明确工程阻塞，不能以旧负数恢复兜底。
- [ ] 写并运行 RED 测试：进出战斗/技能动画/死亡队列/剧情/结算禁用；检查点缺失/损坏/目标变化拒绝；确认后战斗世代或敌人集合变化取消；一次提交与忙碌锁。
- [ ] 实现 Core 策略与插件适配，恢复存活我方气血/护甲/真气并检查实际结果；击败按原生流程调度一次，超时不补发奖励。
- [ ] 面板稳定战斗中可打开且可看战斗页；物品/角色/点数以及 F5/F6 继续禁写。战斗目标选择和单位引用每场更新。
- [ ] 完成战斗页、检查点信息、击败确认和结果；扩展 TestMode 诊断。仅经验证的普通战斗开放，未知/特殊类型说明禁用原因。
- [ ] 完整测试和 Release 零警告构建，提交并记录实际支持范围与原生证据。

### Task 4: Isolated runtime verification, corrections and documentation

**Files:** Extend `Plugin/TestBridge.cs` 和必要验收脚本（只在需要时）；修改 `README.md`；保存 `verification/items-combat-*` 运行证据。

**Consumes:** Tasks 1–3 已构建产物、既有 sandbox/Game 和 TestBridge；不部署正式 DLL。

- [ ] 确认没有正式游戏进程；按已有快速测试工具先备份隔离旧现场，再从正式快存只读复制至隔离副本，启动仅隔离游戏。GUI 启动使用工具或获准命令；后台进程按平台规则隐藏。
- [ ] 验证物品图标两个加载先后顺序、Exiangnang、不同类别、图集及跨读档，保存原生 UI 图片和新增日志片段；检查无重复 Bundle 错误。
- [ ] 在隔离副本做普通物品增删、装备唯一实例与受保护实例拒绝、数量边界、检查点失败拒绝及完整读档还原；保存前后状态。
- [ ] 验证战斗恢复含滞息与死亡单位、击败原生结算、经验/掉落不重复、下一场和剧情离场；确认不支持的战斗被禁用。
- [ ] 测量热滚动/搜索/冷加载及至少 20 轮图标租借释放和内存趋势；未达到目标就定位优化，报告实际结果。
- [ ] 运行完整测试和最终 Release 构建、独立代码审阅，修复实质问题并回归；README 只写实测支持与已知限制。
- [ ] 提供实现、测试证据、构建路径和正式部署前的具体变更说明；需要正式部署时再依授权执行。
