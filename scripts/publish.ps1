[CmdletBinding()]
param(
    [string]$Version = '0.9.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\GpuPreferenceManager.App\GpuPreferenceManager.App.csproj'
$releaseRoot = Join-Path $repositoryRoot 'artifacts\release'
$portableDirectory = Join-Path $releaseRoot 'portable'
$singleDirectory = Join-Path $releaseRoot 'single'
$portableZip = Join-Path $releaseRoot "GpuPreferenceManager-$Version-win-x64-portable.zip"
$singleExe = Join-Path $releaseRoot "GpuPreferenceManager-$Version-win-x64-single.exe"
$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) {
    $dotnetCommand.Source
} else {
    Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
}
if (-not (Test-Path $dotnet)) {
    throw '未找到 Windows .NET SDK。请全局安装 .NET 10 SDK 并确认 dotnet.exe 可用。'
}

function Invoke-DotnetPublish {
    param([string]$Profile, [string]$OutputDirectory)

    $arguments = @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:RestoreLockedMode=true',
        "-p:PublishProfile=$Profile",
        '-o', $OutputDirectory
    )
    $process = Start-Process -FilePath $dotnet -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$Profile 发布失败，dotnet 退出码为 $($process.ExitCode)。"
    }
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
Invoke-DotnetPublish -Profile 'Portable' -OutputDirectory $portableDirectory
Invoke-DotnetPublish -Profile 'SingleFile' -OutputDirectory $singleDirectory

if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
Compress-Archive -Path (Join-Path $portableDirectory '*') -DestinationPath $portableZip -CompressionLevel Optimal
Copy-Item (Join-Path $singleDirectory 'GpuPreferenceManager.exe') $singleExe -Force

Get-Item $portableZip, $singleExe | Select-Object FullName, Length, LastWriteTime
