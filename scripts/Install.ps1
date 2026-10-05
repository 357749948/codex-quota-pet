[CmdletBinding()]
param([string]$SourceDirectory, [switch]$NoLaunch, [switch]$AutoStart, [string]$TestRoot)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Installation.psm1') -Force
if ($TestRoot -and -not $NoLaunch) { throw 'Isolated installation tests require -NoLaunch.' }
if (-not $SourceDirectory) {
    $SourceDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'CodexQuotaPet.exe')) { $PSScriptRoot } else { Join-Path $PSScriptRoot '..\dist' }
}
$SourceDirectory = [IO.Path]::GetFullPath($SourceDirectory)
$context = Get-InstallationContext -TestRoot $TestRoot
Assert-OwnedInstallation $context
$wasAutoStart = Get-OwnedStartup $context
$files = @('CodexQuotaPet.exe', 'CodexQuotaPet.exe.config', 'Uninstall.ps1', 'Installation.psm1', 'Uninstall.cmd', 'README.md', 'README.en.md', 'LICENSE', 'VERSION')
foreach ($name in $files) {
    $source = Join-Path $SourceDirectory $name
    Assert-RegularPath -Path $source
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required build file missing: $name. Run build.ps1 first." }
    Assert-RegularPath -Path (Join-Path $context.Directory $name)
}
if (-not $context.Test) {
    $foreign = @(Get-Process -Name 'CodexQuotaPet' -ErrorAction SilentlyContinue | Where-Object { try { -not [string]::Equals($_.Path, $context.Executable, [StringComparison]::OrdinalIgnoreCase) } catch { $true } })
    if ($foreign.Count -gt 0) { throw 'Another portable copy is running. Exit it from its tray menu before installing; it was not stopped.' }
}
Stop-OwnedApplication $context
New-Item -ItemType Directory -Path $context.Directory -Force | Out-Null
Set-Content -LiteralPath $context.Marker -Value 'CodexQuotaPet.Installation.v1' -Encoding ASCII
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $SourceDirectory $name) -Destination (Join-Path $context.Directory $name) -Force }
if ($AutoStart -or $wasAutoStart) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($context.Startup)) -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($context.Startup)
    $shortcut.TargetPath = $context.Executable
    $shortcut.WorkingDirectory = $context.Directory
    $shortcut.Description = 'Codex pet quota overlay'
    $shortcut.WindowStyle = 7
    $shortcut.IconLocation = $context.Executable + ',0'
    $shortcut.Save()
}
if (-not $NoLaunch) { Start-Process -FilePath $context.Executable -WorkingDirectory $context.Directory -WindowStyle Hidden }
Write-Output ('Installed. Login startup: ' + [bool]($AutoStart -or $wasAutoStart))
