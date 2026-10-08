$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'Build.ps1')
$test = Start-Process -FilePath (Join-Path $root 'bin\StandaloneDisplay.OfflineTests.exe') -Wait -PassThru -NoNewWindow
if ($test.ExitCode -ne 0) { throw "Offline tests failed with exit code $($test.ExitCode)" }
Write-Host 'PASS: build and offline verification. No USB device was opened.'
