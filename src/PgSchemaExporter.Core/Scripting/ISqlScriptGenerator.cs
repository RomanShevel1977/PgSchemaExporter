namespace PgSchemaExporter.Core.Scripting;

public interface ISqlScriptGenerator<in T>
{
    string Generate(T model);
}
