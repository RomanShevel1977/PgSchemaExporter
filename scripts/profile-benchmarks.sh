#!/usr/bin/env bash
set -euo pipefail

FILTER="${1:-*}"
PRODUCTION_LIKE="${5:-false}"

if [[ "$PRODUCTION_LIKE" == "true" ]]; then
    FILTER="*PostgresMetadataProvider*|*DeploymentPlanBuilder*"
fi

PUBLISH_OUTPUT="${2:-$(cd "$(dirname "$0")/.." && pwd)/artifacts/benchmarks}"
TRACE_OUTPUT="${3:-PgSchemaExporter.Benchmarks.nettrace}"
COUNTERS_OUTPUT="${4:-PgSchemaExporter.Benchmarks.counters.csv}"
COUNTERS="System.Runtime[cpu-usage,gc-heap-size,gc-committed-bytes,threadpool-queue-length,threadpool-thread-count,exception-count],Microsoft.AspNetCore.Hosting[requests-per-second]"

PROJECT="$(cd "$(dirname "$0")/.." && pwd)/tests/PgSchemaExporter.Benchmarks/PgSchemaExporter.Benchmarks.csproj"

ensure_tool() {
    if ! command -v "$1" &> /dev/null; then
        echo "Installing $1..."
        dotnet tool install --global "$1"
    fi
}

ensure_tool dotnet-trace
ensure_tool dotnet-counters

echo "Publishing benchmark project to $PUBLISH_OUTPUT ..."
dotnet publish "$PROJECT" -c Release -o "$PUBLISH_OUTPUT"

EXE="$PUBLISH_OUTPUT/PgSchemaExporter.Benchmarks"
if [[ -f "$EXE.exe" ]]; then
    EXE="$EXE.exe"
fi

echo "Launching benchmark: $EXE --filter $FILTER"
"$EXE" --filter "$FILTER" &
BENCHMARK_PID=$!

echo "Attaching collectors to PID $BENCHMARK_PID ..."
dotnet-trace collect --process-id "$BENCHMARK_PID" --output "$TRACE_OUTPUT" &
TRACE_PID=$!

dotnet-counters collect --process-id "$BENCHMARK_PID" --output "$COUNTERS_OUTPUT" --counters "$COUNTERS" --refresh-interval 1 &
COUNTERS_PID=$!

wait "$BENCHMARK_PID"

echo "Benchmark finished. Stopping collectors..."
kill "$TRACE_PID" "$COUNTERS_PID" 2>/dev/null || true

echo "Trace: $TRACE_OUTPUT"
echo "Counters: $COUNTERS_OUTPUT"
