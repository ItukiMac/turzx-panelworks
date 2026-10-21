param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'bin' }
$testRoot = Join-Path $root ('bin\test-temp-' + [Guid]::NewGuid().ToString('N'))
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $env:TEMP = $testRoot
    $env:TMP = $testRoot
    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $bin
    foreach ($name in @('OfflineTests', 'WinFormsSmoke')) {
        $test = Start-Process -FilePath (Join-Path $bin "TurzxPanelWorks.$name.exe") -PassThru -NoNewWindow
        if (-not $test.WaitForExit(60000)) {
            $test.Kill()
            $test.WaitForExit()
            throw "$name timed out after 60 seconds."
        }
        if ($test.ExitCode -ne 0) { throw "$name failed with exit code $($test.ExitCode)" }
    }
    Write-Host 'PASS: build and offline verification. No USB device was opened.'
}
finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $parent = [IO.Path]::GetFullPath((Join-Path $root 'bin')) + '\'
    if (-not $resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test output location.' }
    if (Test-Path -LiteralPath $resolved) {
        $links = @(Get-Item -LiteralPath $resolved) + @(Get-ChildItem -LiteralPath $resolved -Recurse -Force)
        if ($links | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Test output contains a reparse point; retained for inspection.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
