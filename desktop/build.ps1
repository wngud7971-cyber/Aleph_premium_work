param([switch]$Package)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) {
    $taskInstalled = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $taskInstalled) { throw '.NET 10 SDK가 필요합니다. desktop/README.md를 확인하세요.' }
    $taskDotnet = $taskInstalled.Source
}
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.nuget\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location -LiteralPath $taskRoot
try {
    & $taskDotnet build 'Pds.slnx' -c Release
    if ($LASTEXITCODE -ne 0) { throw '빌드에 실패했습니다.' }
    & $taskDotnet run --project 'Pds.Checks/Pds.Checks.csproj' -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw '저장 검증에 실패했습니다.' }
    if ($Package) {
        & $taskDotnet publish 'Pds.Desktop/Pds.Desktop.csproj' -c Release -r win-x64 --self-contained true -o 'artifacts/PDS-preview'
        if ($LASTEXITCODE -ne 0) { throw '실행 파일 묶음 생성에 실패했습니다.' }
    }
}
finally { Pop-Location }
