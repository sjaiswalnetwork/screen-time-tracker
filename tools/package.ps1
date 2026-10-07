# Builds release\ScreenTimeTracker.zip - the file people download:
#   Screen Time Tracker\
#     Install Screen Time Tracker.cmd     <- double-click to install
#     START HERE - How to use.html        <- opens the guide
#     app\ (src, assets, docs, launch.ps1, install.ps1, uninstall.ps1, README.md, LICENSE)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $root "release"
$stage = Join-Path $out "Screen Time Tracker"
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage "app") | Out-Null

foreach ($item in "src", "assets", "docs", "launch.ps1", "install.ps1", "uninstall.ps1", "README.md", "LICENSE") {
    Copy-Item -LiteralPath (Join-Path $root $item) -Destination (Join-Path $stage "app") -Recurse
}
# the website-only sample PDF isn't needed inside the app
$sample = Join-Path $stage "app\docs\sample-daily-report.pdf"
if (Test-Path -LiteralPath $sample) { Remove-Item -LiteralPath $sample }

# installer with Windows line endings
$cmd = [IO.File]::ReadAllText((Join-Path $root "packaging\Install Screen Time Tracker.cmd")) -replace "`r?`n", "`r`n"
[IO.File]::WriteAllText((Join-Path $stage "Install Screen Time Tracker.cmd"), $cmd, (New-Object Text.ASCIIEncoding))

# START HERE: opens the guide inside app\docs
$start = '<!DOCTYPE html><html><head><meta charset="utf-8"><meta http-equiv="refresh" content="0; url=app/docs/guide.html"><title>Screen Time Tracker - How to use</title></head>' +
         '<body style="font-family:Segoe UI,sans-serif;padding:40px">Opening the guide... <a href="app/docs/guide.html">click here if it doesn''t open</a>.</body></html>'
[IO.File]::WriteAllText((Join-Path $stage "START HERE - How to use.html"), $start, (New-Object Text.UTF8Encoding $false))

$zip = Join-Path $out "ScreenTimeTracker.zip"
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("Built {0} ({1:N0} KB)" -f $zip, ((Get-Item $zip).Length / 1KB))
