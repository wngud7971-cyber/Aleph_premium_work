param([ValidatePattern('^PDS-[A-Za-z0-9-]+$')][string]$BundleName = 'PDS-Windows-x64-stage2-legible-20261017')
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) {
    $taskInstalled = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $taskInstalled) { throw '.NET 10 SDK가 필요합니다.' }
    $taskDotnet = $taskInstalled.Source
}
$taskBundle = Join-Path $taskRoot ('artifacts\' + $BundleName)
$taskZip = $taskBundle + '.zip'
if ((Test-Path -LiteralPath $taskBundle) -or (Test-Path -LiteralPath $taskZip)) { throw '같은 이름의 실행 묶음이 이미 있습니다. 덮어쓰지 않습니다.' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.nuget\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location -LiteralPath $taskRoot
try {
    & $taskDotnet publish 'Pds.Desktop/Pds.Desktop.csproj' -c Release -r win-x64 --self-contained true -o (Join-Path $taskBundle 'PDS')
    if ($LASTEXITCODE -ne 0) { throw '실행 묶음 생성 실패. 기존 배포본은 변경하지 않습니다.' }
    $taskLauncher = "@echo off`r`nstart `"`" `"%~dp0PDS\Pds.Desktop.exe`" --data-dir=`"%LOCALAPPDATA%\PdsFunctionalPreview`"`r`n"
    [IO.File]::WriteAllText((Join-Path $taskBundle 'START-TEST.cmd'), $taskLauncher, [Text.Encoding]::ASCII)
    Copy-Item -LiteralPath (Join-Path $taskRoot 'next-prototype-checklist.md') -Destination (Join-Path $taskBundle 'READ-ME.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot '..\plan.md') -Destination (Join-Path $taskBundle 'plan.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'stage2-verification-20261017.md') -Destination (Join-Path $taskBundle 'verification.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'checkpoint-2.md') -Destination (Join-Path $taskBundle 'checkpoint-2.md')
    Write-Output "Candidate: $taskBundle"
    Write-Output '실행 파일 검사 후 ZIP은 별도로 생성합니다.'
}
finally { Pop-Location }
