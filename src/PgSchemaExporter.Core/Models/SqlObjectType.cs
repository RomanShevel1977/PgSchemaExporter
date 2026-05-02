namespace PgSchemaExporter.Core.Models;

public enum SqlObjectType
{
    Unknown = 0,
    Extension,
    Schema,
    Type,
    Sequence,
    Table,
    Constraint,
    Index,
    View,
    Function,
    Trigger,
    Policy,
    Comment,
    Grant,
    Other
}
