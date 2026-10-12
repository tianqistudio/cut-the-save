param(
    [ValidateSet('Install', 'Uninstall')][string]$Mode = 'Install',
    [string]$PayloadDirectory,
    [string]$UninstallerPath,
    [string]$Version,
    [ValidateSet(0, 1)][int]$DesktopShortcut = 1,
    [ValidateSet(0, 1)][int]$StartMenuShortcut = 1,
    [switch]$LibraryOnly
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$script:CtsProductId = 'tianqistudio.cut-the-save'
$script:CtsMaintenanceSource = $PSCommandPath

function ConvertTo-CtsVersion([string]$Value) {
    if ($Value -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Invalid version: $Value" }
    if (($Value -split '\.').Count -eq 3) { $Value += '.0' }
    return [version]::Parse($Value)
}

function Assert-CtsNoReparse([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        try {
            $attributes = [IO.File]::GetAttributes($current)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Redirected path is not permitted: $current" }
        } catch [IO.FileNotFoundException] { } catch [IO.DirectoryNotFoundException] { }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Resolve-CtsFile([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '[<>:"|?*\x00-\x1f]') {
        throw "Invalid managed path: $Relative"
    }
    $Relative = $Relative.Replace('/', '\')
    foreach ($segment in ($Relative -split '\\')) {
        if (!$segment -or $segment -in @('.', '..') -or $segment -match '[. ]$' -or $segment -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') {
            throw "Invalid managed path: $Relative"
        }
    }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $Relative))
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (!$full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Managed path escapes installation directory.' }
    Assert-CtsNoReparse $full
    if ([IO.Directory]::Exists($full)) { throw "A managed file path is a directory: $full" }
    return $full
}

function Get-CtsHash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Read-CtsJson([string]$Path) {
    Assert-CtsNoReparse $Path
    if ((Get-Item -LiteralPath $Path).Length -gt 4MB) { throw 'Installation metadata exceeds 4 MiB.' }
    return [IO.File]::ReadAllText($Path) | ConvertFrom-Json
}

function Get-CtsContext {
    return @{
        Root = [IO.Path]::Combine([Environment]::GetFolderPath('ProgramFiles'), 'tianqistudio', 'Cut the Save')
        Desktop = [IO.Path]::Combine([Environment]::GetFolderPath('CommonDesktopDirectory'), 'Cut the Save.lnk')
        StartMenu = [IO.Path]::Combine([Environment]::GetFolderPath('CommonPrograms'), 'tianqistudio', 'Cut the Save.lnk')
        RegistryPaths = @('SOFTWARE\tianqistudio\CutTheSave', 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\tianqistudio.CutTheSave')
    }
}

function Get-CtsRegistration($Context) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
    try {
        $result = @()
        foreach ($path in $Context.RegistryPaths) {
            $key = $base.OpenSubKey($path)
            $values = @{}; $kinds = @{}
            if ($key) {
                try { foreach ($name in $key.GetValueNames()) { $values[$name] = $key.GetValue($name, $null, 'DoNotExpandEnvironmentNames'); $kinds[$name] = $key.GetValueKind($name).ToString() } }
                finally { $key.Dispose() }
            }
            $result += @{ Path = $path; Exists = ($null -ne $key); Values = $values; Kinds = $kinds }
        }
        return ,$result
    } finally { $base.Dispose() }
}

function Set-CtsRegistration($Context, $State) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
    try {
        foreach ($item in $State) {
            $key = $base.OpenSubKey($item.Path, $true)
            if (!$key -and $item.Exists) { $key = $base.CreateSubKey($item.Path) }
            if (!$key) { continue }
            try {
                foreach ($name in $key.GetValueNames()) { if (!$item.Values.ContainsKey($name)) { $key.DeleteValue($name, $false) } }
                foreach ($name in $item.Values.Keys) { $key.SetValue($name, $item.Values[$name], [Microsoft.Win32.RegistryValueKind]([Enum]::Parse([Microsoft.Win32.RegistryValueKind], $item.Kinds[$name]))) }
                $empty = ($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0)
            } finally { $key.Dispose() }
            if (!$item.Exists -and $empty) { $base.DeleteSubKey($item.Path, $false) }
        }
    } finally { $base.Dispose() }
}

function Get-CtsShortcut([string]$Path) {
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($Path)
        try { return @{ Target = $link.TargetPath; WorkingDirectory = $link.WorkingDirectory; Arguments = $link.Arguments; Description = $link.Description } }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}

function New-CtsShortcut([string]$Path, [string]$Root) {
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($Path)
        try {
            $link.TargetPath = [IO.Path]::Combine($Root, 'CutTheSave.exe')
            $link.WorkingDirectory = $Root
            $link.Arguments = ''
            $link.Description = 'Cut the Save - tianqistudio'
            $link.Save()
        } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}

function Assert-CtsNotRunning {
    # Also catches the elevated RTSS helper and the updater copied to LocalAppData.
    if (@(Get-Process -Name 'CutTheSave', 'CutTheSave.Updater' -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Close Cut the Save and its updater before continuing. No process was terminated.'
    }
}

function Get-CtsManagedFiles($Metadata, [string]$Root, [switch]$Payload, [switch]$AllowEmpty) {
    [void](ConvertTo-CtsVersion ([string]$Metadata.version))
    $files = @{}
    foreach ($entry in @($Metadata.files)) {
        $relative = [string]$entry.path
        $full = Resolve-CtsFile $Root $relative
        if ([string]$entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $files.ContainsKey($full)) { throw 'Invalid or duplicate managed-file identity.' }
        if ($relative -ieq 'CutTheSave.update.ini' -or $relative -ieq '.cts-install.json' -or $relative -ieq 'payload.json') { throw "Protected installation path: $relative" }
        if ($Payload -and $relative -in @('Maintenance.ps1', 'Uninstall.exe')) { throw "Reserved installation path: $relative" }
        $files[$full] = @{ path = $relative.Replace('/', '\'); sha256 = ([string]$entry.sha256).ToLowerInvariant() }
    }
    if (!($AllowEmpty -and $files.Count -eq 0) -and !$files.ContainsKey([IO.Path]::Combine($Root, 'CutTheSave.exe'))) { throw 'Payload is missing CutTheSave.exe.' }
    return $files
}

function Get-CtsInstalledState($Context) {
    Assert-CtsNoReparse $Context.Root
    $marker = [IO.Path]::Combine($Context.Root, '.cts-install.json')
    if (![IO.Directory]::Exists($Context.Root)) {
        if ([IO.File]::Exists($Context.Root)) { throw 'Installation path is a file.' }
        return $null
    }
    if (![IO.File]::Exists($marker)) { throw 'The existing directory is not owned by this installer.' }
    $state = Read-CtsJson $marker
    if ($state.schema -ne 1 -or $state.productId -cne $script:CtsProductId -or [string]$state.installRoot -ine $Context.Root) { throw 'Installation ownership metadata does not match this product.' }
    if ($state.status -notin @('Installed', 'Uninstalled')) { throw 'Invalid installation status.' }
    if ($state.desktopShortcut -notin @(0, 1) -or $state.startMenuShortcut -notin @(0, 1)) { throw 'Invalid saved shortcut choices.' }
    [void](Get-CtsManagedFiles $state $Context.Root -AllowEmpty:($state.status -eq 'Uninstalled'))
    return $state
}

function Assert-CtsResources($Context, $OldState, $Registration) {
    foreach ($item in $Registration) {
        if ($item.Exists -and (!$OldState -or $item.Values['ProductId'] -cne $script:CtsProductId -or $item.Values['InstallLocation'] -ine $Context.Root)) {
            throw 'An existing registration is not owned by this product.'
        }
    }
    foreach ($name in @('Desktop', 'StartMenu')) {
        $path = $Context[$name]
        Assert-CtsNoReparse $path
        if ([IO.Directory]::Exists($path)) { throw "Shortcut path is a directory: $path" }
        if (![IO.File]::Exists($path)) { continue }
        $wasSelected = $false
        if ($OldState) { if ($name -eq 'Desktop') { $wasSelected = $OldState.desktopShortcut -eq 1 } else { $wasSelected = $OldState.startMenuShortcut -eq 1 } }
        if (!$wasSelected) { throw "An existing shortcut is not owned by this installer: $path" }
        $link = Get-CtsShortcut $path
        if ($link.Target -ine [IO.Path]::Combine($Context.Root, 'CutTheSave.exe') -or $link.WorkingDirectory -ine $Context.Root -or $link.Arguments -ne '' -or $link.Description -cne 'Cut the Save - tianqistudio') {
            throw "A shortcut was changed or belongs to another application: $path"
        }
    }
}

function New-CtsDirectory([string]$Path, $Created) {
    Assert-CtsNoReparse $Path
    if ([IO.Directory]::Exists($Path)) { return }
    $parent = [IO.Path]::GetDirectoryName($Path)
    if ($parent) { New-CtsDirectory $parent $Created }
    [void][IO.Directory]::CreateDirectory($Path)
    $Created.Add($Path)
}

function Remove-CtsEmptyDirectories($Directories) {
    foreach ($path in @($Directories | Sort-Object Length -Descending -Unique)) {
        Assert-CtsNoReparse $path
        if ([IO.Directory]::Exists($path) -and [IO.Directory]::GetFileSystemEntries($path).Length -eq 0) { [IO.Directory]::Delete($path, $false) }
    }
}

function Invoke-CtsFileOperation([string]$Source, [string]$Destination) {
    # Sources are completely staged and hashed on the same volume before commit.
    if ($Source) {
        if ([IO.File]::Exists($Destination)) { [IO.File]::Replace($Source, $Destination, [NullString]::Value) }
        else { [IO.File]::Move($Source, $Destination) }
    } elseif ([IO.File]::Exists($Destination)) { [IO.File]::Delete($Destination) }
}

function Invoke-CtsMaintenance($Context, [string]$Action, [string]$PayloadRoot, [string]$Uninstaller, [string]$RequestedVersion, [int]$Desktop, [int]$StartMenu) {
    Assert-CtsNotRunning
    $old = Get-CtsInstalledState $Context
    $registration = Get-CtsRegistration $Context
    Assert-CtsResources $Context $old $registration
    if ($Action -eq 'Uninstall' -and (!$old -or $old.status -eq 'Uninstalled')) { throw 'No owned installation exists.' }
    $oldFiles = @{}
    if ($old) { $oldFiles = Get-CtsManagedFiles $old $Context.Root -AllowEmpty:($old.status -eq 'Uninstalled') }
    $incoming = @{}; $newState = $null
    if ($Action -eq 'Install') {
        $metadata = Read-CtsJson ([IO.Path]::Combine($PayloadRoot, 'payload.json'))
        $newVersion = ConvertTo-CtsVersion ([string]$metadata.version)
        if ($newVersion -ne (ConvertTo-CtsVersion $RequestedVersion)) { throw 'Installer and payload versions disagree.' }
        if ($old -and $newVersion -lt (ConvertTo-CtsVersion ([string]$old.version))) { throw 'Downgrades are not permitted.' }
        $payloadFiles = Get-CtsManagedFiles $metadata $PayloadRoot -Payload
        $entries = @()
        foreach ($item in $payloadFiles.GetEnumerator()) {
            if ((Get-CtsHash $item.Key) -cne $item.Value.sha256) { throw "Payload hash mismatch: $($item.Value.path)" }
            $destination = Resolve-CtsFile $Context.Root $item.Value.path
            $incoming[$destination] = @{ Source = $item.Key; Hash = $item.Value.sha256 }
            $entries += $item.Value
        }
        foreach ($pair in @(@{ path = 'Maintenance.ps1'; source = $script:CtsMaintenanceSource }, @{ path = 'Uninstall.exe'; source = $Uninstaller })) {
            Assert-CtsNoReparse $pair.source
            $hash = Get-CtsHash $pair.source
            $incoming[(Resolve-CtsFile $Context.Root $pair.path)] = @{ Source = $pair.source; Hash = $hash }
            $entries += @{ path = $pair.path; sha256 = $hash }
        }
        foreach ($path in $incoming.Keys) {
            if ([IO.File]::Exists($path) -and !$oldFiles.ContainsKey($path)) { throw "An unowned file conflicts with the payload: $path" }
        }
        $newState = @{ schema = 1; productId = $script:CtsProductId; status = 'Installed'; installRoot = $Context.Root; version = $RequestedVersion; desktopShortcut = $Desktop; startMenuShortcut = $StartMenu; files = @($entries | Sort-Object path) }
    } else {
        # Keep a tiny ownership receipt so preserved user files never prevent reinstall.
        $newState = @{ schema = 1; productId = $script:CtsProductId; status = 'Uninstalled'; installRoot = $Context.Root; version = $old.version; desktopShortcut = 0; startMenuShortcut = 0; files = @() }
    }
    $marker = [IO.Path]::Combine($Context.Root, '.cts-install.json')
    $paths = @(@($oldFiles.Keys) + @($incoming.Keys) + @($marker, $Context.Desktop, $Context.StartMenu) | Sort-Object -Unique)
    $created = New-Object 'System.Collections.Generic.List[string]'
    $scratch = New-Object 'System.Collections.Generic.List[string]'
    $snapshots = @{}; $applied = New-Object 'System.Collections.Generic.List[string]'
    $postHashes = @{}; $registryChanged = $false; $complete = $false; $rolledBack = $false
    $transaction = [IO.Path]::Combine([IO.Path]::GetDirectoryName($Context.Root), 'Cut the Save.install-' + [guid]::NewGuid().ToString('N'))
    try {
        New-CtsDirectory $transaction $created
        $counter = 0
        foreach ($path in $paths) {
            Assert-CtsNoReparse $path
            if ([IO.Directory]::Exists($path)) { throw "Expected a file, found a directory: $path" }
            $backup = $null; $hash = $null
            if ([IO.File]::Exists($path)) {
                $handle = [IO.File]::Open($path, 'Open', 'ReadWrite', 'None')
                $handle.Dispose()
                $hash = Get-CtsHash $path
                $backup = [IO.Path]::Combine($transaction, "backup-$counter.bin")
                $scratch.Add($backup)
                [IO.File]::Copy($path, $backup, $false)
                if ((Get-CtsHash $backup) -cne $hash -or (Get-CtsHash $path) -cne $hash) { throw 'Pre-install backup verification failed.' }
            }
            $snapshots[$path] = @{ Backup = $backup; Hash = $hash }
            $counter++
        }
        $journal = [IO.Path]::Combine($transaction, 'transaction.json'); $scratch.Add($journal)
        [IO.File]::WriteAllText($journal, (@{ productId = $script:CtsProductId; action = $Action; root = $Context.Root; files = $snapshots; registration = $registration } | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($true)))
        $stateSource = [IO.Path]::Combine($transaction, 'new-state.json'); $scratch.Add($stateSource)
        [IO.File]::WriteAllText($stateSource, ($newState | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($true)))
        $incoming[$marker] = @{ Source = $stateSource; Hash = Get-CtsHash $stateSource }
        if ($Action -eq 'Install') {
            foreach ($name in @('Desktop', 'StartMenu')) {
                $selected = if ($name -eq 'Desktop') { $Desktop } else { $StartMenu }
                if ($selected -eq 1) {
                    $linkSource = [IO.Path]::Combine($transaction, "$name.lnk"); $scratch.Add($linkSource)
                    New-CtsShortcut $linkSource $Context.Root
                    $incoming[$Context[$name]] = @{ Source = $linkSource; Hash = Get-CtsHash $linkSource }
                }
            }
        }
        # Stage all new bytes before making the first change to the installed product.
        foreach ($path in @($incoming.Keys)) {
            $stage = [IO.Path]::Combine($transaction, "staged-$counter.bin"); $counter++; $scratch.Add($stage)
            [IO.File]::Copy($incoming[$path].Source, $stage, $false)
            if ((Get-CtsHash $stage) -cne $incoming[$path].Hash) { throw 'Staged payload verification failed.' }
            $incoming[$path].Source = $stage
        }
        Assert-CtsNotRunning
        foreach ($path in $paths) {
            Assert-CtsNoReparse $path
            $expected = $snapshots[$path].Hash
            $actual = if ([IO.File]::Exists($path)) { Get-CtsHash $path } else { $null }
            if ($actual -cne $expected) { throw "A destination changed after backup: $path" }
            if ($incoming.ContainsKey($path)) {
                New-CtsDirectory ([IO.Path]::GetDirectoryName($path)) $created
                $postHashes[$path] = $incoming[$path].Hash
            } else { $postHashes[$path] = $null }
            # Record intent before the native operation; an injected/late failure may
            # arrive after a replacement succeeded but before control returns.
            $applied.Add($path)
            if ($incoming.ContainsKey($path)) { Invoke-CtsFileOperation $incoming[$path].Source $path }
            else { Invoke-CtsFileOperation $null $path }
            $actual = if ([IO.File]::Exists($path)) { Get-CtsHash $path } else { $null }
            if ($actual -cne $postHashes[$path]) { throw "Final file verification failed: $path" }
        }
        $desired = @()
        foreach ($item in $registration) {
            $values = @{}; $kinds = @{}
            if ($Action -eq 'Install') {
                foreach ($name in $item.Values.Keys) { $values[$name] = $item.Values[$name]; $kinds[$name] = $item.Kinds[$name] }
                $values['ProductId'] = $script:CtsProductId; $values['InstallLocation'] = $Context.Root
                $values['DisplayVersion'] = $RequestedVersion; $values['DisplayName'] = 'Cut the Save'; $values['Publisher'] = 'tianqistudio'
                $values['DesktopShortcut'] = $Desktop; $values['StartMenuShortcut'] = $StartMenu
                $values['DisplayIcon'] = [IO.Path]::Combine($Context.Root, 'CutTheSave.exe')
                $values['UninstallString'] = '"' + [IO.Path]::Combine($Context.Root, 'Uninstall.exe') + '"'
                $values['QuietUninstallString'] = $values['UninstallString'] + ' /S'
                $values['NoModify'] = 1; $values['NoRepair'] = 1
                foreach ($name in $values.Keys) { if ($values[$name] -is [int]) { $kinds[$name] = 'DWord' } else { $kinds[$name] = 'String' } }
            }
            $desired += @{ Path = $item.Path; Exists = ($Action -eq 'Install'); Values = $values; Kinds = $kinds }
        }
        $registryChanged = $true
        Set-CtsRegistration $Context $desired
        $complete = $true
    } catch {
        $failure = $_
        try {
            if ($registryChanged) { Set-CtsRegistration $Context $registration }
            for ($index = $applied.Count - 1; $index -ge 0; $index--) {
                $path = $applied[$index]; Assert-CtsNoReparse $path
                $actual = if ([IO.File]::Exists($path)) { Get-CtsHash $path } else { $null }
                if ($actual -ceq $snapshots[$path].Hash) { continue }
                if ($actual -cne $postHashes[$path]) { throw "Rollback retained an externally changed file: $path" }
                $backup = $snapshots[$path].Backup
                if ($backup) {
                    if ((Get-CtsHash $backup) -cne $snapshots[$path].Hash) { throw 'Rollback backup changed.' }
                    if ([IO.File]::Exists($path)) { [IO.File]::Replace($backup, $path, [NullString]::Value) } else { [IO.File]::Move($backup, $path) }
                    if ((Get-CtsHash $path) -cne $snapshots[$path].Hash) { throw 'Rollback verification failed.' }
                } elseif ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
            }
            $rolledBack = $true
        } catch { throw "Installation failed; recovery needs review. Evidence retained at $transaction. Cause: $failure. Recovery: $_" }
        throw $failure
    } finally {
        if ($complete -or $rolledBack) {
            try {
                foreach ($path in $scratch) { Assert-CtsNoReparse $path; if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
                Remove-CtsEmptyDirectories $created
                if ($Action -eq 'Uninstall' -and $complete) {
                    $directories = @($Context.Root)
                    foreach ($path in $oldFiles.Keys) {
                        $directory = [IO.Path]::GetDirectoryName($path)
                        while ($directory -and $directory.StartsWith($Context.Root + '\', [StringComparison]::OrdinalIgnoreCase)) { $directories += $directory; $directory = [IO.Path]::GetDirectoryName($directory) }
                    }
                    Remove-CtsEmptyDirectories $directories
                }
            } catch { Write-Warning "Verified operation finished, but temporary files were retained at $transaction : $_" }
        }
    }
}

if (!$LibraryOnly) {
    try {
        if (![Environment]::Is64BitProcess -or ![Environment]::Is64BitOperatingSystem) { throw 'Run this maintenance script with 64-bit Windows PowerShell.' }
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator rights are required.' }
        $mutex = New-Object Threading.Mutex($false, 'Global\tianqistudio.CutTheSave.Installer')
        $held = $false
        try {
            try { $held = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
            if (!$held) { throw 'Another installation operation is active.' }
            Invoke-CtsMaintenance (Get-CtsContext) $Mode $PayloadDirectory $UninstallerPath $Version $DesktopShortcut $StartMenuShortcut
        } finally { if ($held) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
        Write-Output 'Cut the Save maintenance completed. User data and RTSS recovery data were retained.'
        exit 0
    } catch { Write-Output $_.Exception.Message; exit 1 }
}
