#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Zip,
    [Parameter(Mandatory)][string]$Checksums,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$BuildId = ('cts-setup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')),
    [string]$CompilerRoot,
    [string]$SevenZip
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = Split-Path -Parent $PSScriptRoot
if (-not $CompilerRoot) { $CompilerRoot = Join-Path $project 'installer/tools/nsis-3.12' }
if (-not $SevenZip) { $SevenZip = Join-Path $project 'installer/tools/7zip-full-26.03/7z.exe' }
$CompilerRoot = (Resolve-Path -LiteralPath $CompilerRoot).Path
$SevenZip = (Resolve-Path -LiteralPath $SevenZip).Path
$Zip = (Resolve-Path -LiteralPath $Zip).Path
$Checksums = (Resolve-Path -LiteralPath $Checksums).Path
& (Join-Path $PSScriptRoot 'Verify-CutTheSaveRelease.ps1') -Zip $Zip -Checksums $Checksums

# Validate the managed compiler against its retained official distribution.
$compilerArchive = Join-Path (Split-Path $CompilerRoot) 'nsis-3.12-official.zip'
$compilerSha = '56581F90DB321581C5381193D796FFFCF2D24B2F8FED2160A6C6A3BAA67F2C4F'
if ((Get-FileHash -LiteralPath $compilerArchive).Hash -ne $compilerSha) { throw 'NSIS archive fingerprint mismatch.' }
$archive = [IO.Compression.ZipFile]::OpenRead($compilerArchive)
try {
    foreach ($entry in $archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') }) {
        if (-not $entry.FullName.StartsWith('nsis-3.12/', [StringComparison]::Ordinal)) { throw 'Unexpected NSIS distribution layout.' }
        $file = Join-Path $CompilerRoot $entry.FullName.Substring('nsis-3.12/'.Length)
        $stream = $entry.Open()
        try { $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ((Get-FileHash -LiteralPath $file).Hash -ne $sha) { throw "NSIS dependency changed: $file" }
    }
} finally { $archive.Dispose() }
$inspectionHashes = @{
    '7z.exe' = '6EE3C0ED0B27663C1B948AE85A7C0BB073AED1498983182F3F0DF1F6A8C30B2F'
    '7z.dll' = '65E4C1F855F9EF6E8F0F5DF8E3F27D9EB5F07311408639DA0A1CA0B8F4871B0D'
}
foreach ($name in $inspectionHashes.Keys) {
    $inspectionTool = if ($name -eq '7z.exe') { $SevenZip } else { Join-Path (Split-Path $SevenZip) $name }
    if ((Get-FileHash -LiteralPath $inspectionTool).Hash -ne $inspectionHashes[$name]) { throw "Inspection tool changed: $inspectionTool" }
}
$output = Join-Path $project "artifacts/installer/$BuildId"
if (Test-Path -LiteralPath $output) { throw 'Build ID already exists; select a new ID.' }
$payload = Join-Path $output 'payload'
[IO.Directory]::CreateDirectory($payload) | Out-Null
$sourceCommit = & git -C $project rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Unable to identify the source commit.' }
$sourceSnapshot = @()
foreach ($relative in @('installer/CutTheSave.nsi', 'installer/Maintenance.ps1', 'scripts/Build-CutTheSaveInstaller.ps1', 'scripts/Publish-CutTheSave.ps1', 'docs/installer.md')) {
    $source = Join-Path $project $relative
    $evidence = Join-Path $output "build-source/$relative"
    [IO.Directory]::CreateDirectory((Split-Path $evidence)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $evidence
    $sourceSnapshot += @{path=$relative; sha256=(Get-FileHash -LiteralPath $evidence).Hash.ToLowerInvariant(); evidencePath="build-source/$relative"}
}
[IO.Compression.ZipFile]::ExtractToDirectory($Zip, $payload)
$metadata = Get-Content -LiteralPath (Join-Path $payload 'release-metadata.json') -Raw | ConvertFrom-Json
$version = $metadata.version

# Include the exact runtime licenses from the packages used by this payload.
$licenses = Join-Path $payload 'licenses'
[IO.Directory]::CreateDirectory($licenses) | Out-Null
$runtime = Get-Content -LiteralPath (Join-Path $payload 'CutTheSave.runtimeconfig.json') -Raw | ConvertFrom-Json
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$licenseSources = @()
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $package = "$($framework.name.ToLowerInvariant()).runtime.win-x64"
    $packagePath = Join-Path $nugetRoot "$package/$($framework.version)"
    $licenseFiles = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -in 'LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT' })
    if ($licenseFiles.Count -eq 0) { throw "Runtime license missing: $packagePath" }
    foreach ($license in $licenseFiles) {
        $targetName = "$package-$($framework.version)-$($license.Name)"
        Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licenses $targetName)
        $licenseSources += @{package=$package; version=$framework.version; file=$license.Name; sha256=(Get-FileHash -LiteralPath $license.FullName).Hash}
    }
}
$dependencyLicenseSources = @()
$dependencies = Get-Content -LiteralPath (Join-Path $payload 'CutTheSave.deps.json') -Raw | ConvertFrom-Json
foreach ($dependency in $dependencies.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' }) {
    if ($dependency.Name -notmatch '^(NSec\.Cryptography|libsodium)/([0-9]+\.[0-9]+\.[0-9]+)$') {
        throw "Review the license requirements for new packaged dependency: $($dependency.Name)"
    }
    $package = $Matches[1].ToLowerInvariant(); $packageVersion = $Matches[2]
    $packagePath = Join-Path $nugetRoot "$package/$packageVersion"
    $licenseFiles = @('LICENSE')
    if ($package -eq 'nsec.cryptography') { $licenseFiles += 'NOTICE' }
    foreach ($name in $licenseFiles) {
        $source = Join-Path $packagePath $name
        $targetName = "$package-$packageVersion-$name.txt"
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenses $targetName)
        $dependencyLicenseSources += @{package=$package; version=$packageVersion; file=$name; sha256=(Get-FileHash -LiteralPath $source).Hash}
    }
}
Copy-Item -LiteralPath (Join-Path $CompilerRoot 'COPYING') -Destination (Join-Path $licenses 'NSIS-3.12-COPYING.txt')
Copy-Item -LiteralPath (Join-Path $output 'build-source/docs/installer.md') -Destination (Join-Path $payload 'INSTALLER.md')

# The four explicitly reviewed examples are the only save data in the installer.
$sampleRoot = Join-Path $project 'samples'
$sampleNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath (Join-Path $payload 'samples')) { throw 'The portable ZIP must not supply additional save examples.' }
foreach ($line in Get-Content -LiteralPath (Join-Path $sampleRoot 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([A-Fa-f0-9]{64})  (save-[ab]/(?:original|edited)\.bin)$') { throw 'Unexpected sample manifest entry.' }
    $sampleHash = $Matches[1]; $sampleName = $Matches[2]
    if (-not $sampleNames.Add($sampleName)) { throw "Duplicate sample manifest entry: $sampleName" }
    $source = Join-Path $sampleRoot $sampleName
    if ((Get-FileHash -LiteralPath $source).Hash -ne $sampleHash) { throw "Example checksum mismatch: $sampleName" }
    $destination = Join-Path $payload "samples/$sampleName"
    [IO.Directory]::CreateDirectory((Split-Path $destination)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
if ($sampleNames.Count -ne 4) { throw 'The sample manifest must contain exactly the four reviewed examples.' }
foreach ($name in 'README.md','SHA256SUMS.txt') { Copy-Item -LiteralPath (Join-Path $sampleRoot $name) -Destination (Join-Path $payload "samples/$name") }
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    @{path=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
@{version=$version; files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'payload.json') -Encoding utf8BOM
# Windows PowerShell 5.1 needs a BOM to read Chinese diagnostics correctly.
[IO.File]::WriteAllText((Join-Path $payload 'Maintenance.ps1'), (Get-Content -LiteralPath (Join-Path $output 'build-source/installer/Maintenance.ps1') -Raw), [Text.UTF8Encoding]::new($true))
$exe = Join-Path $output "CutTheSave-$version-win-x64-Setup.exe"
& (Join-Path $CompilerRoot 'makensis.exe') /NOCONFIG /INPUTCHARSET UTF8 /V3 "/DVERSION=$version" "/DPAYLOAD=$payload" "/DOUTPUT=$exe" (Join-Path $output 'build-source/installer/CutTheSave.nsi') | Tee-Object -FilePath (Join-Path $output 'compiler.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe)) { throw 'NSIS compilation failed.' }
& $SevenZip t $exe | Set-Content -LiteralPath (Join-Path $output 'archive-test.log')
if ($LASTEXITCODE -ne 0) { throw 'Installer archive test failed.' }
$inspection = Join-Path $output 'inspection'
& $SevenZip x $exe "-o$inspection" -y | Set-Content -LiteralPath (Join-Path $output 'archive-extract.log')
if ($LASTEXITCODE -ne 0) { throw 'Read-only installer extraction failed.' }
# NSIS exposes the embedded payload in $PLUGINSDIR/payload. Find its manifest,
# then compare every payload byte, including the maintenance helper and manifest.
$manifests = @(Get-ChildItem -LiteralPath $inspection -Recurse -File -Filter 'payload.json')
if ($manifests.Count -ne 1) { throw 'Embedded payload manifest is missing or ambiguous.' }
$embeddedRoot = Split-Path $manifests[0].FullName
$expectedFiles = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
foreach ($file in $expectedFiles) {
    $relative = [IO.Path]::GetRelativePath($payload,$file.FullName)
    $embedded = Join-Path $embeddedRoot $relative
    if (-not (Test-Path -LiteralPath $embedded -PathType Leaf) -or (Get-FileHash -LiteralPath $embedded).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Embedded payload mismatch: $relative" }
}
if (@(Get-ChildItem -LiteralPath $embeddedRoot -File -Recurse).Count -ne $expectedFiles.Count) { throw 'Unexpected embedded payload files.' }
foreach ($source in $sourceSnapshot) {
    if ((Get-FileHash -LiteralPath (Join-Path $project $source.path)).Hash.ToLowerInvariant() -cne $source.sha256) {
        throw "Installer build source changed during compilation: $($source.path)"
    }
}
$sha256 = (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant()
"$sha256  $([IO.Path]::GetFileName($exe))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8
$report = [ordered]@{
    buildId=$BuildId; version=$version; runtime='win-x64'; exe=$exe; sha256=$sha256; bytes=(Get-Item -LiteralPath $exe).Length
    sourceZip=$Zip; sourceZipSha256=(Get-FileHash -LiteralPath $Zip).Hash.ToLowerInvariant(); releaseId=$metadata.release_id
    compiler='NSIS 3.12'; compilerArchiveSha256=$compilerSha; runtimeLicenses=$licenseSources; dependencyLicenses=$dependencyLicenseSources
    sourceCommit=$sourceCommit; sourceSnapshot=$sourceSnapshot
    installerBuilt=$true; archiveIntegrityVerified=$true; embeddedPayloadFilesVerified=$expectedFiles.Count
    installerExecuted=$false; digitallySigned=((Get-AuthenticodeSignature -LiteralPath $exe).Status -eq 'Valid')
    status='development-candidate'; createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'build-report.json') -Encoding utf8
# Local installer pointer only; official release/candidate pointers are unchanged.
@{buildId=$BuildId; exe=$exe; sha256=$sha256; report=(Join-Path $output 'build-report.json')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $project 'artifacts/installer/latest.json') -Encoding utf8
Write-Output $exe
