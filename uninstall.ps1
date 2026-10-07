# Removes Screen Time Tracker: stops it, removes auto-start, shortcuts, the "Installed apps" entry and the program files.
# Your recorded history (%APPDATA%\Screen Time Tracker) is kept unless you choose to delete it.
param([switch]$Silent)
Add-Type -AssemblyName System.Windows.Forms
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$title = "Screen Time Tracker"
if (-not $Silent -and [System.Windows.Forms.MessageBox]::Show("Uninstall Screen Time Tracker?", $title, "YesNo", "Question") -ne "Yes") { exit }

# stop the running tracker (it runs inside powershell.exe via launch.ps1, or as its own .exe)
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*launch.ps1*' -and $_.ProcessId -ne $PID } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-Process "Screen Time Tracker" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Screen Time Tracker" -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenTimeTracker" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\ScreenTimeTracker" -Recurse -ErrorAction SilentlyContinue
$programs = [Environment]::GetFolderPath("Programs")
foreach ($lnk in (Join-Path $programs "Screen Time Tracker.lnk"), (Join-Path $programs "Screen Time Tracker - How to use.lnk"),
                 (Join-Path ([Environment]::GetFolderPath("Desktop")) "Screen Time Tracker.lnk")) {
    if (Test-Path -LiteralPath $lnk) { Remove-Item -LiteralPath $lnk }
}

$data = Join-Path $env:APPDATA "Screen Time Tracker"
if (-not $Silent -and (Test-Path -LiteralPath $data)) {
    if ([System.Windows.Forms.MessageBox]::Show("Also delete your recorded screen time history?`n`n(Choose No to keep it, e.g. if you plan to install again.)", $title, "YesNo", "Warning", "Button2") -eq "Yes") {
        Remove-Item -LiteralPath $data -Recurse -Force
    }
}

# remove the program folder (only the standard install location), a moment after this script exits
$installed = Join-Path $env:LOCALAPPDATA "Programs\Screen Time Tracker"
if ($here.TrimEnd('\') -eq $installed.TrimEnd('\')) {
    Start-Process "$env:WINDIR\System32\cmd.exe" -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$installed`"" -WindowStyle Hidden
}
if (-not $Silent) { [System.Windows.Forms.MessageBox]::Show("Screen Time Tracker has been removed.", $title) | Out-Null }
