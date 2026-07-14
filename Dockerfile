# syntax=docker/dockerfile:1
# Builds and runs integration tests for PgSchemaExporter in a container.
# Requires the Docker daemon socket to be mounted at /var/run/docker.sock
# (Docker-outside-of-Docker) so Testcontainers can start the PostgreSQL container.
FROM mcr.microsoft.com/dotnet/sdk:8.0

# Run as root so the container process can access the mounted Docker socket.
USER root

# Docker Desktop Wormhole / sibling container configuration:
# Use the special DNS name that resolves to the host from inside a container.
# Mount the raw Docker socket at /var/run/docker.sock when running the image.
ENV TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal
# Ryuk depends on the same host connectivity; disable it for the test image.
ENV TESTCONTAINERS_RYUK_DISABLED=true

WORKDIR /src
COPY . .

# Restore and build the test project.
RUN dotnet restore tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj
RUN dotnet build tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj -c Release --no-restore

WORKDIR /src/tests/PgSchemaExporter.Tests

# Run only integration/end-to-end tests and produce a TRX report.
# Mount /testresults when running to retrieve the report.
ENTRYPOINT ["dotnet", "test", "-c", "Release", "--no-build", \
            "--filter", "FullyQualifiedName~IntegrationTests|FullyQualifiedName~EndToEndTests", \
            "--verbosity", "normal", \
            "--logger", "console;verbosity=normal", \
            "--logger", "trx;LogFileName=IntegrationTestResults.trx", \
            "--results-directory", "/testresults"]
