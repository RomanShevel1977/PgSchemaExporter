using System.Text;

namespace PgSchemaExporter.Core.Scripting;

public static class SqlIdentifier
{
    private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());

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
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(InvalidFileNameChars.Contains(ch) ? '_' : ch);

        return sb.ToString();
    }
}
