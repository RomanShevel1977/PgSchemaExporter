using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class SequenceScriptGenerator : ISqlScriptGenerator<DbSequence>
{
    public string Generate(DbSequence model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE SEQUENCE IF NOT EXISTS {SqlIdentifier.Qualified(model.Schema, model.Name)}");
        sb.AppendLine($"    AS {model.DataType}");
        sb.AppendLine($"    START WITH {model.StartValue}");
        sb.AppendLine($"    INCREMENT BY {model.Increment}");
        sb.AppendLine($"    MINVALUE {model.MinimumValue}");
        sb.AppendLine($"    MAXVALUE {model.MaximumValue}");
        sb.AppendLine(model.Cycle ? "    CYCLE;" : "    NO CYCLE;");
        return sb.ToString();
    }
}
