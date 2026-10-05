[CmdletBinding()]
param(
    [string]$CodexPath,
    [string]$PythonPath,
    [switch]$SkipDependencyInstall
)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$venvPython = Join-Path $repoRoot '.venv\Scripts\python.exe'

if (-not (Test-Path -LiteralPath $venvPython)) {
    if ($SkipDependencyInstall) { throw 'The repository .venv is missing. Run without -SkipDependencyInstall first.' }
    if ($PythonPath) {
        & $PythonPath -c "import sys,struct; assert sys.version_info[:2]==(3,13) and struct.calcsize('P')==8, 'Python 3.13 x64 is required'"
        if ($LASTEXITCODE -ne 0) { throw 'Python 3.13 x64 is required.' }
        & $PythonPath -m venv (Join-Path $repoRoot '.venv')
    } elseif (Get-Command py.exe -ErrorAction SilentlyContinue) {
        & py.exe -3.13 -c "import struct; assert struct.calcsize('P')==8, 'Python x64 is required'"
        if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.13 x64 or pass -PythonPath.' }
        & py.exe -3.13 -m venv (Join-Path $repoRoot '.venv')
    } else {
        $pythonCommand = Get-Command python.exe -ErrorAction Stop
        & $pythonCommand.Source -c "import sys,struct; assert sys.version_info[:2]==(3,13) and struct.calcsize('P')==8, 'Python 3.13 x64 is required'"
        if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.13 x64 or pass -PythonPath.' }
        & $pythonCommand.Source -m venv (Join-Path $repoRoot '.venv')
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated Python environment.' }
}
& $venvPython -c "import sys,struct; assert sys.version_info[:2]==(3,13) and struct.calcsize('P')==8"
if ($LASTEXITCODE -ne 0) { throw 'The repository .venv must use Python 3.13 x64.' }
if (-not $SkipDependencyInstall) {
    & $venvPython -m pip install --disable-pip-version-check --only-binary=:all: -r (Join-Path $repoRoot 'tools\requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the pinned template dependencies.' }
}
& $venvPython -c "import importlib.metadata as m; expected={'numpy':'2.4.4','opencv-python-headless':'4.13.0.92','Pillow':'12.2.0'}; assert all(m.version(k)==v for k,v in expected.items()), 'Dependency version mismatch'"
if ($LASTEXITCODE -ne 0) { throw 'Pinned dependencies are missing. Run without -SkipDependencyInstall.' }

if (-not $CodexPath) {
    $candidates = @()
    # Some desktop releases retain the ChatGPT.exe process name.
    foreach ($process in @(Get-Process -Name Codex,ChatGPT -ErrorAction SilentlyContinue)) {
        try {
            $candidate = Join-Path (Split-Path -Parent $process.Path) 'resources\app.asar'
            if (Test-Path -LiteralPath $candidate) { $candidates += $candidate }
        } catch { }
    }
    $candidates = @($candidates | Sort-Object -Unique)
    if ($candidates.Count -gt 1) { throw 'Multiple running Codex installations found. Select one with -CodexPath.' }
    if ($candidates.Count -eq 0) {
        foreach ($package in @(Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue)) {
            $candidate = Join-Path $package.InstallLocation 'app\resources\app.asar'
            if (Test-Path -LiteralPath $candidate) { $candidates += $candidate }
        }
        $candidates = @($candidates | Sort-Object -Unique)
    }
    if ($candidates.Count -ne 1) { throw 'Could not select one Codex desktop installation. Pass -CodexPath.' }
    $CodexPath = $candidates[0]
}
& $venvPython (Join-Path $repoRoot 'tools\generate_templates.py') --codex-path $CodexPath
if ($LASTEXITCODE -ne 0) { throw 'Local template generation failed. The overlay will stay hidden until a compatible cache is available.' }
