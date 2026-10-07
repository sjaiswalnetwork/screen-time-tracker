# Installs Screen Time Tracker for the current Windows user (no admin rights needed).
# Usually started by double-clicking "Install Screen Time Tracker.cmd".
#   - copies the app to %LOCALAPPDATA%\Programs\Screen Time Tracker
#   - asks whether it should start automatically with Windows
#   - adds Start menu + Desktop shortcuts and an entry in Settings > Apps > Installed apps
#   - starts it
# For testing without questions:  install.ps1 -Silent -Autostart yes|no [-NoStart]
param([switch]$Silent, [string]$Autostart = "", [switch]$NoStart)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:LOCALAPPDATA "Programs\Screen Time Tracker"
$title = "Screen Time Tracker"

function Ask($text, $icon = "Question") {
    if ($Silent) { return $true }
    return [System.Windows.Forms.MessageBox]::Show($text, $title, "YesNo", $icon) -eq "Yes"
}

try {
    # 1. stop a running copy (when updating)
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*launch.ps1*' -and $_.CommandLine -notlike '*--data*' -and $_.ProcessId -ne $PID } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    # 2. copy the program
    if ((Resolve-Path $src).Path.TrimEnd('\') -ne $dest.TrimEnd('\')) {
        New-Item -ItemType Directory -Force $dest | Out-Null
        foreach ($item in "src", "assets", "docs", "launch.ps1", "install.ps1", "uninstall.ps1", "README.md", "LICENSE") {
            $p = Join-Path $src $item
            if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $dest -Recurse -Force }
        }
    }
    Get-ChildItem -LiteralPath $dest -Recurse -File | Unblock-File

    $launcher = Join-Path $dest "launch.ps1"
    $icon = Join-Path $dest "assets\screentime.ico"
    $guide = Join-Path $dest "docs\guide.html"
    $ps = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
    $launchArgs = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$launcher`""

    # 3. shortcuts
    $shell = New-Object -ComObject WScript.Shell
    $programs = [Environment]::GetFolderPath("Programs")
    foreach ($t in (Join-Path $programs "Screen Time Tracker.lnk"), (Join-Path ([Environment]::GetFolderPath("Desktop")) "Screen Time Tracker.lnk")) {
        $s = $shell.CreateShortcut($t)
        $s.TargetPath = $ps; $s.Arguments = $launchArgs; $s.WorkingDirectory = $dest
        $s.IconLocation = $icon; $s.WindowStyle = 7
        $s.Description = "See how long you use your PC and each app"
        $s.Save()
    }
    if (Test-Path -LiteralPath $guide) {
        $s = $shell.CreateShortcut((Join-Path $programs "Screen Time Tracker - How to use.lnk"))
        $s.TargetPath = $guide; $s.IconLocation = $icon; $s.Save()
    }

    # 4. start with Windows? (the user chooses; changeable later from the tray icon or Settings)
    $auto = if ($Autostart -eq "yes") { $true } elseif ($Autostart -eq "no") { $false } else {
        Ask ("Start Screen Time Tracker automatically every time you turn on your PC?`n`n" +
             "YES (recommended): it starts by itself in the background - you never have to remember.`n" +
             "NO: it only runs when you open it from the Start menu or Desktop.`n`n" +
             "You can change this any time: right-click the clock icon near the time > 'Start automatically with Windows'.")
    }
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    if ($auto) { New-ItemProperty -Path $runKey -Name "Screen Time Tracker" -Value "$ps $launchArgs --minimized" -PropertyType String -Force | Out-Null }
    else { Remove-ItemProperty -Path $runKey -Name "Screen Time Tracker" -ErrorAction SilentlyContinue }
    New-Item -Path "HKCU:\Software\ScreenTimeTracker" -Force | Out-Null
    Set-ItemProperty "HKCU:\Software\ScreenTimeTracker" AutostartChosen ([int]$auto)

    # 5. Settings > Apps > Installed apps (with an Uninstall button)
    $un = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenTimeTracker"
    New-Item -Path $un -Force | Out-Null
    Set-ItemProperty $un DisplayName "Screen Time Tracker"
    Set-ItemProperty $un DisplayVersion "2.0.0"
    Set-ItemProperty $un Publisher "SJ"
    Set-ItemProperty $un DisplayIcon $icon
    Set-ItemProperty $un InstallLocation $dest
    Set-ItemProperty $un URLInfoAbout "https://sjaiswalnetwork.github.io/screen-time-tracker/"
    Set-ItemProperty $un UninstallString "$ps -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $dest 'uninstall.ps1')`""
    Set-ItemProperty $un NoModify 1 -Type DWord
    Set-ItemProperty $un NoRepair 1 -Type DWord

    # 6. start it
    if (-not $NoStart) { Start-Process $ps -ArgumentList $launchArgs -WindowStyle Hidden }

    if (-not $Silent) {
        $msg = "Screen Time Tracker is installed and running!`n`n" +
               "Look for the small clock icon near the time (bottom-right). If you don't see it, click the ^ arrow.`n" +
               "Click it - or press Ctrl + Alt + S - to see your screen time.`n`n" +
               $(if ($auto) { "It will start by itself every time you turn on your PC." } else { "Open it from the Start menu or Desktop whenever you want tracking." }) +
               "`n`nOpen the 'How to use' guide now?"
        if (Ask $msg "Information") { Start-Process $guide }
    }
    Write-Host "Installed to $dest (start with Windows: $auto)"
}
catch {
    $err = "Installation didn't finish: " + $_.Exception.Message
    if ($Silent) { Write-Error $err } else { [System.Windows.Forms.MessageBox]::Show($err, $title, "OK", "Error") | Out-Null }
    exit 1
}
