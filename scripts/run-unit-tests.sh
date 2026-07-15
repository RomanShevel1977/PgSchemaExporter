#!/usr/bin/env bash

# Runs all PgSchemaExporter unit tests and prints a summary.
# Unit tests are all tests that are not marked as Integration or EndToEnd.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

NO_BUILD=0
for arg in "$@"; do
  if [ "$arg" = '--no-build' ]; then
    NO_BUILD=1
  fi
done

RESULTS_DIRECTORY="${RESULTS_DIRECTORY:-test-results/unit}"
mkdir -p "$RESULTS_DIRECTORY"

PROJECT="tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj"
FILTER='FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEndTests'
LOG_FILE="$RESULTS_DIRECTORY/unit-test.log"

if [ "$NO_BUILD" -eq 1 ]; then
  printf 'Running unit tests (no build)...\n'
else
  printf 'Running unit tests...\n'
fi

DOTNET_ARGS=(
  test "$PROJECT"
  -c Release
  --filter "$FILTER"
  --verbosity normal
  --logger "console;verbosity=normal"
  --logger "trx;LogFileName=UnitTestResults.trx"
  --results-directory "$RESULTS_DIRECTORY"
)
if [ "$NO_BUILD" -eq 1 ]; then
  DOTNET_ARGS+=(--no-build)
fi

dotnet "${DOTNET_ARGS[@]}" \
  2>&1 | tee "$LOG_FILE"

EXIT_CODE=${PIPESTATUS[0]}

printf '\n'
printf 'Unit Test Results Summary\n'
printf '=========================\n'

SUMMARY=$(grep -E '^Test summary:' "$LOG_FILE" | head -n1 || true)
if [ -n "$SUMMARY" ]; then
  TOTAL=$(echo "$SUMMARY" | sed -n 's/.*total: \([0-9]*\).*/\1/p')
  PASSED=$(echo "$SUMMARY" | sed -n 's/.*succeeded: \([0-9]*\).*/\1/p')
  FAILED=$(echo "$SUMMARY" | sed -n 's/.*failed: \([0-9]*\).*/\1/p')
  SKIPPED=$(echo "$SUMMARY" | sed -n 's/.*skipped: \([0-9]*\).*/\1/p')
  DURATION=$(echo "$SUMMARY" | sed -n 's/.*duration: \([0-9.]*s\).*/\1/p')

  if [ -n "$TOTAL" ] && [ "$TOTAL" -gt 0 ]; then
    RATE=$(awk "BEGIN { printf \"%.2f\", $PASSED / $TOTAL * 100 }")
  else
    RATE="0.00"
  fi

  printf 'Total:    %s\n' "$TOTAL"
  printf 'Passed:   %s\n' "$PASSED"
  printf 'Failed:   %s\n' "$FAILED"
  printf 'Skipped:  %s\n' "$SKIPPED"
  printf 'Success:  %s%%\n' "$RATE"
  printf 'Duration: %s\n' "$DURATION"
  printf 'TRX:      %s/UnitTestResults.trx\n' "$RESULTS_DIRECTORY"
else
  printf 'Could not find test summary in log file.\n'
fi

exit "$EXIT_CODE"
