#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [Parameter(Mandatory = $true)][string]$Checksums,
    [string]$PublishRoot, # Compatibility only; never validation evidence.
    [string]$ReleaseId,
    [string]$ReceiptPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$zipPath = (Resolve-Path -LiteralPath $Zip).Path
$checksumPath = (Resolve-Path -LiteralPath $Checksums).Path
$hashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw "Invalid checksum line: $line" }
    if (-not $hashes.TryAdd($Matches[2], $Matches[1].ToLowerInvariant())) { throw 'Duplicate checksum entry.' }
}
$zipName = [IO.Path]::GetFileName($zipPath)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not $hashes.ContainsKey($zipName) -or $hashes[$zipName] -cne $zipHash) { throw 'ZIP checksum missing or incorrect.' }
$required = @('CutTheSave.exe','CutTheSave.dll','CutTheSave.Updater.exe','CutTheSave.deps.json','CutTheSave.runtimeconfig.json','README.md','README.en.md','docs/guide.md','docs/appearance.md','docs/rtss-safety.md','docs/write-safety.md','docs/backup-safety.md','guide.md','PRIVACY.md','SECURITY.md','THIRD_PARTY_NOTICES.md','CHANGELOG.md','RELEASE_CHECKLIST.md','FIELD_ACCEPTANCE.md','CutTheSave.update.ini.example','UPDATE_CONFIGURATION.md','release-metadata.json')
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$files = [Collections.Generic.List[object]]::new()
$total = 0L
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($archive.Entries.Count -gt 1024) { throw 'Too many ZIP entries.' }
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName
        if ($name.EndsWith('/')) { throw 'Explicit directory entries are not permitted.' }
        if ($name.Length -gt 220 -or $name -notmatch '^[A-Za-z0-9][A-Za-z0-9_.()/-]*$') { throw "Unsafe ZIP path: $name" }
        foreach ($part in $name.Split('/')) {
            if ($part -in @('','.', '..') -or $part.EndsWith('.') -or $part -match '^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)') { throw 'Unsafe path segment.' }
        }
        if (-not $names.Add($name)) { throw 'Duplicate ZIP path.' }
        if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 1024)) { throw 'Linked ZIP entry.' }
        if ($name -match '(?i)(^|/)(backups|updates|releases|work|\.agents|AGENTS\.md|completion-audit\.md|ctr_save\.bin|\.env[^/]*)(/|$)|\.(ini|key|pem|pfx|p12|bak)$') { throw "Private or user data in ZIP: $name" }
        $total += $entry.Length
        if ($entry.Length -gt 192MB -or $total -gt 512MB) { throw 'ZIP size limit exceeded.' }
        if (-not $hashes.ContainsKey($name)) { throw "Missing checksum: $name" }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
        if ($hash -cne $hashes[$name]) { throw "ZIP entry checksum mismatch: $name" }
        $files.Add([ordered]@{name=$name; byte_size=$entry.Length; sha256=$hash})
    }
    foreach ($name in $names) {
        $parent = $name
        while ($parent.Contains('/')) { $parent = $parent.Substring(0,$parent.LastIndexOf('/')); if ($names.Contains($parent)) { throw 'ZIP file/directory collision.' } }
    }
    foreach ($name in $required) { if (-not $names.Contains($name)) { throw "Missing required file: $name" } }
    if ($hashes.Count -ne $files.Count + 1) { throw 'Unexpected checksum entries.' }
    $stream = $archive.GetEntry('release-metadata.json').Open()
    $reader = [IO.StreamReader]::new($stream)
    try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($metadata.schema_version -ne 1 -or $metadata.version -notmatch '^\d+\.\d+\.\d+$' -or $metadata.runtime -ne 'win-x64') { throw 'Invalid release metadata.' }
    $stream = $archive.GetEntry('CutTheSave.dll').Open()
    $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
    $memory.Position = 0
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($memory)
    try {
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $assembly = $md.GetAssemblyDefinition()
        if ($assembly.Version -ne [version]($metadata.version + '.0')) { throw 'Packaged assembly/release version mismatch.' }
    } finally { $pe.Dispose(); $memory.Dispose() }
} finally { $archive.Dispose() }
if ($ReceiptPath) {
    if (-not $ReleaseId) { throw 'ReleaseId is required for a receipt.' }
    $root = [IO.Path]::GetFullPath((Join-Path $repoRoot 'releases/receipts')) + [IO.Path]::DirectorySeparatorChar
    $receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
    if (-not $receiptFullPath.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $receiptFullPath)) { throw 'Receipt path must be a new file under releases/receipts.' }
    $receipt = [ordered]@{
        schema_version=1; kind='package_validation'; release_id=$ReleaseId; result='verified'; version=$metadata.version
        verified_at_utc=[DateTimeOffset]::UtcNow.ToString('o')
        zip=@{path="artifacts/release/$zipName"; byte_size=(Get-Item -LiteralPath $zipPath).Length; sha256=$zipHash; entry_count=$files.Count}
        checksum_manifest=@{path="artifacts/release/$([IO.Path]::GetFileName($checksumPath))"; byte_size=(Get-Item -LiteralPath $checksumPath).Length; sha256=(Get-FileHash -LiteralPath $checksumPath).Hash.ToLowerInvariant()}
        validator=@{path='scripts/Verify-CutTheSaveRelease.ps1'; sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()}
        checks=@{required_files_present=$true; archive_name_privacy_scan_passed=$true; checksums_match_archive_entries=$true; complete_unique_inventory=$true; packaged_assembly_version_matches=$true}
        assets=$files.ToArray()
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($receiptFullPath)) | Out-Null
    [IO.File]::WriteAllText($receiptFullPath,($receipt | ConvertTo-Json -Depth 9) + [Environment]::NewLine,[Text.UTF8Encoding]::new($false))
    Write-Host "Receipt: $receiptFullPath"
}
Write-Host "PASS ZIP-contained files, complete checksums, privacy boundaries and assembly version $($metadata.version): $zipPath"

