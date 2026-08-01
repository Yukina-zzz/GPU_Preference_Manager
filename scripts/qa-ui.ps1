param(
    [string]$AppPath,
    [string]$OutputPath,
    [int]$WindowWidth = 1440,
    [int]$WindowHeight = 840
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GpmUiQaNative
{
    public const int GWL_STYLE = -16;
    public const long WS_MINIMIZEBOX = 0x00020000L;
    public const long WS_MAXIMIZEBOX = 0x00010000L;
    public const long WS_SYSMENU = 0x00080000L;
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@

function Find-ByName($root, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Capture-Window([IntPtr]$handle, [string]$path) {
    $rect = New-Object GpmUiQaNative+RECT
    if (-not [GpmUiQaNative]::GetWindowRect($handle, [ref]$rect)) { throw '无法读取窗口矩形。' }
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$rootPath = Split-Path -Parent $PSScriptRoot
$app = if ($AppPath) { $AppPath } else {
    Join-Path $rootPath 'src\GpuPreferenceManager.App\bin\Release\net10.0-windows10.0.22000.0\GpuPreferenceManager.exe'
}
$output = if ($OutputPath) { $OutputPath } else { Join-Path $rootPath 'artifacts\qa-ui-minimum.png' }
$process = Start-Process -FilePath $app -PassThru
try {
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) { throw "应用提前退出，退出码 $($process.ExitCode)。" }
        if ($process.MainWindowHandle -ne 0) { break }
    }
    if ($process.MainWindowHandle -eq 0) { throw '应用主窗口未出现。' }

    $style = [GpmUiQaNative]::GetWindowLongPtr($process.MainWindowHandle, [GpmUiQaNative]::GWL_STYLE)
    $hasMinimize = (($style -band [GpmUiQaNative]::WS_MINIMIZEBOX) -ne 0)
    $hasMaximize = (($style -band [GpmUiQaNative]::WS_MAXIMIZEBOX) -ne 0)
    $hasSystemMenu = (($style -band [GpmUiQaNative]::WS_SYSMENU) -ne 0)
    if (-not ($hasMinimize -and $hasMaximize -and $hasSystemMenu)) { throw '原生窗口控制按钮样式不完整。' }

    $rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $pendingPage = Find-ByName $rootElement '待处理'
    if ($null -eq $pendingPage) { throw '找不到“待处理”导航项。' }
    $pendingPage.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1200

    $allPage = Find-ByName $rootElement '全部占用'
    if ($null -eq $allPage) { throw '找不到“全部占用”导航项。' }
    $allPage.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1200

    $checkCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::CheckBox)
    $checkbox = $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $checkCondition)
    if ($null -eq $checkbox) { throw '当前程序列表中找不到选择框。' }
    $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 250

    $ignoreButton = Find-ByName $rootElement '忽略'
    if ($null -eq $ignoreButton -or -not $ignoreButton.Current.IsEnabled) {
        throw '勾选程序后“忽略”按钮未立即启用。'
    }
    $preferenceButton = Find-ByName $rootElement '设置偏好 ▾'
    if ($null -eq $preferenceButton -or -not $preferenceButton.Current.IsEnabled) {
        throw '勾选程序后“设置偏好”按钮未立即启用。'
    }
    $selectedText = Find-ByName $rootElement '已选择 1 个'
    if ($null -eq $selectedText) { throw '勾选程序后选择数量未立即更新。' }

    Start-Sleep -Seconds 5
    $textCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $texts = $rootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
    foreach ($textElement in $texts) {
        if ($textElement.Current.Name -like '*GPU 采样不可用*') {
            throw "持续采样期间出现不可用状态：$($textElement.Current.Name)"
        }
    }

    [GpmUiQaNative]::MoveWindow($process.MainWindowHandle, 60, 60, $WindowWidth, $WindowHeight, $true) | Out-Null
    Start-Sleep -Milliseconds 700
    $expandButton = Find-ByName $rootElement '展开进程实例'
    if ($null -eq $expandButton) { throw '找不到多进程展开按钮。' }
    $expandPattern = $expandButton.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $expandRect = $expandButton.Current.BoundingRectangle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
        [int]($expandRect.X + $expandRect.Width / 2),
        [int]($expandRect.Y + $expandRect.Height / 2))
    [GpmUiQaNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [GpmUiQaNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Seconds 3
    $expandedStillVisible = $false
    $expandCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, '展开进程实例')
    $expandButtons = $rootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $expandCondition)
    foreach ($candidate in $expandButtons) {
        $candidatePattern = $candidate.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($candidatePattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) {
            $expandedStillVisible = $true
            break
        }
    }
    if (-not $expandedStillVisible) {
        throw '多进程行未能展开。'
    }
    Capture-Window $process.MainWindowHandle $output
    Write-Output "WindowControls=Minimize,Maximize,Close"
    Write-Output "SelectionImmediate=True"
    Write-Output "SamplingStable=True"
    Write-Output "Screenshot=$output"
} finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(10000)) { Stop-Process -Id $process.Id -Force }
    }
}
