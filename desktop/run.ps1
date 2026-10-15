$ErrorActionPreference = 'Stop'
$taskPackaged = Join-Path $PSScriptRoot 'artifacts\PDS-preview\Pds.Desktop.exe'
if (Test-Path -LiteralPath $taskPackaged) {
    Start-Process -FilePath $taskPackaged -WindowStyle Hidden
    return
}
$taskApp = Join-Path $PSScriptRoot 'Pds.Desktop\bin\Release\net10.0-windows\Pds.Desktop.exe'
if (-not (Test-Path -LiteralPath $taskApp)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$taskLocalRuntime = Join-Path $PSScriptRoot '.tools\dotnet'
if (Test-Path -LiteralPath $taskLocalRuntime) { $env:DOTNET_ROOT = $taskLocalRuntime }
Start-Process -FilePath $taskApp -WindowStyle Hidden
