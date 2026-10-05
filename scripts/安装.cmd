@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
if errorlevel 1 (
  echo Installation failed. Please keep the error text above.
  pause
  exit /b 1
)
echo Installation complete.
pause
