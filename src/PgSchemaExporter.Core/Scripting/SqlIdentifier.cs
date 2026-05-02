namespace PgSchemaExporter.Core.Scripting;

public static class SqlIdentifier
{
    public static string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    public static string Qualified(string schema, string name)
    {
        return $"{Quote(schema)}.{Quote(name)}";
    }

    public static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
