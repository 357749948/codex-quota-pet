$ErrorActionPreference = 'Stop'

function Get-BuildEnvironment {
    param([string]$RepositoryRoot)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows x64 is required.' }
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'A 64-bit Windows installation is required.' }
    $release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
    if (-not $release -or $release -lt 528040) { throw '.NET Framework 4.8 or later is required.' }
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The Windows .NET Framework x64 C# compiler was not found.' }
    $version = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'VERSION') -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain major.minor.patch.' }
    $generated = Join-Path $RepositoryRoot '.build\generated'
    New-Item -ItemType Directory -Path $generated -Force | Out-Null
    $versionSource = Join-Path $generated 'VersionInfo.cs'
    [IO.File]::WriteAllText($versionSource, ('namespace CodexQuotaPet { internal static class BuildVersion { public const string Value = "' + $version + '"; public const string Assembly = "' + $version + '.0"; } }'), [Text.UTF8Encoding]::new($false))
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src\app.manifest') -Raw
    $manifest.assembly.assemblyIdentity.SetAttribute('version', ($version + '.0'))
    $manifestPath = Join-Path $generated 'app.manifest'
    $manifest.Save($manifestPath)
    $references = @()
    foreach ($assembly in @('System.dll', 'System.Core.dll', 'System.Web.Extensions.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xaml.dll')) { $references += '/reference:' + (Join-Path $framework $assembly) }
    foreach ($assembly in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'UIAutomationClient.dll', 'UIAutomationTypes.dll')) { $references += '/reference:' + (Join-Path $framework ('WPF\' + $assembly)) }
    return @{ Compiler = $compiler; References = $references; VersionSource = $versionSource; Manifest = $manifestPath; Version = $version }
}

function Invoke-CSharpBuild {
    param($Environment, [string[]]$Sources, [string]$OutputPath, [string]$Main, [switch]$Windowed)
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($OutputPath)) -Force | Out-Null
    $target = if ($Windowed) { '/target:winexe' } else { '/target:exe' }
    $arguments = @('/nologo', '/utf8output', $target, '/platform:x64', '/langversion:5', '/optimize+', '/debug-', ('/main:' + $Main), ('/out:' + $OutputPath), ('/win32manifest:' + $Environment.Manifest)) + $Environment.References + @($Environment.VersionSource) + $Sources
    & $Environment.Compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "C# compilation failed ($LASTEXITCODE)." }
}
