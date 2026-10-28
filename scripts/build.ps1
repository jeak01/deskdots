param([switch]$Test)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler is required.' }
$outputDir = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001',('/out:' + (Join-Path $outputDir 'DeskDots.exe')),('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')),('/resource:' + (Join-Path $taskRoot 'src\App.xaml') + ',DeskDots.App.xaml'))
$compilerArgs += '/resource:' + (Join-Path $taskRoot 'src\Panel.xaml') + ',DeskDots.Panel.xaml'
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $outputDir 'README.md') -Force
New-Item -ItemType Directory -Path (Join-Path $outputDir 'integration') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'integration\DOT-SETUP.md') -Destination (Join-Path $outputDir 'integration\DOT-SETUP.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'assets') -Destination $outputDir -Recurse -Force
if ($env:CODEX_HOME -and $env:CODEX_APP_RUNTIME_CLI_PATH) {
    @{codexHome=$env:CODEX_HOME;codexExe=$env:CODEX_APP_RUNTIME_CLI_PATH} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDir 'DeskDots.connection.json') -Encoding UTF8
}
if ($Test) {
    $resultPath = Join-Path $outputDir 'test-results.json'
    $p = Start-Process -FilePath (Join-Path $outputDir 'DeskDots.exe') -ArgumentList @('--self-test','--result',('"' + $resultPath + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath $resultPath
    if ($p.ExitCode -ne 0) { throw 'Validation failed.' }
}
Get-Item -LiteralPath (Join-Path $outputDir 'DeskDots.exe') | Select-Object FullName,Length,LastWriteTime
