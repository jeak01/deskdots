---
name: deskdots
description: Install, independently start, configure, update, or diagnose the DeskDots Windows desktop widget from jeak01/deskdots, including local Codex task status, quota display, and its optional Dot reply bridge. Use for DeskDots setup, launch, and repair requests.
---

# DeskDots

Use the bundled PowerShell installer on the Windows computer that will display the widget. The installer downloads a versioned GitHub release, checks SHA-256, runs offline self-tests, installs per user, detects Codex paths, preserves settings, registers the `deskdots` MCP server using the Codex CLI, and creates a Start menu shortcut. No Git, Node, Python, administrator account, or API key is needed for the release package.

## Install or update

Read `scripts/install.ps1` before execution, then run it with Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<skill-directory>\scripts\install.ps1'
```

Use `-CodexHome` and `-CodexExe` when the user's data or executable is in a custom location and automatic detection does not find it. Honor an existing configuration. Other options: `-Version v0.1.0`, `-InstallDir`, `-DataDir`, `-SkillDirectory`, `-PackagePath` (an extracted release), `-SkipMcp`, `-SkipSkill`, `-NoShortcut`, `-NoStart`, and `-LocalOnly` (skip the online quota diagnostic). Do not enable startup at login unless requested.

The script writes an `installation.json` report and a `diagnostics.json` report in the data directory, normally `%LOCALAPPDATA%\DeskDots`. Read them to report the installed version, executable, Codex paths, MCP result, and remaining user actions. Report quota errors accurately; an installed executable alone does not establish a working account connection. If installation fails, fix the reported cause and rerun; do not disable Windows security or replace unrelated Codex settings.

## Start independently

Use the bundled `scripts/start.ps1` to start an installed widget without inheriting the agent's cleanup job:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<skill-directory>\scripts\start.ps1'
```

It resolves `installedExe` from the original data directory's `installation.json`. For custom or source installations, pass `-ExePath '<DeskDots.exe>' -DataDir '<original-data-directory>'`. It also works with older release executables; if an older package has no launcher, install with `-NoStart` and use this script afterward. New packages include the same script beside the executable, and the updated installer invokes it automatically.

Require a successful exit and `started=true` in `<data-directory>/launch-report.json`, then verify its PID is still running. The report confirms widget startup, not working quota or Dot synchronization. If an instance is already running, explain that it must exit from its tray menu or be normally closed with restart authorization before rerunning; a running process cannot be detached retroactively.

Use the local desktop execution environment. If the sandbox blocks job breakaway or desktop access, follow its normal authorization flow to run outside the sandbox. If prohibited, report the error and offer Start menu launch. Do not silently replace this with `Start-Process`, `cmd /c start`, a scheduled task, or a service and claim independent launch. Do not enable startup at login unless requested.

## Dot replies

Task and quota display can work once the local Codex account is ready. Dot replies need a separate action in the user's own Dot conversation. After starting the widget, read its generated `给Dot的同步说明.txt` from the data directory and give that text to the user to send to Dot. If it has not been generated, use the installed `integration/DOT-SETUP.md` instructions. Ask the user to connect the Windows computer in Dot if needed. Do not message Dot from this chat or promise continuous cloud subscription.

Never publish this installation chat, user messages, tool output, or other agents' messages as a Dot reply. MCP registration makes the bridge available; it does not grant this chat permission to impersonate Dot.

## Diagnose

Run the installed executable with `--data-dir '<data-directory>' --diagnose '<report-path>'`. Add `--local-only` to troubleshoot offline. Do not read login tokens or copy another computer's settings. If MCP or the skill does not appear, start a new Codex chat or restart the app; newly installed skills are available on the next turn in supported versions.

Upstream: https://github.com/jeak01/deskdots. Installation guide: https://github.com/jeak01/deskdots/blob/main/INSTALL.md.
