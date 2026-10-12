param([Parameter(Mandatory = $true)][string]$WorkRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../installer/Maintenance.ps1') -LibraryOnly
$script:NativeFileOperation = ${function:Invoke-CtsFileOperation}
$script:Cases = 0
$script:TestRoot = [IO.Path]::Combine([IO.Path]::GetFullPath($WorkRoot), 'maintenance-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($script:TestRoot)
$maintenanceFixture = [IO.Path]::Combine($script:TestRoot, 'Maintenance-source.ps1')
[IO.File]::Copy($script:CtsMaintenanceSource, $maintenanceFixture, $false)
$script:CtsMaintenanceSource = $maintenanceFixture

function Copy-TestRegistration($State) {
    $copy = @()
    foreach ($item in $State) {
        $values = @{}; $kinds = @{}
        foreach ($name in $item.Values.Keys) { $values[$name] = $item.Values[$name]; $kinds[$name] = $item.Kinds[$name] }
        $copy += @{ Path = $item.Path; Exists = $item.Exists; Values = $values; Kinds = $kinds }
    }
    return ,$copy
}
function Get-CtsRegistration($Context) { return ,(Copy-TestRegistration $script:Registration) }
function Set-CtsRegistration($Context, $State) {
    $script:Registration = Copy-TestRegistration $State
    if ($script:RegistryFailure) { $script:RegistryFailure = $false; throw 'injected registry failure after mutation' }
}
function Assert-CtsNotRunning { if ($script:Running) { throw 'injected running process' } }
function New-CtsShortcut([string]$Path, [string]$Root) {
    [IO.File]::WriteAllText($Path, (@{ Target = [IO.Path]::Combine($Root, 'CutTheSave.exe'); WorkingDirectory = $Root; Arguments = ''; Description = 'Cut the Save - tianqistudio' } | ConvertTo-Json))
}
function Get-CtsShortcut([string]$Path) { return [IO.File]::ReadAllText($Path) | ConvertFrom-Json }
function Invoke-CtsFileOperation([string]$Source, [string]$Destination) {
    $script:MutationCount++
    if ($script:FailBefore -eq $script:MutationCount) { throw 'injected failure before mutation' }
    & $script:NativeFileOperation $Source $Destination
    if ($script:FailAfter -eq $script:MutationCount) { throw 'injected failure after mutation' }
}
function Assert-Test([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch { if ($_.Exception.Message -notmatch $Expected) { throw "Unexpected error: $_; expected $Expected" }; return }
    throw "Expected failure: $Expected"
}
function New-TestFixture([string]$Name) {
    $root = [IO.Path]::Combine($script:TestRoot, $Name)
    [void][IO.Directory]::CreateDirectory($root)
    $script:Registration = @(@{ Path = 'Product'; Exists = $false; Values = @{}; Kinds = @{} }, @{ Path = 'Uninstall'; Exists = $false; Values = @{}; Kinds = @{} })
    $script:Running = $false; $script:RegistryFailure = $false; $script:MutationCount = 0; $script:FailBefore = 0; $script:FailAfter = 0
    return @{ Root = [IO.Path]::Combine($root, 'programs', 'Cut the Save'); Desktop = [IO.Path]::Combine($root, 'desktop', 'Cut the Save.lnk'); StartMenu = [IO.Path]::Combine($root, 'start', 'tianqistudio', 'Cut the Save.lnk'); Fixture = $root }
}
function New-TestPayload($Context, [string]$Version, [string]$Bytes = 'new application') {
    $root = [IO.Path]::Combine($Context.Fixture, 'payload-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory([IO.Path]::Combine($root, 'licenses'))
    [IO.File]::WriteAllText([IO.Path]::Combine($root, 'CutTheSave.exe'), $Bytes)
    [IO.File]::WriteAllText([IO.Path]::Combine($root, 'licenses', 'notice.txt'), 'License fixture')
    $files = @()
    foreach ($relative in @('CutTheSave.exe', 'licenses\notice.txt')) { $files += @{ path = $relative; sha256 = Get-CtsHash ([IO.Path]::Combine($root, $relative)) } }
    [IO.File]::WriteAllText([IO.Path]::Combine($root, 'payload.json'), (@{ version = $Version; files = $files } | ConvertTo-Json -Depth 5))
    [IO.File]::WriteAllText([IO.Path]::Combine($root, 'uninstaller-source.bin'), 'uninstaller fixture')
    return $root
}
function Install-TestPayload($Context, [string]$Payload, [string]$Version, [int]$Desktop = 1, [int]$StartMenu = 1) {
    Invoke-CtsMaintenance $Context Install $Payload ([IO.Path]::Combine($Payload, 'uninstaller-source.bin')) $Version $Desktop $StartMenu
}
function Read-TestMarker($Context) { return Read-CtsJson ([IO.Path]::Combine($Context.Root, '.cts-install.json')) }
function Snapshot-TestFiles($Context) {
    $result = @{}
    foreach ($path in @([IO.Directory]::GetFiles($Context.Root, '*', 'AllDirectories')) + @($Context.Desktop, $Context.StartMenu)) {
        if ([IO.File]::Exists($path)) { $result[$path] = Get-CtsHash $path }
    }
    return $result
}
function Assert-TestSnapshot($Before, $Context) {
    $after = Snapshot-TestFiles $Context
    Assert-Test ($Before.Count -eq $after.Count) 'Rollback changed the file count.'
    foreach ($path in $Before.Keys) { Assert-Test ($after[$path] -ceq $Before[$path]) "Rollback changed $path" }
}
function Run-Test([string]$Name, [scriptblock]$Body) { & $Body; $script:Cases++; Write-Output "PASS $Name" }

Run-Test 'three/four-part version normalization and invalid versions' {
    Assert-Test ((ConvertTo-CtsVersion '1.0.1') -eq (ConvertTo-CtsVersion '1.0.1.0')) 'Three/four-part equality failed.'
    Assert-Test ((ConvertTo-CtsVersion '1.0.1.1') -gt (ConvertTo-CtsVersion '1.0.1')) 'Nonzero revision was discarded.'
    foreach ($bad in @('1.0', '1.0.1-beta', '-1.0.1', '1.0.1.0.1')) { Assert-Throws { ConvertTo-CtsVersion $bad } 'Invalid version' }
}
Run-Test 'all four independent shortcut combinations and saved choices' {
    foreach ($desktop in @(0, 1)) { foreach ($start in @(0, 1)) {
        $context = New-TestFixture "choices-$desktop-$start"
        $payload = New-TestPayload $context '1.0.1'
        Install-TestPayload $context $payload '1.0.1' $desktop $start
        Assert-Test ([IO.File]::Exists($context.Desktop) -eq ($desktop -eq 1)) 'Wrong desktop choice.'
        Assert-Test ([IO.File]::Exists($context.StartMenu) -eq ($start -eq 1)) 'Wrong start-menu choice.'
        $state = Read-TestMarker $context
        Assert-Test ($state.desktopShortcut -eq $desktop -and $state.startMenuShortcut -eq $start) 'Choices were not saved.'
        Assert-Test ($script:Registration[0].Values['DesktopShortcut'] -eq $desktop) 'Registry preference differs.'
        Install-TestPayload $context $payload '1.0.1.0' $desktop $start
        Assert-Test ([IO.File]::Exists($context.Desktop) -eq ($desktop -eq 1)) 'Repair recreated a disabled shortcut.'
    } }
}
Run-Test 'upgrade, downgrade rejection and same-version repair' {
    $context = New-TestFixture 'versions'; $v1 = New-TestPayload $context '1.0.1' 'old'; Install-TestPayload $context $v1 '1.0.1'
    $v2 = New-TestPayload $context '1.0.2' 'upgraded'; Install-TestPayload $context $v2 '1.0.2'
    $before = Snapshot-TestFiles $context
    Assert-Throws { Install-TestPayload $context $v1 '1.0.1' } 'Downgrades'
    Assert-TestSnapshot $before $context
    [IO.File]::WriteAllText([IO.Path]::Combine($context.Root, 'CutTheSave.exe'), 'damaged')
    Install-TestPayload $context $v2 '1.0.2.0'
    Assert-Test ([IO.File]::ReadAllText([IO.Path]::Combine($context.Root, 'CutTheSave.exe')) -ceq 'upgraded') 'Repair did not restore payload bytes.'
}
Run-Test 'uninstall preserves configuration, unknown files and safe reinstall ownership' {
    $context = New-TestFixture 'uninstall'; $payload = New-TestPayload $context '1.0.1'; Install-TestPayload $context $payload '1.0.1'
    $configuration = [IO.Path]::Combine($context.Root, 'CutTheSave.update.ini'); [IO.File]::WriteAllText($configuration, 'user preferences')
    $unknown = [IO.Path]::Combine($context.Root, 'licenses', 'personal.txt'); [IO.File]::WriteAllText($unknown, 'unknown data')
    Invoke-CtsMaintenance $context Uninstall '' '' '' 0 0
    Assert-Test ([IO.File]::ReadAllText($configuration) -ceq 'user preferences') 'Uninstall changed INI.'
    Assert-Test ([IO.File]::ReadAllText($unknown) -ceq 'unknown data') 'Uninstall removed unknown file.'
    Assert-Test (![IO.File]::Exists([IO.Path]::Combine($context.Root, 'CutTheSave.exe'))) 'Uninstall retained managed executable.'
    Assert-Test ((Read-TestMarker $context).status -ceq 'Uninstalled') 'Missing reinstall ownership receipt.'
    Assert-Test (!$script:Registration[0].Exists -and !$script:Registration[1].Exists) 'Uninstall registration remains.'
    Install-TestPayload $context $payload '1.0.1' 0 1
    Assert-Test ([IO.File]::ReadAllText($configuration) -ceq 'user preferences') 'Reinstall changed INI.'
    Assert-Test ([IO.File]::ReadAllText($unknown) -ceq 'unknown data') 'Reinstall changed unknown data.'
}
Run-Test 'unowned directory, shortcut and payload collision are rejected' {
    $context = New-TestFixture 'foreign-dir'; $payload = New-TestPayload $context '1.0.1'
    [void][IO.Directory]::CreateDirectory($context.Root)
    Assert-Throws { Install-TestPayload $context $payload '1.0.1' } 'not owned'
    $context = New-TestFixture 'foreign-shortcut'; $payload = New-TestPayload $context '1.0.1'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($context.Desktop)); [IO.File]::WriteAllText($context.Desktop, 'foreign shortcut')
    Assert-Throws { Install-TestPayload $context $payload '1.0.1' } 'not owned'
    Assert-Test ([IO.File]::ReadAllText($context.Desktop) -ceq 'foreign shortcut') 'Foreign shortcut changed.'
    $context = New-TestFixture 'foreign-file'; $payload = New-TestPayload $context '1.0.1'; Install-TestPayload $context $payload '1.0.1'
    $manifest = Read-CtsJson ([IO.Path]::Combine($payload, 'payload.json'))
    [IO.File]::WriteAllText([IO.Path]::Combine($payload, 'new.dll'), 'new file'); [IO.File]::WriteAllText([IO.Path]::Combine($context.Root, 'new.dll'), 'foreign file')
    $manifest.files += @{ path = 'new.dll'; sha256 = Get-CtsHash ([IO.Path]::Combine($payload, 'new.dll')) }
    [IO.File]::WriteAllText([IO.Path]::Combine($payload, 'payload.json'), ($manifest | ConvertTo-Json -Depth 5))
    Assert-Throws { Install-TestPayload $context $payload '1.0.1' } 'unowned file'
}
Run-Test 'running process and locked file reject before modification' {
    $context = New-TestFixture 'busy'; $payload = New-TestPayload $context '1.0.1'; Install-TestPayload $context $payload '1.0.1'
    $before = Snapshot-TestFiles $context; $script:Running = $true
    Assert-Throws { Install-TestPayload $context $payload '1.0.1' } 'running process'; $script:Running = $false
    $handle = [IO.File]::Open([IO.Path]::Combine($context.Root, 'CutTheSave.exe'), 'Open', 'Read', 'Read')
    try { Assert-Throws { Install-TestPayload $context $payload '1.0.1' } 'being used|used by|另一个|另一进程|process' } finally { $handle.Dispose() }
    Assert-TestSnapshot $before $context
}
Run-Test 'failure before commit, after commit and registry mutation restores original installation' {
    foreach ($fault in @('before', 'after', 'registry')) {
        $context = New-TestFixture "rollback-$fault"; $v1 = New-TestPayload $context '1.0.1' 'old'; Install-TestPayload $context $v1 '1.0.1'
        $before = Snapshot-TestFiles $context; $v2 = New-TestPayload $context '1.0.2' 'new'
        $script:MutationCount = 0
        if ($fault -eq 'before') { $script:FailBefore = 2 }
        if ($fault -eq 'after') { $script:FailAfter = 2 }
        if ($fault -eq 'registry') { $script:RegistryFailure = $true }
        Assert-Throws { Install-TestPayload $context $v2 '1.0.2' 0 0 } 'injected'
        Assert-TestSnapshot $before $context
        Assert-Test ($script:Registration[0].Values['DisplayVersion'] -ceq '1.0.1') 'Registry was not restored.'
    }
}
Run-Test 'manifest tamper, traversal and protected config paths reject safely' {
    $context = New-TestFixture 'tamper'; $payload = New-TestPayload $context '1.0.1'
    [IO.File]::WriteAllText([IO.Path]::Combine($payload, 'CutTheSave.exe'), 'tampered')
    Assert-Throws { Install-TestPayload $context $payload '1.0.1' } 'hash mismatch'
    foreach ($relative in @('..\escape.exe', 'C:\escape.exe', 'a:stream', 'folder\..\escape', 'CON.txt', 'name.')) { Assert-Throws { Resolve-CtsFile $context.Root $relative } 'Invalid managed' }
    $metadata = @{ version = '1.0.1'; files = @(@{ path = 'CutTheSave.update.ini'; sha256 = ('a' * 64) }) }
    Assert-Throws { Get-CtsManagedFiles $metadata $context.Root -Payload } 'Protected installation'
}
Run-Test 'reparse points in installation path are refused' {
    $context = New-TestFixture 'reparse'; $payload = New-TestPayload $context '1.0.1'
    $target = [IO.Path]::Combine($context.Fixture, 'external'); [void][IO.Directory]::CreateDirectory($target)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($context.Root))
    [void](New-Item -ItemType Junction -Path $context.Root -Target $target)
    Assert-Throws { Install-TestPayload $context $payload '1.0.1' } 'Redirected path'
    Assert-Test ([IO.Directory]::GetFileSystemEntries($target).Length -eq 0) 'Reparse target was modified.'
    # Exact test-owned junction only; leave all other fixtures for the task cleanup queue.
    [IO.Directory]::Delete($context.Root, $false)
}
Write-Output "RESULT executed=$script:Cases passed=$script:Cases failed=0 root=$script:TestRoot"
