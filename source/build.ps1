param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$destination = Split-Path -Parent $PSScriptRoot
$resourceDirectory = Join-Path $destination '.build'
if ($OutputDirectory) { $destination = [IO.Path]::GetFullPath($OutputDirectory); New-Item -ItemType Directory -Path $destination -Force | Out-Null }
New-Item -ItemType Directory -Path $resourceDirectory -Force | Out-Null
$fontResources = Join-Path $resourceDirectory 'MonitorShade.g.resources'
$writer = New-Object System.Resources.ResourceWriter($fontResources)
try {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'fonts') -File | ForEach-Object {
        $stream = New-Object System.IO.MemoryStream(,[IO.File]::ReadAllBytes($_.FullName))
        $writer.AddResource(('fonts/' + $_.Name.ToLowerInvariant()), $stream, $true)
    }
    $writer.Generate()
} finally { $writer.Dispose() }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll','System.Xaml.dll')
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','WindowsFormsIntegration.dll') | ForEach-Object { Join-Path "$framework\WPF" $_ }
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
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $destination\MonitorShade.exe"
