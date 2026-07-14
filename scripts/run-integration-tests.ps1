#requires -Version 5.1
<#
.SYNOPSIS
    Builds and runs PgSchemaExporter integration tests inside a Docker container.
.DESCRIPTION
    This script uses Docker-outside-of-Docker (sibling containers) to run
    Testcontainers-based integration tests. It is intended for Docker Desktop
    on Windows with WSL2 / Linux container mode.
#>

[CmdletBinding()]
param(
    [string]$ImageName = 'pgschema-exporter-tests',
    [string]$ResultsDirectory = 'test-results'
)

$ErrorActionPreference = 'Stop'

# Ensure the results directory exists on the host.
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

# Build the test image.
Write-Host "Building Docker image $ImageName..." -ForegroundColor Cyan
docker build -t $ImageName -f Dockerfile .

# Run the integration tests.
# Use --network host and the raw Docker socket so Testcontainers can connect
# back to the host and manage the PostgreSQL sibling containers.
Write-Host "Running integration tests in Docker..." -ForegroundColor Cyan
docker run --rm `
    --network host `
    -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
    -e TESTCONTAINERS_RYUK_DISABLED=true `
    -v /var/run/docker.sock.raw:/var/run/docker.sock `
    -v "${PWD}/${ResultsDirectory}:/testresults" `
    $ImageName `
    test -c Release --no-build `
    --filter "FullyQualifiedName~IntegrationTests|FullyQualifiedName~EndToEndTests" `
    --verbosity normal `
    --logger "console;verbosity=normal" `
    --logger "trx;LogFileName=IntegrationTestResults.trx" `
    --results-directory /testresults

$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "Integration tests passed. Results are in ./$ResultsDirectory" -ForegroundColor Green
} else {
    Write-Host "Integration tests failed with exit code $exitCode." -ForegroundColor Red
}

exit $exitCode
