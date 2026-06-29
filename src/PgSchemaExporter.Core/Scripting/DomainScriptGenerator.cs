using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class DomainScriptGenerator : ISqlScriptGenerator<DbDomain>
{
    public string Generate(DbDomain model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE DOMAIN {SqlIdentifier.Qualified(model.Schema, model.Name)} AS {model.Definition.Trim().TrimEnd(';')};");
        return sb.ToString();
    }
}
