$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'Build.ps1')
$test = Start-Process -FilePath (Join-Path $root 'bin\StandaloneDisplay.OfflineTests.exe') -Wait -PassThru -NoNewWindow
if ($test.ExitCode -ne 0) { throw "Offline tests failed with exit code $($test.ExitCode)" }
$smoke = Start-Process -FilePath (Join-Path $root 'bin\StandaloneDisplay.WinFormsSmoke.exe') -Wait -PassThru -NoNewWindow
if ($smoke.ExitCode -ne 0) { throw "WinForms smoke tests failed with exit code $($smoke.ExitCode)" }
Write-Host 'PASS: build and offline verification. No USB device was opened.'
