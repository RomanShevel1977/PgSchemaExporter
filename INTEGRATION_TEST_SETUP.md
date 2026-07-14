# Running Integration Tests in Docker

This folder contains everything needed to run the PgSchemaExporter integration tests inside a Docker container.

## What the setup does

The tests use [Testcontainers for .NET](https://dotnet.testcontainers.org/) to start a `postgres:16-alpine` container on the fly. When the tests themselves run inside a Docker container, they must start the PostgreSQL container as a **sibling** container on the host Docker daemon. This is the *Docker Wormhole* / Docker-outside-of-Docker pattern.

## Files

- `Dockerfile` — .NET SDK image with the test project built and the required Testcontainers environment variables.
- `.dockerignore` — excludes `bin/`, `obj/`, `.git/`, and other local files from the image context.
- `docker-compose.yml` — ready-to-use compose file that builds the image and runs the tests.
- `scripts/run-integration-tests.ps1` — PowerShell one-liner for Windows.
- `scripts/run-integration-tests.sh` — Bash one-liner for Linux/macOS.

## Requirements

- Docker Desktop (or Docker Engine) with Linux containers enabled.
- On Windows: WSL2 backend is strongly recommended.
- The raw Docker socket must be available at `/var/run/docker.sock.raw` (Docker Desktop provides it).

## Environment variables

| Variable | Value | Why |
|----------|-------|-----|
| `TESTCONTAINERS_HOST_OVERRIDE` | `host.docker.internal` | Tells Testcontainers to use the special Docker Desktop DNS name to reach the host from inside the container. |
| `TESTCONTAINERS_RYUK_DISABLED` | `true` | Disables the Testcontainers resource-reaper container, which also needs host connectivity. Sibling containers are still stopped/deleted by the test lifecycle. |

## Quick launch

### Option 1: Docker Compose

```bash
docker compose up --build integration-tests
```

Results are written to `./test-results/IntegrationTestResults.trx`.

### Option 2: PowerShell script

```powershell
.\scripts\run-integration-tests.ps1
```

### Option 3: Bash script

```bash
./scripts/run-integration-tests.sh
```

### Option 4: Manual Docker commands

```bash
# Build the image
docker build -t pgschema-exporter-tests -f Dockerfile .

# Run the tests
docker run --rm --network host \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e TESTCONTAINERS_RYUK_DISABLED=true \
  -v /var/run/docker.sock.raw:/var/run/docker.sock \
  -v "$(pwd)/test-results:/testresults" \
  pgschema-exporter-tests
```

## Troubleshooting

### `ResourceReaperException: Initialization has been cancelled`

Missing `TESTCONTAINERS_HOST_OVERRIDE` or wrong Docker socket mount. Use `/var/run/docker.sock.raw` and `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`.

### `NpgsqlException: Failed to connect to 172.17.0.1:xxxxx`

Testcontainers is trying to reach the host using the default bridge gateway IP, but that address is not reachable from inside the container. Add `--network host` and `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`.

### Tests are slow on first run

The first run pulls `postgres:16-alpine` and builds the test image. Subsequent runs reuse the image and cached layers.

## Outputs

- Console output from `dotnet test`.
- `test-results/IntegrationTestResults.trx` — Visual Studio Test Results file.
- `IntegrationTestReport.md` — human-readable Markdown report (generated after a successful run).
