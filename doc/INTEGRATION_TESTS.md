# Integration Tests

This document describes how to run integration tests for PgSchemaExporter.

## Overview

Integration tests use [Testcontainers](https://testcontainers.com/) to spin up real PostgreSQL containers for testing database interactions. These tests verify:

- **PostgresMetadataProvider**: Real database metadata loading, SQL queries, and object type coverage
- **End-to-end CLI workflows**: Full export, diff, and migration cycles against live databases

## Prerequisites

### Docker

Integration tests require Docker to be installed and running:

- **Windows**: Install [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- **Linux**: Install Docker Engine following [official instructions](https://docs.docker.com/engine/install/)
- **macOS**: Install [Docker Desktop](https://www.docker.com/products/docker-desktop/)

Verify Docker is running:
```bash
docker --version
docker ps
```

### .NET SDK

Ensure .NET 8.0 SDK is installed:
```bash
dotnet --version
```

## Running Integration Tests

### Run All Tests (Unit + Integration)

```bash
dotnet test tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj
```

### Run Only Integration Tests

Integration tests are marked with the `[Collection("PostgreSQL Integration")]` attribute. To run only integration tests:

```bash
dotnet test tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj --filter "FullyQualifiedName~PostgresMetadataProviderIntegrationTests"
dotnet test tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj --filter "FullyQualifiedName~CliEndToEndTests"
```

### Run Only Unit Tests

To skip integration tests and run only unit tests:

```bash
dotnet test tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj --filter "FullyQualifiedName!~PostgresMetadataProviderIntegrationTests & FullyQualifiedName!~CliEndToEndTests"
```

## Integration Test Details

### PostgresMetadataProviderIntegrationTests

Tests the `PostgresMetadataProvider` class against a real PostgreSQL 16 container:

- **LoadAsync_LoadsAllObjectTypes**: Verifies all 21 object types are loaded correctly
- **LoadAsync_ParallelMode_ProducesSameResultAsSequential**: Ensures parallel mode matches sequential results
- **LoadAsync_WithSchemaFilter_LoadsOnlySpecifiedSchemas**: Tests schema inclusion filtering
- **LoadAsync_WithExcludeSchemas_ExcludesSpecifiedSchemas**: Tests schema exclusion filtering
- **LoadAsync_WithIncludeFilters_LoadsOnlyIncludedTypes**: Tests object type filtering
- **LoadAsync_SubscriptionPermissionError_DoesNotFailEntireExport**: Verifies graceful error handling
- **LoadAsync_LoadsCustomTypes**: Tests enum, composite, and range types
- **LoadAsync_LoadsDomains**: Tests domain types
- **LoadAsync_LoadsExtensions**: Tests extension loading
- **LoadAsync_LoadsFunctions**: Tests function loading
- **LoadAsync_LoadsViews**: Tests view loading
- **LoadAsync_LoadsTriggers**: Tests trigger loading
- **LoadAsync_LoadsIndexes**: Tests index loading
- **LoadAsync_LoadsConstraints**: Tests constraint loading

### CliEndToEndTests

Tests complete CLI workflows against a real PostgreSQL 16 container:

- **ExportWorkflow_FullCycle**: Full export with file generation
- **ExportWorkflow_WithParallelMode**: Export with parallel metadata loading
- **ExportWorkflow_DryRun_DoesNotWriteFiles**: Dry-run mode verification
- **DiffWorkflow_DirectoryToDirectory**: Directory-to-directory comparison
- **DiffWorkflow_DirectoryToLiveDatabase**: Directory-to-live-database comparison
- **DiffWorkflow_LiveDatabaseToLiveDatabase**: Live-database-to-live-database comparison
- **MigrateWorkflow_GeneratesUpAndDownScripts**: Migration script generation
- **InitWorkflow_CreatesConfigTemplate**: Config template creation
- **InitWorkflow_WithOverwrite_OverwritesExistingFile**: Config overwrite behavior
- **InitWorkflow_WithoutOverwrite_ThrowsOnExistingFile**: Config overwrite protection
- **WatchWorkflow_EmitsInitialDiff**: Watch mode initial diff emission

## CI/CD Integration

### GitHub Actions

Integration tests can be run in CI using GitHub Actions with Docker support:

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_PASSWORD: testpass
          POSTGRES_DB: testdb
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0'
      - run: dotnet test tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj
```

### Skipping Integration Tests in CI

To skip integration tests in environments without Docker:

```bash
dotnet test tests/PgSchemaExporter.Tests/PgSchemaExporter.Tests.csproj --filter "FullyQualifiedName!~PostgresMetadataProviderIntegrationTests & FullyQualifiedName!~CliEndToEndTests"
```

Or set an environment variable and conditionally skip tests in code.

## Troubleshooting

### Docker Not Running

**Error**: `Failed to connect to Docker daemon`

**Solution**: Start Docker Desktop or Docker Engine:
```bash
# Linux
sudo systemctl start docker

# Windows/macOS
# Start Docker Desktop application
```

### Port Already in Use

**Error**: `port is already allocated`

**Solution**: Stop other containers using the port or let Testcontainers choose random ports (default behavior).

### Slow Test Execution

Integration tests are slower than unit tests because they:
- Pull Docker images (first run only)
- Start PostgreSQL containers
- Execute real database queries

**Mitigation**:
- Run integration tests separately from unit tests
- Use `--filter` to run only specific test classes
- Cache Docker images locally

### Windows-Specific Issues

On Windows, ensure:
- Docker Desktop is running with WSL 2 backend
- Firewall allows Docker container communication
- Antivirus doesn't block Docker operations

## Test Configuration

Integration tests use PostgreSQL 16 Alpine image by default. To change the PostgreSQL version:

```csharp
_postgres = new PostgreSqlBuilder()
    .WithImage("postgres:15-alpine")  // Change version here
    .WithDatabase("testdb")
    .WithUsername("testuser")
    .WithPassword("testpass")
    .Build();
```

## Adding New Integration Tests

When adding new integration tests:

1. Add the test to either `PostgresMetadataProviderIntegrationTests.cs` or `CliEndToEndTests.cs`
2. Use the `[Collection("PostgreSQL Integration")]` attribute to group tests
3. Implement `IAsyncLifetime` for setup/teardown if creating a new test class
4. Ensure tests clean up after themselves (use temp directories, drop test objects)
5. Keep tests focused on database-specific behavior
6. Avoid testing logic already covered by unit tests

Example:

```csharp
[Fact]
public async Task MyNewIntegrationTest()
{
    // Arrange
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "CREATE TABLE test_table (id int);";
    await cmd.ExecuteNonQueryAsync();

    var provider = new PostgresMetadataProvider();
    var options = new ExportOptions
    {
        ConnectionString = _connectionString,
        Schemas = ["public"],
        Include = new IncludeOptions { Tables = true }
    };

    // Act
    var model = await provider.LoadAsync(_connectionString, options);

    // Assert
    Assert.NotEmpty(model.Tables);
    Assert.Contains(model.Tables, t => t.Name == "test_table");
}
```

## Performance Considerations

- Integration tests take 30-60 seconds to run (Docker startup + test execution)
- Unit tests take 2-5 seconds
- Consider running integration tests only in CI or before releases
- Use parallel test execution where possible (Testcontainers handles this well)
