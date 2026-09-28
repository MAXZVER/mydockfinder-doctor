# Builds bin\DockDoctor.exe with the .NET Framework compiler (no SDK needed).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$bin\DockDoctor.exe" (Join-Path $root 'src\DockDoctor.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Write-Host "Built $bin\DockDoctor.exe"
