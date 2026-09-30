using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoWerk.Application.Indexing;
using TodoWerk.Application.Indexing.GetHashtagInventory;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;
using Xunit;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The inventory read model, against real SQL Server — which is the only place it can be tested,
/// because the whole point is that the aggregate, the filter, the sort and the page are the
/// database's work rather than the application's.
/// </summary>
public sealed class HashtagInventoryTests(SqlServerDatabaseFixture database)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Tenant = TodoWerkWebApplicationFactory.TenantId;

    private const string User = FakeEntraAndGraphHandler.UserObjectId;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task TheInventory_CountsTasksAndListsPerHashtag()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Angebot #kunde", Now),
            ("list-a", "Rechnung #kunde", Now),
            ("list-b", "Termin #kunde #dringend", Now));

        var page = await ReadAsync(host, new GetHashtagInventoryQuery());

        var kunde = Assert.Single(page.Rows, row => row.CanonicalSpelling == "kunde");
        Assert.Equal(3, kunde.TaskCount);
        Assert.Equal(2, kunde.ListCount);

        var dringend = Assert.Single(page.Rows, row => row.CanonicalSpelling == "dringend");
        Assert.Equal(1, dringend.TaskCount);
        Assert.Equal(1, dringend.ListCount);
    }

    /// <summary>
    /// The row the product exists for: one Hashtag, several Spellings, a flag saying so. Counting
    /// them as two rows is the defect the user bought TodoWerk to fix.
    /// </summary>
    [Fact]
    public async Task TwoSpellingsOfOneHashtag_AreOneRowThatSaysSo()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Angebot #Work", Now),
            ("list-a", "Rechnung #work", Now),
            ("list-a", "Termin #work", Now));

        var page = await ReadAsync(host, new GetHashtagInventoryQuery());

        var row = Assert.Single(page.Rows);
        Assert.Equal(3, row.TaskCount);
        Assert.True(row.HasMultipleSpellings);

        // Canonical is the most-used Spelling, not the first seen and not lower-cased (ADR-0005).
        Assert.Equal("work", row.CanonicalSpelling);
        Assert.Equal(["work", "Work"], row.Spellings);
    }

    [Fact]
    public async Task ASingleSpelling_CarriesNoFlag()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(host, ("list-a", "Angebot #ProjectAlpha", Now));

        var row = Assert.Single((await ReadAsync(host, new GetHashtagInventoryQuery())).Rows);

        Assert.False(row.HasMultipleSpellings);
        Assert.Equal("ProjectAlpha", row.CanonicalSpelling);
    }

    [Fact]
    public async Task LastUsed_IsTheNewestTaskThatCarriesTheHashtag()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Alt #kunde", Now.AddDays(-40)),
            ("list-a", "Neu #kunde", Now.AddDays(-2)));

        var row = Assert.Single((await ReadAsync(host, new GetHashtagInventoryQuery())).Rows);

        Assert.Equal(Now.AddDays(-2), row.LastUsedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AHashtagNobodyHasTouchedInSixMonths_IsStale()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Vergessen #altlast", Now.AddDays(-200)),
            ("list-a", "Aktuell #laufend", Now.AddDays(-3)));

        var page = await ReadAsync(host, new GetHashtagInventoryQuery());

        Assert.True(Assert.Single(page.Rows, row => row.CanonicalSpelling == "altlast").IsStale);
        Assert.False(Assert.Single(page.Rows, row => row.CanonicalSpelling == "laufend").IsStale);
    }

    [Fact]
    public async Task ATagOneCharacterFromAnother_IsFlaggedOnBothRows()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Angebot #kunde", Now),
            ("list-a", "Vertippt #kuned", Now),
            ("list-a", "Anderes #rechnung", Now));

        var page = await ReadAsync(host, new GetHashtagInventoryQuery());

        Assert.True(Assert.Single(page.Rows, row => row.CanonicalSpelling == "kunde").HasNearDuplicates);
        Assert.True(Assert.Single(page.Rows, row => row.CanonicalSpelling == "kuned").HasNearDuplicates);
        Assert.False(Assert.Single(page.Rows, row => row.CanonicalSpelling == "rechnung").HasNearDuplicates);
    }

    /// <summary>
    /// Filtering is the query's job, not the page's: asking for the casing problems must return
    /// all of them, not the ones that happened to land on the first page.
    /// </summary>
    [Fact]
    public async Task FilteringByAFlag_NarrowsTheWholeInventory()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Eins #Work", Now),
            ("list-a", "Zwei #work", Now),
            ("list-a", "Drei #sauber", Now),
            ("list-a", "Vier #alt", Now.AddDays(-300)));

        var casing = await ReadAsync(
            host,
            new GetHashtagInventoryQuery(Filter: HashtagInventoryFilter.MultipleSpellings));

        Assert.Equal(1, casing.TotalCount);

        // One row, both Spellings on it. Which one is canonical is not asserted here: they are
        // used once each at the same moment, and that tie is decided for stability rather than
        // for meaning.
        var row = Assert.Single(casing.Rows);
        Assert.True(row.HasMultipleSpellings);
        Assert.Equal(2, row.TaskCount);
        Assert.Equal(["Work", "work"], row.Spellings.Order(StringComparer.Ordinal));

        var stale = await ReadAsync(host, new GetHashtagInventoryQuery(Filter: HashtagInventoryFilter.Stale));

        Assert.Equal("alt", Assert.Single(stale.Rows).CanonicalSpelling);
    }

    [Fact]
    public async Task FilteringByNearDuplicates_ReturnsBothSidesOfEachPair()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Eins #kunde", Now),
            ("list-a", "Zwei #kuned", Now),
            ("list-a", "Drei #rechnung", Now));

        var page = await ReadAsync(
            host,
            new GetHashtagInventoryQuery(Filter: HashtagInventoryFilter.NearDuplicates));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["kunde", "kuned"], page.Rows.Select(row => row.CanonicalSpelling).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Searching_MatchesAnywhereInTheNameAndIgnoresCasing()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Eins #Grosskunde", Now),
            ("list-a", "Zwei #kundendienst", Now),
            ("list-a", "Drei #rechnung", Now));

        var page = await ReadAsync(host, new GetHashtagInventoryQuery(Search: "KUNDE"));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(
            ["Grosskunde", "kundendienst"],
            page.Rows.Select(row => row.CanonicalSpelling).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SortingAndPaging_HappenInTheQuery()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Eins #haeufig", Now),
            ("list-a", "Zwei #haeufig", Now),
            ("list-a", "Drei #haeufig", Now),
            ("list-a", "Vier #mittel", Now),
            ("list-a", "Fuenf #mittel", Now),
            ("list-a", "Sechs #selten", Now));

        var first = await ReadAsync(host, new GetHashtagInventoryQuery(PageSize: 2));

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(["haeufig", "mittel"], first.Rows.Select(row => row.CanonicalSpelling));

        var second = await ReadAsync(host, new GetHashtagInventoryQuery(Page: 2, PageSize: 2));

        Assert.Equal("selten", Assert.Single(second.Rows).CanonicalSpelling);

        var alphabetical = await ReadAsync(
            host,
            new GetHashtagInventoryQuery(Sort: HashtagInventorySort.Spelling, Descending: false));

        Assert.Equal(
            ["haeufig", "mittel", "selten"],
            alphabetical.Rows.Select(row => row.CanonicalSpelling));
    }

    /// <summary>
    /// Alphabetical for a reader, not for a byte comparer. The key is binary-collated so identity
    /// is byte equality, and byte order puts every umlaut above <c>Z</c> — sorting by it would
    /// exile <c>#Übersicht</c> below <c>#Zebra</c>, which is the mistake the task-list picker
    /// already refuses to make.
    /// </summary>
    [Fact]
    public async Task SortingByName_CollatesUmlautsWhereAReaderExpectsThem()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Eins #Zebra", Now),
            ("list-a", "Zwei #Übersicht", Now),
            ("list-a", "Drei #Apfel", Now));

        var page = await ReadAsync(
            host,
            new GetHashtagInventoryQuery(Sort: HashtagInventorySort.Spelling, Descending: false));

        Assert.Equal(["Apfel", "Übersicht", "Zebra"], page.Rows.Select(row => row.CanonicalSpelling));
    }

    /// <summary>
    /// German data is the data this was built against, and eszett is where SQL's default collation
    /// and .NET disagree: the collation calls "#Straße" and "#Strasse" one string, .NET calls them
    /// two, and the binary-collated key column is what makes the application's answer the one that
    /// counts (ADR-0005).
    /// </summary>
    [Fact]
    public async Task EszettAndDoubleS_AreTwoRows()
    {
        await using var host = await StartHostAsync();
        await SeedAsync(
            host,
            ("list-a", "Eins #Straße", Now),
            ("list-a", "Zwei #Strasse", Now));

        var page = await ReadAsync(host, new GetHashtagInventoryQuery());

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Rows, row => Assert.False(row.HasMultipleSpellings));
    }

    [Fact]
    public async Task AnEmptyIndex_IsAnEmptyPageRatherThanAFailure()
    {
        await using var host = await StartHostAsync();

        var page = await ReadAsync(host, new GetHashtagInventoryQuery());

        Assert.Empty(page.Rows);
        Assert.Equal(0, page.TotalCount);
    }

    private static async Task<HashtagInventoryPage> ReadAsync(
        TodoWerkWebApplicationFactory host,
        GetHashtagInventoryQuery query)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IHashtagInventoryReader>()
            .GetInventoryAsync(
                new IndexUser(Tenant, User),
                query,
                TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        return result.Value;
    }

    /// <summary>
    /// Writes tasks straight into the index, extracting their Hashtags exactly as the scan does.
    /// The scan itself is covered by <see cref="IndexScanTests"/>; this is about what the query
    /// makes of what it left behind.
    /// </summary>
    private static async Task SeedAsync(
        TodoWerkWebApplicationFactory host,
        params (string ListId, string Title, DateTimeOffset LastModifiedAt)[] tasks)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        var number = 0;

        foreach (var (listId, title, lastModifiedAt) in tasks)
        {
            var task = IndexedTask.Create(
                Tenant,
                User,
                listId,
                $"task-{++number}",
                title,
                lastModifiedAt);

            context.Add(task);

            foreach (var hashtag in HashtagExtractor.Extract(title))
            {
                context.Add(HashtagOccurrence.Create(Tenant, User, task.Id, hashtag));
            }
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<TodoWerkWebApplicationFactory> StartHostAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var host = new TodoWerkWebApplicationFactory()
            .WithConfigurationOverride("ConnectionStrings:TodoWerk", database.ConnectionString)
            // The inventory is seeded directly here; a worker scanning in the background would
            // reconcile the seeded lists away underneath the assertions.
            .WithoutBackgroundWorkers()
            .WithOutboundHttpHandler(() => new FakeEntraAndGraphHandler(new EntraAndGraphRecorder()));

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoWerkDbContext>();

        await context.Database.MigrateAsync(cancellationToken);

        // One database for the class, one index per test.
        await context.Set<HashtagOccurrence>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<IndexedTask>().ExecuteDeleteAsync(cancellationToken);

        return host;
    }
}
