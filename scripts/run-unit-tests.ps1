#requires -Version 5.1
<#
.SYNOPSIS
    Runs all PgSchemaExporter unit tests and prints a summary.
.DESCRIPTION
    Unit tests are all tests that are not marked as Integration or EndToEnd.
    The script writes a TRX report to the results directory and prints a concise
    statistics summary at the end.
#>

[CmdletBinding()]
param(
    [string]$ResultsDirectory = 'test-results/unit',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

$project = 'tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj'
$filter = 'FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEndTests'
$trx = Join-Path $ResultsDirectory 'UnitTestResults.trx'

$buildMode = if ($NoBuild) { ' (no build)' } else { '' }
Write-Host "Running unit tests$buildMode..." -ForegroundColor Cyan

$args = @(
    'test', $project,
    '-c', 'Release',
    '--filter', $filter,
    '--verbosity', 'normal',
    '--logger', 'console;verbosity=normal',
    '--logger', 'trx;LogFileName=UnitTestResults.trx',
    '--results-directory', $ResultsDirectory
)
if ($NoBuild) { $args += '--no-build' }

$process = Start-Process -FilePath 'dotnet' -ArgumentList $args -NoNewWindow -Wait -PassThru

$exitCode = $process.ExitCode

if (Test-Path $trx) {
    [xml]$trxXml = Get-Content $trx
    $counters = $trxXml.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $skipped = [int]$counters.notExecuted
    $rate = if ($total -gt 0) { [math]::Round($passed / $total * 100, 2) } else { 0 }

    $start = [datetime]$trxXml.TestRun.Times.start
    $finish = [datetime]$trxXml.TestRun.Times.finish
    $duration = $finish - $start

    Write-Host ''
    Write-Host 'Unit Test Results Summary' -ForegroundColor Cyan
    Write-Host '=========================' -ForegroundColor Cyan
    Write-Host "Total:    $total"
    Write-Host "Passed:   $passed" -ForegroundColor $(if ($failed -gt 0) { 'White' } else { 'Green' })
    Write-Host "Failed:   $failed" -ForegroundColor $(if ($failed -gt 0) { 'Red' } else { 'Green' })
    Write-Host "Skipped:  $skipped"
    Write-Host "Success:  $rate%"
    Write-Host "Duration: $duration"
    Write-Host "TRX:      $trx"
} else {
    Write-Warning "TRX report was not generated at $trx"
}

if ($exitCode -ne 0) {
    Write-Host "Unit tests failed with exit code $exitCode." -ForegroundColor Red
}

exit $exitCode
