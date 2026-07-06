using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class PostgresMetadataProviderIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = string.Empty;

    public PostgresMetadataProviderIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task LoadAsync_LoadsAllObjectTypes()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions
            {
                Schemas = true,
                Extensions = true,
                Types = true,
                Sequences = true,
                Domains = true,
                ForeignTables = true,
                Tables = true,
                Constraints = true,
                Indexes = true,
                Views = true,
                Triggers = true,
                EventTriggers = true,
                Rules = true,
                Aggregates = true,
                Operators = true,
                Casts = true,
                Publications = true,
                Subscriptions = true,
                Policies = true,
                Comments = true,
                Grants = true,
                Functions = true
            }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotNull(model);
        Assert.NotEmpty(model.Schemas);
        Assert.Contains(model.Schemas, s => s.Name == "public");
        Assert.NotEmpty(model.Tables);
        Assert.NotEmpty(model.Extensions);
    }

    [Fact]
    public async Task LoadAsync_ParallelMode_ProducesSameResultAsSequential()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions
            {
                Tables = true,
                Views = true,
                Functions = true,
                Indexes = true
            }
        };

        // Act
        var sequentialModel = await provider.LoadAsync(_connectionString, options);
        options.Parallel = true;
        var parallelModel = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.Equal(sequentialModel.Tables.Count, parallelModel.Tables.Count);
        Assert.Equal(sequentialModel.Views.Count, parallelModel.Views.Count);
        Assert.Equal(sequentialModel.Functions.Count, parallelModel.Functions.Count);
        Assert.Equal(sequentialModel.Indexes.Count, parallelModel.Indexes.Count);
    }

    [Fact]
    public async Task LoadAsync_WithSchemaFilter_LoadsOnlySpecifiedSchemas()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE SCHEMA IF NOT EXISTS test_schema;";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["test_schema"],
            Include = new IncludeOptions { Tables = true, Schemas = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.Contains(model.Schemas, s => s.Name == "test_schema");
        Assert.DoesNotContain(model.Schemas, s => s.Name == "public");
    }

    [Fact]
    public async Task LoadAsync_WithExcludeSchemas_ExcludesSpecifiedSchemas()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE SCHEMA IF NOT EXISTS excluded_schema;";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public", "excluded_schema"],
            ExcludeSchemas = ["excluded_schema"],
            Include = new IncludeOptions { Tables = true, Schemas = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.Contains(model.Schemas, s => s.Name == "public");
        Assert.DoesNotContain(model.Schemas, s => s.Name == "excluded_schema");
    }

    [Fact]
    public async Task LoadAsync_WithIncludeFilters_LoadsOnlyIncludedTypes()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions
            {
                Tables = true,
                Views = false,
                Functions = false,
                Indexes = false
            }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Tables);
        Assert.Empty(model.Views);
        Assert.Empty(model.Functions);
        Assert.Empty(model.Indexes);
    }

    [Fact]
    public async Task LoadAsync_SubscriptionPermissionError_DoesNotFailEntireExport()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions
            {
                Tables = true,
                Subscriptions = true
            }
        };

        // Act & Assert - should not throw even if subscriptions fail due to permissions
        var model = await provider.LoadAsync(_connectionString, options);
        Assert.NotNull(model);
        Assert.NotEmpty(model.Tables);
    }

    [Fact]
    public async Task LoadAsync_LoadsCustomTypes()
    {
        // Arrange
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TYPE status_enum AS ENUM ('active', 'inactive', 'pending');
            CREATE TYPE contact_info AS (
                email text,
                phone text
            );
        ";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions { Types = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Types);
        Assert.Contains(model.Types, t => t.Name == "status_enum");
        Assert.Contains(model.Types, t => t.Name == "contact_info");
    }

    [Fact]
    public async Task LoadAsync_LoadsDomains()
    {
        // Arrange
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE DOMAIN email_domain AS text 
            CHECK (VALUE ~ '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$');
        ";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions { Domains = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Domains);
        Assert.Contains(model.Domains, d => d.Name == "email_domain");
    }

    [Fact]
    public async Task LoadAsync_LoadsExtensions()
    {
        // Arrange
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS plpgsql;";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Include = new IncludeOptions { Extensions = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Extensions);
        Assert.Contains(model.Extensions, e => e.Name == "plpgsql");
    }

    [Fact]
    public async Task LoadAsync_LoadsFunctions()
    {
        // Arrange
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE OR REPLACE FUNCTION add_numbers(a integer, b integer)
            RETURNS integer AS $$
            BEGIN
                RETURN a + b;
            END;
            $$ LANGUAGE plpgsql;
        ";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions { Functions = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Functions);
        Assert.Contains(model.Functions, f => f.Name == "add_numbers");
    }

    [Fact]
    public async Task LoadAsync_LoadsViews()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE OR REPLACE VIEW user_summary AS
            SELECT id, email FROM users;
        ";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions { Views = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Views);
        Assert.Contains(model.Views, v => v.Name == "user_summary");
    }

    [Fact]
    public async Task LoadAsync_LoadsTriggers()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE OR REPLACE FUNCTION update_modified_column()
            RETURNS TRIGGER AS $$
            BEGIN
                NEW.modified = NOW();
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER users_update_modified
                BEFORE UPDATE ON users
                FOR EACH ROW
                EXECUTE FUNCTION update_modified_column();
        ";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions { Triggers = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Triggers);
        Assert.Contains(model.Triggers, t => t.Name == "users_update_modified");
    }

    [Fact]
    public async Task LoadAsync_LoadsIndexes()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE INDEX users_email_idx ON users(email);";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions { Indexes = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Indexes);
        Assert.Contains(model.Indexes, i => i.Name == "users_email_idx");
    }

    [Fact]
    public async Task LoadAsync_LoadsConstraints()
    {
        // Arrange
        await SetupTestDatabaseAsync();
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            Schemas = ["public"],
            Include = new IncludeOptions { Constraints = true }
        };

        // Act
        var model = await provider.LoadAsync(_connectionString, options);

        // Assert
        Assert.NotEmpty(model.Constraints);
        Assert.Contains(model.Constraints, c => c.Name == "users_pkey");
    }

    private async Task SetupTestDatabaseAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id SERIAL PRIMARY KEY,
                email VARCHAR(255) NOT NULL UNIQUE,
                created_at TIMESTAMP DEFAULT NOW()
            );
            
            CREATE TABLE IF NOT EXISTS orders (
                id SERIAL PRIMARY KEY,
                user_id INTEGER REFERENCES users(id),
                total DECIMAL(10,2)
            );
        ";
        await cmd.ExecuteNonQueryAsync();
    }
}
