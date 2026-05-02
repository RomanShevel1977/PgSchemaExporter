namespace PgSchemaExporter.Core.Output;

public sealed class FileWriteResult
{
    public List<string> ExtensionFiles { get; } = [];
    public List<string> SchemaFiles { get; } = [];
    public List<string> TypeFiles { get; } = [];
    public List<string> SequenceFiles { get; } = [];
    public List<string> TableFiles { get; } = [];
    public List<string> ConstraintFiles { get; } = [];
    public List<string> IndexFiles { get; } = [];
    public List<string> ViewFiles { get; } = [];
    public List<string> FunctionFiles { get; } = [];

    public IReadOnlyList<string> GetDeployOrder()
    {
        return ExtensionFiles
            .Concat(SchemaFiles)
            .Concat(TypeFiles)
            .Concat(SequenceFiles)
            .Concat(TableFiles)
            .Concat(ConstraintFiles)
            .Concat(IndexFiles)
            .Concat(ViewFiles)
            .Concat(FunctionFiles)
            .Distinct()
            .ToList();
    }
}
