using System.Diagnostics;
using System.Text;
using Xunit;

namespace PgSchemaExporter.Tests;

public class ProgramIntegrationTests : IDisposable
{
    private readonly string _exePath;
    private readonly string _baseDir;

    public ProgramIntegrationTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"pgschema-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_baseDir);
        _exePath = Path.Combine(AppContext.BaseDirectory, "pgschema-export.exe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
            Directory.Delete(_baseDir, recursive: true);
    }

    private (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var start = new ProcessStartInfo(_exePath, [.. args])
        {
            WorkingDirectory = _baseDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(start);
        var output = process!.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, error);
    }

    [Fact]
    public void Version_PrintsVersionAndSucceeds()
    {
        var (exitCode, output, _) = Run("--version");

        Assert.Equal(0, exitCode);
        Assert.Contains("pgschema-export", output);
    }

    [Fact]
    public void Help_PrintsHelpAndSucceeds()
    {
        var (exitCode, output, _) = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output);
        Assert.Contains("export", output);
    }

    [Fact]
    public void UnknownCommand_ExitsWithErrorCode()
    {
        var (exitCode, _, error) = Run("does-not-exist");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command", error);
    }

    [Fact]
    public void Init_CreatesConfigFile()
    {
        var configPath = Path.Combine(_baseDir, "pgschema-export.json");
        var (exitCode, output, _) = Run("init", "--output", configPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Config template created", output);
        Assert.True(File.Exists(configPath));
    }

    [Fact]
    public void SplitDump_SplitsDumpIntoFiles()
    {
        var dumpPath = Path.Combine(_baseDir, "schema.sql");
        var outputDir = Path.Combine(_baseDir, "split");
        var dump = new StringBuilder();
        dump.AppendLine("CREATE SCHEMA public;");
        dump.AppendLine("CREATE TABLE public.t (id int);");
        dump.AppendLine("CREATE INDEX idx ON public.t (id);");
        File.WriteAllText(dumpPath, dump.ToString());

        var (exitCode, output, _) = Run("split-dump", "--input", dumpPath, "--output", outputDir);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dump split completed", output);
        Assert.True(Directory.Exists(outputDir));
        Assert.Contains("tables", Directory.GetDirectories(outputDir).Select(Path.GetFileName));
    }

    [Fact]
    public void Diagram_FromSchemaDirectory_WritesDiagram()
    {
        var schemaDir = Path.Combine(_baseDir, "schema");
        var tablesDir = Path.Combine(schemaDir, "tables");
        var constraintsDir = Path.Combine(schemaDir, "constraints");
        Directory.CreateDirectory(tablesDir);
        Directory.CreateDirectory(constraintsDir);

        File.WriteAllText(Path.Combine(tablesDir, "posts.sql"),
            """CREATE TABLE "public"."posts" ("id" integer NOT NULL, "title" text NOT NULL);""");
        File.WriteAllText(Path.Combine(tablesDir, "comments.sql"),
            """CREATE TABLE "public"."comments" ("id" integer NOT NULL, "post_id" integer NOT NULL, "body" text);""");
        File.WriteAllText(Path.Combine(constraintsDir, "comments_fk.sql"),
            """ALTER TABLE "public"."comments" ADD CONSTRAINT comments_fk FOREIGN KEY ("post_id") REFERENCES "public"."posts" ("id");""");

        var outputPath = Path.Combine(_baseDir, "diagram.mmd");
        var (exitCode, _, _) = Run("diagram", "--schema", schemaDir, "--output", outputPath, "--format", "mermaid");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("erDiagram", File.ReadAllText(outputPath));
    }

    [Fact]
    public void Migrate_Preview_PrintsMigrationToStdout()
    {
        var fromDir = Path.Combine(_baseDir, "from");
        var toDir = Path.Combine(_baseDir, "to");
        Directory.CreateDirectory(fromDir);
        Directory.CreateDirectory(toDir);
        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        File.WriteAllText(Path.Combine(fromDir, "tables", "t.sql"),
            """CREATE TABLE "public"."t" ("id" integer NOT NULL);""");
        File.WriteAllText(Path.Combine(toDir, "tables", "t.sql"),
            """CREATE TABLE "public"."t" ("id" integer NOT NULL, "name" text);""");

        var (exitCode, output, _) = Run("migrate", "--from", fromDir, "--to", toDir, "--preview");

        Assert.Equal(0, exitCode);
        Assert.Contains("--", output);
    }

    [Fact]
    public void Plan_GeneratesJsonPlanFile()
    {
        var fromDir = Path.Combine(_baseDir, "from");
        var toDir = Path.Combine(_baseDir, "to");
        Directory.CreateDirectory(fromDir);
        Directory.CreateDirectory(toDir);
        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        File.WriteAllText(Path.Combine(fromDir, "tables", "t.sql"),
            """CREATE TABLE "public"."t" ("id" integer NOT NULL);""");
        File.WriteAllText(Path.Combine(toDir, "tables", "t.sql"),
            """CREATE TABLE "public"."t" ("id" integer NOT NULL, "name" text);""");

        var planPath = Path.Combine(_baseDir, "plan.json");
        var (exitCode, output, _) = Run("plan", "--from", fromDir, "--to", toDir, "--output", planPath, "--format", "json");

        Assert.Equal(2, exitCode); // 2 signals pending changes.
        Assert.True(File.Exists(planPath));
        Assert.Contains("Plan written to", output);
    }

    [Fact]
    public void Fingerprint_ComputesAndWritesFingerprint()
    {
        var schemaDir = Path.Combine(_baseDir, "schema");
        Directory.CreateDirectory(Path.Combine(schemaDir, "tables"));
        File.WriteAllText(Path.Combine(schemaDir, "tables", "t.sql"), "CREATE TABLE t (id int);");

        var outputPath = Path.Combine(_baseDir, "fingerprint.json");
        var (exitCode, output, _) = Run("fingerprint", "--schema", schemaDir, "--output", outputPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("Fingerprint:", output);
    }

    [Fact]
    public void Fingerprint_Verify_MatchesStoredFingerprint()
    {
        var schemaDir = Path.Combine(_baseDir, "schema");
        Directory.CreateDirectory(Path.Combine(schemaDir, "tables"));
        File.WriteAllText(Path.Combine(schemaDir, "tables", "t.sql"), "CREATE TABLE t (id int);");

        var outputPath = Path.Combine(_baseDir, "fingerprint.json");
        Run("fingerprint", "--schema", schemaDir, "--output", outputPath);

        var (exitCode, output, _) = Run("fingerprint", "--schema", schemaDir, "--verify", outputPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Fingerprint OK", output);
    }

    [Fact]
    public void Diff_IdenticalDirectories_ExitsZero()
    {
        var leftDir = Path.Combine(_baseDir, "left");
        var rightDir = Path.Combine(_baseDir, "right");
        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));
        File.WriteAllText(Path.Combine(leftDir, "tables", "t.sql"), "CREATE TABLE t (id int);");
        File.WriteAllText(Path.Combine(rightDir, "tables", "t.sql"), "CREATE TABLE t (id int);");

        var (exitCode, output, _) = Run("diff", "--left", leftDir, "--right", rightDir);

        Assert.Equal(0, exitCode);
        Assert.Contains("No differences", output);
    }

    [Fact]
    public void Diff_DifferentDirectories_ExitsTwo()
    {
        var leftDir = Path.Combine(_baseDir, "left");
        var rightDir = Path.Combine(_baseDir, "right");
        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));
        File.WriteAllText(Path.Combine(leftDir, "tables", "t.sql"), "CREATE TABLE t (id int);");
        File.WriteAllText(Path.Combine(rightDir, "tables", "t.sql"), "CREATE TABLE t (id int, name text);");

        var (exitCode, output, _) = Run("diff", "--left", leftDir, "--right", rightDir);

        Assert.Equal(2, exitCode);
        Assert.Contains("changed", output);
    }

    [Fact]
    public void Diagram_DotFormat_WritesDotFile()
    {
        var schemaDir = Path.Combine(_baseDir, "schema");
        var tablesDir = Path.Combine(schemaDir, "tables");
        Directory.CreateDirectory(tablesDir);
        File.WriteAllText(Path.Combine(tablesDir, "posts.sql"),
            """CREATE TABLE "public"."posts" ("id" integer NOT NULL, "title" text NOT NULL);""");

        var outputPath = Path.Combine(_baseDir, "diagram.dot");
        var (exitCode, _, _) = Run("diagram", "--schema", schemaDir, "--output", outputPath, "--format", "dot");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("digraph schema", File.ReadAllText(outputPath));
    }

    [Fact]
    public void Migrate_PreviewSafe_WarnsHazards()
    {
        var fromDir = Path.Combine(_baseDir, "from");
        var toDir = Path.Combine(_baseDir, "to");
        Directory.CreateDirectory(fromDir);
        Directory.CreateDirectory(toDir);
        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        File.WriteAllText(Path.Combine(fromDir, "tables", "t.sql"),
            """CREATE TABLE "public"."t" ("id" integer NOT NULL);""");
        // Target removes the table, which is a destructive change.
        File.WriteAllText(Path.Combine(toDir, "tables", "t.sql"),
            """CREATE TABLE "public"."t" ("id" integer NOT NULL);""");
        Directory.Delete(Path.Combine(toDir, "tables"), recursive: true);

        var (exitCode, output, _) = Run("migrate", "--from", fromDir, "--to", toDir, "--preview", "--safe", "--warn-hazards");

        Assert.Equal(0, exitCode);
        Assert.Contains("--", output);
        Assert.Contains("Hazards", output);
    }
}
