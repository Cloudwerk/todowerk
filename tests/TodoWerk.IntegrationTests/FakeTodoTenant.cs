using System.Globalization;
using System.Text.Json;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// The To Do mailbox the fake Graph serves: lists, their tasks, and enough delta bookkeeping to
/// answer an incremental sync honestly.
/// <para>
/// Every task carries the version the mailbox was at when it last changed, and a delta token is
/// just that number. A delta request with a token therefore returns exactly what changed since —
/// including tombstones for deletions — which is the behaviour the index is written against.
/// Scripted throttling and token expiry live here too, because they are things a mailbox does to
/// a scan rather than things the scan does.
/// </para>
/// </summary>
internal sealed class FakeTodoTenant
{
    private readonly Lock _gate = new();

    private readonly List<FakeList> _lists = [];

    private readonly Dictionary<string, List<FakeTask>> _tasks = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _throttleFor = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _throttleWritesFor = new(StringComparer.Ordinal);

    private readonly HashSet<string> _expiredDeltaTokens = new(StringComparer.Ordinal);

    private readonly HashSet<string> _unreadableLists = new(StringComparer.Ordinal);

    private readonly HashSet<string> _alwaysResync = new(StringComparer.Ordinal);

    private long _version;

    /// <summary>Small on purpose: two pages beat one in a test that means to prove paging.</summary>
    internal int PageSize { get; set; } = 2;

    /// <summary>Requests the fake refused with 429, by list id.</summary>
    internal Dictionary<string, int> ThrottledRequests { get; } = new(StringComparer.Ordinal);

    internal FakeList AddList(string id, string displayName, string wellknownListName = "none")
    {
        lock (_gate)
        {
            var list = new FakeList(id, displayName, wellknownListName);
            _lists.Add(list);
            _tasks[id] = [];

            return list;
        }
    }

    internal void AddTask(string listId, string taskId, string title)
    {
        lock (_gate)
        {
            _tasks[listId].Add(new FakeTask(taskId, title, ++_version, Removed: false));
        }
    }

    /// <summary>A task whose title changed — the case a delta sync has to re-extract.</summary>
    internal void RetitleTask(string listId, string taskId, string title)
    {
        lock (_gate)
        {
            var tasks = _tasks[listId];
            var index = tasks.FindIndex(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

            tasks[index] = tasks[index] with { Title = title, Version = ++_version };
        }
    }

    /// <summary>The title a task carries right now, or null if the mailbox has no such live task.</summary>
    internal string? TitleOf(string listId, string taskId)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(listId, out var tasks))
            {
                return null;
            }

            var task = tasks.FindLast(candidate =>
                string.Equals(candidate.Id, taskId, StringComparison.Ordinal) && !candidate.Removed);

            return task?.Title;
        }
    }

    /// <summary>
    /// The longest title Microsoft To Do keeps, and what it does with the rest. To Do accepts a
    /// longer title, answers success, and keeps the first 252 characters plus an ellipsis of three
    /// full stops, which can cut a Hashtag at the end in half.
    /// </summary>
    private const int StoredTitleLength = 255;

    /// <summary>
    /// What a <c>PATCH</c> does to the mailbox. Returns false when there is no such live task,
    /// which is the 404 a Change has to survive: the plan was made from an index, and a task can
    /// be deleted between the plan and the write.
    /// <para>
    /// An over-long title is <em>not</em> refused here, because the real service does not refuse
    /// it. It answers success and stores something shorter than what it was sent, which is the
    /// single most dangerous thing a fake can be more permissive about: a fake that stored whole
    /// whatever it was handed would hide the truncation from every test.
    /// So <see cref="WrittenTitles"/> keeps what TodoWerk sent, and the task keeps what To Do
    /// would really hold — and the two differing is the defect, not a detail.
    /// </para>
    /// </summary>
    internal bool WriteTitle(string listId, string taskId, string title)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(listId, out var tasks))
            {
                return false;
            }

            var index = tasks.FindLastIndex(candidate =>
                string.Equals(candidate.Id, taskId, StringComparison.Ordinal) && !candidate.Removed);

            if (index < 0)
            {
                return false;
            }

            var stored = title.Length > StoredTitleLength
                ? string.Concat(title.AsSpan(0, StoredTitleLength - 3), "...")
                : title;

            tasks[index] = tasks[index] with { Title = stored, Version = ++_version };
            WrittenTitles.Add((listId, taskId, title));

            return true;
        }
    }

    /// <summary>
    /// Every title a write actually put in the mailbox, in order. A Change is meant to send one
    /// PATCH per task and no more, and that is only assertable if the fake counts them.
    /// </summary>
    internal List<(string ListId, string TaskId, string Title)> WrittenTitles { get; } = [];

    /// <summary>Answer the next <paramref name="times"/> requests for this task with 429.</summary>
    internal void ThrottleTask(string taskId, int times)
    {
        lock (_gate)
        {
            _throttleWritesFor[taskId] = times;
        }
    }

    /// <summary>True when this request should be refused as throttled, consuming one refusal.</summary>
    internal bool ShouldThrottleTask(string taskId)
    {
        lock (_gate)
        {
            if (!_throttleWritesFor.TryGetValue(taskId, out var remaining) || remaining <= 0)
            {
                return false;
            }

            _throttleWritesFor[taskId] = remaining - 1;

            return true;
        }
    }

    /// <summary>A deleted task stays as a tombstone, which is all Graph ever tells us about it.</summary>
    internal void DeleteTask(string listId, string taskId)
    {
        lock (_gate)
        {
            var tasks = _tasks[listId];
            var index = tasks.FindIndex(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

            tasks[index] = tasks[index] with { Version = ++_version, Removed = true };
        }
    }

    internal void RemoveList(string listId)
    {
        lock (_gate)
        {
            _lists.RemoveAll(list => string.Equals(list.Id, listId, StringComparison.Ordinal));
            _tasks.Remove(listId);
        }
    }

    /// <summary>Answer the next <paramref name="times"/> requests for this list with 429.</summary>
    internal void ThrottleList(string listId, int times)
    {
        lock (_gate)
        {
            _throttleFor[listId] = times;
        }
    }

    /// <summary>
    /// Make the next delta request for this list fail the way Graph does when a token has aged
    /// out: 410 with <c>resyncRequired</c>.
    /// </summary>
    internal void ExpireDeltaToken(string listId)
    {
        lock (_gate)
        {
            _expiredDeltaTokens.Add(listId);
        }
    }

    /// <summary>
    /// Keep the list in <c>GET /me/todo/lists</c> but answer 404 when its tasks are read — a list
    /// Graph lists but will not read, which is not the same thing as <see cref="RemoveList"/>: that
    /// takes the list out of the listing as well.
    /// </summary>
    internal void MakeListUnreadable(string listId)
    {
        lock (_gate)
        {
            _unreadableLists.Add(listId);
        }
    }

    /// <summary>
    /// Answer 410 <c>resyncRequired</c> to every read of this list, full reads included — unlike
    /// <see cref="ExpireDeltaToken"/>, which only refuses a request carrying a token and is
    /// therefore satisfied by the full re-read that follows. This is the double fault, the one
    /// branch where dropping the delta link has already been spent and did not help.
    /// </summary>
    internal void AlwaysAskForResync(string listId)
    {
        lock (_gate)
        {
            _alwaysResync.Add(listId);
        }
    }

    internal string ListsJson()
    {
        lock (_gate)
        {
            var value = _lists.Select(list => new Dictionary<string, object?>
            {
                ["id"] = list.Id,
                ["displayName"] = list.DisplayName,
                ["wellknownListName"] = list.WellknownListName,
                ["isShared"] = false,
                ["isOwner"] = true,
            });

            return JsonSerializer.Serialize(new Dictionary<string, object?> { ["value"] = value });
        }
    }

    /// <summary>True when this request should be refused as throttled, consuming one refusal.</summary>
    internal bool ShouldThrottle(string listId)
    {
        lock (_gate)
        {
            if (!_throttleFor.TryGetValue(listId, out var remaining) || remaining <= 0)
            {
                return false;
            }

            _throttleFor[listId] = remaining - 1;
            ThrottledRequests[listId] = ThrottledRequests.GetValueOrDefault(listId) + 1;

            return true;
        }
    }

    internal bool ShouldExpireDeltaToken(string listId, string? deltaToken)
    {
        lock (_gate)
        {
            return _alwaysResync.Contains(listId)
                || (deltaToken is not null && _expiredDeltaTokens.Remove(listId));
        }
    }

    internal bool HasList(string listId)
    {
        lock (_gate)
        {
            return _tasks.ContainsKey(listId) && !_unreadableLists.Contains(listId);
        }
    }

    /// <summary>
    /// One page of a delta response. Without a token it is the whole list; with one it is what has
    /// changed since that version, tombstones included.
    /// </summary>
    internal string DeltaJson(string listId, string? deltaToken, int skip)
    {
        lock (_gate)
        {
            var since = deltaToken is null ? 0 : long.Parse(deltaToken, CultureInfo.InvariantCulture);

            var matching = _tasks[listId]
                .Where(task => task.Version > since && (deltaToken is not null || !task.Removed))
                .OrderBy(task => task.Version)
                .ToList();

            var page = matching.Skip(skip).Take(PageSize).ToList();
            var delivered = skip + page.Count;

            var payload = new Dictionary<string, object?>
            {
                ["value"] = page.Select(ToGraphTask),
            };

            var baseUri = $"https://graph.microsoft.com/v1.0/me/todo/lists/{listId}/tasks/delta";

            if (delivered < matching.Count)
            {
                payload["@odata.nextLink"] = deltaToken is null
                    ? $"{baseUri}?$skiptoken={delivered}"
                    : $"{baseUri}?$deltatoken={deltaToken}&$skiptoken={delivered}";
            }
            else
            {
                payload["@odata.deltaLink"] =
                    $"{baseUri}?$deltatoken={_version.ToString(CultureInfo.InvariantCulture)}";
            }

            return JsonSerializer.Serialize(payload);
        }
    }

    private static Dictionary<string, object?> ToGraphTask(FakeTask task) => task.Removed
        ? new Dictionary<string, object?>
        {
            ["id"] = task.Id,
            ["@removed"] = new Dictionary<string, object?> { ["reason"] = "deleted" },
        }
        : new Dictionary<string, object?>
        {
            ["id"] = task.Id,
            ["title"] = task.Title,
            ["status"] = "notStarted",
            ["lastModifiedDateTime"] = DateTimeOffset.UtcNow.AddMinutes(-task.Version).ToString("O", CultureInfo.InvariantCulture),
        };

    internal sealed record FakeList(string Id, string DisplayName, string WellknownListName);

    private sealed record FakeTask(string Id, string Title, long Version, bool Removed);
}
