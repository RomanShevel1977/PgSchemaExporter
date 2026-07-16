using BenchmarkDotNet.Attributes;
using Npgsql;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using Testcontainers.PostgreSql;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class PostgresMetadataProviderBenchmark : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();
    private readonly PostgresMetadataProvider _provider = new();
    private string _connectionString = "";

    [GlobalSetup]
    public async Task Setup()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Seed a schema with many objects to stress the catalog queries.
        for (var i = 0; i < 100; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
CREATE SCHEMA IF NOT EXISTS bench_{i};
CREATE TABLE bench_{i}.orders (
    id serial PRIMARY KEY,
    customer_id integer NOT NULL,
    amount numeric(12,2) DEFAULT 0.0,
    created_at timestamp DEFAULT now()
);
CREATE INDEX idx_bench_{i}_orders_customer ON bench_{i}.orders(customer_id);
ALTER TABLE bench_{i}.orders ADD CONSTRAINT chk_bench_{i}_amount_positive CHECK (amount >= 0);
COMMENT ON TABLE bench_{i}.orders IS 'benchmark table {i}';
""";
            await command.ExecuteNonQueryAsync();
        }
    }

    [Benchmark]
    public async Task LoadMetadata()
    {
        var options = new ExportOptions
        {
            Schemas = ["bench_0", "bench_25", "bench_50", "bench_75", "bench_99"],
            Include = new IncludeOptions { Tables = true, Constraints = true, Indexes = true, Comments = true }
        };

        _ = await _provider.LoadAsync(_connectionString, options);
    }

    [GlobalCleanup]
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
