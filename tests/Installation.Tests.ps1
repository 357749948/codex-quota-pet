param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$sandbox = Join-Path $RepositoryRoot ('.build\installation-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
Set-Content -LiteralPath (Join-Path $sandbox '.codexquotapet-test-root') -Value 'CodexQuotaPet.IsolatedTests.v1' -Encoding ASCII
$install = Join-Path $RepositoryRoot 'scripts\Install.ps1'
$uninstall = Join-Path $RepositoryRoot 'scripts\Uninstall.ps1'
$directory = Join-Path $sandbox 'LocalAppData\CodexQuotaPet'
$shortcut = Join-Path $sandbox 'Startup\CodexQuotaPet.lnk'
function Assert-Test { param([bool]$Value, [string]$Message) if (-not $Value) { throw "Installer test failed: $Message" } }

& $install -TestRoot $sandbox -NoLaunch
Assert-Test (Test-Path -LiteralPath (Join-Path $directory 'CodexQuotaPet.exe')) 'new installation copies program'
Assert-Test (-not (Test-Path -LiteralPath $shortcut)) 'new installation defaults startup off'
& $install -TestRoot $sandbox -NoLaunch
Assert-Test (-not (Test-Path -LiteralPath $shortcut)) 'upgrade keeps startup off'
& $install -TestRoot $sandbox -NoLaunch -AutoStart
Assert-Test (Test-Path -LiteralPath $shortcut) 'explicit startup creates shortcut'
& $install -TestRoot $sandbox -NoLaunch
Assert-Test (Test-Path -LiteralPath $shortcut) 'upgrade keeps startup on'
$shell = New-Object -ComObject WScript.Shell
Assert-Test ($shell.CreateShortcut($shortcut).TargetPath -eq (Join-Path $directory 'CodexQuotaPet.exe')) 'shortcut points to isolated install'
Set-Content -LiteralPath (Join-Path $directory 'user-note.txt') -Value 'preserve' -Encoding ASCII
# Invoke the installed uninstall script to prove it can remove its own script/module files.
& (Join-Path $directory 'Uninstall.ps1') -TestRoot $sandbox
Assert-Test (-not (Test-Path -LiteralPath $shortcut)) 'uninstall removes owned startup'
Assert-Test (-not (Test-Path -LiteralPath (Join-Path $directory 'CodexQuotaPet.exe'))) 'uninstall removes program'
Assert-Test ((Get-Content -LiteralPath (Join-Path $directory 'user-note.txt') -Raw).Trim() -eq 'preserve') 'uninstall preserves unknown files'
& $install -TestRoot $sandbox -NoLaunch
Assert-Test (Test-Path -LiteralPath (Join-Path $directory 'CodexQuotaPet.exe')) 'preserved ownership allows reinstall beside user files'
Remove-Item -LiteralPath (Join-Path $directory '.codexquotapet-owned') -Force
$refused = $false
try { & $install -TestRoot $sandbox -NoLaunch } catch { $refused = $true }
Assert-Test $refused 'unowned nonempty directory refused'
Set-Content -LiteralPath (Join-Path $directory '.codexquotapet-owned') -Value 'CodexQuotaPet.Installation.v1' -Encoding ASCII
Remove-Item -LiteralPath (Join-Path $directory 'user-note.txt') -Force
& $install -TestRoot $sandbox -NoLaunch
$other = $shell.CreateShortcut($shortcut)
$other.TargetPath = Join-Path $env:WINDIR 'notepad.exe'
$other.Save()
$refused = $false
try { & $install -TestRoot $sandbox -NoLaunch -AutoStart } catch { $refused = $true }
Assert-Test $refused 'unrelated startup shortcut refused'
& $uninstall -TestRoot $sandbox
Assert-Test (Test-Path -LiteralPath $shortcut) 'unrelated startup shortcut preserved'
Assert-Test (-not (Test-Path -LiteralPath $directory)) 'empty installation removed'
Remove-Item -LiteralPath $shortcut -Force

# Recreate a minimal owned 1.1.1 installation using synthetic files, never the real installation.
New-Item -ItemType Directory -Path $directory -Force | Out-Null
Set-Content -LiteralPath (Join-Path $directory '.codexquotapet-owned') -Value 'CodexQuotaPet.Installation.v1' -Encoding ASCII
Set-Content -LiteralPath (Join-Path $directory 'CodexQuotaPet.exe') -Value 'synthetic old version placeholder' -Encoding ASCII
Set-Content -LiteralPath (Join-Path $directory '卸载.cmd') -Value '@echo off' -Encoding ASCII
Set-Content -LiteralPath (Join-Path $directory 'state.json') -Value '{"app":"CodexQuotaPet","version":"1.1.1"}' -Encoding ASCII
$legacyShortcut = $shell.CreateShortcut($shortcut)
$legacyShortcut.TargetPath = Join-Path $directory 'CodexQuotaPet.exe'
$legacyShortcut.Save()
& $install -TestRoot $sandbox -NoLaunch
Assert-Test (Test-Path -LiteralPath $shortcut) 'legacy upgrade retains startup'
Assert-Test ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $directory 'CodexQuotaPet.exe')).FileVersion -eq ((Get-Content -LiteralPath (Join-Path $RepositoryRoot 'VERSION') -Raw).Trim() + '.0')) 'legacy binary upgraded'
$wrapper = Join-Path $directory 'Uninstall.cmd'
$wrapperArguments = '/d /c ""' + $wrapper + '" -TestRoot "' + $sandbox + '""'
$cmd = New-Object Diagnostics.Process
$cmd.StartInfo = New-Object Diagnostics.ProcessStartInfo
$cmd.StartInfo.FileName = $env:ComSpec
$cmd.StartInfo.Arguments = $wrapperArguments
$cmd.StartInfo.UseShellExecute = $false
$cmd.StartInfo.CreateNoWindow = $true
$cmd.StartInfo.RedirectStandardOutput = $true
$cmd.StartInfo.RedirectStandardError = $true
[void]$cmd.Start()
if (-not $cmd.WaitForExit(20000)) { $cmd.Kill(); throw 'Installed uninstall wrapper timed out.' }
if ($cmd.ExitCode -ne 0) { Write-Output $cmd.StandardError.ReadToEnd() }
Assert-Test ($cmd.ExitCode -eq 0) 'installed uninstall command exits successfully after self removal'
$cleanupDeadline = [DateTime]::UtcNow.AddSeconds(10)
while ((Test-Path -LiteralPath $directory) -and [DateTime]::UtcNow -lt $cleanupDeadline) { Start-Sleep -Milliseconds 100 }
Assert-Test (-not (Test-Path -LiteralPath $directory)) 'legacy owned files and installed wrapper removed'
Assert-Test (-not (Test-Path -LiteralPath $shortcut)) 'legacy startup removed'
Write-Output 'PASS installation, explicit startup, upgrade preference, legacy migration, installed wrapper uninstall, ownership and unrelated-file preservation (isolated sandbox)'
