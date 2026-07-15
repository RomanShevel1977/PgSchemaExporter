using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class ScriptGeneratorTests
{
    [Fact]
    public void AggregateScriptGenerator_ReturnsDefinition()
    {
        var generator = new AggregateScriptGenerator();
        var item = new DbAggregate { Schema = "public", Name = "my_agg", InputType = "int", Definition = "CREATE AGGREGATE public.my_agg (int) (sfunc = add, stype = int);" };
        Assert.Equal(item.Definition, generator.Generate(item));
    }

    [Fact]
    public void CastScriptGenerator_ReturnsDefinition()
    {
        var generator = new CastScriptGenerator();
        var item = new DbCast { SourceType = "int", TargetType = "text", Definition = "CREATE CAST (int AS text) WITH INOUT;" };
        Assert.Equal(item.Definition, generator.Generate(item));
    }

    [Fact]
    public void EventTriggerScriptGenerator_ReturnsDefinition()
    {
        var generator = new EventTriggerScriptGenerator();
        var item = new DbEventTrigger { Name = "et", Event = "ddl_command_end", Procedure = "fn", Definition = "CREATE EVENT TRIGGER et ON ddl_command_end EXECUTE FUNCTION fn();" };
        Assert.Equal(item.Definition, generator.Generate(item));
    }

    [Fact]
    public void OperatorScriptGenerator_ReturnsDefinition()
    {
        var generator = new OperatorScriptGenerator();
        var item = new DbOperator { Schema = "public", Name = "===", LeftType = "text", RightType = "text", ResultType = "boolean", Definition = "CREATE OPERATOR public.=== (LEFTARG = text, RIGHTARG = text, PROCEDURE = fn);" };
        Assert.Equal(item.Definition, generator.Generate(item));
    }

    [Fact]
    public void PublicationScriptGenerator_ReturnsDefinition()
    {
        var generator = new PublicationScriptGenerator();
        var item = new DbPublication { Name = "pub", Tables = "ALL TABLES", Definition = "CREATE PUBLICATION pub FOR ALL TABLES;" };
        Assert.Equal(item.Definition, generator.Generate(item));
    }

    [Fact]
    public void RuleScriptGenerator_ReturnsDefinition()
    {
        var generator = new RuleScriptGenerator();
        var item = new DbRule { Schema = "public", TableName = "t", Name = "r", Definition = "CREATE RULE r AS ON INSERT TO public.t DO INSTEAD NOTHING;" };
        Assert.Equal(item.Definition, generator.Generate(item));
    }

    [Fact]
    public void SubscriptionScriptGenerator_ReturnsDefinition()
    {
        var generator = new SubscriptionScriptGenerator();
        var item = new DbSubscription { Name = "sub", Publication = "pub", ConnectionString = "host=localhost", Definition = "CREATE SUBSCRIPTION sub CONNECTION 'host=localhost' PUBLICATION pub;" };
        Assert.Equal(item.Definition, generator.Generate(item));
    }
}
