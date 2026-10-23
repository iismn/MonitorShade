param([switch]$IncludeUi, [string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'MonitorShade.exe'
if ($ExecutablePath) { $executable = [IO.Path]::GetFullPath($ExecutablePath) }
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'Build first: powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1'
}
$reports = Join-Path $PSScriptRoot 'tests'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$checks = @('reconnect', 'recovery', 'manual', 'brightness', 'wallpaper', 'logic', 'layout', 'appearance')
if ($IncludeUi) { $checks += 'web-ui' }

foreach ($check in $checks) {
    $report = Join-Path $reports ($check + '.txt')
    # Remove only this generated report so a failed launch cannot reuse an older result.
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    $process = Start-Process -FilePath $executable -ArgumentList @('--' + $check + '-test', ('"' + $report + '"')) -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(30000)) {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
            throw "Timed out: $check"
        }
        $process.Refresh()
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
            throw "Failed: $check. Check $report"
        }
        $lines = @(Get-Content -LiteralPath $report)
        if (@($lines | Where-Object { $_ -match '^FAIL:' }).Count -gt 0) { throw "Failed: $check. Check $report" }
        $passed = @($lines | Where-Object { $_ -match '^PASS:' }).Count
        if ($passed -eq 0) { throw "No passing checks reported: $check" }
        Write-Output ("{0}: {1} checks passed" -f $check, $passed)
    }
    finally { $process.Dispose() }
}
