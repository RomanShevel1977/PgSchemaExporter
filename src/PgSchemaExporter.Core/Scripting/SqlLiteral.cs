namespace PgSchemaExporter.Core.Scripting;

public static class SqlLiteral
{
    public static string String(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }
}
