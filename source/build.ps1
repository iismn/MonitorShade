$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$destination = Split-Path -Parent $PSScriptRoot
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll','System.Xaml.dll')
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','WindowsFormsIntegration.dll') | ForEach-Object { Join-Path "$framework\WPF" $_ }
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/unsafe+','/codepage:65001',"/win32manifest:$PSScriptRoot\app.manifest","/win32icon:$PSScriptRoot\app.ico","/out:$destination\MonitorShade.exe", "/resource:$PSScriptRoot\Settings.xaml,MonitorShade.Settings.xaml")
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += @("$PSScriptRoot\MonitorShade.cs", "$PSScriptRoot\SettingsForm.cs", "$PSScriptRoot\WindowLayout.cs", "$PSScriptRoot\LayoutVerification.cs", "$PSScriptRoot\Ddc.cs", "$PSScriptRoot\BrightnessKeys.cs", "$PSScriptRoot\BrightnessPage.cs", "$PSScriptRoot\BrightnessVerification.cs")
$arguments += @("$PSScriptRoot\DesktopRecovery.cs", "$PSScriptRoot\RecoveryVerification.cs")
$arguments += @("$PSScriptRoot\WallpaperRegions.cs", "$PSScriptRoot\WallpaperVerification.cs")
$arguments += @("$PSScriptRoot\BrightnessSync.cs", "$PSScriptRoot\ManualShade.cs", "$PSScriptRoot\ManualShadeVerification.cs")
$arguments += @("$PSScriptRoot\DisplayRecovery.cs", "$PSScriptRoot\DisplayRecoveryVerification.cs")
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $destination\MonitorShade.exe"
