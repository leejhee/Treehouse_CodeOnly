param(
    [Parameter(Mandatory = $true)]
    [string]$SummaryCsv,

    [string]$OutputDirectory = '.\PerformanceEvidence\comparison',

    [double]$MaximumPairTemperatureDifferenceC = 1.0,

    [double]$MaximumPairBatteryDifferencePercent = 5.0
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

$metricDefinitions = @(
    [PSCustomObject]@{ Name = 'WarpRaycastP95Ms'; Label = 'Warp raycast p95 (ms)' },
    [PSCustomObject]@{ Name = 'AudioDistanceFadeP95Ms'; Label = 'Audio distance fade p95 (ms)' },
    [PSCustomObject]@{ Name = 'ArrangeDisableNearColliderP95Ms'; Label = 'Arrange collider p95 (ms)' },
    [PSCustomObject]@{ Name = 'GcAllocP95Bytes'; Label = 'GC allocation p95 (bytes/frame)' },
    [PSCustomObject]@{ Name = 'GcAllocNonZeroPercent'; Label = 'GC non-zero frames (%)' },
    [PSCustomObject]@{ Name = 'AudioUpdateCallsP50'; Label = 'Audio update calls p50 (/frame)' },
    [PSCustomObject]@{ Name = 'ArrangeCandidatesP50'; Label = 'Arrange candidates p50 (/frame)' },
    [PSCustomObject]@{ Name = 'CpuMainP95Ms'; Label = 'CPU main thread p95 (ms)' },
    [PSCustomObject]@{ Name = 'CpuFrameP95Ms'; Label = 'CPU frame p95 (ms)' },
    [PSCustomObject]@{ Name = 'GpuP95Ms'; Label = 'GPU frame p95 (ms)' },
    [PSCustomObject]@{ Name = 'Over16_67MsPercent'; Label = 'Frames over 16.67 ms (%)' },
    [PSCustomObject]@{ Name = 'Over33_33MsPercent'; Label = 'Frames over 33.33 ms (%)' }
)

$resolvedSummary = [System.IO.Path]::GetFullPath($SummaryCsv)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$allRows = @(Import-Csv -LiteralPath $resolvedSummary)
if ($allRows.Count -eq 0) {
    throw "No rows found in $resolvedSummary"
}

$validRows = @($allRows | Where-Object { Test-TrueValue $_.Valid })
$invalidRows = @($allRows | Where-Object { -not (Test-TrueValue $_.Valid) })

$pairResults = [System.Collections.Generic.List[object]]::new()
$pairAudit = [System.Collections.Generic.List[object]]::new()

$groups = @($validRows | Group-Object ScenarioId, PairId)
foreach ($group in $groups) {
    $rows = @($group.Group)
    $aRows = @($rows | Where-Object { $_.Variant -eq 'A' })
    $bRows = @($rows | Where-Object { $_.Variant -eq 'B' })
    $scenarioId = [string]$rows[0].ScenarioId
    $pairId = [string]$rows[0].PairId

    if ($aRows.Count -ne 1 -or $bRows.Count -ne 1) {
        $pairAudit.Add([PSCustomObject]@{
                ScenarioId = $scenarioId
                PairId = $pairId
                PairAccepted = $false
                Reason = "expected exactly one A and one B; found A=$($aRows.Count), B=$($bRows.Count)"
                ARunId = if ($aRows.Count -eq 1) { $aRows[0].RunId } else { '' }
                BRunId = if ($bRows.Count -eq 1) { $bRows[0].RunId } else { '' }
                StartTemperatureDifferenceC = ''
                StartBatteryDifferencePercent = ''
            })
        continue
    }

    $a = $aRows[0]
    $b = $bRows[0]
    $temperatureDifference = [Math]::Abs(
        (Convert-Number $a.BatteryStartC) - (Convert-Number $b.BatteryStartC))
    $batteryDifference = [Math]::Abs(
        (Convert-Number $a.BatteryStartPercent) - (Convert-Number $b.BatteryStartPercent))

    $reasons = [System.Collections.Generic.List[string]]::new()
    if ([double]::IsNaN($temperatureDifference) -or
        $temperatureDifference -gt $MaximumPairTemperatureDifferenceC) {
        $reasons.Add('start temperature mismatch')
    }
    if ([double]::IsNaN($batteryDifference) -or
        $batteryDifference -gt $MaximumPairBatteryDifferencePercent) {
        $reasons.Add('start battery mismatch')
    }
    if ($a.PairOrder -ne $b.PairOrder -or $a.PairOrder -notmatch '^(AB|BA)$') {
        $reasons.Add('pair order metadata mismatch')
    }
    if ($a.FixtureVersion -ne $b.FixtureVersion -or
        $a.DeterministicSeed -ne $b.DeterministicSeed) {
        $reasons.Add('scenario fixture metadata mismatch')
    }

    $pairAccepted = $reasons.Count -eq 0
    $pairAudit.Add([PSCustomObject]@{
            ScenarioId = $scenarioId
            PairId = $pairId
            PairAccepted = $pairAccepted
            Reason = $reasons -join '; '
            ARunId = $a.RunId
            BRunId = $b.RunId
            StartTemperatureDifferenceC = Format-Number $temperatureDifference 'F2'
            StartBatteryDifferencePercent = Format-Number $batteryDifference 'F1'
        })

    foreach ($metric in $metricDefinitions) {
        $aValue = Convert-Number $a.($metric.Name)
        $bValue = Convert-Number $b.($metric.Name)
        $delta = $bValue - $aValue
        $reductionPercent = if (-not [double]::IsNaN($aValue) -and $aValue -ne 0.0) {
            100.0 * ($aValue - $bValue) / $aValue
        }
        else {
            [double]::NaN
        }

        $pairResults.Add([PSCustomObject]@{
                ScenarioId = $scenarioId
                PairId = $pairId
                PairOrder = $a.PairOrder
                PairAccepted = $pairAccepted
                Metric = $metric.Name
                MetricLabel = $metric.Label
                A = Format-Number $aValue
                B = Format-Number $bValue
                DeltaBMinusA = Format-Number $delta
                ReductionPercent = Format-Number $reductionPercent 'F2'
                BLowerThanA = -not [double]::IsNaN($aValue) -and $bValue -lt $aValue
            })
    }
}

$pairAuditPath = Join-Path $resolvedOutput 'pair-audit.csv'
$pairAudit | Sort-Object ScenarioId, PairId |
    Export-Csv -LiteralPath $pairAuditPath -NoTypeInformation -Encoding UTF8

$pairResultsPath = Join-Path $resolvedOutput 'pair-comparison.csv'
$pairResults | Sort-Object ScenarioId, PairId, Metric |
    Export-Csv -LiteralPath $pairResultsPath -NoTypeInformation -Encoding UTF8

$aggregateRows = foreach ($scenarioGroup in @($pairResults | Where-Object PairAccepted | Group-Object ScenarioId)) {
    foreach ($metric in $metricDefinitions) {
        $metricRows = @($scenarioGroup.Group | Where-Object { $_.Metric -eq $metric.Name })
        $aValues = [double[]]@($metricRows | ForEach-Object { Convert-Number $_.A })
        $bValues = [double[]]@($metricRows | ForEach-Object { Convert-Number $_.B })
        $aMedian = Get-Median $aValues
        $bMedian = Get-Median $bValues
        $reductionPercent = if (-not [double]::IsNaN($aMedian) -and $aMedian -ne 0.0) {
            100.0 * ($aMedian - $bMedian) / $aMedian
        }
        else {
            [double]::NaN
        }

        [PSCustomObject]@{
            ScenarioId = $scenarioGroup.Name
            Metric = $metric.Name
            MetricLabel = $metric.Label
            AcceptedPairs = $metricRows.Count
            AMedian = Format-Number $aMedian
            BMedian = Format-Number $bMedian
            ReductionPercent = Format-Number $reductionPercent 'F2'
            BWins = @($metricRows | Where-Object { $_.BLowerThanA -eq 'True' -or $_.BLowerThanA -eq $true }).Count
        }
    }
}

$aggregatePath = Join-Path $resolvedOutput 'aggregate-summary.csv'
$aggregateRows | Sort-Object ScenarioId, Metric |
    Export-Csv -LiteralPath $aggregatePath -NoTypeInformation -Encoding UTF8

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine('# TreeHouse A/B 성능 비교')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- 생성 시각: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))")
[void]$markdown.AppendLine("- 유효 원본 실행: $($validRows.Count)")
[void]$markdown.AppendLine("- 원본에서 무효 표시된 실행: $($invalidRows.Count)")
[void]$markdown.AppendLine("- 허용 시작 온도 차이: $MaximumPairTemperatureDifferenceC °C")
[void]$markdown.AppendLine("- 허용 시작 배터리 차이: $MaximumPairBatteryDifferencePercent %p")

foreach ($scenario in @($aggregateRows | Group-Object ScenarioId)) {
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine("## $($scenario.Name)")
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| 지표 | 쌍 수 | A 중앙값 | B 중앙값 | 감소율 | B 우세 |')
    [void]$markdown.AppendLine('|---|---:|---:|---:|---:|---:|')
    foreach ($row in $scenario.Group) {
        $reduction = if ([string]::IsNullOrWhiteSpace($row.ReductionPercent)) {
            '-'
        }
        else {
            "$($row.ReductionPercent)%"
        }
        [void]$markdown.AppendLine(
            "| $($row.MetricLabel) | $($row.AcceptedPairs) | $($row.AMedian) | $($row.BMedian) | $reduction | $($row.BWins)/$($row.AcceptedPairs) |")
    }
}

$rejectedPairs = @($pairAudit | Where-Object { $_.PairAccepted -eq $false })
if ($rejectedPairs.Count -gt 0) {
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('## 제외된 대응 쌍')
    [void]$markdown.AppendLine()
    foreach ($pair in $rejectedPairs) {
        [void]$markdown.AppendLine("- $($pair.ScenarioId) / P$($pair.PairId): $($pair.Reason)")
    }
}

$markdownPath = Join-Path $resolvedOutput 'comparison-report.md'
[System.IO.File]::WriteAllText(
    $markdownPath,
    $markdown.ToString(),
    [System.Text.UTF8Encoding]::new($false))

Get-Item -LiteralPath $pairAuditPath, $pairResultsPath, $aggregatePath, $markdownPath
