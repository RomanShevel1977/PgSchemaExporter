using PgSchemaExporter.Core.Integrity;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>fingerprint</c> command.
/// </summary>
public sealed class FingerprintCommand : ICommand
{
    public string Name => "fingerprint";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var (schema, output, verify) = CliParser.ParseFingerprintOptions(context.Args);

        var result = SchemaFingerprint.Compute(schema);

        if (!string.IsNullOrWhiteSpace(verify))
        {
            var expected = await SchemaFingerprintFile.ReadAsync(verify);
            if (string.Equals(expected.Fingerprint, result.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Fingerprint OK. The schema matches the stored fingerprint.");
                Console.WriteLine($"  Fingerprint: {result.Fingerprint}");
                return 0;
            }
            else
            {
                Console.Error.WriteLine("Fingerprint MISMATCH. The schema has changed since the fingerprint was generated.");
                Console.Error.WriteLine($"  Expected: {expected.Fingerprint}");
                Console.Error.WriteLine($"  Actual:   {result.Fingerprint}");

                var comparison = SchemaFingerprint.CompareFiles(expected.Files, result);
                if (comparison.HasDifferences)
                {
                    foreach (var path in comparison.Added)
                        Console.Error.WriteLine($"  + added:    {path}");
                    foreach (var path in comparison.Removed)
                        Console.Error.WriteLine($"  - removed:  {path}");
                    foreach (var path in comparison.Modified)
                        Console.Error.WriteLine($"  ~ modified: {path}");
                }

                return 2;
            }
        }

        Console.WriteLine($"Fingerprint: {result.Fingerprint}");
        Console.WriteLine($"Files:       {result.FileCount}");

        if (!string.IsNullOrWhiteSpace(output))
        {
            await SchemaFingerprintFile.WriteAsync(output, result);
            Console.WriteLine($"Written to: {Path.GetFullPath(output)}");
        }

        return 0;
    }
}
