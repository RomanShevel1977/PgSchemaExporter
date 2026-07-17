using System.Text;

namespace PgSchemaExporter.Core.Scripting;

public static class SqlLiteral
{
    public static string String(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('\'');
        foreach (var c in value)
        {
            if (c == '\'')
                sb.Append("''");
            else
                sb.Append(c);
        }
        sb.Append('\'');
        return sb.ToString();
    }
}
