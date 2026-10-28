#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^(latest|v\d+\.\d+\.\d+)$')][string]$Version = 'latest',
    [string]$PackagePath,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\DeskDots'),
    [string]$DataDir = (Join-Path $env:LOCALAPPDATA 'DeskDots'),
    [string]$CodexHome,
    [string]$CodexExe,
    [string]$SkillDirectory,
    [switch]$SkipMcp,
    [switch]$SkipSkill,
    [switch]$NoShortcut,
    [switch]$NoStart,
    [switch]$LocalOnly
)
$ErrorActionPreference = 'Stop'
# A parent PowerShell 7/Python process can leave an incompatible PSModulePath.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Force
if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') {
    throw 'DeskDots requires Windows 10/11 x64.'
}
$repository = 'jeak01/deskdots'
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$workDir = Join-Path $tempParent ('DeskDots-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDir | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)

function Resolve-Within([string]$Root, [string]$Relative) {
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath((Join-Path $prefix $Relative))
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escapes its directory.' }
    return $resolved
}
function Write-Json([string]$Path, $Value) {
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 12), $utf8)
    if (Test-Path -LiteralPath $Path) {
        $previous = $temporary + '.previous'
        [IO.File]::Replace($temporary, $Path, $previous)
        Remove-Item -LiteralPath $previous
    }
    else { [IO.File]::Move($temporary, $Path) }
}
function Backup-File([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $backup = $Path + '.deskdots-backup-' + [Guid]::NewGuid().ToString('N')
        Copy-Item -LiteralPath $Path -Destination $backup
        return $backup
    }
    return $null
}
function Quote-Argument([string]$Value) {
    return '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}
function Invoke-Program([string]$Exe, [string[]]$Arguments, [int]$Timeout = 60000, [string]$ChildCodexHome) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Exe
    $info.Arguments = ($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    if ($ChildCodexHome) { $info.EnvironmentVariables['CODEX_HOME'] = $ChildCodexHome }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($Timeout)) { $process.Kill(); throw ('Timed out running ' + [IO.Path]::GetFileName($Exe)) }
        return @{ ExitCode=$process.ExitCode; Output=$stdout.GetAwaiter().GetResult(); Error=$stderr.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}

try {
    if (-not $PackagePath -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'release.json'))) { $PackagePath = $PSScriptRoot }
    if (-not $PackagePath) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $endpoint = if ($Version -eq 'latest') { 'latest' } else { 'tags/' + $Version }
        $release = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $repository + '/releases/' + $endpoint) -Headers @{ 'User-Agent'='DeskDots-Installer'; Accept='application/vnd.github+json' }
        $Version = $release.tag_name
        if ($Version -notmatch '^v\d+\.\d+\.\d+$') { throw 'Unsupported release version.' }
        $zipName = 'DeskDots-' + $Version + '-windows-x64.zip'
        $zipPath = Join-Path $workDir $zipName
        $checksumPath = Join-Path $workDir 'SHA256SUMS.txt'
        foreach ($download in @(@{Name=$zipName; Path=$zipPath}, @{Name='SHA256SUMS.txt'; Path=$checksumPath})) {
            $asset = @($release.assets | Where-Object { $_.name -eq $download.Name })
            if ($asset.Count -ne 1 -or -not $asset[0].browser_download_url.StartsWith(('https://github.com/' + $repository + '/releases/download/' + $Version + '/'))) { throw ('Missing release asset: ' + $download.Name) }
            Invoke-WebRequest -UseBasicParsing -Uri $asset[0].browser_download_url -OutFile $download.Path
        }
        $match = [regex]::Match([IO.File]::ReadAllText($checksumPath), ('(?m)^([a-fA-F0-9]{64})  ' + [regex]::Escape($zipName) + '\r?$'))
        if (-not $match.Success -or (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ne $match.Groups[1].Value) { throw 'Release SHA-256 verification failed.' }
        $PackagePath = Join-Path $workDir 'extracted'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try { foreach ($entry in $archive.Entries) { [void](Resolve-Within $PackagePath $entry.FullName) } } finally { $archive.Dispose() }
        [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $PackagePath)
    }
    $PackagePath = [IO.Path]::GetFullPath($PackagePath)
    $manifest = Get-Content -LiteralPath (Join-Path $PackagePath 'release.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.repository -ne $repository -or $manifest.version -notmatch '^v\d+\.\d+\.\d+$') { throw 'Invalid DeskDots package manifest.' }
    if ($Version -ne 'latest' -and $Version -ne $manifest.version) { throw 'Package version does not match the requested release.' }
    $Version = $manifest.version
    $files = @($manifest.files.PSObject.Properties)
    foreach ($required in @('DeskDots.exe','assets/icons.json','skills/deskdots/SKILL.md','skills/deskdots/scripts/install.ps1','integration/DOT-SETUP.md')) {
        if ($required -notin $files.Name) { throw ('Incomplete package: ' + $required) }
    }
    foreach ($file in $files) {
        $source = Resolve-Within $PackagePath $file.Name
        if ($file.Value -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.Value) { throw ('Package file verification failed: ' + $file.Name) }
    }
    $selfTest = Invoke-Program (Join-Path $PackagePath 'DeskDots.exe') @('--data-dir',(Join-Path $workDir 'self-test-data'),'--self-test','--result',(Join-Path $workDir 'self-test.json'))
    if (-not (Test-Path -LiteralPath (Join-Path $workDir 'self-test.json'))) { throw 'Self-test did not produce a report. Run the installer with write access to its temporary and installation directories.' }
    $selfTestReport = Get-Content -LiteralPath (Join-Path $workDir 'self-test.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($selfTest.ExitCode -ne 0 -or -not $selfTestReport.passed) { throw ('DeskDots offline self-tests failed: ' + $selfTestReport.error + ' (exit ' + $selfTest.ExitCode + ')') }

    $InstallDir = [IO.Path]::GetFullPath($InstallDir)
    $DataDir = [IO.Path]::GetFullPath($DataDir)
    $settingsPath = Join-Path $DataDir 'settings.json'
    $settings = [ordered]@{}
    if (Test-Path -LiteralPath $settingsPath) {
        $existingSettings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($property in $existingSettings.PSObject.Properties) { $settings[$property.Name] = $property.Value }
    }
    if (-not $CodexHome) { $CodexHome = $settings['codexHome'] }
    if (-not $CodexHome) { $CodexHome = $env:CODEX_HOME }
    if (-not $CodexHome) { $CodexHome = [Environment]::GetEnvironmentVariable('CODEX_HOME','User') }
    if (-not $CodexHome) { $CodexHome = Join-Path $env:USERPROFILE '.codex' }
    $CodexHome = [IO.Path]::GetFullPath($CodexHome)
    $explicitExe = $PSBoundParameters.ContainsKey('CodexExe')
    if (-not $CodexExe -and $settings['codexExe'] -and (Test-Path -LiteralPath ([string]$settings['codexExe']))) { $CodexExe = $settings['codexExe'] }
    if (-not $CodexExe -and $env:CODEX_APP_RUNTIME_CLI_PATH -and (Test-Path -LiteralPath $env:CODEX_APP_RUNTIME_CLI_PATH)) { $CodexExe = $env:CODEX_APP_RUNTIME_CLI_PATH }
    if (-not $CodexExe) {
        $cliRoot = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
        if (Test-Path -LiteralPath $cliRoot) { $CodexExe = (Get-ChildItem -LiteralPath $cliRoot -Filter codex.exe -File -Recurse | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).FullName }
    }
    if (-not $CodexExe) { $CodexExe = (Get-Command codex.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source }
    if ($explicitExe -and -not (Test-Path -LiteralPath $CodexExe -PathType Leaf)) { throw 'The specified Codex executable does not exist.' }
    if ($CodexExe) { $CodexExe = [IO.Path]::GetFullPath($CodexExe) }

    $versionDir = Join-Path $InstallDir ('versions\' + $Version)
    $exePath = Join-Path $versionDir 'DeskDots.exe'
    $running = @(Get-Process -Name DeskDots -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith(($InstallDir.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase) })
    foreach ($instance in $running) {
        if ($instance.Path -ne $exePath -or (Get-FileHash -LiteralPath $instance.Path -Algorithm SHA256).Hash -ne $manifest.files.'DeskDots.exe') {
            [void]$instance.CloseMainWindow()
            if (-not $instance.WaitForExit(5000)) { throw 'Please exit the installed DeskDots from its tray menu, then retry.' }
        }
    }
    foreach ($file in $files) {
        $destination = Resolve-Within $versionDir $file.Name
        if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $file.Value) { continue }
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Resolve-Within $PackagePath $file.Name) -Destination $destination -Force
    }
    Copy-Item -LiteralPath (Join-Path $PackagePath 'release.json') -Destination (Join-Path $versionDir 'release.json') -Force
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    $settingsBackup = Backup-File $settingsPath
    $settings['codexHome'] = $CodexHome
    $settings['codexExe'] = [string]$CodexExe
    Write-Json $settingsPath $settings
    $nextActions = [Collections.Generic.List[string]]::new()
    if (-not $CodexExe) { $nextActions.Add('Install or locate Codex CLI, log in, then rerun with -CodexExe.') }

    $mcpStatus = 'skipped'
    $configBackup = $null
    if (-not $SkipMcp -and $CodexExe) {
        New-Item -ItemType Directory -Path $CodexHome -Force | Out-Null
        $mcp = Invoke-Program $CodexExe @('mcp','get','deskdots','--json') -ChildCodexHome $CodexHome
        $existingMcp = $null
        if ($mcp.ExitCode -eq 0) { $existingMcp = $mcp.Output | ConvertFrom-Json }
        if ($existingMcp -and ([IO.Path]::GetFileName([string]$existingMcp.transport.command) -ine 'DeskDots.exe' -or '--mcp' -notin $existingMcp.transport.args)) {
            $mcpStatus = 'name-conflict'
            $nextActions.Add('An unrelated MCP server named deskdots already exists; resolve its name before enabling this bridge.')
        } else {
            $configBackup = Backup-File (Join-Path $CodexHome 'config.toml')
            $mcp = Invoke-Program $CodexExe @('mcp','add','deskdots','--',$exePath,'--mcp','--data-dir',$DataDir) -ChildCodexHome $CodexHome
            if ($mcp.ExitCode -ne 0) {
                $mcpStatus = 'failed'
                $nextActions.Add('MCP registration failed; inspect Codex configuration and retry. The widget can still display tasks and quota.')
            } else { $mcpStatus = 'configured' }
        }
    }
    if (-not $SkipSkill) {
        if (-not $SkillDirectory) {
            $skillParent = if ($env:CODEX_HOME -or $PSBoundParameters.ContainsKey('CodexHome')) { Join-Path $CodexHome 'skills' } else { Join-Path $env:USERPROFILE '.agents\skills' }
            $SkillDirectory = Join-Path $skillParent 'deskdots'
        }
        $SkillDirectory = [IO.Path]::GetFullPath($SkillDirectory)
        if (Test-Path -LiteralPath $SkillDirectory) {
            $skillBackup = Join-Path $DataDir ('skill-backups\' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $skillBackup -Force | Out-Null
            Copy-Item -LiteralPath $SkillDirectory -Destination $skillBackup -Recurse
        }
        New-Item -ItemType Directory -Path $SkillDirectory -Force | Out-Null
        Copy-Item -Path (Join-Path $versionDir 'skills\deskdots\*') -Destination $SkillDirectory -Recurse -Force
    }
    $shortcutPath = $null
    if (-not $NoShortcut) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'DeskDots.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $exePath
        $shortcut.Arguments = '--data-dir ' + (Quote-Argument $DataDir)
        $shortcut.WorkingDirectory = $versionDir
        $shortcut.Save()
    }
    $diagnosticPath = Join-Path $DataDir 'diagnostics.json'
    $diagnosticArgs = @('--data-dir',$DataDir,'--diagnose',$diagnosticPath)
    if ($LocalOnly) { $diagnosticArgs += '--local-only' }
    $diagnostic = Invoke-Program $exePath $diagnosticArgs
    if ($diagnostic.ExitCode -ne 0) { $nextActions.Add('The diagnostic failed; inspect last-error.txt in the data directory.') }
    else {
        $health = Get-Content -LiteralPath $diagnosticPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($health.taskError) { $nextActions.Add('Local task data is not ready; check CodexHome and open Codex on this computer.') }
        if ($health.quotaError) { $nextActions.Add('Quota is not connected; check the Codex login and network, then refresh the widget.') }
    }
    $nextActions.Add('For Dot replies, send the generated Dot synchronization instructions to your own Dot conversation and connect this computer if needed.')
    $report = [ordered]@{ version=$Version; installedExe=$exePath; dataDir=$DataDir; codexHome=$CodexHome; codexExe=$CodexExe; mcp=$mcpStatus; skillDirectory=$SkillDirectory; shortcut=$shortcutPath; settingsBackup=$settingsBackup; configBackup=$configBackup; diagnostics=$diagnosticPath; nextActions=@($nextActions.ToArray()) }
    Write-Json (Join-Path $DataDir 'installation.json') $report
    if (-not $NoStart) { Start-Process -FilePath $exePath -ArgumentList @('--data-dir',(Quote-Argument $DataDir)) -WindowStyle Hidden | Out-Null }
    $report | ConvertTo-Json -Depth 6
} finally {
    $resolvedWork = [IO.Path]::GetFullPath($workDir)
    if ((Split-Path -Parent $resolvedWork) -ne $tempParent -or (Split-Path -Leaf $resolvedWork) -notmatch '^DeskDots-[a-f0-9]{32}$') { throw 'Refusing to clean an unexpected temporary path.' }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
