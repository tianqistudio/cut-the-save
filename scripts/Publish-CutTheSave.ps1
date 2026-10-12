#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$ReleaseVersion,
    [switch]$SkipTests,
    [switch]$RunUpdaterAcceptance
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReleaseVersion) {
    $ReleaseVersion = ([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.ReleaseVersion.'#text'
}
if ($ReleaseVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'A three-component ReleaseVersion is required.' }
if ($Runtime -ne 'win-x64') { throw 'Only the reviewed win-x64 runtime is supported.' }
if (@(([version]$ReleaseVersion).Major, ([version]$ReleaseVersion).Minor, ([version]$ReleaseVersion).Build) | Where-Object { $_ -gt 65534 }) { throw 'Version components must fit assembly metadata.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts\release'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$publishRoot = Join-Path $repoRoot "artifacts\publish\$stamp"
$buildArtifacts = Join-Path $publishRoot 'build'
$appPublish = Join-Path $publishRoot $Runtime
$updaterPublish = Join-Path $publishRoot "updater-$Runtime"
$releaseId = "cts-$Runtime-$stamp"
$validationReceipt = Join-Path $repoRoot "releases\receipts\$releaseId-package.json"
$stage = Join-Path $repoRoot "work\release-stage-$stamp"
$zip = Join-Path $artifactRoot "CutTheSave-$Runtime-$stamp.zip"
$checksumFile = Join-Path $artifactRoot "CutTheSave-$Runtime-$stamp-SHA256.txt"

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Push-Location $repoRoot
try {
    dotnet restore CutTheSave.sln --locked-mode --artifacts-path $buildArtifacts -p:ReleaseVersion=$ReleaseVersion
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    dotnet build CutTheSave.sln --configuration $Configuration --no-restore --artifacts-path $buildArtifacts -p:ReleaseVersion=$ReleaseVersion
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    if (-not $SkipTests) {
        $testProjects = @(
            'tests/CutTheSave.Core.Tests',
            'tests/CutTheSave.Infrastructure.Windows.Tests',
            'tests/CutTheSave.App.Tests'
        )
        foreach ($testProject in $testProjects) {
            dotnet run --project $testProject --configuration $Configuration --no-build --artifacts-path $buildArtifacts -p:ReleaseVersion=$ReleaseVersion -- --isolated
            if ($LASTEXITCODE -ne 0) { throw "Test failed: $testProject" }
        }
    }

    dotnet publish src/CutTheSave.App/CutTheSave.App.csproj --configuration $Configuration --runtime $Runtime -p:DebugType=None -p:DebugSymbols=false --self-contained true --output $appPublish --artifacts-path $buildArtifacts -p:ReleaseVersion=$ReleaseVersion
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    dotnet publish src/CutTheSave.Updater/CutTheSave.Updater.csproj --configuration $Configuration --runtime $Runtime -p:DebugType=None -p:DebugSymbols=false --self-contained true --output $updaterPublish --artifacts-path $buildArtifacts -p:ReleaseVersion=$ReleaseVersion
    if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }

    if ($RunUpdaterAcceptance) {
        & (Join-Path $PSScriptRoot 'Test-CutTheSaveUpdater.ps1') -PublishRoot $publishRoot -Runtime $Runtime
        if ($LASTEXITCODE -ne 0) { throw 'Updater acceptance failed.' }
    }

    Copy-Item -Path (Join-Path $appPublish '*') -Destination $stage -Recurse -Force
    Copy-Item -Path (Join-Path $updaterPublish 'CutTheSave.Updater.exe') -Destination $stage -Force
    Copy-Item -Path @(
        (Join-Path $repoRoot 'README.md'),
        (Join-Path $repoRoot 'README.en.md'),
        (Join-Path $repoRoot 'CutTheSave.update.ini.example'),
        (Join-Path $repoRoot 'PRIVACY.md'),
        (Join-Path $repoRoot 'SECURITY.md'),
        (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md'),
        (Join-Path $repoRoot 'CHANGELOG.md'),
        (Join-Path $repoRoot 'RELEASE_CHECKLIST.md'),
        (Join-Path $repoRoot 'FIELD_ACCEPTANCE.md'),
        (Join-Path $repoRoot 'UPDATE_CONFIGURATION.md'),
        (Join-Path $repoRoot 'docs/guide.md')
    ) -Destination $stage -Force

    # Keep repository-local README links usable in the offline package. Do not
    # include maintainer-local completion audits, receipts or source archives.
    $docsStage = Join-Path $stage 'docs'
    New-Item -ItemType Directory -Path $docsStage -Force | Out-Null
    foreach ($document in @('guide.md', 'appearance.md', 'rtss-safety.md', 'write-safety.md', 'backup-safety.md')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "docs/$document") -Destination $docsStage
    }

    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $appPublish 'CutTheSave.dll')).Version
    if ($assemblyVersion -ne [version]($ReleaseVersion + '.0')) { throw 'Published assembly version mismatch.' }
    $metadata = @{schema_version=1; version=$ReleaseVersion; runtime=$Runtime; release_id=$releaseId}
    [IO.File]::WriteAllText((Join-Path $stage 'release-metadata.json'), ($metadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $lines = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\','/')
        "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash )  $relative"
    })
    $lines += "$( (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash )  $([IO.Path]::GetFileName($zip))"
    [System.IO.File]::WriteAllLines($checksumFile, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
    & (Join-Path $PSScriptRoot 'Verify-CutTheSaveRelease.ps1') -Zip $zip -Checksums $checksumFile -PublishRoot $publishRoot -ReleaseId $releaseId -ReceiptPath $validationReceipt
    if ($LASTEXITCODE -ne 0) { throw 'Release verification failed.' }
    Write-Host "Release ID: $releaseId"
    Write-Host "Published: $appPublish"
    Write-Host "Created: $zip"
    Write-Host "Checksums: $checksumFile"
}
finally {
    Pop-Location
}
