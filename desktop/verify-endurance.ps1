$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $PSScriptRoot 'Pds.Desktop\bin\Release\net10.0-windows\Pds.Desktop.exe'
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.tools\dotnet'
$taskVerification = Join-Path $PSScriptRoot ('artifacts\verification-endurance-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskVerification | Out-Null
$taskArgument = '--data-dir="' + $taskVerification + '"'
# Only synthetic data, an inactive off-screen window and its own temporary tray icon.
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--verify-endurance', $taskArgument) -WindowStyle Hidden -PassThru
Write-Output "Evidence: $taskVerification"
Write-Output "Verification PID: $($taskProcess.Id)"
while (-not $taskProcess.WaitForExit(1000)) {
    if ((Get-Date) - $taskProcess.StartTime -gt [TimeSpan]::FromMinutes(40)) { throw "검사가 아직 실행 중입니다. 개인 앱은 종료하지 않습니다: $taskVerification" }
}
if ($taskProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $taskVerification 'verification-error.txt') -ErrorAction SilentlyContinue
    Get-Content -LiteralPath (Join-Path $taskVerification 'startup-error.txt') -ErrorAction SilentlyContinue
    throw "상주 검사 실패: $taskVerification"
}
Get-Content -LiteralPath (Join-Path $taskVerification 'endurance.json')
