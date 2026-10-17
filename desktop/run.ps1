param([switch]$UsePersonalRecords)
$ErrorActionPreference = 'Stop'
$taskPackaged = Join-Path $PSScriptRoot 'artifacts\PDS-Windows-x64-stage2-legible-20261017\PDS\Pds.Desktop.exe'
$taskApp = Join-Path $PSScriptRoot 'Pds.Desktop\bin\Release\net10.0-windows\Pds.Desktop.exe'
if (Test-Path -LiteralPath $taskPackaged) { $taskApp = $taskPackaged }
elseif (-not (Test-Path -LiteralPath $taskApp)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$taskLocalRuntime = Join-Path $PSScriptRoot '.tools\dotnet'
if (Test-Path -LiteralPath $taskLocalRuntime) { $env:DOTNET_ROOT = $taskLocalRuntime }
if ($UsePersonalRecords) { Start-Process -FilePath $taskApp -WindowStyle Normal }
else {
    $taskPreviewData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PdsFunctionalPreview'
    Start-Process -FilePath $taskApp -ArgumentList ('--data-dir="' + $taskPreviewData + '"') -WindowStyle Normal
}