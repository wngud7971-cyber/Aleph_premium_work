$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $PSScriptRoot 'Pds.Desktop\bin\Release\net10.0-windows\Pds.Desktop.exe'
$taskRuntime = Join-Path $PSScriptRoot '.tools\dotnet'
$env:DOTNET_ROOT = $taskRuntime
$taskVerification = Join-Path $PSScriptRoot ('artifacts\verification-features-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskVerification | Out-Null
$taskArgument = '--data-dir="' + $taskVerification + '"'
# Verification uses its own data and an inactive, off-screen window, without desktop input.
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--verify-features', $taskArgument) -WindowStyle Hidden -PassThru
if (-not $taskProcess.WaitForExit(45000)) {
    Write-Output "화면·모션 검사가 진행 중입니다: $taskVerification"
    if (-not $taskProcess.WaitForExit(45000)) { throw "검증이 아직 실행 중입니다. 개인 앱은 종료하지 않습니다. 검증 폴더: $taskVerification" }
}
if ($taskProcess.ExitCode -ne 0) {
    Get-Content -Encoding UTF8 -LiteralPath (Join-Path $taskVerification 'verification-error.txt') -ErrorAction SilentlyContinue
    Get-Content -Encoding UTF8 -LiteralPath (Join-Path $taskVerification 'startup-error.txt') -ErrorAction SilentlyContinue
    throw "격리 검증 실패: $taskVerification"
}
Get-Content -Encoding UTF8 -LiteralPath (Join-Path $taskVerification 'features.json')
$taskReopened = Start-Process -FilePath $taskExe -ArgumentList @('--verify-review-reopen', $taskArgument) -WindowStyle Hidden -PassThru
if (-not $taskReopened.WaitForExit(45000)) { throw "회고 재실행 검증이 아직 진행 중입니다. 검증 폴더: $taskVerification" }
if ($taskReopened.ExitCode -ne 0) {
    Get-Content -Encoding UTF8 -LiteralPath (Join-Path $taskVerification 'verification-error.txt') -ErrorAction SilentlyContinue
    throw "회고 재실행 검증 실패: $taskVerification"
}
Get-Content -Encoding UTF8 -LiteralPath (Join-Path $taskVerification 'review-reopen.json')
Write-Output "Evidence: $taskVerification"
