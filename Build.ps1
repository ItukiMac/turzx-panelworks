$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
function Compile($target, $output, $references, $sources) {
    $arguments = @('/nologo', '/optimize+', "/target:$target", "/out:$output") + ($references | ForEach-Object { "/reference:$_" }) + $sources
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $output" }
}
$system = @('System.dll','System.Core.dll','System.Data.dll','System.Drawing.dll','System.Management.dll')
Compile library "$bin\StandaloneDisplay.Core.dll" $system @(
    "$root\src\Core\DashboardModels.cs", "$root\src\Core\FormulaEngine.cs", "$root\src\Core\SceneModels.cs")
Compile library "$bin\StandaloneDisplay.Telemetry.Demo.dll" ($system + "$bin\StandaloneDisplay.Core.dll") @("$root\src\Telemetry.Demo\DummyTelemetryProvider.cs")
Compile library "$bin\StandaloneDisplay.Telemetry.Windows.dll" ($system + "$bin\StandaloneDisplay.Core.dll") @("$root\src\Telemetry.Windows\WindowsTelemetryProvider.cs")
Compile library "$bin\StandaloneDisplay.Rendering.dll" ($system + "$bin\StandaloneDisplay.Core.dll") @("$root\src\Rendering\SceneRenderer.cs", "$root\src\Rendering\Rgb565Encoder.cs")
Compile library "$bin\StandaloneDisplay.Transport.Abstractions.dll" ($system + "$bin\StandaloneDisplay.Core.dll") @("$root\src\Transport.Abstractions\DisplayContracts.cs")
Compile library "$bin\StandaloneDisplay.Transport.Usb.dll" ($system + "$bin\StandaloneDisplay.Transport.Abstractions.dll" + "$bin\StandaloneDisplay.Rendering.dll") @("$root\src\Transport.Usb\UsbDisplayDiscovery.cs", "$root\src\Transport.Usb\UsbDisplaySessionFactory.cs", "$root\src\Transport.Usb\UsbDisplaySession.cs", "$root\src\Transport.Usb\RevAFrameEncoder.cs")
Compile library "$bin\StandaloneDisplay.Runtime.dll" ($system + "$bin\StandaloneDisplay.Core.dll" + "$bin\StandaloneDisplay.Rendering.dll" + "$bin\StandaloneDisplay.Transport.Abstractions.dll") @("$root\src\Runtime\RefreshOptions.cs", "$root\src\Runtime\SceneFrameSource.cs", "$root\src\Runtime\SceneFrameSourceFactory.cs", "$root\src\Runtime\DisplayTarget.cs", "$root\src\Runtime\DisplayUpdateCoordinator.cs")
$winForms = $system + 'System.Windows.Forms.dll' + "$bin\StandaloneDisplay.Core.dll" + "$bin\StandaloneDisplay.Rendering.dll"
Compile winexe "$bin\StandaloneDisplay.Editor.exe" ($winForms + "$bin\StandaloneDisplay.Telemetry.Demo.dll" + "$bin\StandaloneDisplay.Telemetry.Windows.dll") @("$root\src\Editor.WinForms\DesignerForm.cs", "$root\src\Editor.WinForms\SelectionAdorner.cs", "$root\src\Editor.WinForms\Program.cs")
Compile winexe "$bin\StandaloneDisplay.Host.exe" ($winForms + "$bin\StandaloneDisplay.Telemetry.Windows.dll" + "$bin\StandaloneDisplay.Transport.Abstractions.dll" + "$bin\StandaloneDisplay.Transport.Usb.dll" + "$bin\StandaloneDisplay.Runtime.dll") @("$root\src\DisplayHost.WinForms\DisplayHostForm.cs", "$root\src\DisplayHost.WinForms\Program.cs")
Compile exe "$bin\StandaloneDisplay.OfflineTests.exe" ($system + "$bin\StandaloneDisplay.Core.dll" + "$bin\StandaloneDisplay.Telemetry.Demo.dll" + "$bin\StandaloneDisplay.Rendering.dll" + "$bin\StandaloneDisplay.Transport.Abstractions.dll" + "$bin\StandaloneDisplay.Transport.Usb.dll" + "$bin\StandaloneDisplay.Runtime.dll") @("$root\tests\OfflineTests\Program.cs")
Compile exe "$bin\StandaloneDisplay.WinFormsSmoke.exe" ($winForms + "$bin\StandaloneDisplay.Editor.exe" + "$bin\StandaloneDisplay.Host.exe") @("$root\tests\WinFormsSmoke\Program.cs")
Write-Host "Built outputs in $bin"
