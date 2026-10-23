param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$destination = Split-Path -Parent $PSScriptRoot
$resourceDirectory = Join-Path $destination '.build'
if ($OutputDirectory) { $destination = [IO.Path]::GetFullPath($OutputDirectory); New-Item -ItemType Directory -Path $destination -Force | Out-Null }
New-Item -ItemType Directory -Path $resourceDirectory -Force | Out-Null
$webSdk = Join-Path $resourceDirectory 'webview2'
$webVersion = '1.0.4258.31'
if (-not (Test-Path -LiteralPath (Join-Path $webSdk 'lib\net462\Microsoft.Web.WebView2.Wpf.dll'))) {
    New-Item -ItemType Directory -Path $webSdk -Force | Out-Null
    $archive = Join-Path $webSdk 'sdk.zip'
    Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$webVersion/microsoft.web.webview2.$webVersion.nupkg" -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '56F7F4B8BF9AEE4B8EFEFBBDD4F67D5F74EBD1B100ED0806DA71BF76AF481AA9') { throw 'WebView2 SDK checksum mismatch.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $webSdk -Force
}
$fontResources = Join-Path $resourceDirectory 'MonitorShade.g.resources'
$writer = New-Object System.Resources.ResourceWriter($fontResources)
try {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'fonts') -File | ForEach-Object {
        $stream = New-Object System.IO.MemoryStream(,[IO.File]::ReadAllBytes($_.FullName))
        $writer.AddResource(('fonts/' + $_.Name.ToLowerInvariant()), $stream, $true)
    }
    $writer.Generate()
} finally { $writer.Dispose() }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll','System.Xaml.dll','System.Web.Extensions.dll')
$references += @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll') | ForEach-Object { Join-Path $webSdk "lib\net462\$_" }
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','WindowsFormsIntegration.dll') | ForEach-Object { Join-Path "$framework\WPF" $_ }
$references += Join-Path $framework 'System.Runtime.WindowsRuntime.dll'
foreach ($facade in @('System.Runtime','System.Threading.Tasks','System.Runtime.InteropServices.WindowsRuntime')) {
    $references += (Get-ChildItem -LiteralPath "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\$facade" -Filter '*.dll' -Recurse | Select-Object -First 1).FullName
}
$references += @('Windows.Foundation','Windows.Graphics','Windows.Storage','Windows.UI') | ForEach-Object { "$env:WINDIR\System32\WinMetadata\$_.winmd" }
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/unsafe+','/codepage:65001',"/win32manifest:$PSScriptRoot\app.manifest","/win32icon:$PSScriptRoot\app.ico","/out:$destination\MonitorShade.exe", "/resource:$PSScriptRoot\Settings.xaml,MonitorShade.Settings.xaml")
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += @("$PSScriptRoot\MonitorShade.cs", "$PSScriptRoot\SettingsForm.cs", "$PSScriptRoot\WindowLayout.cs", "$PSScriptRoot\LayoutVerification.cs", "$PSScriptRoot\Ddc.cs", "$PSScriptRoot\BrightnessKeys.cs", "$PSScriptRoot\BrightnessPage.cs", "$PSScriptRoot\BrightnessVerification.cs")
$arguments += @("$PSScriptRoot\DesktopRecovery.cs", "$PSScriptRoot\RecoveryVerification.cs")
$arguments += @("$PSScriptRoot\WallpaperRegions.cs", "$PSScriptRoot\WallpaperVerification.cs")
$arguments += @("$PSScriptRoot\BrightnessSync.cs", "$PSScriptRoot\ManualShade.cs", "$PSScriptRoot\ManualShadeVerification.cs")
$arguments += @("$PSScriptRoot\DisplayRecovery.cs", "$PSScriptRoot\DisplayRecoveryVerification.cs")
$arguments += @("$PSScriptRoot\SettingsWindow.cs", "$PSScriptRoot\Appearance.cs", "$PSScriptRoot\BrightnessControls.cs", "$PSScriptRoot\AppearanceVerification.cs", "/resource:$fontResources,MonitorShade.g.resources")
$arguments += "$PSScriptRoot\UiMotion.cs"
$arguments += "$PSScriptRoot\TextRasterVerification.cs"
$arguments += @("$PSScriptRoot\BackdropCapture.cs", "$PSScriptRoot\CompositedBackdrop.cs")
$arguments += @('WebRuntime.cs', 'WebUi.cs', 'WebSettings.cs', 'WebUiVerification.cs', 'WebLiveVerification.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
foreach ($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll')) { $arguments += "/resource:$webSdk\lib\net462\$name,MonitorShade.WebRuntime.$name" }
$arguments += "/resource:$webSdk\runtimes\win-x64\native\WebView2Loader.dll,MonitorShade.WebRuntime.WebView2Loader.dll"
foreach ($name in @('index.html','app.css','app.js')) { $arguments += "/resource:$PSScriptRoot\web\$name,MonitorShade.Web.$name" }
foreach ($name in @('WebView2-LICENSE.txt','WebView2-NOTICE.txt')) { $arguments += "/resource:$PSScriptRoot\licenses\$name,MonitorShade.Licenses.$name" }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $destination\MonitorShade.exe"
