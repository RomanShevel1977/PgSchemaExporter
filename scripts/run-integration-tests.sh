#!/usr/bin/env bash
set -euo pipefail

# Builds and runs PgSchemaExporter integration tests inside a Docker container.
# Uses Docker-outside-of-Docker (sibling containers) so Testcontainers can start
# the PostgreSQL container. Intended for Docker Desktop / Docker Engine on Linux.

IMAGE_NAME="${IMAGE_NAME:-pgschema-exporter-tests}"
RESULTS_DIRECTORY="${RESULTS_DIRECTORY:-test-results}"

mkdir -p "$RESULTS_DIRECTORY"

echo "Building Docker image $IMAGE_NAME..."
docker build -t "$IMAGE_NAME" -f Dockerfile .

echo "Running integration tests in Docker..."
docker run --rm \
  --network host \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e TESTCONTAINERS_RYUK_DISABLED=true \
  -v /var/run/docker.sock.raw:/var/run/docker.sock \
  -v "$(pwd)/$RESULTS_DIRECTORY:/testresults" \
  "$IMAGE_NAME"
