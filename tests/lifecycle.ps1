param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$widgetExe = Join-Path $projectRoot 'dist\DeskDots.exe'
$independentLauncher = Join-Path $projectRoot 'skills\deskdots\scripts\start.ps1'
if (-not (Test-Path -LiteralPath $widgetExe)) { throw 'Build DeskDots before running this probe.' }
$outputRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot ('.local\lifecycle-' + [Guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$nativeSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class DeskDotsLifecycleNative {
    [StructLayout(LayoutKind.Sequential)] public struct BasicLimits {
        public long PerProcessTime, PerJobTime;
        public uint Flags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] public struct IoCounters {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] public struct ExtendedLimits {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct Startup {
        public uint Size;
        public string Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize;
        public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Child {
        public IntPtr Process, Thread;
        public uint Pid, Tid;
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr CreateJobObject(IntPtr security, string name);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetInformationJobObject(IntPtr job, int info, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool QueryInformationJobObject(IntPtr job, int info, out ExtendedLimits limits, uint length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool contained);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out Child child);
    static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public static IntPtr NewJob(bool allowBreakaway) {
        IntPtr job=CreateJobObject(IntPtr.Zero, null);
        Check(job!=IntPtr.Zero);
        var limits=new ExtendedLimits();
        limits.Basic.Flags=0x2000|(allowBreakaway?0x800U:0U);
        try { Check(SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(ExtendedLimits)))); return job; }
        catch { CloseHandle(job); throw; }
    }
    public static uint CurrentFlags() {
        ExtendedLimits limits;
        if (!QueryInformationJobObject(IntPtr.Zero,9,out limits,(uint)Marshal.SizeOf(typeof(ExtendedLimits)),IntPtr.Zero)) return 0;
        return limits.Basic.Flags;
    }
    public static Child Start(string executable, string arguments, bool suspended, bool breakaway) {
        var startup=new Startup { Size=(uint)Marshal.SizeOf(typeof(Startup)), Flags=1, Show=0 };
        Child child;
        Check(CreateProcess(executable,new StringBuilder("\""+executable+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,false,0x08000000U|(suspended?4U:0U)|(breakaway?0x01000000U:0U),IntPtr.Zero,null,ref startup,out child));
        return child;
    }
    public static void Assign(IntPtr job, IntPtr process) { Check(AssignProcessToJobObject(job,process)); }
    public static bool Contains(IntPtr job, IntPtr process) { bool contained; Check(IsProcessInJob(process,job,out contained)); return contained; }
}
'@
$nativePath = Join-Path $outputRoot 'native.cs'
[IO.File]::WriteAllText($nativePath, $nativeSource)
Add-Type -TypeDefinition $nativeSource

# Exercise the shipped launcher rather than duplicating its implementation.
$childPath = Join-Path $outputRoot 'launcher.ps1'
[IO.File]::WriteAllText($childPath, @'
param($NativePath,$WidgetExe,$DataDir,$ResultPath,$LauncherPath,$Mode)
$ErrorActionPreference='Stop'
try {
    Add-Type -Path $NativePath
    $launchedAt=[DateTime]::UtcNow
    if ($Mode -eq 'blocked') {
        $refused=$false
        try { & $LauncherPath -ExePath $WidgetExe -DataDir $DataDir | Out-Null } catch { $refused=$true }
        $launchReport=Get-Content -LiteralPath (Join-Path $DataDir 'launch-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $refused -or $launchReport.started -or -not $launchReport.error) { throw 'Blocked breakaway was not reported as a failure.' }
        @{refused=$true;error=$launchReport.error;inheritedFlags=[DeskDotsLifecycleNative]::CurrentFlags()} | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
        exit 0
    } elseif ($Mode -eq 'independent') {
        @{installedExe=$WidgetExe} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $DataDir 'installation.json') -Encoding UTF8
        & $LauncherPath -DataDir $DataDir | Out-Null
        $launchReport=Get-Content -LiteralPath (Join-Path $DataDir 'launch-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $launchReport.started -or $launchReport.method -ne 'CREATE_BREAKAWAY_FROM_JOB') { throw 'Independent launch did not report success.' }
        $widget=[Diagnostics.Process]::GetProcessById($launchReport.pid)
        $duplicateRefused=$false
        try { & $LauncherPath -ExePath $WidgetExe -DataDir $DataDir | Out-Null } catch { $duplicateRefused=$true }
        $duplicateReport=Get-Content -LiteralPath (Join-Path $DataDir 'launch-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $duplicateRefused -or $duplicateReport.started -or $duplicateReport.error -notlike '*already running*' -or $widget.HasExited) { throw 'An existing instance was not safely refused.' }
    } else {
        $widget=Start-Process -FilePath $WidgetExe -ArgumentList @('--data-dir',('"'+$DataDir+'"')) -WindowStyle Hidden -PassThru
    }
    if (-not $widget.WaitForInputIdle(10000)) { throw 'Widget did not become ready.' }
    # STARTF_USESHOWWINDOW can keep the fixture hidden. Window.Loaded writes this
    # file even then, so readiness must not depend on a visible taskbar window.
    $loadedFile=Join-Path $DataDir 'mcp-config.toml'
    $readyDeadline=[DateTime]::UtcNow.AddSeconds(10)
    $loaded=$false
    do {
        $widget.Refresh()
        $loaded=(Test-Path -LiteralPath $loadedFile) -and (Get-Item -LiteralPath $loadedFile).LastWriteTimeUtc -ge $launchedAt
        if ($loaded) { break }
        Start-Sleep -Milliseconds 100
    } while (-not $widget.HasExited -and [DateTime]::UtcNow -lt $readyDeadline)
    if (-not $loaded) { throw 'Widget did not finish loading its main window.' }
    @{pid=$widget.Id;launcherPid=$PID;ready=(-not $widget.HasExited);window=$widget.MainWindowHandle.ToInt64();inheritedFlags=[DeskDotsLifecycleNative]::CurrentFlags();duplicateRefused=$duplicateRefused} | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    exit 0
} catch {
    if ($null -ne $widget -and -not $widget.HasExited) { $widget.Kill(); [void]$widget.WaitForExit(3000) }
    @{error=$_.Exception.ToString()} | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    exit 1
}
'@)
$homeDir = Join-Path $outputRoot 'empty-codex'
New-Item -ItemType Directory -Path $homeDir -Force | Out-Null
# An existing invalid executable prevents Settings.FindExe from selecting a real CLI.
$offlineCli = Join-Path $outputRoot 'offline-cli.exe'
[IO.File]::WriteAllText($offlineCli, 'offline lifecycle fixture')
$shellExe = (Get-Process -Id $PID).Path
$observations = @()
foreach ($mode in @('ordinary','independent','blocked')) {
    $dataDir = Join-Path $outputRoot ($mode+' data '+[char]0x7EC4+[char]0x4EF6)
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    @{codexHome=$homeDir;codexExe=$offlineCli;compactLayout=1} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataDir 'settings.json') -Encoding UTF8
    $resultPath = Join-Path $dataDir 'launcher-result.json'
    $arguments = '-NoProfile -File "{0}" -NativePath "{1}" -WidgetExe "{2}" -DataDir "{3}" -ResultPath "{4}" -LauncherPath "{5}" -Mode {6}' -f $childPath,$nativePath,$widgetExe,$dataDir,$resultPath,$independentLauncher,$mode
    $job = [DeskDotsLifecycleNative]::NewJob(($mode -ne 'blocked'))
    $child = $null
    $widget = $null
    $launcher = $null
    try {
        $child = [DeskDotsLifecycleNative]::Start($shellExe,$arguments,$true,$false)
        $launcher = [Diagnostics.Process]::GetProcessById($child.Pid)
        [void]$launcher.Handle
        [DeskDotsLifecycleNative]::Assign($job,$child.Process)
        if ([DeskDotsLifecycleNative]::ResumeThread($child.Thread) -eq [uint32]::MaxValue) { throw 'Could not resume test launcher.' }
        if (-not $launcher.WaitForExit(20000)) { throw 'Test launcher timed out.' }
        $ready = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($mode -eq 'blocked' -and $launcher.ExitCode -eq 0 -and $ready.refused) {
            $observation=[ordered]@{mode=$mode;launcherJobFlags=$ready.inheritedFlags;refused=$ready.refused;error=$ready.error}
            $observations+=$observation
            $observation | ConvertTo-Json -Compress | Write-Output
            continue
        }
        if ($launcher.ExitCode -ne 0 -or -not $ready.ready) { throw ('Test widget failed to start (exit '+$launcher.ExitCode+'): '+$ready.error) }
        $widget = [Diagnostics.Process]::GetProcessById($ready.pid)
        $inJob = [DeskDotsLifecycleNative]::Contains($job,$widget.Handle)
        $survivesParentExit = -not $widget.HasExited
        # Closing this handle models shutdown of the owner of a kill-on-close job.
        [void][DeskDotsLifecycleNative]::CloseHandle($job)
        $job = [IntPtr]::Zero
        $survivesJobClose = -not $widget.WaitForExit(2000)
        $observation = [ordered]@{mode=$mode;pid=$widget.Id;launcherPid=$ready.launcherPid;launcherJobFlags=$ready.inheritedFlags;survivesParentExit=$survivesParentExit;inManagedJob=$inJob;survivesJobClose=$survivesJobClose;duplicateRefused=$ready.duplicateRefused}
        $observations += $observation
        $observation | ConvertTo-Json -Compress | Write-Output
        if (-not $survivesParentExit -or $inJob -ne ($mode -eq 'ordinary') -or $survivesJobClose -ne ($mode -eq 'independent')) { throw 'Unexpected lifecycle result.' }
    } finally {
        if ($job -ne [IntPtr]::Zero) { [void][DeskDotsLifecycleNative]::CloseHandle($job) }
        if ($null -ne $launcher) { if (-not $launcher.HasExited) { $launcher.Kill(); [void]$launcher.WaitForExit(3000) }; $launcher.Dispose() }
        if ($null -ne $widget) {
            if (-not $widget.HasExited) { [void]$widget.CloseMainWindow(); if (-not $widget.WaitForExit(3000)) { $widget.Kill(); [void]$widget.WaitForExit(3000) } }
            $widget.Dispose()
        }
        if ($null -ne $child) { [void][DeskDotsLifecycleNative]::CloseHandle($child.Thread); [void][DeskDotsLifecycleNative]::CloseHandle($child.Process) }
    }
}
$report = [ordered]@{passed=$true;testedAt=[DateTime]::UtcNow.ToString('o');widgetExe=$widgetExe;hostJobFlags=[DeskDotsLifecycleNative]::CurrentFlags();checks=$observations}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot 'report.json') -Encoding UTF8
Write-Output ('PASS: lifecycle comparison; report: '+(Join-Path $outputRoot 'report.json'))
