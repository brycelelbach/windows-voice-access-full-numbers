[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$name = 'Windows Voice Access Full Numbers'
$shortcutName = 'Full Numbers'
$programs = [Environment]::GetFolderPath('Programs')
$appDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Programs\$name"
$exe = Join-Path $appDir "$name.exe"
$licensePath = Join-Path $appDir 'LICENSE'
$link = Join-Path $programs "$shortcutName.lnk"
$id = [guid]::NewGuid().ToString('N')
$stage = Join-Path $appDir ".install-$id"
$stageLink = Join-Path $programs ".$name-$id.lnk"
$backupExe = Join-Path $stage 'previous.exe'
$stagedLicense = Join-Path $stage 'LICENSE'
$backupLicense = Join-Path $stage 'previous.license'
$backupLink = Join-Path $programs ".$name-$id.bak"
$mutex = New-Object Threading.Mutex($false, 'Local\WindowsVoiceAccessFullNumbers-Installer')
$owned = $false
$exeChanged = $false
$licenseChanged = $false
$linkChanged = $false
$cleanBackups = $true
try {
    try { $owned = $mutex.WaitOne(30000) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (!$owned) { throw 'Another installation is in progress.' }
    [void][IO.Directory]::CreateDirectory($stage)
    [void][IO.Directory]::CreateDirectory($programs)
    $built = & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $stage
    [IO.File]::Copy((Join-Path $PSScriptRoot 'LICENSE'), $stagedLicense)
    $shell = New-Object -ComObject WScript.Shell
    if ([IO.File]::Exists($link) -and $shell.CreateShortcut($link).TargetPath -ne $exe) {
        throw "An existing Start shortcut points to another program: $link"
    }
    $shortcut = $shell.CreateShortcut($stageLink)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $appDir
    $shortcut.Description = 'Number available controls across visible Windows apps'
    $shortcut.Save()
    if ([IO.File]::Exists($exe)) { [IO.File]::Replace($built.FullName, $exe, $backupExe) }
    else { [IO.File]::Move($built.FullName, $exe) }
    $exeChanged = $true
    if ([IO.File]::Exists($licensePath)) { [IO.File]::Replace($stagedLicense, $licensePath, $backupLicense) }
    else { [IO.File]::Move($stagedLicense, $licensePath) }
    $licenseChanged = $true
    if ([IO.File]::Exists($link)) { [IO.File]::Replace($stageLink, $link, $backupLink) }
    else { [IO.File]::Move($stageLink, $link) }
    $linkChanged = $true
    $release = Get-Content -Raw -LiteralPath "$PSScriptRoot\release.json" | ConvertFrom-Json
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()
    if ($hash -ne $release.sha256 -or $shell.CreateShortcut($link).TargetPath -ne $exe) {
        throw 'Installed file or shortcut verification failed.'
    }
    [pscustomobject]@{ Version = $release.version; Executable = $exe; License = $licensePath; Shortcut = $link; SHA256 = $hash }
} catch {
    $failure = $_
    try {
        if ($linkChanged) {
            if ([IO.File]::Exists($backupLink)) { [IO.File]::Copy($backupLink, $link, $true) }
            else { [IO.File]::Delete($link) }
        }
        if ($licenseChanged) {
            if ([IO.File]::Exists($backupLicense)) { [IO.File]::Copy($backupLicense, $licensePath, $true) }
            else { [IO.File]::Delete($licensePath) }
        }
        if ($exeChanged) {
            if ([IO.File]::Exists($backupExe)) { [IO.File]::Copy($backupExe, $exe, $true) }
            else { [IO.File]::Delete($exe) }
        }
    } catch {
        $cleanBackups = $false
        throw "Installation failed and rollback needs attention. Backups are in $stage. $failure. $_"
    }
    throw $failure
} finally {
    try {
        if ([IO.File]::Exists($stageLink)) { [IO.File]::Delete($stageLink) }
        if ($cleanBackups -and [IO.Directory]::Exists($stage)) {
            foreach ($file in @($backupExe, $backupLink, $backupLicense, $stagedLicense, (Join-Path $stage "$name.exe"))) {
                if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
            }
            [IO.Directory]::Delete($stage)
        }
    } finally {
        if ($owned) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
