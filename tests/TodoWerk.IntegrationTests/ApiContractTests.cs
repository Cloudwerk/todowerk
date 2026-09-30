using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Indexing;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The SPA mirrors the API contracts by hand in <c>ClientApp/src/api/types.ts</c>. These tests
/// assert the wire format that mirror assumes, using the running application's own serializer
/// options — so a serializer change breaks a test here rather than the UI at runtime.
/// </summary>
public sealed class ApiContractTests(TodoWerkWebApplicationFactory factory)
    : IClassFixture<TodoWerkWebApplicationFactory>
{
    private JsonSerializerOptions SerializerOptions => factory.Services
        .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
        .Value.SerializerOptions;

    /// <summary>
    /// By name, not by ordinal: a numeric <c>kind</c> would keep deserialising happily on the
    /// client while meaning something else the day an enum member is inserted rather than appended.
    /// </summary>
    [Fact]
    public void TaskList_SerialisesKindByName()
    {
        var list = new TaskListDto(
            "flagged-id",
            "Flagged Emails",
            TaskListKind.FlaggedEmails,
            IsShared: false,
            IsOwner: true);

        var json = JsonSerializer.Serialize(list, SerializerOptions);

        Assert.Contains("\"kind\":\"FlaggedEmails\"", json, StringComparison.Ordinal);
    }

    /// <summary>The client reads these as plain fields; they must not vanish because they are derived.</summary>
    [Fact]
    public void TaskList_SerialisesTheDerivedFlagsTheClientReads()
    {
        var list = new TaskListDto(
            "shared-id",
            "Nächste Schritte",
            TaskListKind.Normal,
            IsShared: true,
            IsOwner: false);

        var json = JsonSerializer.Serialize(list, SerializerOptions);

        Assert.Contains("\"isDefault\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"isSystemList\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"isShared\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"isOwner\":false", json, StringComparison.Ordinal);
    }
}
