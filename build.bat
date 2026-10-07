@echo off
setlocal
cd /d "%~dp0"
REM Screen Time Tracker build script - needs nothing but Windows itself (uses the C# compiler that ships with .NET Framework 4.x).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
for %%I in ("%CSC%") do set WPF=%%~dpIWPF
if not exist dist mkdir dist
if not exist assets mkdir assets

if not exist assets\screentime.ico (
  echo Generating icon...
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\make-icon.ps1" "%~dp0assets\screentime.ico"
)

echo Compiling Screen Time Tracker...
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu /out:"dist\Screen Time Tracker.exe" /win32icon:assets\screentime.ico /resource:assets\screentime.ico,ScreenTime.ico /win32manifest:src\app.manifest /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%WPF%\UIAutomationClient.dll" /r:"%WPF%\UIAutomationTypes.dll" /r:"%WPF%\WindowsBase.dll" src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo Built "dist\Screen Time Tracker.exe"
endlocal
