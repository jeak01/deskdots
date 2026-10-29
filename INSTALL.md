# 安装 DeskDots

支持 Windows 10/11 x64。目标电脑需要已安装、已登录的 Codex，才能显示账号额度和本机任务。安装包自带编译好的程序、字体、图标和安装 Skill；不需要 Git、Node、Python、API Key 或管理员权限。

## 给目标电脑的 Codex 复制这一句话

> 请在这台 Windows 电脑上安装并配置 DeskDots：https://github.com/jeak01/deskdots 。先阅读仓库的 INSTALL.md，检查安装脚本，再使用最新正式 Release 完成当前用户安装、检测本机 Codex 路径、注册 deskdots MCP、安装配套 Skill、创建开始菜单快捷方式，并按「Agent 独立启动」用 start.ps1 启动，让组件在关闭 Codex 后继续运行；保留已有配置，读取安装、诊断和启动报告，说明需要我在自己的 Dot 会话完成的同步步骤。

Codex 需要有这台电脑的本地执行能力及下载、写入安装目录的权限。目标环境要求的系统权限审批仍需正常完成。不要只把这句话发给一个无法访问目标电脑的云端会话。

给执行安装的 Codex：

1. 从 [Releases](https://github.com/jeak01/deskdots/releases/latest) 获取最新正式版本的 `install.ps1`、`SHA256SUMS.txt` 和版本号。检查脚本，并用 SHA-256 清单核对它。不要复制开发者机器的 `.local`、`dist/DeskDots.connection.json` 或账号数据。
2. 通过 Windows PowerShell 执行下载后的脚本，传入该版本号，例如：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\path\install.ps1' -Version v0.1.0 -NoStart
   ```

   `Bypass` 只应用于这一次进程，不修改电脑的全局执行策略。安装脚本会下载同一版本的 ZIP，校验 SHA-256 和包内文件，再执行安装。Agent 先用 `-NoStart` 完成安装，再统一使用下方的独立启动方式，也兼容尚未带启动脚本的旧 Release。
3. 按「Agent 独立启动」运行 `start.ps1`。读取 `installation.json`、`diagnostics.json` 和 `launch-report.json`，将安装位置、连接结果和待完成步骤告诉用户。不要把“已安装”当作“额度和 Dot 同步都已连通”。

## Agent 独立启动

让组件继续常驻，需要使用本项目的 `start.ps1`，避免继承 Agent 在退出时清理的 Windows 进程组。新版安装脚本已自动调用它；仅启动已安装的组件时，也可以单独运行：

```powershell
# 从 installation.json 读取已安装的 exe；默认使用原来的组件数据目录。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\path\start.ps1'

# 自定义安装或便携目录：显式指定 exe 和原数据目录。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\path\start.ps1' -ExePath 'C:\path\DeskDots.exe' -DataDir 'C:\path\DeskDots-data'
```

脚本位置：新版解压目录和已安装版本目录的根目录、配套 Skill 的 `scripts/start.ps1`；源码用户构建后使用 `dist/start.ps1`。如果正在使用不含此脚本的旧 Release，可从[当前仓库](skills/deskdots/scripts/start.ps1)获取并检查脚本，配合旧版 exe 使用，无需迁移组件数据。用当前安装脚本搭配旧包时，可先加 `-NoStart` 完成安装，再按上面的命令独立启动。

给 Agent 的执行要求：

1. 保留原 `DataDir`。已安装用户从该目录的 `installation.json` 读取 `installedExe`；源码用户使用构建后的 `dist/DeskDots.exe`。
2. 在能访问用户桌面的本机环境执行脚本。若 Agent 的沙箱禁止脱离进程组或访问桌面，按该环境的正常授权流程在沙箱外执行；如果仍被禁止，说明原因，用户也可从 Windows 开始菜单启动。不要静默换成普通 `Start-Process` 或 `cmd /c start` 并宣称成功。
3. 检查命令成功退出，并读取原数据目录的 `launch-report.json`：`started` 应为 `true`，`method` 为 `CREATE_BREAKAWAY_FROM_JOB`，`pid` 对应仍存活的 DeskDots。报告只表示组件已独立启动，账号额度与 Dot 同步仍以各自诊断为准。
4. 如果同一数据目录已有组件实例，脚本会明确拒绝；旧进程无法事后脱离进程组。让用户从托盘退出，或在用户授权重启后正常关闭该实例，再重新运行脚本。

这个方式不会设置开机启动、计划任务或系统服务。组件随 Windows 注销或关机正常结束；常驻期间可以从托盘单独退出。独立启动使用 [Windows 官方的进程组脱离机制](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)，若系统策略禁止该机制，脚本返回非零退出码并记录错误。

## 不使用 Codex：下载后双击安装

1. 在 [最新正式版本](https://github.com/jeak01/deskdots/releases/latest) 下载 `DeskDots-v版本号-windows-x64.zip`，解压整个文件夹。
2. 双击里面的 `install.cmd`。不要直接在 ZIP 内运行。
3. 安装完成后组件会启动，并创建开始菜单的 **DeskDots** 快捷方式。

也可以在解压目录中用 PowerShell 运行 `install.ps1 -PackagePath <解压目录>`。没有 Codex CLI 时仍可安装，安装报告会提示补齐路径和登录状态。

## 只安装 Skill

向目标 Codex 发送：

> 使用 skill-installer，从 https://github.com/jeak01/deskdots/tree/main/skills/deskdots 安装 deskdots 技能。

Skill 安装完成后，在下一轮发送：

> 使用 $deskdots，在这台电脑安装并配置 DeskDots。

如果技能没有出现，重启 Codex 或新建聊天。Skill 包含安装指导和同一份安装脚本；单独安装 Skill 不会立即启动桌面组件。

## 自动配置的内容

- 程序：`%LOCALAPPDATA%\Programs\DeskDots\versions\v版本号`，保留旧版本文件以便回退。
- 组件数据：`%LOCALAPPDATA%\DeskDots`；保留已有窗口位置和其他设置，修改前创建备份。
- Codex：优先使用已保存的组件设置，再检测环境变量、桌面版 CLI 和 PATH。自定义路径可用 `-CodexHome`、`-CodexExe` 指定。
- MCP：通过官方 `codex mcp add` 更新当前 Codex 数据目录中的 `deskdots` 项，修改前备份 `config.toml`。如果同名项目属于其他程序，则保留它并报告冲突。
- Skill：有 `CODEX_HOME` 或显式 `-CodexHome` 时安装到该目录的 `skills/deskdots`；否则使用用户的 `.agents/skills/deskdots`。旧版本 Codex 可通过 `-SkillDirectory` 指定其实际技能目录。
- 开始菜单快捷方式、自测结果、安装报告、连接诊断和独立启动报告。不会自动加入开机启动。

可选参数：`-InstallDir`、`-DataDir`、`-SkipMcp`、`-SkipSkill`、`-NoShortcut`、`-NoStart`、`-LocalOnly`（只做本地诊断）。重复执行可修复安装；新版本会安装到独立版本目录，并尝试正常关闭旧的已安装组件。

## Dot 回复同步的最后一步

额度和任务可自动检测；Dot 回复需要在你自己的 Dot 会话中启用。组件启动后，在设置里点「复制同步说明，发给 Dot 一次」，将说明发给 Dot。也可以读取数据目录里的 `给Dot的同步说明.txt`。如果 Dot 尚未连接此电脑，先在主应用连接。

注册 MCP 不会自动让 Dot 持续发布消息，也不会让安装会话变成 Dot。同步是否持续执行取决于 Dot 的本地执行能力与指令执行情况。详见 [Dot 接入说明](integration/DOT-SETUP.md)。

## 卸载

在托盘退出 DeskDots，删除其开始菜单快捷方式和安装目录。如果启用了 MCP，在对应的 Codex 环境中运行 `codex mcp remove deskdots`。删除配套 Skill 目录即可移除技能；组件数据目录可以保留，确认不再需要后再删除。

Codex 的技能与 MCP 接入方式参考 [OpenAI 官方技能文档](https://learn.chatgpt.com/docs/build-skills) 和 [MCP 文档](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)。
