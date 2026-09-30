using System.Net;
using TodoWerk.Application.Indexing;
using TodoWerk.Infrastructure.Indexing;
using TodoWerk.SharedKernel;
using Xunit;
using TodoWerk.Infrastructure.Graph;

namespace TodoWerk.UnitTests.Indexing;

public sealed class GraphTaskListsReaderTests
{
    private const string ListsJson = """
        {
          "value": [
            { "id": "b-id", "displayName": "Groceries", "wellknownListName": "none" },
            { "id": "a-id", "displayName": "Tasks", "wellknownListName": "defaultList" }
          ]
        }
        """;

    private const string GermanListsJson = """
        {
          "value": [
            { "id": "z-id", "displayName": "Zebra", "wellknownListName": "none" },
            { "id": "ae-id", "displayName": "Änderungen", "wellknownListName": "none" },
            { "id": "a-id", "displayName": "Apfel", "wellknownListName": "none" }
          ]
        }
        """;

    // A system list and a shared list, to check metadata mapping.
    private const string ListMetadataJson = """
        {
          "value": [
            {
              "id": "flagged-id",
              "displayName": "Flagged Emails",
              "wellknownListName": "flaggedEmails",
              "isShared": false,
              "isOwner": true
            },
            {
              "id": "shared-id",
              "displayName": "Nächste Schritte",
              "wellknownListName": "none",
              "isShared": true,
              "isOwner": false
            }
          ]
        }
        """;

    [Fact]
    public async Task GetTaskListsAsync_MapsGraphListsAndFlagsTheDefaultList()
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, ListsJson);

        var result = await ReadAsync(host);

        Assert.True(result.IsSuccess);
        var lists = result.Value;
        Assert.Equal(2, lists.Count);

        var groceries = Assert.Single(lists, list => list.Id == "b-id");
        Assert.Equal("Groceries", groceries.DisplayName);
        Assert.False(groceries.IsDefault);

        var tasks = Assert.Single(lists, list => list.Id == "a-id");
        Assert.True(tasks.IsDefault);
    }

    [Fact]
    public async Task GetTaskListsAsync_OrdersListsByDisplayName()
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, ListsJson);

        var result = await ReadAsync(host);

        Assert.Equal(["Groceries", "Tasks"], result.Value.Select(list => list.DisplayName));
    }

    /// <summary>
    /// German list names are the norm for this product, and an umlaut is a code unit above 'Z'.
    /// Ordinal ordering therefore banishes "Änderungen" to the bottom of the picker.
    /// </summary>
    [Fact]
    public async Task GetTaskListsAsync_OrdersUmlautsLinguistically_NotByCodeUnit()
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, GermanListsJson);

        var result = await ReadAsync(host);

        Assert.Equal(["Änderungen", "Apfel", "Zebra"], result.Value.Select(list => list.DisplayName));
    }

    /// <summary>
    /// The fields that decide whether a rename is even legal: a system list Graph refuses to
    /// rename, and a shared list where a rename is visible to everyone it is shared with.
    /// </summary>
    [Fact]
    public async Task GetTaskListsAsync_MapsSystemListsAndSharingMetadata()
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, ListMetadataJson);

        var result = await ReadAsync(host);

        var flagged = Assert.Single(result.Value, list => list.Id == "flagged-id");
        Assert.Equal(TaskListKind.FlaggedEmails, flagged.Kind);
        Assert.True(flagged.IsSystemList);
        Assert.False(flagged.IsDefault);
        Assert.True(flagged.IsOwner);
        Assert.False(flagged.IsShared);

        var shared = Assert.Single(result.Value, list => list.Id == "shared-id");
        Assert.Equal(TaskListKind.Normal, shared.Kind);
        Assert.False(shared.IsSystemList);
        Assert.True(shared.IsShared);
        Assert.False(shared.IsOwner);
    }

    [Fact]
    public async Task GetTaskListsAsync_WhenGraphNamesAListThisCodeDoesNotKnow_TreatsItAsASystemList()
    {
        const string json = """
            {
              "value": [
                { "id": "new-id", "displayName": "Something New", "wellknownListName": "unknownFutureValue" }
              ]
            }
            """;
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, json);

        var result = await ReadAsync(host);

        var list = Assert.Single(result.Value);
        Assert.Equal(TaskListKind.Unknown, list.Kind);
        Assert.True(list.IsSystemList);
    }

    [Fact]
    public async Task GetTaskListsAsync_PassesAFailureThroughUntouched()
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.Unauthorized, "{}");

        var result = await ReadAsync(host);

        Assert.True(result.IsFailure);
        Assert.Equal(GraphErrors.ReconnectRequired, result.Error);
    }

    /// <summary>
    /// Lists are paged too, and this is not a cosmetic gap: the scan treats a list it did not see
    /// as deleted and drops its indexed tasks, so ignoring a second page would delete half a
    /// user's index on every scan rather than merely hiding it.
    /// </summary>
    [Fact]
    public async Task GetTaskListsAsync_FollowsTheNextLink()
    {
        const string firstPage = """
            {
              "value": [{ "id": "a-id", "displayName": "Arbeit", "wellknownListName": "none" }],
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/todo/lists?$skiptoken=1"
            }
            """;
        const string secondPage = """
            {
              "value": [{ "id": "b-id", "displayName": "Privat", "wellknownListName": "none" }]
            }
            """;

        using var host = new GraphTestHost(new GraphTestHost.ScriptedHandler(
            (HttpStatusCode.OK, firstPage),
            (HttpStatusCode.OK, secondPage)));

        var result = await ReadAsync(host);

        Assert.Equal(["Arbeit", "Privat"], result.Value.Select(list => list.DisplayName));
        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/todo/lists?$skiptoken=1",
            host.Handler.Requests[1].RequestUri?.ToString());
    }

    /// <summary>
    /// A failure part-way through paging must not look like a short list, or reconciliation would
    /// read the lists it did get as "the rest were deleted".
    /// </summary>
    [Fact]
    public async Task GetTaskListsAsync_WhenALaterPageFails_FailsRatherThanReturningWhatItHas()
    {
        const string firstPage = """
            {
              "value": [{ "id": "a-id", "displayName": "Arbeit", "wellknownListName": "none" }],
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/todo/lists?$skiptoken=1"
            }
            """;

        using var host = new GraphTestHost(new GraphTestHost.ScriptedHandler(
            (HttpStatusCode.OK, firstPage),
            (HttpStatusCode.Unauthorized, "{}")));

        var result = await ReadAsync(host);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetTaskListsAsync_ReadsTheGraphListsEndpoint()
    {
        using var host = GraphTestHost.Answering(HttpStatusCode.OK, ListsJson);

        await ReadAsync(host);

        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/todo/lists",
            host.Handler.LastRequest?.RequestUri?.ToString());
    }

    private static Task<Result<IReadOnlyList<TaskListDto>>> ReadAsync(GraphTestHost host) =>
        new GraphTaskListsReader(host.Gateway)
            .GetTaskListsAsync(GraphTestHost.User, TestContext.Current.CancellationToken);
}
