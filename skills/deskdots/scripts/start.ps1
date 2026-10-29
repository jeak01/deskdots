#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ExePath,
    [string]$DataDir = (Join-Path $env:LOCALAPPDATA 'DeskDots')
)
$ErrorActionPreference = 'Stop'
$DataDir = [IO.Path]::GetFullPath($DataDir)
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
$reportPath = Join-Path $DataDir 'launch-report.json'
$widget = $null
$report = [ordered]@{started=$false;method='CREATE_BREAKAWAY_FROM_JOB';pid=$null;exe=$null;dataDir=$DataDir;error=''}

function Quote-Argument([string]$Value) {
    return '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}

try {
    if (-not $ExePath) {
        $installedReport = Join-Path $DataDir 'installation.json'
        if (Test-Path -LiteralPath $installedReport) {
            $installed = Get-Content -LiteralPath $installedReport -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($installed.installedExe -and (Test-Path -LiteralPath $installed.installedExe -PathType Leaf)) { $ExePath = $installed.installedExe }
        }
        if (-not $ExePath) {
            foreach ($candidate in @((Join-Path $PSScriptRoot 'DeskDots.exe'), (Join-Path $PSScriptRoot '..\..\..\dist\DeskDots.exe'))) {
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $ExePath = $candidate; break }
            }
        }
    }
    if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw 'DeskDots.exe was not found. Install/build DeskDots first, or pass -ExePath.' }
    $ExePath = [IO.Path]::GetFullPath($ExePath)
    $report.exe = $ExePath

    # A running process cannot be detached retroactively. Match the widget's
    # per-data-directory mutex instead of silently activating an attached instance.
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $scope = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($DataDir.ToLowerInvariant()))).Replace('-','').Substring(0,20) }
    finally { $sha.Dispose() }
    $existing = $null
    try { $existing = [Threading.Mutex]::OpenExisting(('Local\DeskDotsWidget-' + $scope)) }
    catch [Threading.WaitHandleCannotBeOpenedException] { }
    if ($null -ne $existing) {
        try {
            $held = $false
            try { $held = $existing.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
            if (-not $held) { throw 'DeskDots is already running for this data directory. Exit it from its tray menu, then run this independent launcher again.' }
            $existing.ReleaseMutex()
        } finally { $existing.Dispose() }
    }

    if (-not ('DeskDotsIndependentStart' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class DeskDotsIndependentStart {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Startup {
        public uint Size;
        public string Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize;
        public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct Child {
        public IntPtr Process, Thread;
        public uint Pid, Tid;
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out Child child);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    public static bool HasVisibleWidget(int pid) {
        bool found=false;
        EnumWindows((window, parameter)=> {
            uint owner; GetWindowThreadProcessId(window,out owner);
            if (owner!=(uint)pid || !IsWindowVisible(window)) return true;
            var title=new StringBuilder(128); GetWindowText(window,title,title.Capacity);
            if (title.ToString()!="DeskDots") return true;
            found=true; return false;
        },IntPtr.Zero);
        return found;
    }
    public static int Start(string executable, string command, string directory) {
        // No inherited handles or console; let the requested WPF widget display
        // normally. SW_HIDE would leave the actual widget invisible.
        var startup = new Startup { Size=(uint)Marshal.SizeOf(typeof(Startup)) };
        Child child;
        if (!CreateProcessW(executable,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,false,0x01000000U|0x08000000U,IntPtr.Zero,directory,ref startup,out child)) {
            int error=Marshal.GetLastWin32Error();
            throw new Win32Exception(error,"Independent launch failed. The host may prohibit job breakaway or desktop access. Run outside the agent sandbox or use the Start menu. Win32 error: "+error);
        }
        try { return (int)child.Pid; }
        finally { CloseHandle(child.Thread); CloseHandle(child.Process); }
    }
}
'@
    }
    $command = (Quote-Argument $ExePath) + ' --data-dir ' + (Quote-Argument $DataDir)
    $widgetId = [DeskDotsIndependentStart]::Start($ExePath,$command,([IO.Path]::GetDirectoryName($ExePath)))
    $widget = [Diagnostics.Process]::GetProcessById($widgetId)
    [void]$widget.Handle
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $widget.Refresh()
        if ($widget.HasExited) { throw ('DeskDots exited during startup (exit '+$widget.ExitCode+'). Check last-error.txt; another instance may have started concurrently.') }
        if ([DeskDotsIndependentStart]::HasVisibleWidget($widgetId)) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not [DeskDotsIndependentStart]::HasVisibleWidget($widgetId)) { throw 'DeskDots did not create a visible window within 15 seconds. Check desktop access and last-error.txt.' }
    $report.started = $true
    $report.pid = $widget.Id
} catch {
    $report.error = $_.Exception.Message
    # Only clean up the exact process created by this invocation on failed startup.
    if ($null -ne $widget -and -not $widget.HasExited) { $widget.Kill(); [void]$widget.WaitForExit(3000) }
} finally {
    if ($null -ne $widget) { $widget.Dispose() }
}
$temporary = $reportPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary,($report | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $reportPath) {
        $previous = $temporary + '.previous'
        [IO.File]::Replace($temporary,$reportPath,$previous)
        Remove-Item -LiteralPath $previous
    }
    else { [IO.File]::Move($temporary,$reportPath) }
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
$report | ConvertTo-Json -Depth 4
if (-not $report.started) { throw $report.error }
