param(
    [Parameter(Mandatory = $true)]
    [string] $BaselineDir,

    [Parameter(Mandatory = $true)]
    [string] $CurrentDir,

    [Parameter()]
    [string] $OutputPath = "benchmark-comparison.md",

    [Parameter()]
    [double] $MaxMeanRegression = 1.10,

    [Parameter()]
    [double] $MaxAllocatedRegression = 1.20
)

function ParseValue($text)
{
    $m = [regex]::Match($text, '^\s*([\d.,]+)\s*(\S+)\s*$')
    if (-not $m.Success) { return $null }

    $value = [double]::Parse($m.Groups[1].Value.Replace(',', ''), [System.Globalization.CultureInfo]::InvariantCulture)
    $unit = $m.Groups[2].Value
    return @{ Value = $value; Unit = $unit }
}

function UnitToScale($unit)
{
    switch ($unit)
    {
        'ns' { return 1e-9 }
        'us' { return 1e-6 }
        'ms' { return 1e-3 }
        's'  { return 1 }
        'B'  { return 1 }
        'KB' { return 1024 }
        'MB' { return 1024 * 1024 }
        'GB' { return 1024 * 1024 * 1024 }
        default { return 1 }
    }
}

function ToBaseValue($parsed)
{
    if ($null -eq $parsed) { return $null }
    return $parsed.Value * (UnitToScale $parsed.Unit)
}

$baselineFiles = Get-ChildItem -Path $BaselineDir -Filter '*-report.csv' -ErrorAction SilentlyContinue
$currentFiles = Get-ChildItem -Path $CurrentDir -Filter '*-report.csv' -ErrorAction SilentlyContinue

$baselineByName = @{}
foreach ($file in $baselineFiles) { $baselineByName[$file.Name] = $file.FullName }

$currentByName = @{}
foreach ($file in $currentFiles) { $currentByName[$file.Name] = $file.FullName }

$hasRegression = $false

$lines = @()
$lines += "# Benchmark comparison"
$lines += ""
$lines += "Comparing current branch results in ``$CurrentDir`` against baseline in ``$BaselineDir``."
$lines += ""
$lines += "Thresholds: mean <= $($MaxMeanRegression.ToString('P0')), allocated <= $($MaxAllocatedRegression.ToString('P0'))."
$lines += ""
$lines += "| Benchmark | Method | Baseline Mean | Current Mean | Mean Ratio | Baseline Allocated | Current Allocated | Allocated Ratio |"
$lines += "|---|---|---|---|---|---|---|---|"

foreach ($fileName in ($currentByName.Keys | Sort-Object))
{
    if (-not $baselineByName.ContainsKey($fileName))
    {
        continue
    }

    $baselineCsv = Import-Csv -Path $baselineByName[$fileName]
    $currentCsv = Import-Csv -Path $currentByName[$fileName]

    foreach ($currentRow in $currentCsv)
    {
        $method = $currentRow.Method
        $baseRow = $baselineCsv | Where-Object { $_.Method -eq $method } | Select-Object -First 1
        if ($null -eq $baseRow) { continue }

        $baseMean = ToBaseValue (ParseValue $baseRow.Mean)
        $curMean = ToBaseValue (ParseValue $currentRow.Mean)
        $baseAlloc = ToBaseValue (ParseValue $baseRow.Allocated)
        $curAlloc = ToBaseValue (ParseValue $currentRow.Allocated)

        $meanRatioNum = if ($baseMean -and $curMean) { $curMean / $baseMean } else { $null }
        $allocRatioNum = if ($baseAlloc -and $curAlloc) { $curAlloc / $baseAlloc } else { $null }

        $meanRatio = if ($meanRatioNum) { "{0:P1}" -f $meanRatioNum } else { "" }
        $allocRatio = if ($allocRatioNum) { "{0:P1}" -f $allocRatioNum } else { "" }

        if ($meanRatioNum -and $meanRatioNum -gt $MaxMeanRegression)
        {
            $hasRegression = $true
            $lines += "| $fileName | $method | $($baseRow.Mean) | $($currentRow.Mean) | **$meanRatio** | $($baseRow.Allocated) | $($currentRow.Allocated) | $allocRatio |"
        }
        elseif ($allocRatioNum -and $allocRatioNum -gt $MaxAllocatedRegression)
        {
            $hasRegression = $true
            $lines += "| $fileName | $method | $($baseRow.Mean) | $($currentRow.Mean) | $meanRatio | $($baseRow.Allocated) | $($currentRow.Allocated) | **$allocRatio** |"
        }
        else
        {
            $lines += "| $fileName | $method | $($baseRow.Mean) | $($currentRow.Mean) | $meanRatio | $($baseRow.Allocated) | $($currentRow.Allocated) | $allocRatio |"
        }
    }
}

if ($lines.Length -eq 8)
{
    $lines += "| No matching benchmark reports found. | | | | | | | |"
}

$lines | Out-File -FilePath $OutputPath -Encoding utf8
Write-Host "Comparison table written to $OutputPath"

if ($hasRegression)
{
    Write-Error "Performance regression detected. See $OutputPath for details."
    exit 1
}
