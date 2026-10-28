@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -PackagePath "%~dp0."
if errorlevel 1 (
  echo DeskDots installation failed. See the error above.
  pause
  exit /b 1
)
echo DeskDots installation finished.
pause
