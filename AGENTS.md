# Beast-Saga-better：Agent 工作入口

本仓库是《灵兽江湖 / Beast Saga》的 Windows BepInEx IL2CPP 插件，提供对话预测、独立检查点、修改面板和当前地图点击传送。面向玩家的说明见 [README.md](README.md)。

## 按任务读取

先读本文件，再只打开当前任务需要的文档和源码；无需一次加载全部资料。

| 当前任务 | 按需阅读 | 首要源码入口 |
| --- | --- | --- |
| 理解功能、模块边界、配置或查找实现 | [架构与功能地图](docs/agents/architecture.md) | `Plugin/Plugin.cs`、`Core/` |
| 构建、测试、界面预览、沙箱验证或部署 | [开发与验证流程](docs/agents/development.md) | 各 `.csproj`、`Tests/Program.cs`、`tools/` |
| 修改存读档、版本适配、游戏状态写入或输入拦截 | [安全约束与数据流程](docs/agents/safety.md) | `Core/SafetyPolicy.cs`、`Plugin/NativeSave.cs` |

这些文档是阅读导航，不是额外的自动加载入口。源码、项目文件和脚本决定当前行为；文档或旧设计与实现不一致时，先核对实现并明确差异。

## 必须保持的边界

- 开始修改前检查 `git status --short` 和相关 diff；保留已有改动，不重置工作区。
- 对话预测只能静态解析已支持的脚本和条件；未知内容显示未知或停止，不能执行 Lua、奖励函数或有副作用的判定来求结果。
- 不绕过游戏指纹校验、检查点授权、运行状态检查或校验失败后的停用状态。具体条件见[安全约束](docs/agents/safety.md)。
- 检查点与原生 `SaveData` 分开；不拿正式存档做试验。游戏内验证使用 `sandbox/Game`，先确认其身份和数据来源。
- 修改、构建与部署是不同步骤。只有任务包含部署时才运行 `deploy-production.ps1`；部署前退出游戏并使用脚本的备份、哈希校验与迁移流程。
- `backups/`、`sandbox/`、`verification/`、`dist/`、`.local/` 和 `tools/sdk/` 是本地产物或环境。不要提交游戏文件、个人存档、DLL、日志或这些目录的内容。

## 项目结构与改动约定

- `Core/`：不依赖 Unity/BepInEx 的策略、解析、数据存储与几何计算；可脱离游戏测试的规则优先放这里。
- `Plugin/`：Harmony 挂钩、IL2CPP 对象、原生存读档与 Unity IMGUI。`RuntimeDriver` 分布于 `Plugin.cs`、`CheatPanel.cs`、`ItemPanel.cs` 的 partial 类中。
- `Tests/`：自定义控制台断言程序，不是 xUnit/NUnit；用 `dotnet run` 执行。
- `tools/`：部署、备份、沙箱脚本与 `PanelPreview` 网页布局预览。
- 遵循现有 C# 文件级命名空间、nullable 和四空格缩进。`.gitattributes` 要求源码、脚本与 Markdown 使用 LF。
- `Plugin/BeastSagaBetter.csproj` 直接编译链接 `Core/*.cs`；测试和网页预览通过 `ProjectReference` 引用 Core。新增或移动文件时检查这两条构建路径。
- UI 使用中文，操作被拒绝时给出具体原因。修改布局或输入时同时核对几何逻辑、游戏 IMGUI 和网页预览的相关部分。

## 常用验证命令

在仓库根目录使用 PowerShell 7。本机脚本使用未纳入版本控制的 `tools/sdk/dotnet.exe`；缺失时先按[环境说明](docs/agents/development.md#环境与路径)检查 SDK。

```powershell
# 纯逻辑与受控桩测试
./tools/sdk/dotnet.exe run --project Tests/BeastSagaBetter.Tests.csproj -c Release

# 游戏接入层 Release 构建；依赖游戏目录中的 BepInEx/interop
./tools/sdk/dotnet.exe build Plugin/BeastSagaBetter.csproj -c Release --nologo
```

脚本测试、预览、沙箱和部署命令见[开发与验证流程](docs/agents/development.md)。没有仓库级 `.sln`、统一 lint 或自动 CI 定义；不要假设存在这些入口。

## Windows 执行规则

- 路径使用 `/`；文件操作优先用 PowerShell 的 `-LiteralPath`，不要拼接跨 shell 的删除或移动命令。
- 递归删除或移动前，解析并确认绝对目标位于预期工作目录内。特别注意沙箱部分资源是正式游戏文件的硬链接，不可原地修改。
- 所有 Python 调用先设置 `PYTHON_BASIC_REPL=1`：PowerShell 使用 `$env:PYTHON_BASIC_REPL = '1'`，Bash 使用 `PYTHON_BASIC_REPL=1 python ...`。
- 打开文件位置使用 `explorer /select, "path"`；后台进程默认隐藏窗口，需要交互验证时才打开可见窗口。

## Code Review Rules

- 存读档或游戏写入的改动必须保留拒绝条件、完整校验、会话失效与故障处理；重点检查失败后是否仍可能继续写入。
- 对话预测不能把未知路径、回溯路径或互斥分支的奖励当作确定结果。
- 物品移除必须验证具体实例及穿戴/锁定状态；面板关闭、读档、场景或玩家变化后不得沿用旧实例。
- 测试通过只证明相应纯逻辑或桩覆盖。Unity 渲染、Harmony 挂钩、正式存档恢复等结论需要相应运行证据。

## 完成标准

运行与改动匹配的验证，检查最终 diff，并报告实际命令、结果和未验证部分。功能、配置或工作流变化时，同步更新对应维护文档及 README 的相关描述。文档修改检查链接、路径与命令；不要把历史测试数或旧设计当作当前完成证据。
