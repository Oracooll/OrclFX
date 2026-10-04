# Builds Orcl File Explorer and its tests into dist\tests\ and runs them.
#   .\test.ps1              unit tests (no windows open)
#   .\test.ps1 -Smoke       also starts the app with throw-away settings, checks it opens, saves and closes cleanly
#   .\test.ps1 -Filter Merge  only tests whose name contains "Merge"
param([switch]$Smoke, [string]$Filter)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$outDir = Join-Path $root 'dist\tests'
$app = Join-Path $outDir 'orclfx.exe'
$tests = Join-Path $outDir 'orclfx.tests.exe'

& (Join-Path $root 'build.ps1') -Out $app
$sources = Get-ChildItem (Join-Path $root 'tests') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:exe /platform:anycpu "/out:$tests" "/r:$app" /r:System.Windows.Forms.dll /r:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }

Write-Host ""
if ($Filter) { & $tests $Filter } else { & $tests }
$failed = $LASTEXITCODE

if ($Smoke) {
    Write-Host ""
    Write-Host "Smoke test: starting the app with temporary settings"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("orclfx-smoke-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $state = Join-Path $tmp 'state.txt'
    $env:DUALPANE_STATE = $state
    $env:DUALPANE_SHORTCUTS = Join-Path $tmp 'shortcuts.txt'
    $problems = @()
    try {
        $p = Start-Process -FilePath $app -ArgumentList '--portable' -PassThru
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt 20 -and -not $p.HasExited) {
            $p.Refresh()
            if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 200
        }
        if ($p.HasExited) { $problems += "the app exited on its own (code $($p.ExitCode))" }
        elseif ($p.MainWindowHandle -eq [IntPtr]::Zero) { $problems += "no window appeared within 20 seconds" }
        else {
            Start-Sleep -Seconds 4   # let the views and the shortcuts pane load
            if ($p.HasExited) { $problems += "the app closed shortly after opening" }
            else {
                [void]$p.CloseMainWindow()
                if (-not $p.WaitForExit(15000)) { $problems += "the app didn't close within 15 seconds"; $p.Kill() }
            }
        }
        if (-not (Test-Path $state) -or -not (Select-String -Path $state -Pattern '^pane0\.tab=' -Quiet)) { $problems += "the settings file wasn't saved with tabs" }
        $log = Join-Path $tmp 'errors.log'
        if (Test-Path $log) { $problems += "errors were logged:`n" + (Get-Content $log -Raw) }
    }
    finally {
        Remove-Item Env:DUALPANE_STATE, Env:DUALPANE_SHORTCUTS -ErrorAction SilentlyContinue
        [IO.Directory]::Delete($tmp, $true)
    }
    if ($problems.Count -eq 0) { Write-Host "  pass  smoke test (opened, saved its settings, closed)" }
    else { foreach ($m in $problems) { Write-Host "  FAIL  smoke test: $m" -ForegroundColor Red }; $failed++ }
}

if ($failed -ne 0) { Write-Host ""; Write-Host "$failed failure(s)" -ForegroundColor Red; exit 1 }
