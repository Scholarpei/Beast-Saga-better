# 安全约束与数据流程

用途：修改存读档、版本适配、游戏状态写入、输入或部署前阅读相关章节。返回 [AGENTS.md](../../AGENTS.md)；命令见[开发流程](development.md)。

## 启动指纹与授权

[`Plugin/Plugin.cs`](../../Plugin/Plugin.cs) 对下列文件分别计算 SHA-256，再合并得到游戏指纹：

- `GameAssembly.dll`
- `BeastSaga_Data/il2cpp_data/Metadata/global-metadata.dat`
- `BepInEx/interop/Model.Runtime.dll`
- `BepInEx/interop/Dialogue.dll`

实际允许值由 `Plugin.KnownFingerprint` 定义；指纹不一致时 `Load()` 不安装挂钩。版本适配不能仅改这个常量，必须核对原生类型、字段、签名和相关保存/读取流程并验证。

`CheckpointsActive` 调用 [`SafetyPolicy.CanOperate()`](../../Core/SafetyPolicy.cs)：

| 运行环境 | 启用检查点的条件 |
| --- | --- |
| 已验证沙箱 | `EnableCheckpoints = true` |
| 正式游戏 | `EnableCheckpoints = true` 且游戏根目录存在 `SAFE_DIALOGUE_PRODUCTION_SL_AUTHORIZED` |

授权标记是既有运行机制，不代表每项维护任务都获得了修改正式存档或部署的授权。不要为了让验证通过而自动创建它或跳过检查。

## 检查点与读档流程

存储实现为 [`Core/CheckpointStore.cs`](../../Core/CheckpointStore.cs)，原生数据捕获及验证为 [`Plugin/NativeSave.cs`](../../Plugin/NativeSave.cs)。

```text
状态检查 → .pending 目录 → 写入原生数据 → 完整校验
         → 文件 flush + SHA-256 清单 → 发布为检查点代次

读取目标 → 清单/哈希/原生数据校验 → 复制到独立 load-work
         → 再校验 → 官方整场景读取入口 → 等待原生加载完成
```

- 核心载荷包含 `save.es3` 与 `map-util.es3`；可附缩略图。`manifest.json` 记录格式版本、游戏指纹、文件哈希及可选元数据。
- 无元数据的旧格式为 manifest v1；当前带元数据格式为 v2。修改格式时保留明确的兼容或拒绝策略。
- 选择与备注由仓库管理。手动默认保留 50 份未备注历史，对话前默认保留 10 代；已备注手动检查点额外保留，包括最新的备注档，不占普通滚动额度。
- 创建、选择、清理等操作使用 `DataRootLock`；保留路径、文件名、重解析点、哈希与结构校验，不能用异常吞噬把损坏目标替换成其他存档。
- `NativeSave.Write()` 临时重定向地图工具数据路径，并在 `finally` 恢复原路径和当前存档路径；不可把这些全局路径永久指向检查点目录。
- `NativeSave.Validate()` 检查必需原生键、地图/玩家/队友/时间等状态、对话持久化 JSON，以及内嵌地图数据与 `map-util.es3` 的一致性。
- `LoadPressed()` 校验选中目标并准备工作副本，再经 `Login.LoadSaveDataInGame(1, 1)` 与 `IsolatedLoadPatch` 重定向到该副本。原检查点不应成为读档过程中的可写工作区。
- 面板列表为性能可使用 `List(verifyPayloads: false)`；列表展示成功不代表已完成载荷完整校验。选择、读取或写入保护时仍需相应完整验证。
- 损坏或不兼容的未选中清单应逐代隔离，不得占用健康检查点的保留额度或令整个列表失败；损坏的选中目标仍严格拒绝，不能换成其他读取目标。

## 运行状态与失败处理

- `Plugin.UnsafeReason()` 拦截故障、忙碌、加载未完成、原生保存、战斗/结算、转场及必要运行对象缺失。
- `CheatEditor.EditBlockReason()` 额外要求非对话、玩家可控制、无剧情演出；它复用 `Core/CheatPanel.cs` 的 `CheatEditPolicy`。
- 面板打开与读取允许的状态不完全相同；不能把某个 `permitDialogue` 条件不加区分地套到修改写入。
- 读档等待既要观察加载阶段，也要确认原生完成与游戏可操作；当前超时为 90 秒。致命失败进入 `Faulted`，当前运行的存读档/修改停用，不自动清除故障重试。
- `LoadRequestPolicy` 处理继续游戏的重复请求。改动登录、读取或重试逻辑时，保留请求去重和过期恢复，并测试没有重复触发原生加载。

## 预测必须保持只读

[`Core/CheckPreview.cs`](../../Core/CheckPreview.cs) 与 [`Core/FutureScriptPreview.cs`](../../Core/FutureScriptPreview.cs) 负责受限解析；[`Plugin/Indicators.cs`](../../Plugin/Indicators.cs) 读取实际选项、受支持的当前状态并组织结果。

- 只扩展明确识别、可静态理解的表达式或调用；不执行 Lua、原生奖励或可能产生副作用的条件函数。
- 遇到未知条件、未知控制流或危险边界，保留此前已知结果并说明停止原因；不展示边界后的奖励为确定结果。
- 区分共同结果、可能分支与回溯路径；互斥选项与“返回再选”不能混进当前路径的收益。
- 物品变化以当前背包、穿戴与储存的受支持总量为基数累计；无法读取基数时保持未知。路径内变量、属性、生活经验或队伍变化影响后继条件时，不能继续用实时旧状态给出确定结论。
- 已知与未知后继并存时保留未知终止路径，已知后继只按可能分支展示；整条路径先合计效果再格式化，空停止列表不能视作回溯。
- 修改预测时优先补充真实触发场景的回归，尤其检查未知调用之后、循环、物品不足、按钮复用和悬停状态清理。

## 游戏状态写入

### 角色、点数与一般修改

`CheatEditor.EnsurePreEditCheckpoint()` 使用 `CheatSessionState` 复用本次面板会话的保护检查点。已有检查点会重新验证；创建或验证失败时阻止该次修改。关闭面板、读档等流程重置保护状态。

但当前实现如果 `TryBeginPreEditCheckpoint()` 未取得 reservation，会返回允许；这包含检查点未启用等情况。因此不能宣称所有角色/点数修改都强制先成功建档。相关行为变更要明确需求并增加针对性回归，不能仅修改提示掩盖差异。

已认识但未入队的角色只允许好感修改；点数不能变成低于已用数量的可用值；生活点保持派生只读。批量修改应保留范围检查、当前角色身份核对和失败反馈。

### 物品与装备

[`Plugin/ItemEditor.cs`](../../Plugin/ItemEditor.cs) 的物品写入单独要求检查点启用且授权成功，由 `ItemSubmission` / `ItemEditSession` 确保创建、验证检查点后再执行。

- 任务物品和信件只能浏览；装备删除只接受明确选中的、确认未穿戴且未锁定的实例。
- 每次提交重新验证数量、模板、实例和会话 epoch；原生调用后核对实际数量与实例，状态变化时停止后续写入。
- 原生增删可能触发剧情观察者；发生部分完成时报告实际变化和检查点，不宣称自动撤销了全部效果。
- 关闭面板、读档、玩家/数据对象或场景变化、进入不安全状态时使会话失效。不要跨场景缓存并继续使用 IL2CPP 指针。
- 原生图标异步加载保留预算、完成队列和会话失效处理；不要把每帧全量加载或未验证的旧回调引入 IMGUI。

### 地图选择与输入

传送使用 `MapTeleportController`、地图落点策略与输入 Gate。修改时检查短点/拖动区分、障碍与角色占地、当前地图身份、相机和可见性系统恢复。

`MapTeleportMode.ObservePointer()` 在每帧采样中锁存超过拖动阈值的位移；返回按下位置不能恢复本次点击资格，新按压和关闭模式重置手势历史。

当前成功路径直接设置玩家位置并调用地图更新与加载检查；不等同于跨地图传送 API。取消、失败和退出时也要恢复原相机、Cinemachine 与可见性绑定。

`PanelKeyboardGate` / `PanelInputGate` 是全局输入挂钩：面板、文本输入或地图选择接管输入时，拦截相应游戏输入；接管结束恢复。只改界面控件而不核对 Gate 容易使游戏收到同一次按键或点击。

## 沙箱与正式目录边界

沙箱身份不只依靠一个标记。`Plugin.Load()` 要求同时满足：

1. 根目录有 `SAFE_DIALOGUE_TEST_COPY`；
2. 根目录名为 `Game`，其父目录名为 `sandbox`；
3. 存在 `SaveData/seed.es3`。

有标记但不符合布局时插件抛出异常。不要在正式目录伪造标记；测试桥和抑制 PlayerPrefs/Steam 初始化等补丁依赖已验证的 `TestMode`。

`prepare-sandbox.ps1` 将部分游戏资源、原生二进制、BepInEx core/interop 和运行时建立为硬链接。**不能原地修改这些共享文件**，否则可能同时改坏正式安装。存档和插件待测文件应使用独立副本。

运行启动、同步、清理或部署脚本前确认其源和目标绝对路径。测试记录写入 `verification/`，不要将个人存档、检查点内容或整套游戏资源加入 Git。
