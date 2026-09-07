param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,

    [string]$OutputPath = '.\PerformanceEvidence\validation\run-validation.csv',

    [switch]$AllowInvalidRuns
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$requiredColumns = @(
    'run_id',
    'frame_index',
    'elapsed_ms',
    'cpu_frame_ms',
    'cpu_main_thread_ms',
    'cpu_render_thread_ms',
    'gpu_frame_ms',
    'gc_allocated_bytes',
    'warp_raycast_ms',
    'audio_distance_fade_ms',
    'arrange_disable_near_collider_ms',
    'audio_update_calls',
    'arrange_candidates_checked',
    'thermal_status',
    'thermal_headroom',
    'battery_temperature_c',
    'thermal_telemetry_updated'
)
$fpsDiagnosticColumns = @(
    'frame_interval_ms',
    'batches_count',
    'draw_calls_count',
    'setpass_calls_count',
    'triangles_count',
    'vertices_count'
)

function Convert-Number {
    param($Value)

    $number = 0.0
    if ($null -ne $Value -and [double]::TryParse(
            [string]$Value,
            [System.Globalization.NumberStyles]::Float,
            $culture,
            [ref]$number)) {
        return $number
    }
    return [double]::NaN
}

function Get-Median {
    param([double[]]$Values)

    $valid = @($Values | Where-Object { -not [double]::IsNaN($_) } | Sort-Object)
    if ($valid.Count -eq 0) {
        return [double]::NaN
    }
    $middle = [int][Math]::Floor($valid.Count / 2)
    if (($valid.Count % 2) -eq 1) {
        return [double]$valid[$middle]
    }
    return ([double]$valid[$middle - 1] + [double]$valid[$middle]) / 2.0
}

function Format-Number {
    param([double]$Value)
    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) {
        return ''
    }
    return $Value.ToString('F4', $culture)
}

$resolvedInput = [System.IO.Path]::GetFullPath($InputDirectory)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory((Split-Path $resolvedOutput -Parent)) | Out-Null

$metadataFiles = @(Get-ChildItem -LiteralPath $resolvedInput -Filter '*.metadata.json' -File -Recurse)
if ($metadataFiles.Count -eq 0) {
    throw "No benchmark metadata files found under $resolvedInput"
}

$results = foreach ($metadataFile in $metadataFiles) {
    $metadata = Get-Content -LiteralPath $metadataFile.FullName -Raw | ConvertFrom-Json
    $runBasePath = $metadataFile.FullName -replace '\.metadata\.json$', ''
    $csvPath = $runBasePath + '.csv'
    $rawPath = $runBasePath + '.raw'
    $errors = [System.Collections.Generic.List[string]]::new()
    $isMetadataOnlyInvalid = $AllowInvalidRuns -and
        -not [bool]$metadata.valid -and
        [int]$metadata.capturedFrames -eq 0

    if (-not (Test-Path -LiteralPath $csvPath -PathType Leaf)) {
        $errors.Add('CSV missing')
    }
    $metricsSchemaVersion = [string]$metadata.metricsSchemaVersion
    if ($metricsSchemaVersion -notin @(
            'targeted-recorders-v2',
            'fps-render-diagnostics-v1',
            'fps-render-diagnostics-v2',
            'fps-render-diagnostics-v3',
            'fps-render-diagnostics-v4',
            'fps-render-diagnostics-v5',
            'fps-render-diagnostics-v6')) {
        $errors.Add('metrics schema version mismatch')
    }
    $rawEnabledProperty = $metadata.PSObject.Properties['unityProfilerRawEnabled']
    if ($null -eq $rawEnabledProperty -or [bool]$metadata.unityProfilerRawEnabled) {
        $errors.Add('Unity Profiler raw must be explicitly disabled')
    }
    if (Test-Path -LiteralPath $rawPath -PathType Leaf) {
        $errors.Add('unexpected Profiler raw artifact')
    }

    if ([string]$metadata.scenarioFixtureVersion -ne 'fixture-v1') {
        $errors.Add('fixture version mismatch')
    }
    if ([int]$metadata.deterministicSeed -ne 20230901) {
        $errors.Add('deterministic seed mismatch')
    }
    if ([int]$metadata.fixtureFieldObjectCount -ne 62 -or
        [int]$metadata.fixtureHouseFurnitureCount -ne 24 -or
        [int]$metadata.fixtureHouseCarpetCount -ne 4) {
        $errors.Add('fixture object-count metadata mismatch')
    }
    if (-not $AllowInvalidRuns -and -not [bool]$metadata.valid) {
        $errors.Add('metadata marks run invalid: ' + [string]$metadata.invalidReason)
    }

    $rows = @()
    $missingColumns = @()
    $elapsedEndMs = [double]::NaN
    $warpP50 = [double]::NaN
    $audioP50 = [double]::NaN
    $arrangeP50 = [double]::NaN
    $audioCallsP50 = [double]::NaN
    $arrangeCandidatesP50 = [double]::NaN
    $thermalUpdateCount = 0

    if (Test-Path -LiteralPath $csvPath -PathType Leaf) {
        $rows = @(Import-Csv -LiteralPath $csvPath)
        if ($rows.Count -eq 0) {
            if (-not $isMetadataOnlyInvalid) {
                $errors.Add('CSV has no frame rows')
            }
        }
        else {
            $columns = @($rows[0].PSObject.Properties.Name)
            $missingColumns = @($requiredColumns | Where-Object { $_ -notin $columns })
            if ($metricsSchemaVersion -in @('fps-render-diagnostics-v1', 'fps-render-diagnostics-v2', 'fps-render-diagnostics-v3', 'fps-render-diagnostics-v4', 'fps-render-diagnostics-v5', 'fps-render-diagnostics-v6')) {
                $missingColumns += @($fpsDiagnosticColumns | Where-Object { $_ -notin $columns })
            }
            if ($missingColumns.Count -gt 0) {
                $errors.Add('missing CSV columns: ' + ($missingColumns -join ','))
            }
            else {
                $elapsedEndMs = Convert-Number $rows[-1].elapsed_ms
                $warpP50 = Get-Median ([double[]]@($rows | ForEach-Object { Convert-Number $_.warp_raycast_ms }))
                $audioP50 = Get-Median ([double[]]@($rows | ForEach-Object { Convert-Number $_.audio_distance_fade_ms }))
                $arrangeP50 = Get-Median ([double[]]@($rows | ForEach-Object { Convert-Number $_.arrange_disable_near_collider_ms }))
                $audioCallsP50 = Get-Median ([double[]]@($rows | ForEach-Object { Convert-Number $_.audio_update_calls }))
                $arrangeCandidatesP50 = Get-Median ([double[]]@($rows | ForEach-Object { Convert-Number $_.arrange_candidates_checked }))
                $thermalUpdateCount = @($rows | Where-Object { $_.thermal_telemetry_updated -eq '1' }).Count

                if ($elapsedEndMs -lt 118000 -or $elapsedEndMs -gt 121000) {
                    $errors.Add('capture duration is outside 118-121 seconds')
                }
                if ([int]$metadata.capturedFrames -ne $rows.Count) {
                    $errors.Add('metadata capturedFrames does not match CSV rows')
                }
                if ($rows.Count -lt 1000) {
                    $errors.Add('too few captured frames')
                }
                if ($thermalUpdateCount -lt 10 -or $thermalUpdateCount -gt 13) {
                    $errors.Add('expected 10-13 thermal telemetry frames')
                }
                if ($warpP50 -lt 0) {
                    $errors.Add('warp marker recorder unavailable')
                }

                if ($metricsSchemaVersion -in @('fps-render-diagnostics-v1', 'fps-render-diagnostics-v2', 'fps-render-diagnostics-v3', 'fps-render-diagnostics-v4', 'fps-render-diagnostics-v5', 'fps-render-diagnostics-v6')) {
                    $presetId = [string]$metadata.renderPresetId
                    $expectedPresetPattern = if ($metricsSchemaVersion -in @('fps-render-diagnostics-v4', 'fps-render-diagnostics-v5', 'fps-render-diagnostics-v6')) {
                        '^R[0-6]$'
                    }
                    elseif ($metricsSchemaVersion -eq 'fps-render-diagnostics-v3') {
                        '^R[0-5]$'
                    }
                    elseif ($metricsSchemaVersion -eq 'fps-render-diagnostics-v2') {
                        '^R[0-4]$'
                    }
                    else {
                        '^R[0-3]$'
                    }
                    if ($presetId -notmatch $expectedPresetPattern) {
                        $errors.Add('render preset ID does not match the metrics schema')
                    }
                    if ([int]$metadata.targetFrameRate -ne 60) {
                        $errors.Add('FPS diagnostic target frame rate must be 60')
                    }
                    if ([int]$metadata.screenWidth -le 0 -or [int]$metadata.screenHeight -le 0 -or
                        (Convert-Number $metadata.screenRefreshRateHz) -le 0) {
                        $errors.Add('screen metadata is missing or invalid')
                    }

                    $expectedRenderScale = if ($presetId -in @('R2', 'R3')) { 0.8 } else { 1.0 }
                    if ([Math]::Abs((Convert-Number $metadata.renderScale) - $expectedRenderScale) -gt 0.01) {
                        $errors.Add('render scale does not match selected preset')
                    }
                    if ($presetId -in @('R1', 'R3') -and [bool]$metadata.additionalLightShadows) {
                        $errors.Add('additional light shadows must be disabled for R1/R3')
                    }
                    if ($presetId -in @('R1', 'R3') -and
                        [int]$metadata.shadowedAdditionalLightCount -ne 0) {
                        $errors.Add('shadowed additional light count must be zero for R1/R3')
                    }

                    if ($metricsSchemaVersion -in @('fps-render-diagnostics-v2', 'fps-render-diagnostics-v3', 'fps-render-diagnostics-v4', 'fps-render-diagnostics-v5', 'fps-render-diagnostics-v6')) {
                        $requiredInstancingProperties = @(
                            'supportsGpuInstancing',
                            'grassGpuInstancing',
                            'grassInstancingMaterialCount',
                            'grassInstancingRendererCount'
                        )
                        foreach ($propertyName in $requiredInstancingProperties) {
                            if ($null -eq $metadata.PSObject.Properties[$propertyName]) {
                                $errors.Add('missing instancing metadata: ' + $propertyName)
                            }
                        }

                        if ([int]$metadata.grassInstancingMaterialCount -ne 3) {
                            $errors.Add('grass instancing material count must be 3')
                        }
                        if ([int]$metadata.grassInstancingRendererCount -le 0) {
                            $errors.Add('grass instancing renderer count must be positive')
                        }
                        $expectedGrassInstancing = $presetId -eq 'R4'
                        if ([bool]$metadata.grassGpuInstancing -ne $expectedGrassInstancing) {
                            $errors.Add('grass GPU instancing state does not match selected preset')
                        }
                        if ($presetId -eq 'R4' -and -not [bool]$metadata.supportsGpuInstancing) {
                            $errors.Add('R4 requires device GPU instancing support')
                        }

                        if ($metricsSchemaVersion -eq 'fps-render-diagnostics-v3') {
                            if ($null -eq $metadata.PSObject.Properties['grassShadowCasterRendererCount']) {
                                $errors.Add('missing grass shadow caster metadata')
                            }
                            elseif ($presetId -eq 'R5' -and
                                [int]$metadata.grassShadowCasterRendererCount -ne 0) {
                                $errors.Add('grass shadow caster renderer count must be zero for R5')
                            }
                            elseif ($presetId -ne 'R5' -and
                                [int]$metadata.grassShadowCasterRendererCount -le 0) {
                                $errors.Add('grass shadow caster renderer count must be positive outside R5')
                            }
                        }

                        if ($metricsSchemaVersion -in @('fps-render-diagnostics-v4', 'fps-render-diagnostics-v5', 'fps-render-diagnostics-v6')) {
                            $requiredGrassRendererProperties = @(
                                'grassShadowCasterRendererCount',
                                'grassVisibleRendererCount'
                            )
                            foreach ($propertyName in $requiredGrassRendererProperties) {
                                if ($null -eq $metadata.PSObject.Properties[$propertyName]) {
                                    $errors.Add('missing grass renderer metadata: ' + $propertyName)
                                }
                            }

                            if ($presetId -eq 'R5' -and
                                [int]$metadata.grassShadowCasterRendererCount -ne 0) {
                                $errors.Add('grass shadow caster renderer count must be zero for R5')
                            }
                            elseif ($presetId -ne 'R5' -and
                                [int]$metadata.grassShadowCasterRendererCount -le 0) {
                                $errors.Add('grass shadow caster renderer count must be positive outside R5')
                            }

                            if ($presetId -eq 'R6' -and
                                [int]$metadata.grassVisibleRendererCount -ne 0) {
                                $errors.Add('visible grass renderer count must be zero for R6')
                            }
                            elseif ($presetId -ne 'R6' -and
                                [int]$metadata.grassVisibleRendererCount -le 0) {
                                $errors.Add('visible grass renderer count must be positive outside R6')
                            }
                        }

                        if ($metricsSchemaVersion -in @('fps-render-diagnostics-v5', 'fps-render-diagnostics-v6')) {
                            $requiredStaticBatchProperties = @(
                                'grassStaticBatchingExpected',
                                'grassStaticBatchRendererCount'
                            )
                            foreach ($propertyName in $requiredStaticBatchProperties) {
                                if ($null -eq $metadata.PSObject.Properties[$propertyName]) {
                                    $errors.Add('missing grass static batching metadata: ' + $propertyName)
                                }
                            }

                            $expectsGrassStaticBatching =
                                [string]$metadata.variant -eq 'FPSGB'
                            if ([bool]$metadata.grassStaticBatchingExpected -ne
                                $expectsGrassStaticBatching) {
                                $errors.Add('grass static batching expectation does not match variant')
                            }
                            if ($expectsGrassStaticBatching -and
                                [int]$metadata.grassStaticBatchRendererCount -le 0) {
                                $errors.Add('FPSGB requires statically batched grass renderers')
                            }
                            if ([string]$metadata.variant -eq 'FPS' -and
                                [int]$metadata.grassStaticBatchRendererCount -ne 0) {
                                $errors.Add('FPS baseline must not contain statically batched grass renderers')
                            }
                        }

                        if ($metricsSchemaVersion -eq 'fps-render-diagnostics-v6') {
                            $requiredMeshCombineProperties = @(
                                'grassMeshCombiningExpected',
                                'grassSourceRendererCount',
                                'grassRuntimeRendererCount',
                                'grassCombinedChunkCount'
                            )
                            foreach ($propertyName in $requiredMeshCombineProperties) {
                                if ($null -eq $metadata.PSObject.Properties[$propertyName]) {
                                    $errors.Add('missing grass mesh combining metadata: ' + $propertyName)
                                }
                            }

                            $expectsGrassMeshCombining =
                                [string]$metadata.variant -eq 'FPSGC'
                            if ([bool]$metadata.grassMeshCombiningExpected -ne
                                $expectsGrassMeshCombining) {
                                $errors.Add('grass mesh combining expectation does not match variant')
                            }
                            if ($expectsGrassMeshCombining) {
                                if ([int]$metadata.grassSourceRendererCount -ne 40240) {
                                    $errors.Add('FPSGC source grass renderer count must be 40240')
                                }
                                if ([int]$metadata.grassCombinedChunkCount -le 0 -or
                                    [int]$metadata.grassRuntimeRendererCount -ne
                                        [int]$metadata.grassCombinedChunkCount) {
                                    $errors.Add('FPSGC runtime grass renderers must match combined chunks')
                                }
                                if ([int]$metadata.grassRuntimeRendererCount -ge
                                    [int]$metadata.grassSourceRendererCount) {
                                    $errors.Add('FPSGC must reduce the grass renderer count')
                                }
                            }
                            elseif ([string]$metadata.variant -eq 'FPS') {
                                if ([int]$metadata.grassCombinedChunkCount -ne 0) {
                                    $errors.Add('FPS baseline must not contain combined grass chunks')
                                }
                                if ([int]$metadata.grassSourceRendererCount -ne
                                    [int]$metadata.grassRuntimeRendererCount) {
                                    $errors.Add('FPS baseline source/runtime grass counts must match')
                                }
                            }
                        }
                    }
                }

                if ([string]$metadata.scenarioId -eq 'FIELD_STATIC_V1') {
                    if ([string]$metadata.variant -eq 'A' -and $audioCallsP50 -ne 12) {
                        $errors.Add('FIELD audio_update_calls p50 must be 12')
                    }
                    if ($audioP50 -lt 0) {
                        $errors.Add('audio marker recorder unavailable')
                    }
                    if ($metricsSchemaVersion -in @('fps-render-diagnostics-v1', 'fps-render-diagnostics-v2', 'fps-render-diagnostics-v3', 'fps-render-diagnostics-v4', 'fps-render-diagnostics-v5', 'fps-render-diagnostics-v6') -and
                        [string]$metadata.variant -in @('FPS', 'FPSGB', 'FPSGC') -and
                        $audioCallsP50 -ne 4) {
                        $errors.Add('FPS diagnostic FIELD audio_update_calls p50 must be 4')
                    }
                }
                elseif ([string]$metadata.scenarioId -eq 'HOUSE_MIXED_24_STATIC_V1') {
                    if ([string]$metadata.variant -eq 'A' -and $arrangeCandidatesP50 -ne 24) {
                        $errors.Add('HOUSE arrange_candidates_checked p50 must be 24')
                    }
                    if ($arrangeP50 -lt 0) {
                        $errors.Add('arrange marker recorder unavailable')
                    }
                }
                else {
                    $errors.Add('unknown scenario ID')
                }
            }
        }
    }

    $batteryTemperatureDifference = [Math]::Abs(
        (Convert-Number $metadata.batteryTemperaturePreWarmupC) -
        (Convert-Number $metadata.referenceTemperatureC))
    $temperatureTolerance = Convert-Number $metadata.temperatureToleranceC
    if ([double]::IsNaN($batteryTemperatureDifference) -or
        [double]::IsNaN($temperatureTolerance) -or
        $batteryTemperatureDifference -gt [Math]::Abs($temperatureTolerance)) {
        $errors.Add('pre-warmup battery temperature is outside configured tolerance')
    }
    $batteryStart = Convert-Number $metadata.batteryPercentPreWarmup
    if ([double]::IsNaN($batteryStart) -or $batteryStart -lt 40 -or $batteryStart -gt 80) {
        $errors.Add('pre-warmup battery percentage is outside 40-80%')
    }
    if ([int]$metadata.thermalStatusPreWarmup -ne 0) {
        $errors.Add('pre-warmup thermal status is not NONE')
    }

    $preWarmupPowerProperty = $metadata.PSObject.Properties['externalPowerConnectedPreWarmup']
    $captureStartPowerProperty = $metadata.PSObject.Properties['externalPowerConnectedStart']
    $captureEndPowerProperty = $metadata.PSObject.Properties['externalPowerConnectedEnd']
    if ($null -eq $preWarmupPowerProperty -or [bool]$metadata.externalPowerConnectedPreWarmup) {
        $errors.Add('pre-warmup external power state is missing or connected')
    }
    if (-not $isMetadataOnlyInvalid) {
        if ($null -eq $captureStartPowerProperty -or [bool]$metadata.externalPowerConnectedStart) {
            $errors.Add('capture-start external power state is missing or connected')
        }
        if ([int]$metadata.thermalStatusStart -ne 0) {
            $errors.Add('capture-start thermal status is not NONE')
        }
    }
    if ([bool]$metadata.valid -and
        ($null -eq $captureEndPowerProperty -or [bool]$metadata.externalPowerConnectedEnd)) {
        $errors.Add('capture-end external power state is missing or connected')
    }

    $warmupRise = Convert-Number $metadata.warmupTemperatureRiseC
    $calculatedWarmupRise =
        (Convert-Number $metadata.batteryTemperatureStartC) -
        (Convert-Number $metadata.batteryTemperaturePreWarmupC)
    if (-not $isMetadataOnlyInvalid -and
        ([double]::IsNaN($warmupRise) -or
         [double]::IsNaN($calculatedWarmupRise) -or
         [Math]::Abs($warmupRise - $calculatedWarmupRise) -gt 0.11)) {
        $errors.Add('warmup temperature-rise metadata mismatch')
    }

    [PSCustomObject]@{
        RunId = [string]$metadata.runId
        Variant = [string]$metadata.variant
        ScenarioId = [string]$metadata.scenarioId
        Passed = $errors.Count -eq 0
        Errors = $errors -join '; '
        Frames = $rows.Count
        ElapsedEndMs = Format-Number $elapsedEndMs
        ThermalTelemetryFrames = $thermalUpdateCount
        WarpRaycastP50Ms = Format-Number $warpP50
        AudioDistanceFadeP50Ms = Format-Number $audioP50
        ArrangeDisableNearColliderP50Ms = Format-Number $arrangeP50
        AudioUpdateCallsP50 = Format-Number $audioCallsP50
        ArrangeCandidatesP50 = Format-Number $arrangeCandidatesP50
        MetadataPath = $metadataFile.FullName
    }
}

$results | Sort-Object ScenarioId, Variant, RunId |
    Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding UTF8

$failed = @($results | Where-Object { -not $_.Passed })
if ($failed.Count -gt 0) {
    throw "$($failed.Count) of $($results.Count) benchmark runs failed validation. See $resolvedOutput"
}

Get-Item -LiteralPath $resolvedOutput
