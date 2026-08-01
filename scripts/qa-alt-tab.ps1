param(
    [string]$AppPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowCaptureNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
}
'@

$root = Split-Path -Parent $PSScriptRoot
$app = if ($AppPath) {
    $AppPath
} else {
    Join-Path $root 'src\GpuPreferenceManager.App\bin\Release\net10.0-windows10.0.22000.0\GpuPreferenceManager.exe'
}
$output = if ($OutputPath) { $OutputPath } else { Join-Path $root 'artifacts\qa-alt-tab.png' }
$process = Start-Process -FilePath $app -PassThru
try {
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) { throw "应用提前退出，退出码 $($process.ExitCode)。" }
        if ($process.MainWindowHandle -ne 0) { break }
    }
    if ($process.MainWindowHandle -eq 0) { throw '应用主窗口未出现。' }

    $shell = New-Object -ComObject WScript.Shell
    $null = $shell.AppActivate($process.Id)
    for ($i = 0; $i -lt 3; $i++) {
        [System.Windows.Forms.SendKeys]::SendWait('%{TAB}')
        Start-Sleep -Milliseconds 350
        [System.Windows.Forms.SendKeys]::SendWait('%{TAB}')
        Start-Sleep -Milliseconds 350
    }
    $null = $shell.AppActivate($process.Id)
    $rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        '显卡与系统')
    $adapterTab = $rootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
    if ($null -eq $adapterTab) { throw '找不到“显卡与系统”页签。' }
    $selection = $adapterTab.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $selection.Select()
    Start-Sleep -Milliseconds 500

    $rect = New-Object WindowCaptureNative+RECT
    if (-not [WindowCaptureNative]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
        throw '无法读取窗口矩形。'
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    Write-Output $output
} finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(10000)) {
            Stop-Process -Id $process.Id -Force
        }
    }
}
