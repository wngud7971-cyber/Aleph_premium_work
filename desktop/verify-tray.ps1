$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $PSScriptRoot 'Pds.Desktop\bin\Release\net10.0-windows\Pds.Desktop.exe'
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.tools\dotnet'
$taskVerification = Join-Path $PSScriptRoot ('artifacts\verification-tray-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskVerification | Out-Null
$taskArgument = '--data-dir="' + $taskVerification + '"'
# Own off-screen test window and temporary icon; no desktop input or personal app changes.
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--verify-tray', $taskArgument) -WindowStyle Hidden -PassThru
if (-not $taskProcess.WaitForExit(30000)) { throw "트레이 검사가 아직 진행 중입니다: $taskVerification" }
if ($taskProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $taskVerification 'verification-error.txt') -ErrorAction SilentlyContinue
    Get-Content -LiteralPath (Join-Path $taskVerification 'startup-error.txt') -ErrorAction SilentlyContinue
    throw "트레이 검사 실패: $taskVerification"
}
Get-Content -LiteralPath (Join-Path $taskVerification 'tray.json')
Write-Output "Evidence: $taskVerification"
