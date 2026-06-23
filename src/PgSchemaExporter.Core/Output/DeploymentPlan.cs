namespace PgSchemaExporter.Core.Output;

public sealed class DeploymentPlan
{
    public IReadOnlyList<string> OrderedFiles { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();
}
