$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $PSScriptRoot 'Pds.Desktop\bin\Release\net10.0-windows\Pds.Desktop.exe'
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.tools\dotnet'
$taskVerification = Join-Path $PSScriptRoot ('artifacts\verification-performance-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskVerification | Out-Null
$taskArgument = '--data-dir="' + $taskVerification + '"'
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--verify', $taskArgument) -WindowStyle Hidden -PassThru
# Polling the process handle reads no personal app state and sends no desktop input.
while (-not $taskProcess.WaitForExit(1000)) {
    if ((Get-Date) - $taskProcess.StartTime -gt [TimeSpan]::FromMinutes(3)) { throw "격리 성능 검사가 아직 진행 중입니다: $taskVerification" }
}
if ($taskProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $taskVerification 'verification-error.txt') -ErrorAction SilentlyContinue
    throw "성능 검증 실패: $taskVerification"
}
Get-Content -LiteralPath (Join-Path $taskVerification 'verification.json')
Get-Content -LiteralPath (Join-Path $taskVerification 'statistics-performance.json')
Write-Output "Evidence: $taskVerification"
