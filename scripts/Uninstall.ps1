[CmdletBinding()]
param([string]$TestRoot, [switch]$FromWrapper)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Installation.psm1') -Force
$context = Get-InstallationContext -TestRoot $TestRoot
Assert-OwnedInstallation $context
$ownsStartup = Get-OwnedStartup $context -AllowUnrelated
Stop-OwnedApplication $context
$names = @('CodexQuotaPet.exe', 'CodexQuotaPet.exe.config', 'README.md', 'README.en.md', 'LICENSE', 'VERSION', 'Uninstall.ps1', 'Installation.psm1', 'Uninstall.cmd', '卸载.cmd', 'error.txt', 'state.json.tmp')
if ($FromWrapper) { $names = @($names | Where-Object { $_ -ne 'Uninstall.cmd' -and $_ -ne '卸载.cmd' }) }
$statePath = Join-Path $context.Directory 'state.json'
if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    Assert-RegularPath -Path $statePath
    try { if ((Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).app -eq 'CodexQuotaPet') { $names += 'state.json' } } catch { }
}
foreach ($name in $names) { Assert-RegularPath -Path (Join-Path $context.Directory $name) }
if ($ownsStartup) { Remove-Item -LiteralPath $context.Startup -Force }
foreach ($name in $names) {
    $path = [IO.Path]::GetFullPath((Join-Path $context.Directory $name))
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($path), $context.Directory, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe removal path.' }
    if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
}
if (Test-Path -LiteralPath $context.Directory) {
    $remaining = @(Get-ChildItem -LiteralPath $context.Directory -Force | Where-Object Name -ne '.codexquotapet-owned')
    if ($remaining.Count -eq 0) {
        if (Test-Path -LiteralPath $context.Marker) { Remove-Item -LiteralPath $context.Marker -Force }
        Remove-Item -LiteralPath $context.Directory -Force
    }
}
if ($FromWrapper -and (Test-Path -LiteralPath $context.Directory)) {
    # cmd.exe reads its batch file again after PowerShell returns. Leave only the
    # wrappers for a short delayed cleanup; all executable files are already gone.
    $workerPath = Join-Path ([IO.Path]::GetTempPath()) ('CodexQuotaPet-cleanup-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    $worker = @'
param([string]$InstallationDirectory, [int]$ParentProcessId)
$ErrorActionPreference = 'Stop'
try {
    $parent = Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
    if ($parent -and -not $parent.WaitForExit(15000)) { return }
    Start-Sleep -Milliseconds 1000
    $target = [IO.Path]::GetFullPath($InstallationDirectory).TrimEnd('\')
    if ([IO.Path]::GetFileName($target) -ne 'CodexQuotaPet' -or -not (Test-Path -LiteralPath $target -PathType Container)) { return }
    if ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return }
    $marker = Join-Path $target '.codexquotapet-owned'
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or ((Get-Item -LiteralPath $marker -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -or (Get-Content -LiteralPath $marker -Raw).Trim() -ne 'CodexQuotaPet.Installation.v1') { return }
    # A new installation wins this race; never remove files from a reinstall.
    if (Test-Path -LiteralPath (Join-Path $target 'CodexQuotaPet.exe')) { return }
    foreach ($name in @('Uninstall.cmd', '卸载.cmd')) {
        $path = [IO.Path]::GetFullPath((Join-Path $target $name))
        if (-not [string]::Equals([IO.Path]::GetDirectoryName($path), $target, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe wrapper path.' }
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force
            if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return }
            Remove-Item -LiteralPath $path -Force
        }
    }
    if (@(Get-ChildItem -LiteralPath $target -Force | Where-Object Name -ne '.codexquotapet-owned').Count -eq 0) {
        Remove-Item -LiteralPath $marker -Force
        Remove-Item -LiteralPath $target -Force
    }
} finally { Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue }
'@
    Set-Content -LiteralPath $workerPath -Value $worker -Encoding UTF8
    $powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    Start-Process -FilePath $powershell -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $workerPath + '"'), '-InstallationDirectory', ('"' + $context.Directory + '"'), '-ParentProcessId', [string]$PID)
}
Write-Output 'Uninstalled. Unknown user files, generated template cache, and all Codex files were preserved.'
