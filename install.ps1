# Builds DockDoctor, installs it into %USERPROFILE%\MyDockFinderDoctor, adds it to
# Startup and starts it. Safe to re-run: stops the old copy first, keeps the .ini.
param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:USERPROFILE 'MyDockFinderDoctor'
$exe = Join-Path $dest 'DockDoctor.exe'

& (Join-Path $root 'build.ps1')

if (Test-Path $exe) {
    Start-Process $exe -ArgumentList '--stop' -Wait
    # The daemon wakes up immediately unless it is in the middle of a dock restart.
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Process DockDoctor -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    Get-Process DockDoctor -ErrorAction SilentlyContinue | Stop-Process -Force
}

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $root 'bin\DockDoctor.exe') $dest -Force

$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'MyDockFinder Doctor.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.Description = 'Restarts MyDockFinder when it leaks, hangs or crashes'
$shortcut.Save()
Write-Host "Startup shortcut: $lnk"

if (-not $NoStart) {
    Start-Process $exe -WorkingDirectory $dest
    Write-Host "Started. Log: $dest\DockDoctor.log"
}
