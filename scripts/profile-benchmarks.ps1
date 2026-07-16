#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the BenchmarkDotNet suite under dotnet-trace and dotnet-counters.

.DESCRIPTION
    Publishes the benchmark project, starts dotnet-trace and dotnet-counters
    collectors, then launches the benchmark executable.  When the benchmark exits
    the collectors stop automatically.  Run this from the repository root or
    any subdirectory.

.PARAMETER Filter
    BenchmarkDotNet filter expression, e.g. "*SqlStatement*".

.PARAMETER PublishOutput
    Directory where the benchmark executable is published.

.PARAMETER TraceOutput
    Output file for dotnet-trace (nettrace format).

.PARAMETER CountersOutput
    Output file for dotnet-counters (CSV format).
#>
param(
    [string]$Filter = "*",
    [string]$PublishOutput = "$PSScriptRoot/../artifacts/benchmarks",
    [string]$TraceOutput = "PgSchemaExporter.Benchmarks.nettrace",
    [string]$CountersOutput = "PgSchemaExporter.Benchmarks.counters.csv",
    [switch]$ProductionLike
)

if ($ProductionLike) {
    $Filter = "*PostgresMetadataProvider*|*DeploymentPlanBuilder*"
}

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "../tests/PgSchemaExporter.Benchmarks/PgSchemaExporter.Benchmarks.csproj" | Resolve-Path
$publishDir = Join-Path $PSScriptRoot "../artifacts/benchmarks" | Resolve-Path

$counterList = @(
    "System.Runtime[cpu-usage,gc-heap-size,gc-committed-bytes,threadpool-queue-length,threadpool-thread-count,exception-count]"
    "Microsoft.AspNetCore.Hosting[requests-per-second]"
)

function Ensure-Tool {
    param([string]$tool)
    if (!(Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "Installing $tool..."
        dotnet tool install --global $tool
    }
}

Ensure-Tool "dotnet-trace"
Ensure-Tool "dotnet-counters"

Write-Host "Publishing benchmark project to $publishDir ..."
dotnet publish $project -c Release -o $publishDir | Out-Host

$exe = Join-Path $publishDir "PgSchemaExporter.Benchmarks.exe"
if (!(Test-Path $exe)) {
    $exe = Join-Path $publishDir "PgSchemaExporter.Benchmarks"
}

Write-Host "Launching benchmark: $exe --filter $Filter"
$benchmark = Start-Process -FilePath $exe -ArgumentList "--filter", $Filter -PassThru -NoNewWindow

Write-Host "Attaching collectors to PID $($benchmark.Id) ..."
$trace = Start-Process -FilePath "dotnet-trace" `
    -ArgumentList "collect", "--process-id", $benchmark.Id, "--output", $TraceOutput `
    -PassThru -NoNewWindow

$counters = Start-Process -FilePath "dotnet-counters" `
    -ArgumentList "collect", "--process-id", $benchmark.Id, "--counters", ($counterList -join ','), "--output", $CountersOutput, "--refresh-interval", "1" `
    -PassThru -NoNewWindow

try {
    Wait-Process -Id $benchmark.Id
    Write-Host "Benchmark finished. Collectors will stop automatically."
} finally {
    if (!$trace.HasExited) { Stop-Process -Id $trace.Id -ErrorAction SilentlyContinue }
    if (!$counters.HasExited) { Stop-Process -Id $counters.Id -ErrorAction SilentlyContinue }
}

Write-Host "Trace: $TraceOutput"
Write-Host "Counters: $CountersOutput"
