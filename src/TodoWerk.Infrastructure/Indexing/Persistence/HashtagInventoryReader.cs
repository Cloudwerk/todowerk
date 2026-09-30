using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Indexing;
using TodoWerk.Application.Indexing.GetHashtagInventory;
using TodoWerk.Domain.Indexing;
using TodoWerk.Infrastructure.Persistence;
using TodoWerk.SharedKernel;
using TodoWerk.Domain.Hashtags;
using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Infrastructure.Indexing.Persistence;

/// <summary>
/// The inventory, computed by the database.
/// <para>
/// The aggregate — task counts, lists touched, last used, how many Spellings — is a
/// <c>GROUP BY</c>, and so are the filter, the sort and the page. Assembling it in memory would
/// mean reading every Occurrence a tenant has on every page load, which is the access pattern
/// ADR-0003 says plain SQL over this index should never need.
/// </para>
/// <para>
/// It is written as SQL rather than as LINQ for two reasons that are the same reason. Every count
/// here is a <c>COUNT(DISTINCT …)</c> inside a grouping, which EF does not translate; and the
/// counts have to be taken under the binary collation the Hashtag columns carry, because under the
/// database's default collation <c>#Work</c> and <c>#work</c> are one distinct Spelling and the
/// casing flag would never fire (ADR-0005). Values reach the statement as parameters; the only
/// thing built from the request is the <c>ORDER BY</c>, and it comes from a closed set.
/// </para>
/// <para>
/// The near-duplicate flag is the one thing left to code: edit distance is not something SQL
/// Server computes. The keys are one narrow column and a few thousand short strings, so they are
/// read and paired here — before the page query rather than over its results, or "show me the
/// near-duplicates" would only ever show the ones that happened to be on screen.
/// </para>
/// </summary>
internal sealed class HashtagInventoryReader(
    TodoWerkDbContext context,
    IMemoryCache memoryCache,
    IOptions<IndexingOptions> options,
    TimeProvider timeProvider) : IHashtagInventoryReader
{
    private const string From = """
        FROM HashtagOccurrences AS o
        INNER JOIN IndexedTasks AS t ON t.Id = o.IndexedTaskId
        """;

    /// <summary>
    /// Where one person's memoised near-duplicate pairing lives. A method rather than an inline
    /// string because a second caller needs the same key: the set it holds is that person's Hashtag
    /// names, so erasing them has to evict it, and a purge that guessed at the key would leave it
    /// sitting in memory (<see cref="IndexPersonalDataPurge"/>).
    /// </summary>
    internal static string NearDuplicateCacheKey(IndexUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return $"TodoWerk.NearDuplicates:{user.TenantId}:{user.UserId}";
    }

    public async Task<Result<HashtagInventoryPage>> GetInventoryAsync(
        IndexUser user,
        GetHashtagInventoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(query);

        var settings = options.Value;
        var staleBefore = timeProvider.GetUtcNow() - settings.StaleAfter;

        // Longer than any stored key can be: nothing can match, and the folded pattern would
        // only grow. Answered here rather than sent to SQL as a LIKE that scans for nothing.
        if (query.Search is not null && query.Search.Length > HashtagKey.MaxLength)
        {
            return Result.Success(EmptyPage(query));
        }

        var nearDuplicates = await FlagNearDuplicatesAsync(user, settings, cancellationToken);

        // Every fragment writes its own parameter as {0} and is renumbered on render, so the
        // clauses do not have to know which optional ones came before them.
        var where = new Clauses();
        where.Add("o.TenantId = {0}", user.TenantId);
        where.Add("o.UserId = {0}", user.UserId);

        var having = new Clauses();

        if (query.Search is not null)
        {
            // Folded, because the column holds the folded key: searching for "Kunde" and for
            // "kunde" is one search by construction rather than by collation.
            where.Add("o.[Key] LIKE {0}", $"%{Escape(HashtagKey.Fold(query.Search))}%");
        }

        switch (query.Filter)
        {
            case HashtagInventoryFilter.NearDuplicates when nearDuplicates.Count == 0:
                return Result.Success(EmptyPage(query));

            case HashtagInventoryFilter.NearDuplicates:
                // OPENJSON hands back the database's default collation, and comparing that against
                // a binary-collated column is an error rather than a wrong answer — so the side
                // that can be re-collated is.
                where.Add(
                    $"o.[Key] IN (SELECT value COLLATE {StorageConventions.KeyCollation} FROM OPENJSON({{0}}))",
                    System.Text.Json.JsonSerializer.Serialize(nearDuplicates));
                break;

            case HashtagInventoryFilter.MultipleSpellings:
                having.Add("COUNT(DISTINCT o.Spelling) > 1");
                break;

            case HashtagInventoryFilter.Stale:
                having.Add("MAX(t.LastModifiedAt) < {0}", staleBefore);
                break;

            default:
                break;
        }

        var aggregates = await ReadPageAsync(where, having, query, cancellationToken);

        // The page carries the total alongside every row, so the common case is one aggregate
        // pass instead of two over the same grouping. An empty page still has to distinguish
        // "nothing matches" from "asked past the end", and only then is a count worth its own
        // query.
        var totalCount = aggregates.Count > 0
            ? aggregates[0].TotalCount
            : await CountAsync(where, having, cancellationToken);

        if (totalCount == 0)
        {
            return Result.Success(EmptyPage(query));
        }
        var spellings = await ReadSpellingsAsync(user, [.. aggregates.Select(row => row.Key)], cancellationToken);

        IReadOnlyList<HashtagInventoryRow> rows =
        [
            .. aggregates.Select(row =>
            {
                var observed = spellings.TryGetValue(row.Key, out var found) ? found : [row.Key];

                return new HashtagInventoryRow(
                    row.Key,
                    observed[0],
                    observed,
                    row.TaskCount,
                    row.ListCount,
                    row.LastUsedAt,
                    HasMultipleSpellings: row.SpellingCount > 1,
                    HasNearDuplicates: nearDuplicates.Contains(row.Key),
                    IsStale: row.LastUsedAt < staleBefore);
            }),
        ];

        return Result.Success(new HashtagInventoryPage(rows, totalCount, query.Page, query.PageSize));
    }

    private async Task<int> CountAsync(Clauses where, Clauses having, CancellationToken cancellationToken)
    {
        var whereSql = where.Render(out var arguments);
        var havingSql = having.Render(out var havingArguments, "HAVING", arguments.Count);

        arguments.AddRange(havingArguments);

        // One row per key, then count the rows: a grouped result cannot be counted in place, and
        // the HAVING clauses are part of what is being counted.
        var sql = $$"""
            SELECT COUNT(*) FROM (
                SELECT o.[Key]
                {{From}}
                {{whereSql}}
                GROUP BY o.[Key]
                {{havingSql}}
            ) AS grouped
            """;

        var counts = await context.Database
            .SqlQuery<int>(FormattableStringFactory.Create(sql, [.. arguments]))
            .ToListAsync(cancellationToken);

        return counts[0];
    }

    private async Task<List<Aggregate>> ReadPageAsync(
        Clauses where,
        Clauses having,
        GetHashtagInventoryQuery query,
        CancellationToken cancellationToken)
    {
        var whereSql = where.Render(out var arguments);
        var havingSql = having.Render(out var havingArguments, "HAVING", arguments.Count);

        arguments.AddRange(havingArguments);

        var skipPlaceholder = Placeholder(arguments.Count);
        var takePlaceholder = Placeholder(arguments.Count + 1);

        // The offset multiplication runs in 64 bits: the page number is caller-supplied, and in
        // 32 bits a large one wraps negative — which SQL Server answers with an error rather
        // than an empty page.
        arguments.Add(((long)query.Page - 1) * query.PageSize);
        arguments.Add(query.PageSize);

        var sql = $$"""
            SELECT
                o.[Key] AS [Key],
                COUNT(DISTINCT o.IndexedTaskId) AS TaskCount,
                COUNT(DISTINCT t.TaskListId) AS ListCount,
                MAX(t.LastModifiedAt) AS LastUsedAt,
                COUNT(DISTINCT o.Spelling) AS SpellingCount,
                COUNT(*) OVER () AS TotalCount
            {{From}}
            {{whereSql}}
            GROUP BY o.[Key]
            {{havingSql}}
            ORDER BY {{OrderBy(query)}}
            OFFSET {{skipPlaceholder}} ROWS
            FETCH NEXT {{takePlaceholder}} ROWS ONLY
            """;

        return await context.Database
            .SqlQuery<Aggregate>(FormattableStringFactory.Create(sql, [.. arguments]))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The Spellings behind each key on the page, most used first — which makes the first one the
    /// Canonical Spelling, ties broken by most recent use (ADR-0005).
    /// </summary>
    private async Task<Dictionary<string, List<string>>> ReadSpellingsAsync(
        IndexUser user,
        List<string> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var sql = $$"""
            SELECT o.[Key] AS [Key], o.Spelling AS Spelling, COUNT(*) AS Uses, MAX(t.LastModifiedAt) AS LastUsedAt
            {{From}}
            WHERE o.TenantId = {0} AND o.UserId = {1}
              AND o.[Key] IN (SELECT value COLLATE {{StorageConventions.KeyCollation}} FROM OPENJSON({2}))
            GROUP BY o.[Key], o.Spelling
            """;

        var counted = await context.Database
            .SqlQuery<SpellingUse>(FormattableStringFactory.Create(
                sql,
                user.TenantId,
                user.UserId,
                System.Text.Json.JsonSerializer.Serialize(keys)))
            .ToListAsync(cancellationToken);

        return counted
            .GroupBy(use => use.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(use => use.Uses)
                    .ThenByDescending(use => use.LastUsedAt)
                    // ADR-0005 stops at "ties broken by most recent use", which leaves two
                    // Spellings used once each at the same moment undecided. Deciding it here
                    // rather than letting the database's row order decide keeps the Canonical
                    // Spelling from changing between two identical page loads.
                    .ThenBy(use => use.Spelling, StringComparer.Ordinal)
                    .Select(use => use.Spelling)
                    .ToList(),
                StringComparer.Ordinal);
    }

    private async Task<IReadOnlySet<string>> FlagNearDuplicatesAsync(
        IndexUser user,
        IndexingOptions settings,
        CancellationToken cancellationToken)
    {
        // Memoised per user: the pairing reads every distinct key the user has, and the grid
        // asks again for every page, sort and poll. A result up to a minute old is still honest
        // — the index itself is allowed to be half an hour behind the tasks. The length ceiling
        // keeps the work bounded by configuration rather than by whoever writes the longest
        // titles.
        var cacheKey = NearDuplicateCacheKey(user);

        if (memoryCache.TryGetValue(cacheKey, out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var keys = await context.Set<HashtagOccurrence>()
            .AsNoTracking()
            .Where(occurrence => occurrence.TenantId == user.TenantId
                && occurrence.UserId == user.UserId
                && occurrence.Key.Length <= settings.NearDuplicateMaximumLength)
            .Select(occurrence => occurrence.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        var flagged = NearDuplicateDetector.Flag(keys, settings.NearDuplicateMinimumLength);

        memoryCache.Set(cacheKey, flagged, settings.NearDuplicateCacheLifetime);

        return flagged;
    }

    /// <summary>
    /// Alphabetical order for a reader, which is not the order the key is stored in. The key is
    /// binary-collated so that identity is byte equality, and byte order puts every umlaut above
    /// <c>Z</c> — sorting by it would exile <c>#Übersicht</c> below <c>#Zebra</c>, the same
    /// mistake the task-list picker already refuses to make. Sorting is presentation, so it asks
    /// for a linguistic collation; identity is untouched by it.
    /// </summary>
    private const string TagOrder = "o.[Key] COLLATE Latin1_General_100_CI_AS";

    /// <summary>
    /// The one part of the statement built rather than parameterised, so it comes from a closed
    /// set and never from anything a caller wrote. Every other sort falls back to the tag, or two
    /// rows with the same count could swap places between pages and hide a row entirely.
    /// </summary>
    private static string OrderBy(GetHashtagInventoryQuery query)
    {
        var direction = query.Descending ? "DESC" : "ASC";

        var column = query.Sort switch
        {
            HashtagInventorySort.Spelling => TagOrder,
            HashtagInventorySort.LastUsed => "MAX(t.LastModifiedAt)",
            HashtagInventorySort.ListCount => "COUNT(DISTINCT t.TaskListId)",
            _ => "COUNT(DISTINCT o.IndexedTaskId)",
        };

        return query.Sort is HashtagInventorySort.Spelling
            ? $"{TagOrder} {direction}"
            : $"{column} {direction}, {TagOrder} ASC";
    }

    /// <summary>
    /// The <c>{n}</c> a <see cref="FormattableString"/> substitutes a parameter for. Built here
    /// rather than written into the SQL, because the number depends on how many optional clauses
    /// came before it.
    /// </summary>
    private static string Placeholder(int index) =>
        "{" + index.ToString(CultureInfo.InvariantCulture) + "}";

    private static HashtagInventoryPage EmptyPage(GetHashtagInventoryQuery query) =>
        new([], 0, query.Page, query.PageSize);

    /// <summary>
    /// <c>LIKE</c> reads three characters as wildcards, and a Hashtag may legitimately contain the
    /// third: a search for <c>#kunde-nord</c> must not silently match <c>#kundeXnord</c>.
    /// </summary>
    private static string Escape(string search) => search
        .Replace("[", "[[]", StringComparison.Ordinal)
        .Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("_", "[_]", StringComparison.Ordinal);

    /// <summary>
    /// A conditional list of SQL fragments and the values behind them. The fragments are written
    /// with <c>{0}</c> for their own parameter and renumbered on render, so a clause does not have
    /// to know how many came before it.
    /// </summary>
    private sealed class Clauses
    {
        private readonly List<string> _fragments = [];

        private readonly List<object> _values = [];

        /// <summary>A clause with no parameter of its own. It must not claim a placeholder.</summary>
        internal void Add(string fragment)
        {
            if (fragment.Contains("{0}", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A fragment with a placeholder needs a value; without one, every later parameter "
                    + "would shift by one position and bind the wrong value.",
                    nameof(fragment));
            }

            _fragments.Add(fragment);
        }

        /// <summary>A clause carrying exactly one parameter, written as <c>{0}</c>.</summary>
        internal void Add(string fragment, object value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!fragment.Contains("{0}", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A fragment given a value must consume it as {0}; an orphaned value would shift "
                    + "every later parameter by one position.",
                    nameof(fragment));
            }

            _fragments.Add(fragment);
            _values.Add(value);
        }

        internal string Render(out List<object> arguments, string keyword = "WHERE", int offset = 0)
        {
            arguments = [.. _values];

            if (_fragments.Count == 0)
            {
                return string.Empty;
            }

            var placeholder = offset;
            var rendered = new List<string>(_fragments.Count);

            foreach (var fragment in _fragments)
            {
                if (!fragment.Contains("{0}", StringComparison.Ordinal))
                {
                    rendered.Add(fragment);
                    continue;
                }

                rendered.Add(fragment.Replace("{0}", Placeholder(placeholder), StringComparison.Ordinal));

                placeholder++;
            }

            if (placeholder - offset != _values.Count)
            {
                throw new InvalidOperationException(
                    "Rendered placeholders and collected values disagree; a parameter would bind to "
                    + "the wrong position.");
            }

            return $"{keyword} {string.Join(" AND ", rendered)}";
        }
    }

    private sealed record Aggregate(
        string Key,
        int TaskCount,
        int ListCount,
        DateTimeOffset LastUsedAt,
        int SpellingCount,
        int TotalCount);

    private sealed record SpellingUse(string Key, string Spelling, int Uses, DateTimeOffset LastUsedAt);
}
