# 开发与验证流程

用途：按需要选择环境检查、自动测试、游戏验证或部署。只读对应章节即可。返回 [AGENTS.md](../../AGENTS.md)；数据保护要求见[安全约束](safety.md)。

## 环境与路径

所有示例从仓库根目录运行，脚本优先使用 PowerShell 7（`pwsh`）；部分脚本调用的 `System.IO.Path.GetRelativePath()` 等 API 不适用于旧版 Windows PowerShell 的 .NET Framework。

```powershell
git status --short
Test-Path -LiteralPath ./tools/sdk/dotnet.exe
./tools/sdk/dotnet.exe --list-sdks
./tools/sdk/dotnet.exe --list-runtimes
```

- 本地脚本硬编码使用 `tools/sdk/dotnet.exe`，此 SDK 未随仓库发布；本次整理时其版本为 `8.0.427`。
- 插件与 Core 目标为 `net6.0`；测试和网页预览目标为 `net8.0`，需要相应 SDK、引用包和运行时。
- 本地 SDK 缺失时，可先用 `Get-Command dotnet` 和 `dotnet --info` 检查系统环境，直接用兼容的系统 `dotnet` 执行构建/测试。脚本不会自动回退到系统 SDK；运行脚本前需解决路径依赖。
- 插件构建还依赖游戏的 BepInEx IL2CPP 环境及已生成的 interop DLL；纯 Core 测试不要求这些游戏程序集。
- 仓库没有统一 solution、lint 命令或已跟踪的 CI 配置。不要用 `dotnet test` 的成功退出代替执行此处的控制台断言程序。

## 自动化检查

### 纯逻辑与受控桩测试

```powershell
./tools/sdk/dotnet.exe run --project Tests/BeastSagaBetter.Tests.csproj -c Release
```

入口为 [`Tests/Program.cs`](../../Tests/Program.cs)，汇总检查点、预测、修改、物品、地图传送和面板回归。末尾应为 `RESULT <通过数>/<总数> passed` 且退出码为 0；以本次输出为准。

`Tests/IndicatorHarness.cs` 提供游戏类型桩。测试项目直接链接 `Plugin/Indicators.cs` 和 `Plugin/FuturePreviewPresentation.cs`，其余多数插件接入逻辑不在这个测试程序中执行。

### 插件构建

```powershell
./tools/sdk/dotnet.exe build Plugin/BeastSagaBetter.csproj -c Release --nologo
```

默认 `GameDir` 是仓库父目录。游戏放在其他位置时可显式传入：

```powershell
./tools/sdk/dotnet.exe build Plugin/BeastSagaBetter.csproj -c Release --nologo '-p:GameDir=D:/Games/Beast Saga'
```

产物为 `Plugin/bin/Release/net6.0/BeastSagaBetter.dll`。Core 源文件被直接编进插件；不要再复制一份 Core 依赖 DLL 充当插件必需文件。

### 部署与备份脚本回归

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/deployment.tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/backup-saves.tests.ps1
```

这两个测试使用临时目录测试部署辅助函数和备份脚本，不执行正式游戏部署。都以 `RESULT ... passed` 和退出码判定；不要为验证文档而运行正式部署或正式备份。

### 根据改动选择验证

| 改动范围 | 至少执行 / 核对 |
| --- | --- |
| 仅维护文档 | Markdown 链接、路径、命令与源码一致性、`git diff --check` |
| Core、预测或修改规则 | 控制台测试；若随插件交付，再构建插件 |
| Unity/Harmony/interop 接入 | 插件构建；相关控制台测试；必要时在沙箱复现，说明运行验证范围 |
| 面板布局、按键、鼠标 | 面板回归测试、插件构建；网页查看布局；游戏中核对文字测量、滚动与输入拦截 |
| 部署 / 备份脚本 | 对应 PowerShell 回归；不能用正式目录试跑替代临时目录测试 |

## 网页布局预览

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/start-panel-preview.ps1 -Port 5080
```

访问 `http://127.0.0.1:5080`，健康检查为 `/api/health`。前提是 `sandbox/Game/BeastSagaBetterData/checkpoints/` 已存在，服务拒绝仓库外的检查点路径。

入口为 `tools/PanelPreview/Program.cs` 和 `wwwroot/index.html`。列表来自检查点仓库，但运行状态含演示值，选择只改服务内存，备注只预览。它不触发游戏保存、读档或传送，也不能证明 Unity IMGUI 的实际布局与输入正常。

默认以前台进程运行并用 Ctrl+C 停止。需要后台启动时隐藏辅助进程窗口，记录 PID 并只停止本次启动的进程。

“战斗演示”选择器覆盖可操作、技能执行中、二次确认及原生处理状态。该页使用明确标注的演示单位与检查点，只改网页内存，不连接游戏战斗接口。

若沙箱仍沿用 `SafeDialogueToolsData/`，上述启动脚本的固定新目录可能不存在；可给预览程序显式传入已有检查点目录，例如设置 `$env:ASPNETCORE_URLS = 'http://127.0.0.1:5087'` 后执行 `./tools/sdk/dotnet.exe run --project tools/PanelPreview/PanelPreview.csproj -c Release --no-launch-profile -- ./sandbox/Game/SafeDialogueToolsData/checkpoints`。不要为了预览迁移或重建沙箱数据。

## 游戏沙箱验证

涉及游戏内操作前先阅读[沙箱身份与硬链接规则](safety.md#沙箱与正式目录边界)。以下脚本有实际副作用，不属于常规文档检查。

### 已有沙箱

确认路径、标记、`SaveData/seed.es3` 与待测 DLL；不要重建或覆盖已有副本。

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/start-test.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/send-test.ps1 -Command state
```

`start-test.ps1` 默认以 `-batchmode -nographics` 隐藏启动；加 `-Graphical` 可见启动并聚焦游戏。它会向已有同路径沙箱进程发 `quit`，然后复制当前 Release DLL、调整沙箱控制台配置并重新启动；不要当作只读检查使用。

测试桥仅在 `Plugin.TestMode` 下轮询 `test-command.txt`，返回 `test-result.txt`；`state` 还生成 `test-state.json`。可用命令以 `Plugin/TestBridge.cs` 的分发为准，发送下一条前确认前一条已完成。

战斗验证命令包括 `combat-state`（阶段、单位、检查点、迭代器和经验/背包快照）、`combat-triggers`、`combat-start-trigger:<当前场景:实例ID>`、`combat-restore:<单位键>`、`combat-restore-all`、`combat-defeat-prepare`、`combat-defeat-confirm` 和 `combat-cancel`。状态写入 `test-combat.json`，触发器写入 `test-combat-triggers.json`。入场命令仅调用已有且已具备玩家/敌方原生条件的真实场景触发器，不构造敌人或结算参数。

`combat-ui-wait` / `combat-ui-close-result` 调用真实战斗 UI 的待机/结果关闭回调，可用于检查面板输入隔断及原生离场；`combat-resolution:宽,高` 仅接受 1280×720、1920×1080、2560×1440、3840×2160。所有这些命令只在已验证沙箱启用，不能成为正式环境远程入口。

### 首次准备沙箱

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/prepare-sandbox.ps1
```

普通模式要求退出游戏，先备份并校验原生 `SaveData`，再建立副本；已有沙箱时拒绝覆盖。`-UseReadOnlySeed` 跳过完整备份，仅读取 `quick1.es3` 作为 seed，不能称为完整备份。沙箱身份还要求 `seed.es3` 存在；普通准备后也要检查此条件，不能只看脚本退出码。

`tools/test-latest-formal-quick.ps1` 会备份并重置沙箱存档及检查点，读取正式目录的最新原生 quick 或较新的手动检查点，再启动沙箱验证。仅在任务确实需要这类同步时使用。

`tools/native.py`、`tools/export-methods.ps1` 和 `tools/compare-snapshots.py` 是按需分析工具；使用前检查脚本内路径和依赖，并遵循根入口的 Python 环境变量要求。

## 正式备份与部署

仅在任务包含这些操作时执行。正式游戏默认位于仓库父目录；核对目标绝对路径并退出游戏。

```powershell
# 备份原生存档、配置及新旧插件数据；输出 SHA-256 清单和 VERIFIED
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/backup-saves.ps1

# 默认跑测试、构建、备份旧插件、部署并校验哈希
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/deploy-production.ps1
```

两个脚本会向本地 `verification/` 写记录，使用前确认该目录存在。部署脚本备份的是插件文件，不等于存档全量备份。

- 正式游戏不在仓库父目录时，部署传 `-GameDir 'D:/Games/Beast Saga'`。注意脚本的 `-GameDir` 用于部署目标，**没有传给内部 `dotnet build`**；此布局应先用相同 `GameDir` 手动构建，再按任务使用 `-SkipBuild`。
- `-PluginDir` 自定义目标并跳过默认的新旧目录迁移；它不会自动证明不存在其他活动插件副本。
- `-SkipTests` / `-SkipBuild` 只用于已有相应验证证据的任务；`-SkipBuild` 仍检查 `Plugin/`、`Core/` 的 `.cs`、`.csproj` 时间是否晚于 DLL。
- 默认迁移拒绝新旧活动插件共存；唯一旧插件目录先完整备份，再迁至 `.disabled/`，DLL 被加禁用后缀。只有目录中仅剩 `.disabled/` 且没有活动 `.dll` 时才视为已禁用。
- 复制使用目标同目录临时文件、SHA-256 校验及替换，部分失败路径有旧目标恢复处理；不要承诺任意阶段失败都能整体事务回滚。异常时先查备份和部署记录。
- 成功记录位于 `verification/production-deployment-<时间戳>.json`，插件备份位于 `backups/<时间戳>-deploy/`。`dist/` 包含插件 DLL，不包含 BepInEx、游戏文件、存档或授权标记。

## 交付前检查

运行与改动匹配的检查，查看退出码与失败输出，然后执行 `git diff --check` 并核对改动范围。报告纯测试、构建、网页和游戏验证各自的证据及限制；不要从其中一种推断另一种已经完成。
