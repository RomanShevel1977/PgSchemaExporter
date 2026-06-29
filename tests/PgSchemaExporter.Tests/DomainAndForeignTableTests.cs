using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class DomainAndForeignTableTests
{
    [Fact]
    public void Generate_EmitsDomainDefinition()
    {
        var domain = new DbDomain
        {
            Schema = "public",
            Name = "email",
            Definition = "text CHECK (VALUE ~ '^[^@]+@[^@]+\\.[^@]+$')"
        };

        var generator = new DomainScriptGenerator();
        var sql = generator.Generate(domain);

        Assert.Contains("CREATE DOMAIN", sql);
        Assert.Contains("email", sql);
        Assert.Contains("public", sql);
    }

    [Fact]
    public void Generate_EmitsForeignTableDefinition()
    {
        var table = new DbForeignTable
        {
            Schema = "public",
            Name = "remote_orders",
            Definition = "CREATE FOREIGN TABLE public.remote_orders (id integer) SERVER foo OPTIONS (schema_name 'public', table_name 'orders')"
        };

        var generator = new ForeignTableScriptGenerator();
        var sql = generator.Generate(table);

        Assert.Contains("CREATE FOREIGN TABLE", sql);
        Assert.Contains("remote_orders", sql);
    }
}
