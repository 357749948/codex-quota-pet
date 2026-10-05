$ErrorActionPreference = 'Stop'

function Assert-RegularPath {
    param([string]$Path, [switch]$Directory)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or ($item.PSIsContainer -ne [bool]$Directory)) { throw 'Unexpected file type or symbolic link; no changes were made.' }
}

function Get-InstallationContext {
    param([string]$TestRoot)
    $isTest = -not [string]::IsNullOrEmpty($TestRoot)
    if ($isTest) {
        $root = [IO.Path]::GetFullPath($TestRoot).TrimEnd('\')
        Assert-RegularPath -Path $root -Directory
        $marker = Join-Path $root '.codexquotapet-test-root'
        Assert-RegularPath -Path $marker
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -LiteralPath $marker -Raw).Trim() -ne 'CodexQuotaPet.IsolatedTests.v1') { throw '-TestRoot requires a prepared, isolated test directory.' }
        $localRoot = Join-Path $root 'LocalAppData'
        $startupRoot = Join-Path $root 'Startup'
    } else {
        $localRoot = [IO.Path]::GetFullPath([Environment]::GetFolderPath('LocalApplicationData')).TrimEnd('\')
        $startupRoot = [Environment]::GetFolderPath('Startup')
    }
    Assert-RegularPath -Path $localRoot -Directory
    Assert-RegularPath -Path $startupRoot -Directory
    $directory = [IO.Path]::GetFullPath((Join-Path $localRoot 'CodexQuotaPet'))
    if (-not [string]::Equals($directory, ($localRoot + '\CodexQuotaPet'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Installation path validation failed.' }
    return @{ Directory = $directory; Executable = (Join-Path $directory 'CodexQuotaPet.exe'); Marker = (Join-Path $directory '.codexquotapet-owned'); Startup = (Join-Path $startupRoot 'CodexQuotaPet.lnk'); Test = $isTest }
}

function Assert-OwnedInstallation {
    param($Context)
    Assert-RegularPath -Path $Context.Directory -Directory
    if (-not (Test-Path -LiteralPath $Context.Directory)) { return }
    Assert-RegularPath -Path $Context.Marker
    if (@(Get-ChildItem -LiteralPath $Context.Directory -Force).Count -gt 0 -and (-not (Test-Path -LiteralPath $Context.Marker -PathType Leaf) -or (Get-Content -LiteralPath $Context.Marker -Raw).Trim() -ne 'CodexQuotaPet.Installation.v1')) { throw 'This directory is not owned by CodexQuotaPet; it was left unchanged.' }
}

function Get-OwnedProcesses {
    param($Context)
    @(Get-Process -Name 'CodexQuotaPet' -ErrorAction SilentlyContinue | Where-Object { try { [string]::Equals($_.Path, $Context.Executable, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
}

function Stop-OwnedApplication {
    param($Context)
    if (@(Get-OwnedProcesses $Context).Count -eq 0) { return }
    $exitProcess = Start-Process -FilePath $Context.Executable -ArgumentList '--exit' -WindowStyle Hidden -PassThru
    if (-not $exitProcess.WaitForExit(5000)) { throw 'The application did not accept its exit request.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (@(Get-OwnedProcesses $Context).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (@(Get-OwnedProcesses $Context).Count -gt 0) { throw 'The application is still running; its files were preserved.' }
}

function Get-OwnedStartup {
    param($Context, [switch]$AllowUnrelated)
    Assert-RegularPath -Path $Context.Startup
    if (-not (Test-Path -LiteralPath $Context.Startup)) { return $false }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Context.Startup)
    $owned = [string]::Equals($shortcut.TargetPath, $Context.Executable, [StringComparison]::OrdinalIgnoreCase)
    if (-not $owned -and -not $AllowUnrelated) { throw 'An unrelated startup shortcut has the same name; it was left unchanged.' }
    return $owned
}

Export-ModuleMember -Function Assert-RegularPath, Get-InstallationContext, Assert-OwnedInstallation, Get-OwnedProcesses, Stop-OwnedApplication, Get-OwnedStartup
