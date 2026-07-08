using System.Net.Sockets;
using PgSchemaExporter.Core.Diagnostics;
using Xunit;

namespace PgSchemaExporter.Tests;

public class FriendlyErrorTests
{
    [Fact]
    public void Describe_ArgumentException_SuggestsHelp()
    {
        var (message, suggestion) = FriendlyError.Describe(new ArgumentException("bad arg"));

        Assert.Equal("bad arg", message);
        Assert.Contains("--help", suggestion);
    }

    [Fact]
    public void Describe_FileNotFound_MentionsPath()
    {
        var (_, suggestion) = FriendlyError.Describe(new FileNotFoundException("missing", "config.json"));

        Assert.Contains("config.json", suggestion);
    }

    [Fact]
    public void Describe_SocketException_SuggestsHostCheck()
    {
        var (_, suggestion) = FriendlyError.Describe(new SocketException());

        Assert.Contains("unreachable", suggestion);
    }

    [Fact]
    public void Describe_UnknownException_HasNoSuggestion()
    {
        var (message, suggestion) = FriendlyError.Describe(new InvalidOperationException("boom"));

        Assert.Equal("boom", message);
        Assert.Null(suggestion);
    }
}
