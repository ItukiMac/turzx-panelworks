$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
function Compile($target, $output, $references, $sources, $icon = $null) {
    $arguments = @('/nologo', '/optimize+', "/target:$target", "/out:$output") + ($references | ForEach-Object { "/reference:$_" }) + $sources
    if ($icon) { $arguments += @("/win32icon:$icon", "/resource:$icon,AppIcon") }
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $output" }
}
$system = @('System.dll','System.Core.dll','System.Data.dll','System.Drawing.dll','System.Management.dll')
Compile library "$bin\TurzxPanelWorks.Core.dll" $system @(
    "$root\src\Core\DashboardModels.cs", "$root\src\Core\FormulaEngine.cs", "$root\src\Core\SceneModels.cs")
Compile library "$bin\TurzxPanelWorks.Telemetry.Demo.dll" ($system + "$bin\TurzxPanelWorks.Core.dll") @("$root\src\Telemetry.Demo\DummyTelemetryProvider.cs")
Compile library "$bin\TurzxPanelWorks.Telemetry.Windows.dll" ($system + "$bin\TurzxPanelWorks.Core.dll") @("$root\src\Telemetry.Windows\WindowsTelemetryProvider.cs")
Compile library "$bin\TurzxPanelWorks.Rendering.dll" ($system + "$bin\TurzxPanelWorks.Core.dll") @("$root\src\Rendering\SceneRenderer.cs", "$root\src\Rendering\Rgb565Encoder.cs", "$root\src\Rendering\FrameRotation.cs")
Compile library "$bin\TurzxPanelWorks.Transport.Abstractions.dll" ($system + "$bin\TurzxPanelWorks.Core.dll") @("$root\src\Transport.Abstractions\DisplayContracts.cs")
Compile library "$bin\TurzxPanelWorks.Transport.Usb.dll" ($system + "$bin\TurzxPanelWorks.Transport.Abstractions.dll" + "$bin\TurzxPanelWorks.Rendering.dll") @("$root\src\Transport.Usb\UsbDisplayDiscovery.cs", "$root\src\Transport.Usb\UsbDisplaySessionFactory.cs", "$root\src\Transport.Usb\UsbDisplaySession.cs", "$root\src\Transport.Usb\RevAFrameEncoder.cs")
Compile library "$bin\TurzxPanelWorks.Runtime.dll" ($system + "$bin\TurzxPanelWorks.Core.dll" + "$bin\TurzxPanelWorks.Rendering.dll" + "$bin\TurzxPanelWorks.Transport.Abstractions.dll") @("$root\src\Runtime\RefreshOptions.cs", "$root\src\Runtime\SceneFrameSource.cs", "$root\src\Runtime\SceneFrameSourceFactory.cs", "$root\src\Runtime\DisplayTarget.cs", "$root\src\Runtime\DisplayUpdateCoordinator.cs")
Compile library "$bin\TurzxPanelWorks.Desktop.dll" ($system + 'System.Windows.Forms.dll') @("$root\src\Desktop\AppLifetime.cs")
$winForms = @("$bin\TurzxPanelWorks.Desktop.dll") + $system + 'System.Windows.Forms.dll' + "$bin\TurzxPanelWorks.Core.dll" + "$bin\TurzxPanelWorks.Rendering.dll"
Compile winexe "$bin\TurzxPanelWorks.Editor.exe" ($winForms + "$bin\TurzxPanelWorks.Telemetry.Demo.dll" + "$bin\TurzxPanelWorks.Telemetry.Windows.dll") @("$root\src\Editor.WinForms\DesignerForm.cs", "$root\src\Editor.WinForms\SelectionAdorner.cs", "$root\src\Editor.WinForms\Program.cs") "$root\assets\PanelWorks.Editor.ico"
Compile winexe "$bin\TurzxPanelWorks.Host.exe" ($winForms + "$bin\TurzxPanelWorks.Telemetry.Windows.dll" + "$bin\TurzxPanelWorks.Transport.Abstractions.dll" + "$bin\TurzxPanelWorks.Transport.Usb.dll" + "$bin\TurzxPanelWorks.Runtime.dll") @("$root\src\DisplayHost.WinForms\DisplayHostForm.cs", "$root\src\DisplayHost.WinForms\Program.cs") "$root\assets\PanelWorks.Host.ico"
Compile exe "$bin\TurzxPanelWorks.OfflineTests.exe" ($system + "$bin\TurzxPanelWorks.Core.dll" + "$bin\TurzxPanelWorks.Telemetry.Demo.dll" + "$bin\TurzxPanelWorks.Rendering.dll" + "$bin\TurzxPanelWorks.Transport.Abstractions.dll" + "$bin\TurzxPanelWorks.Transport.Usb.dll" + "$bin\TurzxPanelWorks.Runtime.dll") @("$root\tests\OfflineTests\Program.cs")
Compile exe "$bin\TurzxPanelWorks.WinFormsSmoke.exe" ($winForms + "$bin\TurzxPanelWorks.Editor.exe" + "$bin\TurzxPanelWorks.Host.exe") @("$root\tests\WinFormsSmoke\Program.cs")
Write-Host "Built outputs in $bin"
