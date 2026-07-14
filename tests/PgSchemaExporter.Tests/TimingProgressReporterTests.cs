using PgSchemaExporter.Core.Diagnostics;
using Xunit;

namespace PgSchemaExporter.Tests;

public class TimingProgressReporterTests
{
    [Fact]
    public void TimingProgressReporter_CapturesPhases()
    {
        var inner = new CollectingProgressReporter();
        var timing = new TimingProgressReporter(inner);

        timing.Start("Operation", totalSteps: 3);
        timing.Step("Step one");
        timing.Step("Step two");
        timing.Complete("Done");

        var summary = timing.BuildSummary();

        Assert.Contains("Operation", summary);
        Assert.Contains("Step one", summary);
        Assert.Contains("Step two", summary);
        Assert.Contains("total", summary);
        Assert.True(timing.Phases.Count >= 3);
        Assert.All(timing.Phases, p => Assert.True(p.Elapsed >= TimeSpan.Zero));
    }

    [Fact]
    public void TimingProgressReporter_ForwardsToInnerReporter()
    {
        var inner = new CollectingProgressReporter();
        var timing = new TimingProgressReporter(inner);

        timing.Start("Test");
        timing.Step("A");
        timing.Complete("B");

        Assert.Equal(["Start: Test", "Step: A", "Complete: B"], inner.Messages);
    }

    private sealed class CollectingProgressReporter : IProgressReporter
    {
        public List<string> Messages { get; } = [];

        public void Start(string operation, int? totalSteps = null)
            => Messages.Add($"Start: {operation}");

        public void Step(string message)
            => Messages.Add($"Step: {message}");

        public void Complete(string message)
            => Messages.Add($"Complete: {message}");
    }
}
