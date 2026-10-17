param(
    [Parameter(Mandatory=$true)][string]$CandidateEvidence,
    [ValidatePattern('^pds-[a-z0-9-]+$')][string]$StageName = 'pds-stage2-final-20261017',
    [ValidatePattern('^PDS-[A-Za-z0-9-]+$')][string]$BundleName = 'PDS-stage2-upload-20261017'
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskProject = Split-Path -Parent $taskRoot
$taskStage = Join-Path $taskProject ('publish\' + $StageName)
$taskZip = Join-Path $taskRoot ('artifacts\' + $BundleName + '.zip')
$taskDestination = Join-Path $taskStage 'desktop'
if ((Test-Path -LiteralPath $taskZip) -or (Test-Path -LiteralPath $taskDestination)) { throw '같은 이름의 소스 ZIP 또는 desktop 준비 폴더가 있습니다. 덮어쓰지 않고 새 이름을 사용하세요.' }
if (-not (Test-Path -LiteralPath (Join-Path $taskStage 'site\index.html')) -or -not (Test-Path -LiteralPath (Join-Path $taskStage 'UPLOAD.md'))) { throw '소개 페이지와 업로드 안내가 준비된 staging 폴더가 필요합니다.' }
$taskCandidate = Get-Content -LiteralPath (Join-Path $CandidateEvidence 'features.json') -Raw | ConvertFrom-Json
$taskTray = Get-Content -LiteralPath (Join-Path $CandidateEvidence 'tray.json') -Raw | ConvertFrom-Json
$taskReopen = Get-Content -LiteralPath (Join-Path $CandidateEvidence 'review-reopen.json') -Raw | ConvertFrom-Json
$taskAnniversary = Get-Content -LiteralPath (Join-Path $CandidateEvidence 'anniversary-reopen.json') -Raw | ConvertFrom-Json
if (-not ($taskCandidate.success -and $taskTray.success -and $taskReopen.success -and $taskAnniversary.success)) { throw '완료된 현재 실행본의 검사 근거가 필요합니다.' }
New-Item -ItemType Directory -Path $taskDestination | Out-Null
$taskExtensions = @('.cs','.csproj','.xaml','.manifest','.ico','.ttf','.txt')
foreach ($taskArea in @('Pds.Core','Pds.Checks','Pds.Desktop')) {
    foreach ($taskFile in (Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskArea) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in $taskExtensions })) {
        $taskRelative = [IO.Path]::GetRelativePath($taskRoot, $taskFile.FullName)
        $taskTarget = Join-Path $taskDestination $taskRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
        Copy-Item -LiteralPath $taskFile.FullName -Destination $taskTarget
    }
}
foreach ($taskFile in (Get-ChildItem -LiteralPath $taskRoot -File | Where-Object { ($_.Extension -in @('.ps1','.slnx','.props','.md','.cmd') -or $_.Name -in @('global.json','.gitignore')) -and $_.Name -notlike 'user-feedback-*' })) {
    Copy-Item -LiteralPath $taskFile.FullName -Destination (Join-Path $taskDestination $taskFile.Name)
}
$taskReadme = Join-Path $taskDestination 'README.md'
$taskReadmeText = [IO.File]::ReadAllText($taskReadme).Replace('(user-feedback-20261016.md)','(../docs/user-check.md)').Replace('(user-feedback-20261017.md)','(../docs/user-check.md)')
[IO.File]::WriteAllText($taskReadme,$taskReadmeText)
foreach ($taskPair in @(@('..\plan.md','plan.md'),@('checkpoint-2.md','checkpoint-2.md'),@('stage2-verification-20261017.md','verification.md'),@('korean-holiday-sources-20261017.md','holiday-sources.md'))) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskPair[0]) -Destination (Join-Path $taskStage ('docs\' + $taskPair[1]))
}
foreach ($taskReport in @('features.json','tray.json','review-reopen.json','anniversary.json','anniversary-reopen.json','meditation-layout.json','fixed-window.json','calendar-geometry.json','presentation.json')) {
    Copy-Item -LiteralPath (Join-Path $CandidateEvidence $taskReport) -Destination (Join-Path $taskStage ('docs\' + $taskReport))
}
$taskForbidden = @(Get-ChildItem -LiteralPath $taskStage -Recurse -File | Where-Object { $_.FullName -match '[\\/](\.git|\.tools|\.nuget|bin|obj|artifacts)[\\/]' -or $_.Name -match '(?i)(\.(db(?:-(?:wal|shm|journal))?|dll|exe|pdb|zip)$|pds-check\.json|settings\.json|auth\.json|^\.env)' })
if ($taskForbidden.Count -ne 0) { throw '제외할 파일이 있으므로 압축을 중지합니다.' }
$taskAppCode = @(Get-ChildItem -LiteralPath $taskDestination -Recurse -File | Where-Object { $_.Extension -in $taskExtensions })
foreach ($taskFile in $taskAppCode) {
    $taskRelative = [IO.Path]::GetRelativePath($taskDestination,$taskFile.FullName)
    if ((Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskRoot $taskRelative) -Algorithm SHA256).Hash) { throw '원본과 소스 복사본의 파일이 다릅니다.' }
}
[IO.Compression.ZipFile]::CreateFromDirectory($taskStage,$taskZip,[IO.Compression.CompressionLevel]::Optimal,$true)
$taskHash = Get-FileHash -LiteralPath $taskZip -Algorithm SHA256
[IO.File]::WriteAllText($taskZip + '.sha256',$taskHash.Hash + '  ' + [IO.Path]::GetFileName($taskZip) + [Environment]::NewLine)
[pscustomobject]@{SourceZip=$taskZip;Bytes=(Get-Item -LiteralPath $taskZip).Length;SHA256=$taskHash.Hash;SourceFilesMatched=$taskAppCode.Count;CurrentUiChecks=$taskCandidate.count;PersonalFiles=0;PublicUploadPerformed=$false} | ConvertTo-Json
