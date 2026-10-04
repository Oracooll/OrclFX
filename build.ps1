# Builds dist\orclfx.exe (Orcl File Explorer) with the C# compiler that ships with Windows (.NET Framework 4.x).
param([string]$Out)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

# Regenerate the icon whenever Icon.png is newer than src\app.ico.
$png = Join-Path $root 'Icon.png'
$ico = Join-Path $root 'src\app.ico'
if ((Test-Path $png) -and (-not (Test-Path $ico) -or (Get-Item $png).LastWriteTime -gt (Get-Item $ico).LastWriteTime)) {
    & (Join-Path $root 'src\make-icon.ps1') -Png $png -Out $ico
}

if (-not $Out) { $Out = Join-Path $root 'dist\orclfx.exe' }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
& $csc /nologo /target:winexe /platform:anycpu /optimize+ `
    "/out:$Out" "/win32manifest:$root\src\app.manifest" "/win32icon:$ico" "/resource:$ico,app.ico" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll "$root\src\OrclFileExplorer.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $Out"

