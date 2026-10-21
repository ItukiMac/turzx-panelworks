param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'bin' }
$lib = Join-Path $bin 'lib'
New-Item -ItemType Directory -Path $bin, $lib -Force | Out-Null
function Compile($target, $output, $references, $sources, $icon = $null) {
    $arguments = @('/nologo', '/optimize+', "/target:$target", "/out:$output") + ($references | ForEach-Object { "/reference:$_" }) + $sources
    if ($output -eq "$lib\TurzxPanelWorks.Core.dll") { $arguments += "/resource:$root\src\Core\Resources\ClockSystemOverview.scene.xml,ClockSystemOverview.scene.xml" }
    if ($icon) { $arguments += @("/win32icon:$icon", "/resource:$icon,AppIcon") }
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $output" }
}
$system = @('System.dll','System.Core.dll','System.Data.dll','System.Drawing.dll','System.Management.dll','System.Web.Extensions.dll','System.Security.dll')
Compile library "$lib\TurzxPanelWorks.Core.dll" $system @(
    "$root\src\Core\DashboardModels.cs", "$root\src\Core\HostParameters.cs", "$root\src\Core\FormulaEngine.cs", "$root\src\Core\SceneModels.cs", "$root\src\Core\LayoutCatalog.cs")
Compile library "$lib\TurzxPanelWorks.Telemetry.Demo.dll" ($system + "$lib\TurzxPanelWorks.Core.dll") @("$root\src\Telemetry.Demo\DummyTelemetryProvider.cs")
Compile library "$lib\TurzxPanelWorks.Telemetry.Windows.dll" ($system + "$lib\TurzxPanelWorks.Core.dll") @("$root\src\Telemetry.Windows\WindowsTelemetryProvider.cs")
Compile library "$lib\TurzxPanelWorks.Rendering.dll" ($system + "$lib\TurzxPanelWorks.Core.dll") @("$root\src\Rendering\SceneRenderer.cs", "$root\src\Rendering\ContentTiming.cs", "$root\src\Rendering\WidgetRenderer.cs", "$root\src\Rendering\NewsCards.cs", "$root\src\Rendering\MarketQuotes.cs", "$root\src\Rendering\WebsiteCapture.cs", "$root\src\Rendering\Rgb565Encoder.cs", "$root\src\Rendering\FrameRotation.cs")
Compile library "$lib\TurzxPanelWorks.Transport.Abstractions.dll" ($system + "$lib\TurzxPanelWorks.Core.dll") @("$root\src\Transport.Abstractions\DisplayContracts.cs")
Compile library "$lib\TurzxPanelWorks.Transport.Usb.dll" ($system + "$lib\TurzxPanelWorks.Transport.Abstractions.dll" + "$lib\TurzxPanelWorks.Rendering.dll") @("$root\src\Transport.Usb\UsbDisplayDiscovery.cs", "$root\src\Transport.Usb\UsbDisplaySessionFactory.cs", "$root\src\Transport.Usb\UsbDisplaySession.cs", "$root\src\Transport.Usb\RevAFrameEncoder.cs")
Compile library "$lib\TurzxPanelWorks.Runtime.dll" ($system + "$lib\TurzxPanelWorks.Core.dll" + "$lib\TurzxPanelWorks.Rendering.dll" + "$lib\TurzxPanelWorks.Transport.Abstractions.dll") @("$root\src\Runtime\RefreshOptions.cs", "$root\src\Runtime\TimedTelemetryProvider.cs", "$root\src\Runtime\SceneFrameSource.cs", "$root\src\Runtime\SceneFrameSourceFactory.cs", "$root\src\Runtime\DisplayTarget.cs", "$root\src\Runtime\DisplayUpdateCoordinator.cs")
Compile library "$lib\TurzxPanelWorks.Desktop.dll" ($system + 'System.Windows.Forms.dll') @("$root\src\Desktop\AppLifetime.cs")
$winForms = @("$lib\TurzxPanelWorks.Desktop.dll") + $system + 'System.Windows.Forms.dll' + "$lib\TurzxPanelWorks.Core.dll" + "$lib\TurzxPanelWorks.Rendering.dll"
Compile winexe "$bin\TurzxPanelWorks.Editor.exe" ($winForms + "$lib\TurzxPanelWorks.Telemetry.Demo.dll" + "$lib\TurzxPanelWorks.Telemetry.Windows.dll") @("$root\src\Editor.WinForms\DesignerForm.cs", "$root\src\Editor.WinForms\QuickLayoutEditor.cs", "$root\src\Editor.WinForms\WidgetEditor.cs", "$root\src\Editor.WinForms\MarketEditor.cs", "$root\src\Editor.WinForms\SelectionAdorner.cs", "$root\src\Editor.WinForms\Program.cs") "$root\src\Editor.WinForms\Resources\App.ico"
Compile winexe "$bin\TurzxPanelWorks.Host.exe" ($winForms + "$lib\TurzxPanelWorks.Telemetry.Windows.dll" + "$lib\TurzxPanelWorks.Transport.Abstractions.dll" + "$lib\TurzxPanelWorks.Transport.Usb.dll" + "$lib\TurzxPanelWorks.Runtime.dll") @("$root\src\DisplayHost.WinForms\DisplayHostForm.cs", "$root\src\DisplayHost.WinForms\HostParameterEditor.cs", "$root\src\DisplayHost.WinForms\Program.cs") "$root\src\DisplayHost.WinForms\Resources\App.ico"
Compile exe "$bin\TurzxPanelWorks.OfflineTests.exe" ($system + "$lib\TurzxPanelWorks.Core.dll" + "$lib\TurzxPanelWorks.Telemetry.Demo.dll" + "$lib\TurzxPanelWorks.Rendering.dll" + "$lib\TurzxPanelWorks.Transport.Abstractions.dll" + "$lib\TurzxPanelWorks.Transport.Usb.dll" + "$lib\TurzxPanelWorks.Runtime.dll") @(Get-ChildItem -LiteralPath "$root\tests\OfflineTests" -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
Compile exe "$bin\TurzxPanelWorks.WinFormsSmoke.exe" ($winForms + "$lib\TurzxPanelWorks.Runtime.dll" + "$lib\TurzxPanelWorks.Transport.Abstractions.dll" + "$lib\TurzxPanelWorks.Telemetry.Demo.dll" + "$bin\TurzxPanelWorks.Editor.exe" + "$bin\TurzxPanelWorks.Host.exe") @("$root\tests\WinFormsSmoke\Program.cs")
# .NET Framework resolves private dependencies from lib for every executable.
foreach ($name in @('Editor', 'Host', 'OfflineTests', 'WinFormsSmoke')) {
    Copy-Item -LiteralPath (Join-Path $root 'src\Desktop\App.config') -Destination (Join-Path $bin ("TurzxPanelWorks.$name.exe.config")) -Force
}
Write-Host "Built outputs in $bin (DLLs in lib)"

$templates = Start-Process -FilePath (Join-Path $bin 'TurzxPanelWorks.Editor.exe') -ArgumentList '--export-templates' -WindowStyle Hidden -Wait -PassThru
if ($templates.ExitCode -ne 0) { throw 'Template generation failed.' }
