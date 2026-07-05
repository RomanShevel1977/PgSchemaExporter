namespace PgSchemaExporter.Core.Output;

public sealed class FileWriteResult
{
    public List<string> ExtensionFiles { get; } = [];
    public List<string> SchemaFiles { get; } = [];
    public List<string> TypeFiles { get; } = [];
    public List<string> SequenceFiles { get; } = [];
    public List<string> DomainFiles { get; } = [];
    public List<string> ForeignTableFiles { get; } = [];
    public List<string> TableFiles { get; } = [];
    public List<string> ConstraintFiles { get; } = [];
    public List<string> IndexFiles { get; } = [];
    public List<string> ViewFiles { get; } = [];
    public List<string> FunctionFiles { get; } = [];
    public List<string> TriggerFiles { get; } = [];
    public List<string> EventTriggerFiles { get; } = [];
    public List<string> RuleFiles { get; } = [];
    public List<string> AggregateFiles { get; } = [];
    public List<string> OperatorFiles { get; } = [];
    public List<string> CastFiles { get; } = [];
    public List<string> PublicationFiles { get; } = [];
    public List<string> SubscriptionFiles { get; } = [];
    public List<string> PolicyFiles { get; } = [];
    public List<string> CommentFiles { get; } = [];
    public List<string> GrantFiles { get; } = [];
    public List<string> OtherFiles { get; } = [];

    public IReadOnlyList<string> GetDeployOrder()
    {
        return ExtensionFiles
            .Concat(SchemaFiles)
            .Concat(TypeFiles)
            .Concat(SequenceFiles)
            .Concat(DomainFiles)
            .Concat(ForeignTableFiles)
            .Concat(TableFiles)
            .Concat(ConstraintFiles)
            .Concat(IndexFiles)
            .Concat(FunctionFiles)
            .Concat(ViewFiles)
            .Concat(TriggerFiles)
            .Concat(EventTriggerFiles)
            .Concat(RuleFiles)
            .Concat(AggregateFiles)
            .Concat(OperatorFiles)
            .Concat(CastFiles)
            .Concat(PublicationFiles)
            .Concat(SubscriptionFiles)
            .Concat(PolicyFiles)
            .Concat(CommentFiles)
            .Concat(GrantFiles)
            .Concat(OtherFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetAllFiles()
    {
        return GetDeployOrder();
    }
}
