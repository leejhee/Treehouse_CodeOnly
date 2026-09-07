param(
    [Parameter(Mandatory = $true)]
    [string]$SummaryCsv,

    [string]$OutputDirectory = '.\PerformanceEvidence\render-preset-comparison'
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

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

    $valid = @(
        $Values |
            Where-Object { -not [double]::IsNaN($_) -and $_ -ge 0 } |
            Sort-Object
    )
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
    param(
        [double]$Value,
        [string]$Format = 'F4'
    )

    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) {
        return ''
    }

    return $Value.ToString($Format, $culture)
}

function Test-TrueValue {
    param($Value)
    return [string]$Value -match '^(?i:true|1)$'
}

$metrics = @(
    [PSCustomObject]@{ Name = 'AverageFps'; Label = 'Average FPS'; HigherIsBetter = $true },
    [PSCustomObject]@{ Name = 'FrameIntervalP95Ms'; Label = 'Frame interval p95 (ms)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'CpuFrameP50Ms'; Label = 'CPU frame p50 (ms)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'CpuFrameP95Ms'; Label = 'CPU frame p95 (ms)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'CpuMainP95Ms'; Label = 'CPU main p95 (ms)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'GpuP50Ms'; Label = 'GPU p50 (ms)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'GpuP95Ms'; Label = 'GPU p95 (ms)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'Over16_67MsPercent'; Label = 'Frames over 16.67 ms (%)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'Over20MsPercent'; Label = 'Frames over 20 ms (%)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'Over33_33MsPercent'; Label = 'Frames over 33.33 ms (%)'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'BatchesP50'; Label = 'Batches p50'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'DrawCallsP50'; Label = 'Draw calls p50'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'SetPassCallsP50'; Label = 'SetPass calls p50'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'TrianglesP50'; Label = 'Triangles p50'; HigherIsBetter = $false },
    [PSCustomObject]@{ Name = 'VerticesP50'; Label = 'Vertices p50'; HigherIsBetter = $false }
)

$resolvedSummary = [System.IO.Path]::GetFullPath($SummaryCsv)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$rows = @(
    Import-Csv -LiteralPath $resolvedSummary |
        Where-Object {
            (Test-TrueValue $_.Valid) -and
            $_.ScenarioId -eq 'FIELD_STATIC_V1' -and
            $_.RenderPresetId -match '^R[0-6]$'
        }
)
if ($rows.Count -eq 0) {
    throw "No valid FIELD render-preset rows found in $resolvedSummary"
}

$presetSummaries = foreach ($group in @($rows | Group-Object RenderPresetId)) {
    $first = $group.Group[0]
    $summary = [ordered]@{
        RenderPresetId = $group.Name
        RenderPresetDisplayName = $first.RenderPresetDisplayName
        Runs = $group.Count
        RenderScale = $first.RenderScale
        MainLightShadows = $first.MainLightShadows
        AdditionalLightShadows = $first.AdditionalLightShadows
        SupportsGpuInstancing = $first.SupportsGpuInstancing
        GrassGpuInstancing = $first.GrassGpuInstancing
        GrassInstancingMaterialCount = $first.GrassInstancingMaterialCount
        GrassInstancingRendererCount = $first.GrassInstancingRendererCount
        GrassShadowCasterRendererCount = $first.GrassShadowCasterRendererCount
        GrassVisibleRendererCount = $first.GrassVisibleRendererCount
    }

    foreach ($metric in $metrics) {
        $values = [double[]]@(
            $group.Group | ForEach-Object { Convert-Number $_.($metric.Name) }
        )
        $summary[$metric.Name] = Format-Number (Get-Median $values)
    }

    [PSCustomObject]$summary
}

$presetSummaryPath = Join-Path $resolvedOutput 'preset-summary.csv'
$presetSummaries | Sort-Object RenderPresetId |
    Export-Csv -LiteralPath $presetSummaryPath -NoTypeInformation -Encoding UTF8

$baseline = @($presetSummaries | Where-Object RenderPresetId -eq 'R0')
if ($baseline.Count -ne 1) {
    throw "Expected exactly one R0 aggregate row, found $($baseline.Count)"
}

$comparisons = foreach ($candidate in @($presetSummaries | Where-Object RenderPresetId -ne 'R0')) {
    foreach ($metric in $metrics) {
        $baselineValue = Convert-Number $baseline[0].($metric.Name)
        $candidateValue = Convert-Number $candidate.($metric.Name)
        $changePercent = if (-not [double]::IsNaN($baselineValue) -and $baselineValue -ne 0) {
            if ($metric.HigherIsBetter) {
                100.0 * ($candidateValue - $baselineValue) / $baselineValue
            }
            else {
                100.0 * ($baselineValue - $candidateValue) / $baselineValue
            }
        }
        else {
            [double]::NaN
        }

        [PSCustomObject]@{
            CandidatePresetId = $candidate.RenderPresetId
            Metric = $metric.Name
            MetricLabel = $metric.Label
            R0 = Format-Number $baselineValue
            Candidate = Format-Number $candidateValue
            ImprovementPercent = Format-Number $changePercent 'F2'
            CandidateBetter = if ([double]::IsNaN($baselineValue) -or [double]::IsNaN($candidateValue)) {
                $false
            }
            elseif ($metric.HigherIsBetter) {
                $candidateValue -gt $baselineValue
            }
            else {
                $candidateValue -lt $baselineValue
            }
        }
    }
}

$comparisonPath = Join-Path $resolvedOutput 'comparison-to-R0.csv'
$comparisons | Sort-Object CandidatePresetId, Metric |
    Export-Csv -LiteralPath $comparisonPath -NoTypeInformation -Encoding UTF8

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine('# TreeHouse FIELD 렌더 프리셋 비교')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- 생성 시각: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))")
[void]$markdown.AppendLine("- 유효 실행: $($rows.Count)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| 프리셋 | 실행 | 평균 FPS | CPU Frame p95 | GPU p95 | >33.33ms | Batches | Draw Calls |')
[void]$markdown.AppendLine('|---|---:|---:|---:|---:|---:|---:|---:|')
foreach ($row in @($presetSummaries | Sort-Object RenderPresetId)) {
    [void]$markdown.AppendLine(
        "| $($row.RenderPresetId) | $($row.Runs) | $($row.AverageFps) | $($row.CpuFrameP95Ms) | " +
        "$($row.GpuP95Ms) | $($row.Over33_33MsPercent) | $($row.BatchesP50) | $($row.DrawCallsP50) |")
}

$reportPath = Join-Path $resolvedOutput 'render-preset-report.md'
[System.IO.File]::WriteAllText(
    $reportPath,
    $markdown.ToString(),
    [System.Text.UTF8Encoding]::new($false))

Get-Item -LiteralPath $presetSummaryPath, $comparisonPath, $reportPath
