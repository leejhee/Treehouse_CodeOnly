param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('A', 'B')]
    [string]$Variant,

    [Parameter(Mandatory = $true)]
    [string]$ApkPath,

    [string]$ProjectDirectory = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,

    [string]$OutputDirectory = '.\PerformanceEvidence\snapshots'
)

$ErrorActionPreference = 'Stop'

function Invoke-GitText {
    param([string[]]$Arguments)

    $output = & git -c "safe.directory=$resolvedGitSafePath" @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }
    return $output -join [Environment]::NewLine
}

$resolvedProject = [System.IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\')
$resolvedGitSafePath = $resolvedProject.Replace('\', '/')
$resolvedApk = [System.IO.Path]::GetFullPath($ApkPath)
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $resolvedApk -PathType Leaf)) {
    throw "APK not found: $resolvedApk"
}

$timestamp = [DateTime]::Now.ToString('yyyyMMddTHHmmss')
$snapshotDirectory = Join-Path $resolvedOutputRoot "${Variant}_$timestamp"
[System.IO.Directory]::CreateDirectory($snapshotDirectory) | Out-Null

$head = (Invoke-GitText @('rev-parse', 'HEAD')).Trim()
$branch = (Invoke-GitText @('branch', '--show-current')).Trim()
$statusText = Invoke-GitText @('status', '--short', '--untracked-files=all')
$diffText = Invoke-GitText @('diff', '--binary', '--no-ext-diff', 'HEAD', '--')

[System.IO.File]::WriteAllText(
    (Join-Path $snapshotDirectory 'git-status.txt'),
    $statusText + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText(
    (Join-Path $snapshotDirectory 'tracked-changes.patch'),
    $diffText + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$hashRoots = @('Assets', 'Packages', 'ProjectSettings')
$projectFiles = @(
    foreach ($relativeRoot in $hashRoots) {
        $absoluteRoot = Join-Path $resolvedProject $relativeRoot
        if (Test-Path -LiteralPath $absoluteRoot) {
            Get-ChildItem -LiteralPath $absoluteRoot -File -Recurse
        }
    }
)

$hashManifestPath = Join-Path $snapshotDirectory 'project-file-hashes.csv'
$projectFiles |
    Sort-Object FullName |
    ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
        [PSCustomObject]@{
            RelativePath = $_.FullName.Substring($resolvedProject.Length).TrimStart('\')
            Bytes = $_.Length
            Sha256 = $hash.Hash
        }
    } |
    Export-Csv -LiteralPath $hashManifestPath -NoTypeInformation -Encoding UTF8
$projectFileManifestHash = Get-FileHash -Algorithm SHA256 -LiteralPath $hashManifestPath

$snapshotSelection = @(
    'Assets\PerformanceBenchmark',
    'Tools\PerformanceBenchmark',
    'Assets\Script\Control\Housing\WarpManager.cs',
    'Assets\Script\Control\Housing\ArrangeManager.cs',
    'Assets\Script\sfxPlayer.cs',
    'Packages\manifest.json',
    'Packages\packages-lock.json',
    'ProjectSettings\ProjectVersion.txt'
)

$stagingDirectory = Join-Path $snapshotDirectory '_source_staging'
[System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
try {
    foreach ($relativePath in $snapshotSelection) {
        $source = Join-Path $resolvedProject $relativePath
        if (-not (Test-Path -LiteralPath $source)) {
            continue
        }

        $destination = Join-Path $stagingDirectory $relativePath
        if (Test-Path -LiteralPath $source -PathType Container) {
            [System.IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
            Copy-Item -LiteralPath $source -Destination (Split-Path $destination -Parent) -Recurse -Force
        }
        else {
            [System.IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
    }

    $sourceArchivePath = Join-Path $snapshotDirectory 'benchmark-source-snapshot.zip'
    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $sourceArchivePath -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}

$apk = Get-Item -LiteralPath $resolvedApk
$apkHash = Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedApk
$sourceArchive = Get-Item -LiteralPath $sourceArchivePath
$sourceArchiveHash = Get-FileHash -Algorithm SHA256 -LiteralPath $sourceArchivePath
$projectVersionPath = Join-Path $resolvedProject 'ProjectSettings\ProjectVersion.txt'
$unityVersion = if (Test-Path -LiteralPath $projectVersionPath) {
    $match = Select-String -LiteralPath $projectVersionPath -Pattern '^m_EditorVersion:\s*(.+)$'
    if ($match) { $match.Matches[0].Groups[1].Value.Trim() } else { '' }
}
else {
    ''
}

$metadata = [ordered]@{
    schemaVersion = 1
    createdAtLocal = [DateTime]::Now.ToString('O')
    variant = $Variant
    projectDirectory = $resolvedProject
    gitHead = $head
    gitBranch = $branch
    gitWorkingTreeClean = [string]::IsNullOrWhiteSpace($statusText)
    unityVersion = $unityVersion
    apkPath = $resolvedApk
    apkBytes = $apk.Length
    apkSha256 = $apkHash.Hash
    sourceArchiveBytes = $sourceArchive.Length
    sourceArchiveSha256 = $sourceArchiveHash.Hash
    projectFileHashCount = $projectFiles.Count
    projectFileManifestSha256 = $projectFileManifestHash.Hash
}

$metadataPath = Join-Path $snapshotDirectory 'snapshot-metadata.json'
[System.IO.File]::WriteAllText(
    $metadataPath,
    ($metadata | ConvertTo-Json -Depth 4),
    [System.Text.UTF8Encoding]::new($false))

Get-Item -LiteralPath $snapshotDirectory
