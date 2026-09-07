param(
    [string]$BaselineApk = '.\Builds\Performance\TreeHouse_Perf_A.apk',

    [string]$ControlApk = '.\Builds\Performance\TreeHouse_Perf_B.apk',

    [string]$AdbPath = 'adb',

    [string]$OutputDirectory = '.\PerformanceEvidence\deployment'
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

$baselinePath = [System.IO.Path]::GetFullPath($BaselineApk)
$controlPath = [System.IO.Path]::GetFullPath($ControlApk)
foreach ($apk in @($baselinePath, $controlPath)) {
    if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) {
        throw "APK not found: $apk"
    }
}

$deviceOutput = & $adb devices -l 2>&1
$connectedDevices = @(
    $deviceOutput | Where-Object { $_ -match '^(\S+)\s+device(?:\s|$)' }
)
if ($connectedDevices.Count -ne 1) {
    throw "Exactly one authorized Android device is required.`n$($deviceOutput -join [Environment]::NewLine)"
}
$serial = ([regex]::Match($connectedDevices[0], '^(\S+)')).Groups[1].Value

foreach ($apk in @($baselinePath, $controlPath)) {
    $installOutput = & $adb install -r $apk 2>&1
    if ($LASTEXITCODE -ne 0 -or $installOutput -notcontains 'Success') {
        throw "Failed to install $apk`n$($installOutput -join [Environment]::NewLine)"
    }
}

$packages = @(
    [PSCustomObject]@{ Variant = 'A'; Name = 'com.expstudio.treehouse.perf.a'; Apk = $baselinePath },
    [PSCustomObject]@{ Variant = 'B'; Name = 'com.expstudio.treehouse.perf.b'; Apk = $controlPath }
)

$deploymentRows = foreach ($package in $packages) {
    $packagePathOutput = & $adb shell pm path $package.Name 2>&1
    if ($LASTEXITCODE -ne 0 -or $packagePathOutput -notmatch '^package:') {
        throw "Installed package was not found: $($package.Name)"
    }

    $packageDump = & $adb shell dumpsys package $package.Name 2>&1
    $versionNameLine = $packageDump | Where-Object { $_ -match '^\s*versionName=' } | Select-Object -First 1
    $versionCodeLine = $packageDump | Where-Object { $_ -match '^\s*versionCode=' } | Select-Object -First 1
    $apkItem = Get-Item -LiteralPath $package.Apk
    $apkHash = Get-FileHash -Algorithm SHA256 -LiteralPath $package.Apk

    [PSCustomObject]@{
        InstalledAtLocal = [DateTime]::Now.ToString('O')
        DeviceSerial = $serial
        Variant = $package.Variant
        PackageName = $package.Name
        VersionName = ([string]$versionNameLine).Trim() -replace '^versionName=', ''
        VersionCode = (([string]$versionCodeLine).Trim() -replace '^versionCode=', '').Split(' ')[0]
        ApkPath = $package.Apk
        ApkBytes = $apkItem.Length
        ApkSha256 = $apkHash.Hash
        DevicePackagePath = $packagePathOutput -join ';'
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$manifestPath = Join-Path $resolvedOutput (
    'deployment-' + [DateTime]::Now.ToString('yyyyMMddTHHmmss') + '.csv')
$deploymentRows | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8

Get-Item -LiteralPath $manifestPath
