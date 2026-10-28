# 安装 DeskDots

支持 Windows 10/11 x64。目标电脑需要已安装、已登录的 Codex，才能显示账号额度和本机任务。安装包自带编译好的程序、字体、图标和安装 Skill；不需要 Git、Node、Python、API Key 或管理员权限。

## 给目标电脑的 Codex 复制这一句话

> 请在这台 Windows 电脑上安装并配置 DeskDots：https://github.com/jeak01/deskdots 。先阅读仓库的 INSTALL.md，检查安装脚本，再使用最新正式 Release 完成当前用户安装、检测本机 Codex 路径、注册 deskdots MCP、安装配套 Skill、创建开始菜单快捷方式并启动；保留已有配置，读取安装报告和诊断结果，说明需要我在自己的 Dot 会话完成的同步步骤。

Codex 需要有这台电脑的本地执行能力及下载、写入安装目录的权限。目标环境要求的系统权限审批仍需正常完成。不要只把这句话发给一个无法访问目标电脑的云端会话。

给执行安装的 Codex：

1. 从 [Releases](https://github.com/jeak01/deskdots/releases/latest) 获取最新正式版本的 `install.ps1`、`SHA256SUMS.txt` 和版本号。检查脚本，并用 SHA-256 清单核对它。不要复制开发者机器的 `.local`、`dist/DeskDots.connection.json` 或账号数据。
2. 通过 Windows PowerShell 执行下载后的脚本，传入该版本号，例如：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\path\install.ps1' -Version v0.1.0
   ```

   `Bypass` 只应用于这一次进程，不修改电脑的全局执行策略。安装脚本会下载同一版本的 ZIP，校验 SHA-256 和包内文件，再执行安装。
3. 读取输出中的 `installation.json` 和 `diagnostics.json`。将安装位置、连接结果和待完成步骤告诉用户。不要把“已安装”当作“额度和 Dot 同步都已连通”。

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
- 开始菜单快捷方式、自测结果、安装报告和连接诊断。不会自动加入开机启动。

可选参数：`-InstallDir`、`-DataDir`、`-SkipMcp`、`-SkipSkill`、`-NoShortcut`、`-NoStart`、`-LocalOnly`（只做本地诊断）。重复执行可修复安装；新版本会安装到独立版本目录，并尝试正常关闭旧的已安装组件。

## Dot 回复同步的最后一步

额度和任务可自动检测；Dot 回复需要在你自己的 Dot 会话中启用。组件启动后，在设置里点「复制同步说明，发给 Dot 一次」，将说明发给 Dot。也可以读取数据目录里的 `给Dot的同步说明.txt`。如果 Dot 尚未连接此电脑，先在主应用连接。

注册 MCP 不会自动让 Dot 持续发布消息，也不会让安装会话变成 Dot。同步是否持续执行取决于 Dot 的本地执行能力与指令执行情况。详见 [Dot 接入说明](integration/DOT-SETUP.md)。

## 卸载

在托盘退出 DeskDots，删除其开始菜单快捷方式和安装目录。如果启用了 MCP，在对应的 Codex 环境中运行 `codex mcp remove deskdots`。删除配套 Skill 目录即可移除技能；组件数据目录可以保留，确认不再需要后再删除。

Codex 的技能与 MCP 接入方式参考 [OpenAI 官方技能文档](https://learn.chatgpt.com/docs/build-skills) 和 [MCP 文档](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)。
