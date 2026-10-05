[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\BuildSupport.ps1')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'dist' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$environment = Get-BuildEnvironment -RepositoryRoot $PSScriptRoot
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
Invoke-CSharpBuild -Environment $environment -Sources $sources -OutputPath (Join-Path $OutputDirectory 'CodexQuotaPet.exe') -Main 'CodexQuotaPet.Program' -Windowed
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\App.config') -Destination (Join-Path $OutputDirectory 'CodexQuotaPet.exe.config') -Force
foreach ($name in @('Install.ps1', 'Uninstall.ps1', 'Installation.psm1', 'Install.cmd', 'Uninstall.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('scripts\' + $name)) -Destination (Join-Path $OutputDirectory $name) -Force }
foreach ($name in @('README.md', 'README.en.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'VERSION')) {
    $source = Join-Path $PSScriptRoot $name
    if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination (Join-Path $OutputDirectory $name) -Force }
}
Write-Output ('Built Codex Quota Pet ' + $environment.Version + ': ' + (Join-Path $OutputDirectory 'CodexQuotaPet.exe'))
