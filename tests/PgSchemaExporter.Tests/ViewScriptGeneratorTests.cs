using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class ViewScriptGeneratorTests
{
    [Fact]
    public void Generate_UsesMaterializedViewSyntaxWhenRequested()
    {
        var view = new DbView
        {
            Schema = "public",
            Name = "recent_orders",
            Definition = "SELECT id, created_at FROM orders WHERE created_at > now() - interval '7 days'",
            IsMaterialized = true
        };

        var generator = new ViewScriptGenerator();
        var sql = generator.Generate(view);

        Assert.Contains("CREATE MATERIALIZED VIEW", sql);
        Assert.Contains("recent_orders", sql);
        Assert.Contains("public", sql);
    }
}
