param(
    [Parameter(Mandatory = $true)][string]$ApplicationPath,
    [string]$OutputDirectory = '.build\readme-recordings'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdk = Join-Path $root '.build\webview2\lib\net462'
$core = Join-Path $sdk 'Microsoft.Web.WebView2.Core.dll'
if (-not (Test-Path -LiteralPath $core)) { throw 'Build the application first to obtain the pinned WebView2 SDK.' }
$recorder = Join-Path $output 'RecordUiDemos.exe'
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', $core)
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { Join-Path "$framework\WPF" $_ }
$arguments = @('/nologo','/target:winexe','/platform:x64','/codepage:65001',"/out:$recorder", "/win32manifest:$root\source\app.manifest")
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += Join-Path $PSScriptRoot 'RecordUiDemos.cs'
& (Join-Path $framework 'csc.exe') $arguments
if ($LASTEXITCODE -ne 0) { throw 'Recorder compilation failed.' }
Copy-Item -LiteralPath $core -Destination $output -Force
$appPath = (Resolve-Path -LiteralPath $ApplicationPath).Path
$process = Start-Process -FilePath $recorder -ArgumentList @(('"' + $appPath + '"'), ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath (Join-Path $output 'capture-report.txt')) { Get-Content -LiteralPath (Join-Path $output 'capture-report.txt') }
    throw "Recorder exited with code $($process.ExitCode)."
}
Get-Content -LiteralPath (Join-Path $output 'capture-report.txt')
