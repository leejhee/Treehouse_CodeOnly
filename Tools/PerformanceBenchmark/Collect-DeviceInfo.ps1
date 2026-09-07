param(
    [string]$OutputDirectory = '.\PerformanceEvidence\device',
    [string]$AdbPath = 'adb'
)

$ErrorActionPreference = 'Stop'

function Resolve-Adb {
    param([string]$Candidate)

    if (Test-Path -LiteralPath $Candidate) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'adb was not found. Pass -AdbPath with the Unity Android SDK adb.exe path.'
    }

    return $command.Source
}

function Invoke-AdbText {
    param(
        [string]$Executable,
        [string[]]$Arguments
    )

    $output = & $Executable @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "adb failed: $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }

    return ($output -join [Environment]::NewLine).TrimEnd()
}

$adb = Resolve-Adb $AdbPath
$deviceList = Invoke-AdbText $adb @('devices', '-l')
$connectedDevices = @(
    $deviceList -split "`r?`n" |
        Where-Object { $_ -match '^\S+\s+device(?:\s|$)' }
)

if ($connectedDevices.Count -ne 1) {
    throw "Exactly one authorized Android device is required.`n$deviceList"
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$timestamp = [DateTime]::Now.ToString('yyyyMMddTHHmmss')
$outputPath = Join-Path $resolvedOutput "device-info-$timestamp.txt"

$commands = @(
    @{ Name = 'adb devices -l'; Arguments = @('devices', '-l') },
    @{ Name = 'manufacturer'; Arguments = @('shell', 'getprop', 'ro.product.manufacturer') },
    @{ Name = 'model'; Arguments = @('shell', 'getprop', 'ro.product.model') },
    @{ Name = 'device'; Arguments = @('shell', 'getprop', 'ro.product.device') },
    @{ Name = 'Android release'; Arguments = @('shell', 'getprop', 'ro.build.version.release') },
    @{ Name = 'SDK level'; Arguments = @('shell', 'getprop', 'ro.build.version.sdk') },
    @{ Name = 'build fingerprint'; Arguments = @('shell', 'getprop', 'ro.build.fingerprint') },
    @{ Name = 'security patch'; Arguments = @('shell', 'getprop', 'ro.build.version.security_patch') },
    @{ Name = 'screen size'; Arguments = @('shell', 'wm', 'size') },
    @{ Name = 'screen density'; Arguments = @('shell', 'wm', 'density') },
    @{ Name = 'peak refresh rate'; Arguments = @('shell', 'settings', 'get', 'system', 'peak_refresh_rate') },
    @{ Name = 'minimum refresh rate'; Arguments = @('shell', 'settings', 'get', 'system', 'min_refresh_rate') },
    @{ Name = 'brightness mode'; Arguments = @('shell', 'settings', 'get', 'system', 'screen_brightness_mode') },
    @{ Name = 'brightness'; Arguments = @('shell', 'settings', 'get', 'system', 'screen_brightness') },
    @{ Name = 'battery'; Arguments = @('shell', 'dumpsys', 'battery') },
    @{ Name = 'thermal service'; Arguments = @('shell', 'dumpsys', 'thermalservice') }
)

$builder = [System.Text.StringBuilder]::new()
$null = $builder.AppendLine("captured_at=$([DateTime]::Now.ToString('O'))")
$null = $builder.AppendLine("adb=$adb")

foreach ($entry in $commands) {
    $null = $builder.AppendLine()
    $null = $builder.AppendLine("===== $($entry.Name) =====")
    $null = $builder.AppendLine((Invoke-AdbText $adb $entry.Arguments))
}

[System.IO.File]::WriteAllText($outputPath, $builder.ToString(), [System.Text.Encoding]::UTF8)
Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath |
    Select-Object Algorithm, Hash, Path

