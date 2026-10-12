#Requires -Version 7.0
[CmdletBinding()]
param([string]$PublishRoot, [string]$Runtime='win-x64', [string]$BuildArtifacts)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if (-not $BuildArtifacts) {
    if (-not $PublishRoot) { throw 'Pass BuildArtifacts or the exact PublishRoot.' }
    $BuildArtifacts=Join-Path $PublishRoot 'build'
}
dotnet run --project (Join-Path $repoRoot 'tests/CutTheSave.Core.Tests') -c Release --no-build --artifacts-path $BuildArtifacts -- --update-transactions
if ($LASTEXITCODE -ne 0) { throw 'Isolated updater transaction acceptance failed.' }
if ($PublishRoot) {
    $worker=Join-Path $PublishRoot "updater-$Runtime/CutTheSave.Updater.exe"
    & $worker --source unused --target unused --pid 999999
    if ($LASTEXITCODE -ne 2) { throw 'Published worker did not reject the obsolete protocol.' }
    Write-Host 'PASS published updater rejects obsolete arguments; signed install/rollback were exercised with isolated Core fixtures.'
}
Write-Host 'Production trust remains unchanged. This does not claim a provisioned production updater performed replacement.'
$global:LASTEXITCODE=0

