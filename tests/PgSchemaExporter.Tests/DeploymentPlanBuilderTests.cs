using PgSchemaExporter.Core.Output;
using Xunit;

namespace PgSchemaExporter.Tests;

public class DeploymentPlanBuilderTests
{
    [Fact]
    public void Build_OrdersDependentFilesAfterTheirDependencies()
    {
        var files = new FileWriteResult();
        files.SchemaFiles.Add("schemas/public.sql");
        files.TableFiles.Add("tables/public.users.sql");
        files.ViewFiles.Add("views/public.user_view.sql");

        var model = new PgSchemaExporter.Core.Models.DatabaseModel
        {
            Schemas =
            [
                new PgSchemaExporter.Core.Models.DbSchema { Name = "public" }
            ],
            Tables =
            [
                new PgSchemaExporter.Core.Models.DbTable { Schema = "public", Name = "users" }
            ],
            Views =
            [
                new PgSchemaExporter.Core.Models.DbView
                {
                    Schema = "public",
                    Name = "user_view",
                    Definition = "SELECT * FROM public.users"
                }
            ]
        };

        var builder = new DeploymentPlanBuilder();
        var plan = builder.Build(model, files);

        Assert.Contains("schemas/public.sql", plan.OrderedFiles);
        Assert.Contains("tables/public.users.sql", plan.OrderedFiles);
        Assert.Contains("views/public.user_view.sql", plan.OrderedFiles);
        var ordered = plan.OrderedFiles.ToList();
        Assert.True(ordered.IndexOf("tables/public.users.sql") < ordered.IndexOf("views/public.user_view.sql"));
    }

    [Fact]
    public void Build_HandlesCyclesWithoutThrowing()
    {
        var files = new FileWriteResult();
        files.SchemaFiles.Add("schemas/public.sql");
        files.ViewFiles.Add("views/public.a.sql");
        files.ViewFiles.Add("views/public.b.sql");

        var model = new PgSchemaExporter.Core.Models.DatabaseModel
        {
            Schemas =
            [
                new PgSchemaExporter.Core.Models.DbSchema { Name = "public" }
            ],
            Views =
            [
                new PgSchemaExporter.Core.Models.DbView { Schema = "public", Name = "a", Definition = "SELECT * FROM public.b" },
                new PgSchemaExporter.Core.Models.DbView { Schema = "public", Name = "b", Definition = "SELECT * FROM public.a" }
            ]
        };

        var builder = new DeploymentPlanBuilder();
        var plan = builder.Build(model, files);

        Assert.NotEmpty(plan.OrderedFiles);
    }

    [Fact]
    public void Build_OrdersFunctionsTriggersAndPoliciesAfterTheirDependencies()
    {
        var files = new FileWriteResult();
        files.SchemaFiles.Add("schemas/public.sql");
        files.TableFiles.Add("tables/public.orders.sql");
        files.FunctionFiles.Add("functions/public.log_event.sql");
        files.TriggerFiles.Add("triggers/public.orders.audit_trigger.sql");
        files.PolicyFiles.Add("policies/public.orders.audit_policy.sql");

        var model = new PgSchemaExporter.Core.Models.DatabaseModel
        {
            Schemas =
            [
                new PgSchemaExporter.Core.Models.DbSchema { Name = "public" }
            ],
            Tables =
            [
                new PgSchemaExporter.Core.Models.DbTable { Schema = "public", Name = "orders" }
            ],
            Functions =
            [
                new PgSchemaExporter.Core.Models.DbFunction
                {
                    Schema = "public",
                    Name = "log_event",
                    Definition = "SELECT public.log_event();"
                }
            ],
            Triggers =
            [
                new PgSchemaExporter.Core.Models.DbTrigger
                {
                    Schema = "public",
                    TableSchema = "public",
                    TableName = "orders",
                    Name = "audit_trigger",
                    Definition = "CREATE TRIGGER audit_trigger AFTER INSERT ON public.orders FOR EACH ROW EXECUTE FUNCTION public.log_event();"
                }
            ],
            Policies =
            [
                new PgSchemaExporter.Core.Models.DbPolicy
                {
                    Schema = "public",
                    TableSchema = "public",
                    TableName = "orders",
                    Name = "audit_policy",
                    Definition = "CREATE POLICY audit_policy ON public.orders FOR SELECT USING (public.log_event());"
                }
            ]
        };

        var builder = new DeploymentPlanBuilder();
        var plan = builder.Build(model, files);
        var ordered = plan.OrderedFiles.ToList();

        Assert.True(ordered.IndexOf("tables/public.orders.sql") < ordered.IndexOf("functions/public.log_event.sql"));
        Assert.True(ordered.IndexOf("tables/public.orders.sql") < ordered.IndexOf("triggers/public.orders.audit_trigger.sql"));
        Assert.True(ordered.IndexOf("functions/public.log_event.sql") < ordered.IndexOf("triggers/public.orders.audit_trigger.sql"));
        Assert.True(ordered.IndexOf("tables/public.orders.sql") < ordered.IndexOf("policies/public.orders.audit_policy.sql"));
        Assert.True(ordered.IndexOf("functions/public.log_event.sql") < ordered.IndexOf("policies/public.orders.audit_policy.sql"));
    }
}
