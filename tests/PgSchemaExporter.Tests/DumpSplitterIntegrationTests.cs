using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class DumpSplitterIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public DumpSplitterIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-split-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task SplitAsync_ValidDump_SplitsIntoFiles()
    {
        var dumpPath = Path.Combine(_tempRoot, "dump.sql");
        var outputDir = Path.Combine(_tempRoot, "out");

        await File.WriteAllTextAsync(dumpPath, @"
-- PostgreSQL database dump

SET statement_timeout = 0;
SELECT pg_catalog.set_config('search_path', '', false);

CREATE SCHEMA public;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TYPE public.status AS ENUM ('active', 'inactive');

CREATE SEQUENCE public.users_id_seq;

CREATE TABLE public.users (
    id integer NOT NULL,
    email character varying(255) NOT NULL
);

ALTER TABLE public.users ADD CONSTRAINT users_pkey PRIMARY KEY (id);

CREATE INDEX users_email_idx ON public.users (email);

CREATE OR REPLACE VIEW public.active_users AS
SELECT id, email FROM public.users WHERE status = 'active';

CREATE OR REPLACE FUNCTION public.add_numbers(a integer, b integer) RETURNS integer
    LANGUAGE sql
    AS $$ SELECT $1 + $2; $$;

COMMENT ON TABLE public.users IS 'Application users';

GRANT SELECT ON TABLE public.users TO app_reader;
");

        var splitter = new DumpSplitter(
            new SqlStatementSplitter(),
            new PgDumpObjectClassifier(),
            new DumpSplitFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await splitter.SplitAsync(new SplitDumpOptions
        {
            InputFile = dumpPath,
            OutputDirectory = outputDir,
            GenerateDeployScript = true
        });

        Assert.True(Directory.Exists(outputDir));
        Assert.True(File.Exists(Path.Combine(outputDir, "schemas", "public.public.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "extensions", "public.pgcrypto.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "types", "public.status.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "sequences", "public.users_id_seq.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "tables", "public.users.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "constraints", "public.users.constraints.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "indexes", "public.users.indexes.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "views", "public.active_users.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "functions", "public.add_numbers.sql")));
        var commentFiles = Directory.GetFiles(Path.Combine(outputDir, "comments"), "comments.comment_*.sql");
        if (commentFiles.Length != 1)
        {
            var allFiles = Directory.GetFiles(outputDir, "*.*", SearchOption.AllDirectories);
            throw new InvalidOperationException(
                $"Expected one comment file, found {commentFiles.Length}. All files: {string.Join(", ", allFiles)}");
        }
        Assert.Single(Directory.GetFiles(Path.Combine(outputDir, "grants"), "grants.grant_*.sql"));
        Assert.True(File.Exists(Path.Combine(outputDir, "deploy.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "README.md")));
        Assert.True(File.Exists(Path.Combine(outputDir, "dependencies.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "split-report.md")));
    }

    [Fact]
    public async Task SplitAsync_CleanOutputDirectory_RemovesStaleFiles()
    {
        var dumpPath = Path.Combine(_tempRoot, "dump.sql");
        var outputDir = Path.Combine(_tempRoot, "clean");

        await File.WriteAllTextAsync(dumpPath, "CREATE TABLE public.t (id int);");
        Directory.CreateDirectory(outputDir);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "stale.txt"), "stale");

        var splitter = new DumpSplitter(
            new SqlStatementSplitter(),
            new PgDumpObjectClassifier(),
            new DumpSplitFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await splitter.SplitAsync(new SplitDumpOptions
        {
            InputFile = dumpPath,
            OutputDirectory = outputDir,
            CleanOutputDirectory = true
        });

        Assert.False(File.Exists(Path.Combine(outputDir, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(outputDir, "tables", "public.t.sql")));
    }

    [Fact]
    public async Task SplitAsync_MissingInput_ThrowsFileNotFoundException()
    {
        var splitter = new DumpSplitter(
            new SqlStatementSplitter(),
            new PgDumpObjectClassifier(),
            new DumpSplitFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            splitter.SplitAsync(new SplitDumpOptions
            {
                InputFile = Path.Combine(_tempRoot, "missing.sql"),
                OutputDirectory = Path.Combine(_tempRoot, "out")
            }));
    }

    [Fact]
    public async Task SplitAsync_NoDeployScript_DoesNotWriteDeploySql()
    {
        var dumpPath = Path.Combine(_tempRoot, "dump.sql");
        var outputDir = Path.Combine(_tempRoot, "no-deploy");

        await File.WriteAllTextAsync(dumpPath, "CREATE TABLE public.t (id int);");

        var splitter = new DumpSplitter(
            new SqlStatementSplitter(),
            new PgDumpObjectClassifier(),
            new DumpSplitFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await splitter.SplitAsync(new SplitDumpOptions
        {
            InputFile = dumpPath,
            OutputDirectory = outputDir,
            GenerateDeployScript = false
        });

        Assert.False(File.Exists(Path.Combine(outputDir, "deploy.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "tables", "public.t.sql")));
    }
}
