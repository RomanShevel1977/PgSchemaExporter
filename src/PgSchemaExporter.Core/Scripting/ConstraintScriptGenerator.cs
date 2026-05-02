using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class ConstraintScriptGenerator : ISqlScriptGenerator<DbConstraint>
{
    public string Generate(DbConstraint model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ALTER TABLE ONLY {SqlIdentifier.Qualified(model.Schema, model.TableName)}");
        sb.AppendLine($"    ADD CONSTRAINT {SqlIdentifier.Quote(model.Name)} {model.Definition};");
        return sb.ToString();
    }
}
