# Runs Screen Time Tracker straight from source, compiled in memory by PowerShell.
# Use this on PCs where Windows Smart App Control blocks the unsigned "Screen Time Tracker.exe".
# Normal start (no console window):  powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File launch.ps1
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ps1 = $MyInvocation.MyCommand.Path

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes, WindowsBase
$files = Get-ChildItem (Join-Path $here "src") -Filter *.cs | Where-Object { $_.Name -ne "AssemblyInfo.cs" } | ForEach-Object { $_.FullName }
Add-Type -Path $files -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Core, UIAutomationClient, UIAutomationTypes, WindowsBase

$icon = Join-Path $here "assets\screentime.ico"
$runCmd = "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$ps1`" --minimized"
$argv = @("--icon", $icon, "--run-cmd", $runCmd)
if ($Rest) { $argv += $Rest }
exit [Program]::Main([string[]]$argv)
