param([ValidatePattern('^PDS-[A-Za-z0-9-]+$')][string]$BundleName = 'PDS-Windows-x64-stage2-legible-20261017')
$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $PSScriptRoot ('artifacts\' + $BundleName + '\PDS\Pds.Desktop.exe')
$taskVerification = Join-Path $PSScriptRoot ('artifacts\verification-candidate-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskVerification | Out-Null
$taskArgument = '--data-dir="' + $taskVerification + '"'
# The published self-contained host runs with its own runtime and separate fixture data.
$env:DOTNET_ROOT = $null
foreach ($taskMode in @('--verify-features','--verify-review-reopen','--verify-tray')) {
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @($taskMode, $taskArgument) -WindowStyle Hidden -PassThru
    # The expanded feature suite contains several hundred independent UI checks.
    # Yield a progress line every 45 seconds; retain a bounded three-minute limit.
    for ($taskAttempt=0; $taskAttempt -lt 4; $taskAttempt++) {
        if ($taskProcess.WaitForExit(45000)) { break }
        Write-Output "화면·모션 검사가 진행 중입니다: $taskVerification"
    }
    if (-not $taskProcess.HasExited) { throw "실행 묶음 검사가 아직 진행 중입니다: $taskVerification" }
    if ($taskProcess.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $taskVerification 'verification-error.txt') -ErrorAction SilentlyContinue
        Get-Content -LiteralPath (Join-Path $taskVerification 'startup-error.txt') -ErrorAction SilentlyContinue
        throw "실행 묶음 검사 실패: $taskVerification"
    }
}
$taskReport = Get-Content -LiteralPath (Join-Path $taskVerification 'features.json') -Raw | ConvertFrom-Json
$taskReopen = Get-Content -LiteralPath (Join-Path $taskVerification 'review-reopen.json') -Raw | ConvertFrom-Json
$taskTray = Get-Content -LiteralPath (Join-Path $taskVerification 'tray.json') -Raw | ConvertFrom-Json
$taskAnniversaries = Get-Content -LiteralPath (Join-Path $taskVerification 'anniversary-reopen.json') -Raw | ConvertFrom-Json
if (-not $taskAnniversaries.success) { throw '기념일 재실행 검사 실패' }
@{anniversariesReopened=$taskAnniversaries.success;success=$taskReport.success;checks=$taskReport.count;reopen=$taskReopen.success;tray=$taskTray.success;trayRegistered=$taskTray.registered;personalDataUsed=$false;evidence=$taskVerification} | ConvertTo-Json
