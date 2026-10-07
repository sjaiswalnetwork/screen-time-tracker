@echo off
title Installing Screen Time Tracker
if not exist "%~dp0app\install.ps1" (
  echo.
  echo  Please EXTRACT the zip first:
  echo  right-click the downloaded zip file, choose "Extract All...", then open the extracted folder
  echo  and double-click "Install Screen Time Tracker" again.
  echo.
  pause
  exit /b 1
)
echo.
echo  Installing Screen Time Tracker... this takes a few seconds.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0.' -Recurse -File | Unblock-File"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0app\install.ps1"
