# Stops DockDoctor, removes it from Startup and deletes %USERPROFILE%\MyDockFinderDoctor.
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:USERPROFILE 'MyDockFinderDoctor'
$exe = Join-Path $dest 'DockDoctor.exe'

if (Test-Path $exe) {
    Start-Process $exe -ArgumentList '--stop' -Wait
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Process DockDoctor -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    Get-Process DockDoctor -ErrorAction SilentlyContinue | Stop-Process -Force
}

Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'MyDockFinder Doctor.lnk') -ErrorAction SilentlyContinue
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Uninstalled.'
