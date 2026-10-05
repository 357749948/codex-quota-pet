[CmdletBinding()]
param([switch]$Live, [switch]$DesktopProbe, [string]$PythonPath)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
. (Join-Path $PSScriptRoot 'scripts\BuildSupport.ps1')
$environment = Get-BuildEnvironment -RepositoryRoot $PSScriptRoot
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$testDirectory = Join-Path $PSScriptRoot '.build\tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$mock = Join-Path $testDirectory 'MockAppServer.exe'
$offline = Join-Path $testDirectory 'OfflineTests.exe'
Invoke-CSharpBuild -Environment $environment -Sources @((Join-Path $PSScriptRoot 'tests\MockAppServer.cs')) -OutputPath $mock -Main 'MockAppServer'
Invoke-CSharpBuild -Environment $environment -Sources ($sources + @((Join-Path $PSScriptRoot 'tests\OfflineTests.cs'))) -OutputPath $offline -Main 'OfflineTests'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\App.config') -Destination ($offline + '.config') -Force
& $offline $mock (Join-Path $testDirectory 'mock-control.txt')
if ($LASTEXITCODE -ne 0) { throw 'Offline C# tests failed.' }
& (Join-Path $PSScriptRoot 'tests\Installation.Tests.ps1') -RepositoryRoot $PSScriptRoot
if (-not $PythonPath) {
    $PythonPath = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $PythonPath -PathType Leaf)) { throw 'Template tests require Python dependencies. Run prepare-templates.ps1, or create .venv and install tools/requirements.txt. Use -PythonPath for an existing test environment.' }
}
Push-Location $PSScriptRoot
try {
    & $PythonPath -m unittest discover -s tests -p 'test_templates.py' -v
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic Python template tests failed.' }
} finally { Pop-Location }
if ($Live) {
    $liveExecutable = Join-Path $testDirectory 'LiveQuotaTest.exe'
    Invoke-CSharpBuild -Environment $environment -Sources ($sources + @((Join-Path $PSScriptRoot 'tests\LiveQuotaTest.cs'))) -OutputPath $liveExecutable -Main 'LiveQuotaTest'
    & $liveExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Opt-in real-account quota test failed.' }
}
if ($DesktopProbe) {
    $report = Join-Path $testDirectory 'desktop-probe.json'
    $probe = New-Object Diagnostics.Process
    $probe.StartInfo = New-Object Diagnostics.ProcessStartInfo
    $probe.StartInfo.FileName = Join-Path $PSScriptRoot 'dist\CodexQuotaPet.exe'
    $probe.StartInfo.Arguments = '--probe --report "' + $report + '"'
    $probe.StartInfo.UseShellExecute = $false
    $probe.StartInfo.CreateNoWindow = $true
    [void]$probe.Start()
    if (-not $probe.WaitForExit(20000)) { $probe.Kill(); throw 'Opt-in desktop probe timed out.' }
    if ($probe.ExitCode -ne 0) { throw 'Opt-in desktop probe failed.' }
    Write-Output 'Desktop probe completed. Review .build/tests/desktop-probe.json locally; do not publish it without removing personal data.'
}
Write-Output 'All requested tests passed.'
