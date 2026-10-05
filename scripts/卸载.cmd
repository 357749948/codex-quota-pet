@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
if errorlevel 1 (
  echo Uninstall failed. Please keep the error text above.
  pause
  exit /b 1
)
echo Uninstall requested. Cleanup completes in a moment.
pause
