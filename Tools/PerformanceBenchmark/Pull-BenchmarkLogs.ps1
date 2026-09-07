param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('A', 'B', 'FPS', 'FPSGB', 'FPSGC')]
    [string]$Variant,

    [string]$OutputDirectory = '.\PerformanceEvidence\runs',
    [string]$AdbPath = 'adb'
)

$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $AdbPath) {
    $adb = (Resolve-Path -LiteralPath $AdbPath).Path
}
else {
    $command = Get-Command $AdbPath -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'adb was not found. Pass -AdbPath with the Unity Android SDK adb.exe path.'
    }
    $adb = $command.Source
}

$packageName = switch ($Variant) {
    'A' { 'com.expstudio.treehouse.perf.a' }
    'B' { 'com.expstudio.treehouse.perf.b' }
    'FPS' { 'com.expstudio.treehouse.perf.fps' }
    'FPSGB' { 'com.expstudio.treehouse.perf.fps.grassbatch' }
    'FPSGC' { 'com.expstudio.treehouse.perf.fps.grasscombine' }
}

$deviceOutput = & $adb devices -l 2>&1
$connectedDevices = @(
    $deviceOutput |
        Where-Object { $_ -match '^\S+\s+device(?:\s|$)' }
)
if ($connectedDevices.Count -ne 1) {
    throw "Exactly one authorized Android device is required.`n$($deviceOutput -join [Environment]::NewLine)"
}

$timestamp = [DateTime]::Now.ToString('yyyyMMddTHHmmss')
$destination = [System.IO.Path]::GetFullPath(
    (Join-Path $OutputDirectory "${Variant}_$timestamp"))
[System.IO.Directory]::CreateDirectory($destination) | Out-Null

$remote = "/sdcard/Android/data/$packageName/files/PerformanceBenchmark"
& $adb pull $remote $destination
if ($LASTEXITCODE -ne 0) {
    throw "Failed to pull benchmark logs from $remote"
}

$manifestPath = Join-Path $destination 'sha256-manifest.csv'
Get-ChildItem -LiteralPath $destination -File -Recurse |
    Where-Object { $_.FullName -ne $manifestPath } |
    ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
        [PSCustomObject]@{
            RelativePath = $_.FullName.Substring($destination.Length).TrimStart('\')
            Bytes = $_.Length
            Sha256 = $hash.Hash
        }
    } |
    Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8

Get-Item -LiteralPath $destination
