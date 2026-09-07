param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineApk,

    [Parameter(Mandatory = $true)]
    [string]$ControlApk,

    [string]$OutputDirectory = '.\PerformanceEvidence\build-equivalence'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-ZipEntryRows {
    param([string]$ApkPath)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ApkPath)
    try {
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            $stream = $entry.Open()
            try {
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try {
                    $hashBytes = $sha.ComputeHash($stream)
                    $hash = [BitConverter]::ToString($hashBytes).Replace('-', '')
                }
                finally {
                    $sha.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }

            [PSCustomObject]@{
                Path = $entry.FullName.Replace('\', '/')
                Bytes = $entry.Length
                Sha256 = $hash
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

$baselinePath = [System.IO.Path]::GetFullPath($BaselineApk)
$controlPath = [System.IO.Path]::GetFullPath($ControlApk)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
    throw "Baseline APK not found: $baselinePath"
}
if (-not (Test-Path -LiteralPath $controlPath -PathType Leaf)) {
    throw "Control APK not found: $controlPath"
}

[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$baselineEntries = @(Get-ZipEntryRows $baselinePath)
$controlEntries = @(Get-ZipEntryRows $controlPath)
$baselineByPath = @{}
$controlByPath = @{}
foreach ($entry in $baselineEntries) { $baselineByPath[$entry.Path] = $entry }
foreach ($entry in $controlEntries) { $controlByPath[$entry.Path] = $entry }

$allPaths = @($baselineByPath.Keys + $controlByPath.Keys | Sort-Object -Unique)
$rows = foreach ($path in $allPaths) {
    $a = $baselineByPath[$path]
    $b = $controlByPath[$path]
    $expectedDifference = $path -eq 'AndroidManifest.xml' -or
        $path -eq 'resources.arsc' -or
        $path -eq 'assets/bin/Data/boot.config' -or
        $path -eq 'assets/bin/Data/globalgamemanagers' -or
        $path -eq 'assets/bin/Data/unity_app_guid' -or
        $path -like 'classes*.dex' -or
        $path -like 'META-INF/*'

    [PSCustomObject]@{
        Path = $path
        PresentInBaseline = $null -ne $a
        PresentInControl = $null -ne $b
        BaselineBytes = if ($a) { $a.Bytes } else { '' }
        ControlBytes = if ($b) { $b.Bytes } else { '' }
        ContentEqual = $null -ne $a -and $null -ne $b -and $a.Sha256 -eq $b.Sha256
        ExpectedPackageOrSignatureDifference = $expectedDifference
        BaselineSha256 = if ($a) { $a.Sha256 } else { '' }
        ControlSha256 = if ($b) { $b.Sha256 } else { '' }
    }
}

$entryReportPath = Join-Path $resolvedOutput 'apk-entry-comparison.csv'
$rows | Export-Csv -LiteralPath $entryReportPath -NoTypeInformation -Encoding UTF8

$requiredIdenticalEntries = @(
    'lib/arm64-v8a/libil2cpp.so',
    'lib/arm64-v8a/libunity.so',
    'assets/bin/Data/Managed/Metadata/global-metadata.dat'
)

$missingOrDifferentRequired = @(
    $requiredIdenticalEntries | Where-Object {
        $requiredPath = $_
        $row = $rows | Where-Object { $_.Path -eq $requiredPath } | Select-Object -First 1
        $null -eq $row -or -not $row.ContentEqual
    }
)

$baselineAbis = @(
    $baselineEntries.Path |
        Where-Object { $_ -match '^lib/([^/]+)/' } |
        ForEach-Object { ([regex]::Match($_, '^lib/([^/]+)/')).Groups[1].Value } |
        Sort-Object -Unique
)
$controlAbis = @(
    $controlEntries.Path |
        Where-Object { $_ -match '^lib/([^/]+)/' } |
        ForEach-Object { ([regex]::Match($_, '^lib/([^/]+)/')).Groups[1].Value } |
        Sort-Object -Unique
)

$unexpectedDifferences = @(
    $rows | Where-Object {
        (-not $_.ContentEqual) -and (-not $_.ExpectedPackageOrSignatureDifference)
    }
)

$summary = [ordered]@{
    schemaVersion = 1
    createdAtLocal = [DateTime]::Now.ToString('O')
    baselineApk = $baselinePath
    baselineBytes = (Get-Item -LiteralPath $baselinePath).Length
    baselineSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $baselinePath).Hash
    controlApk = $controlPath
    controlBytes = (Get-Item -LiteralPath $controlPath).Length
    controlSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $controlPath).Hash
    apkBytesEqual = (Get-Item -LiteralPath $baselinePath).Length -eq (Get-Item -LiteralPath $controlPath).Length
    baselineAbis = $baselineAbis
    controlAbis = $controlAbis
    entryPathSetsEqual = @($rows | Where-Object { -not $_.PresentInBaseline -or -not $_.PresentInControl }).Count -eq 0
    requiredCodeEntriesEqual = $missingOrDifferentRequired.Count -eq 0
    requiredCodeEntries = $requiredIdenticalEntries
    missingOrDifferentRequiredEntries = $missingOrDifferentRequired
    unexpectedDifferentEntryCount = $unexpectedDifferences.Count
    unexpectedDifferentEntries = @($unexpectedDifferences | ForEach-Object { $_.Path })
}

$summaryPath = Join-Path $resolvedOutput 'build-equivalence.json'
[System.IO.File]::WriteAllText(
    $summaryPath,
    ($summary | ConvertTo-Json -Depth 5),
    [System.Text.UTF8Encoding]::new($false))

if ($missingOrDifferentRequired.Count -gt 0) {
    throw "Required code entries differ: $($missingOrDifferentRequired -join ', ')"
}
if ($baselineAbis.Count -ne 1 -or $baselineAbis[0] -ne 'arm64-v8a' -or
    $controlAbis.Count -ne 1 -or $controlAbis[0] -ne 'arm64-v8a') {
    throw "Both APKs must contain only arm64-v8a. Baseline=$($baselineAbis -join ','), Control=$($controlAbis -join ',')"
}

Get-Item -LiteralPath $summaryPath, $entryReportPath
