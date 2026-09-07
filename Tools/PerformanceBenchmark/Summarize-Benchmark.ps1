param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,

    [string]$OutputDirectory = '.\PerformanceEvidence\results'
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

function Convert-Number {
    param($Value)
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [double]::NaN
    }

    $number = 0.0
    if ([double]::TryParse(
            [string]$Value,
            [System.Globalization.NumberStyles]::Float,
            $culture,
            [ref]$number)) {
        return $number
    }

    return [double]::NaN
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    $valid = @($Values | Where-Object { -not [double]::IsNaN($_) } | Sort-Object)
    if ($valid.Count -eq 0) {
        return [double]::NaN
    }
    if ($valid.Count -eq 1) {
        return [double]$valid[0]
    }

    $position = ($Percentile / 100.0) * ($valid.Count - 1)
    $lower = [Math]::Floor($position)
    $upper = [Math]::Ceiling($position)
    if ($lower -eq $upper) {
        return [double]$valid[$lower]
    }

    $weight = $position - $lower
    return [double]$valid[$lower] * (1.0 - $weight) + [double]$valid[$upper] * $weight
}

function Get-Percentage {
    param(
        [double[]]$Values,
        [scriptblock]$Predicate
    )

    $valid = @($Values | Where-Object { -not [double]::IsNaN($_) })
    if ($valid.Count -eq 0) {
        return [double]::NaN
    }

    $matched = @($valid | Where-Object $Predicate).Count
    return 100.0 * $matched / $valid.Count
}

function Format-ResultNumber {
    param([double]$Value)
    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) {
        return ''
    }
    return $Value.ToString('F4', $culture)
}

$resolvedInput = [System.IO.Path]::GetFullPath($InputDirectory)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$csvFiles = @(
    Get-ChildItem -LiteralPath $resolvedInput -Filter '*.csv' -File -Recurse |
        Where-Object { $_.Name -ne 'sha256-manifest.csv' -and $_.Name -ne 'run-summary.csv' }
)
if ($csvFiles.Count -eq 0) {
    throw "No benchmark CSV files found under $resolvedInput"
}

$summaries = foreach ($csvFile in $csvFiles) {
    $rows = @(Import-Csv -LiteralPath $csvFile.FullName)
    if ($rows.Count -eq 0 -or -not ($rows[0].PSObject.Properties.Name -contains 'run_id')) {
        continue
    }

    $metadataPath = $csvFile.FullName -replace '\.csv$', '.metadata.json'
    $metadata = if (Test-Path -LiteralPath $metadataPath) {
        Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }

    $cpuFrame = [double[]]@($rows | ForEach-Object { Convert-Number $_.cpu_frame_ms })
    $cpuMain = [double[]]@($rows | ForEach-Object { Convert-Number $_.cpu_main_thread_ms })
    $gpuFrame = [double[]]@($rows | ForEach-Object { Convert-Number $_.gpu_frame_ms })
    $frameInterval = [double[]]@(
        if ($rows[0].PSObject.Properties.Name -contains 'frame_interval_ms') {
            $rows | ForEach-Object { Convert-Number $_.frame_interval_ms }
        }
    )
    $batches = [double[]]@(
        if ($rows[0].PSObject.Properties.Name -contains 'batches_count') {
            $rows | ForEach-Object { Convert-Number $_.batches_count }
        }
    )
    $drawCalls = [double[]]@(
        if ($rows[0].PSObject.Properties.Name -contains 'draw_calls_count') {
            $rows | ForEach-Object { Convert-Number $_.draw_calls_count }
        }
    )
    $setPassCalls = [double[]]@(
        if ($rows[0].PSObject.Properties.Name -contains 'setpass_calls_count') {
            $rows | ForEach-Object { Convert-Number $_.setpass_calls_count }
        }
    )
    $triangles = [double[]]@(
        if ($rows[0].PSObject.Properties.Name -contains 'triangles_count') {
            $rows | ForEach-Object { Convert-Number $_.triangles_count }
        }
    )
    $vertices = [double[]]@(
        if ($rows[0].PSObject.Properties.Name -contains 'vertices_count') {
            $rows | ForEach-Object { Convert-Number $_.vertices_count }
        }
    )
    $warpRaycast = [double[]]@($rows | ForEach-Object { Convert-Number $_.warp_raycast_ms })
    $audioDistanceFade = [double[]]@($rows | ForEach-Object { Convert-Number $_.audio_distance_fade_ms })
    $arrangeDisableNearCollider = [double[]]@($rows | ForEach-Object { Convert-Number $_.arrange_disable_near_collider_ms })
    $audioCalls = [double[]]@($rows | ForEach-Object { Convert-Number $_.audio_update_calls })
    $arrangeCandidates = [double[]]@($rows | ForEach-Object { Convert-Number $_.arrange_candidates_checked })
    $gcRows = @($rows | Where-Object { $_.thermal_telemetry_updated -eq '0' })
    $gcAllocated = [double[]]@($gcRows | ForEach-Object { Convert-Number $_.gc_allocated_bytes })
    $elapsedStartMs = Convert-Number $rows[0].elapsed_ms
    $elapsedEndMs = Convert-Number $rows[-1].elapsed_ms
    $elapsedSpanMs = $elapsedEndMs - $elapsedStartMs
    $averageFps = if ($rows.Count -gt 1 -and
        -not [double]::IsNaN($elapsedSpanMs) -and $elapsedSpanMs -gt 0) {
        1000.0 * ($rows.Count - 1) / $elapsedSpanMs
    }
    else {
        [double]::NaN
    }

    [PSCustomObject]@{
        RunId = $rows[0].run_id
        Variant = if ($metadata) { $metadata.variant } else { '' }
        PairId = if ($metadata) { $metadata.pairId } else { '' }
        PairOrder = if ($metadata) { $metadata.pairOrder } else { '' }
        ScenarioId = if ($metadata) { $metadata.scenarioId } else { '' }
        FixtureVersion = if ($metadata) { $metadata.scenarioFixtureVersion } else { '' }
        DeterministicSeed = if ($metadata) { $metadata.deterministicSeed } else { '' }
        MetricsSchemaVersion = if ($metadata) { $metadata.metricsSchemaVersion } else { '' }
        UnityProfilerRawEnabled = if ($metadata) { $metadata.unityProfilerRawEnabled } else { '' }
        Valid = if ($metadata) { $metadata.valid } else { '' }
        InvalidReason = if ($metadata) { $metadata.invalidReason } else { '' }
        RenderPresetId = if ($metadata) { $metadata.renderPresetId } else { '' }
        RenderPresetDisplayName = if ($metadata) { $metadata.renderPresetDisplayName } else { '' }
        RenderPipelineName = if ($metadata) { $metadata.renderPipelineName } else { '' }
        QualityLevel = if ($metadata) { $metadata.qualityLevel } else { '' }
        QualityName = if ($metadata) { $metadata.qualityName } else { '' }
        TargetFrameRate = if ($metadata) { $metadata.targetFrameRate } else { '' }
        ScreenWidth = if ($metadata) { $metadata.screenWidth } else { '' }
        ScreenHeight = if ($metadata) { $metadata.screenHeight } else { '' }
        ScreenRefreshRateHz = if ($metadata) { $metadata.screenRefreshRateHz } else { '' }
        RenderScale = if ($metadata) { $metadata.renderScale } else { '' }
        MainLightShadows = if ($metadata) { $metadata.mainLightShadows } else { '' }
        AdditionalLightShadows = if ($metadata) { $metadata.additionalLightShadows } else { '' }
        ShadowedAdditionalLightCount = if ($metadata) { $metadata.shadowedAdditionalLightCount } else { '' }
        SupportsGpuInstancing = if ($metadata) { $metadata.supportsGpuInstancing } else { '' }
        GrassGpuInstancing = if ($metadata) { $metadata.grassGpuInstancing } else { '' }
        GrassInstancingMaterialCount = if ($metadata) { $metadata.grassInstancingMaterialCount } else { '' }
        GrassInstancingRendererCount = if ($metadata) { $metadata.grassInstancingRendererCount } else { '' }
        GrassShadowCasterRendererCount = if ($metadata) { $metadata.grassShadowCasterRendererCount } else { '' }
        GrassVisibleRendererCount = if ($metadata) { $metadata.grassVisibleRendererCount } else { '' }
        Frames = $rows.Count
        AverageFps = Format-ResultNumber $averageFps
        FrameIntervalP50Ms = Format-ResultNumber (Get-Percentile $frameInterval 50)
        FrameIntervalP95Ms = Format-ResultNumber (Get-Percentile $frameInterval 95)
        FrameIntervalP99Ms = Format-ResultNumber (Get-Percentile $frameInterval 99)
        CpuFrameP50Ms = Format-ResultNumber (Get-Percentile $cpuFrame 50)
        CpuFrameP95Ms = Format-ResultNumber (Get-Percentile $cpuFrame 95)
        CpuFrameP99Ms = Format-ResultNumber (Get-Percentile $cpuFrame 99)
        CpuMainP50Ms = Format-ResultNumber (Get-Percentile $cpuMain 50)
        CpuMainP95Ms = Format-ResultNumber (Get-Percentile $cpuMain 95)
        CpuMainP99Ms = Format-ResultNumber (Get-Percentile $cpuMain 99)
        GpuP50Ms = Format-ResultNumber (Get-Percentile $gpuFrame 50)
        GpuP95Ms = Format-ResultNumber (Get-Percentile $gpuFrame 95)
        GpuP99Ms = Format-ResultNumber (Get-Percentile $gpuFrame 99)
        WarpRaycastP50Ms = Format-ResultNumber (Get-Percentile $warpRaycast 50)
        WarpRaycastP95Ms = Format-ResultNumber (Get-Percentile $warpRaycast 95)
        WarpRaycastP99Ms = Format-ResultNumber (Get-Percentile $warpRaycast 99)
        AudioDistanceFadeP50Ms = Format-ResultNumber (Get-Percentile $audioDistanceFade 50)
        AudioDistanceFadeP95Ms = Format-ResultNumber (Get-Percentile $audioDistanceFade 95)
        AudioDistanceFadeP99Ms = Format-ResultNumber (Get-Percentile $audioDistanceFade 99)
        ArrangeDisableNearColliderP50Ms = Format-ResultNumber (Get-Percentile $arrangeDisableNearCollider 50)
        ArrangeDisableNearColliderP95Ms = Format-ResultNumber (Get-Percentile $arrangeDisableNearCollider 95)
        ArrangeDisableNearColliderP99Ms = Format-ResultNumber (Get-Percentile $arrangeDisableNearCollider 99)
        Over16_67MsPercent = Format-ResultNumber (Get-Percentage $cpuFrame { $_ -gt 16.67 })
        Over20MsPercent = Format-ResultNumber (Get-Percentage $cpuFrame { $_ -gt 20.0 })
        Over33_33MsPercent = Format-ResultNumber (Get-Percentage $cpuFrame { $_ -gt 33.33 })
        BatchesP50 = Format-ResultNumber (Get-Percentile $batches 50)
        DrawCallsP50 = Format-ResultNumber (Get-Percentile $drawCalls 50)
        SetPassCallsP50 = Format-ResultNumber (Get-Percentile $setPassCalls 50)
        TrianglesP50 = Format-ResultNumber (Get-Percentile $triangles 50)
        VerticesP50 = Format-ResultNumber (Get-Percentile $vertices 50)
        GcAllocP50Bytes = Format-ResultNumber (Get-Percentile $gcAllocated 50)
        GcAllocP95Bytes = Format-ResultNumber (Get-Percentile $gcAllocated 95)
        GcAllocNonZeroPercent = Format-ResultNumber (Get-Percentage $gcAllocated { $_ -gt 0 })
        AudioUpdateCallsP50 = Format-ResultNumber (Get-Percentile $audioCalls 50)
        AudioUpdateCallsP95 = Format-ResultNumber (Get-Percentile $audioCalls 95)
        ArrangeCandidatesP50 = Format-ResultNumber (Get-Percentile $arrangeCandidates 50)
        ArrangeCandidatesP95 = Format-ResultNumber (Get-Percentile $arrangeCandidates 95)
        BatteryStartC = if ($metadata) { $metadata.batteryTemperaturePreWarmupC } else { '' }
        BatteryCaptureStartC = if ($metadata) { $metadata.batteryTemperatureStartC } else { '' }
        BatteryEndC = if ($metadata) { $metadata.batteryTemperatureEndC } else { '' }
        WarmupTemperatureRiseC = if ($metadata) { $metadata.warmupTemperatureRiseC } else { '' }
        BatteryStartPercent = if ($metadata) { $metadata.batteryPercentPreWarmup } else { '' }
        BatteryCaptureStartPercent = if ($metadata) { $metadata.batteryPercentStart } else { '' }
        BatteryEndPercent = if ($metadata) { $metadata.batteryPercentEnd } else { '' }
        ThermalStatusStart = if ($metadata) { $metadata.thermalStatusPreWarmup } else { '' }
        ThermalStatusCaptureStart = if ($metadata) { $metadata.thermalStatusStart } else { '' }
        ThermalStatusEnd = if ($metadata) { $metadata.thermalStatusEnd } else { '' }
        PowerSourceStart = if ($metadata) { $metadata.powerSourcePreWarmup } else { '' }
        PowerSourceCaptureStart = if ($metadata) { $metadata.powerSourceStart } else { '' }
        PowerSourceEnd = if ($metadata) { $metadata.powerSourceEnd } else { '' }
        SourceCsv = $csvFile.FullName
    }
}

$summaryPath = Join-Path $resolvedOutput 'run-summary.csv'
$summaries |
    Sort-Object ScenarioId, PairId, Variant, RunId |
    Export-Csv -LiteralPath $summaryPath -NoTypeInformation -Encoding UTF8

Get-Item -LiteralPath $summaryPath
