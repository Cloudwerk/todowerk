using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Licensing;
using TodoWerk.Infrastructure.Licensing;

namespace TodoWerk.UnitTests.Licensing;

/// <summary>
/// A resolver over a scripted portal and a clock the test moves. Everything the Licensing module
/// decides — the cache, the fail-open window, the deny, the two problem codes — is a function of
/// what the portal said and how long ago, so both of those have to be things a test can set.
/// </summary>
internal sealed class LicensingTestHost : IDisposable
{
    internal const string TenantId = "8f14e45f-ceea-467a-9c19-0d1c4b2a9f01";

    internal const string ObjectId = "1c383cd3-0b3c-4f43-9f74-c1ac2f0f5aa3";

    internal const string ColleagueObjectId = "2b1f7a90-5e6d-4c8b-8a11-77c3d4e5f6a7";

    internal static readonly IndexUser User = new(TenantId, ObjectId);

    internal static readonly IndexUser Colleague = new(TenantId, ColleagueObjectId);

    private readonly ScriptedPortal _portal;

    private LicensingTestHost(ScriptedPortal portal, LicensingOptions options, TestClock clock)
    {
        _portal = portal;
        Clock = clock;
        Options = options;
        Cache = new LicenceCache();

        var client = new FirstPartyPortalClient(
            new SingleHandlerFactory(portal),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<FirstPartyPortalClient>.Instance);

        Portal = client;
        Resolver = new PortalLicenceResolver(
            client,
            Cache,
            clock,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PortalLicenceResolver>.Instance);
    }

    internal TestClock Clock { get; }

    internal LicensingOptions Options { get; }

    internal LicenceCache Cache { get; }

    internal FirstPartyPortalClient Portal { get; }

    internal PortalLicenceResolver Resolver { get; }

    /// <summary>Every request the portal saw, in order, with its body.</summary>
    internal IReadOnlyList<RecordedCall> Calls => _portal.Calls;

    internal static LicensingTestHost Answering(params PortalReply[] replies) =>
        Answering(new LicensingOptions
        {
            PortalHost = "https://portal.test",
            ApplicationKey = "test-key",
            SolutionSlug = "todowerk",
        },
        replies);

    internal static LicensingTestHost Answering(LicensingOptions options, params PortalReply[] replies)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

        return new(new ScriptedPortal(replies, clock), options, clock);
    }

    public void Dispose() => _portal.Dispose();

    /// <summary>
    /// One scripted answer: a status code and a body, or a transport failure — and how long the
    /// portal took over it.
    /// </summary>
    /// <param name="Takes">
    /// How long the round trip took, moved on the test's clock while the call is in flight. A
    /// timeout is the one shape of unreachability that costs the caller time, and the retry floor
    /// is about that time: a reply that takes none cannot tell a floor counted from the start of
    /// the attempt from one counted from its end.
    /// </param>
    /// <param name="Holds">
    /// A reply the portal does not give until the test says so. The only way to have two
    /// resolutions for one person in flight at once: a scripted portal that answers the moment it
    /// is asked leaves nothing for a second caller to arrive during.
    /// </param>
    internal sealed record PortalReply(
        HttpStatusCode StatusCode,
        string Body,
        bool Throws = false,
        TimeSpan Takes = default,
        HeldReply? Holds = null)
    {
        internal static PortalReply Ok(string body, TimeSpan takes = default, HeldReply? holds = null) =>
            new(HttpStatusCode.OK, body, Takes: takes, Holds: holds);

        internal static PortalReply Unreachable(TimeSpan takes = default) =>
            new(default, string.Empty, Throws: true, Takes: takes);

        internal static PortalReply Status(HttpStatusCode statusCode) => new(statusCode, "{}");
    }

    internal sealed record RecordedCall(string Path, string Body, string? ApplicationKey);

    /// <summary>
    /// The portal's hand on a reply. The call is recorded the moment it arrives, so a test can
    /// count what reached the portal while the answer is still being withheld.
    /// </summary>
    internal sealed class HeldReply
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Released => _released.Task;

        internal void Release() => _released.TrySetResult();
    }

    /// <summary>
    /// Answers from a queue and repeats the last answer once the queue runs dry, so a test about
    /// caching does not have to script one reply per request it hopes will not happen.
    /// </summary>
    private sealed class ScriptedPortal : HttpMessageHandler
    {
        private readonly Queue<PortalReply> _replies;
        private readonly TestClock _clock;
        private PortalReply _last;
        private readonly List<RecordedCall> _calls = [];

        internal ScriptedPortal(IEnumerable<PortalReply> replies, TestClock clock)
        {
            _replies = new Queue<PortalReply>(replies);
            _clock = clock;
            _last = _replies.Count > 0 ? _replies.Peek() : PortalReply.Unreachable();
        }

        internal IReadOnlyList<RecordedCall> Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            _calls.Add(new RecordedCall(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                body,
                request.Headers.TryGetValues(FirstPartyPortalClient.ApplicationKeyHeader, out var key)
                    ? key.FirstOrDefault()
                    : null));

            if (_replies.Count > 0)
            {
                _last = _replies.Dequeue();
            }

            if (_last.Holds is { } held)
            {
                // Honours the token the way HttpClient does: a caller that gives up on a held
                // reply stops waiting, whether or not the reply is ever given.
                await held.Released.WaitAsync(cancellationToken);
            }

            // The time the call took passes before it ends, whichever way it ends: the resolver
            // reads the clock after this returns, and what it reads there is the whole point.
            _clock.Advance(_last.Takes);

            if (_last.Throws)
            {
                throw new HttpRequestException("The portal is unreachable in this test.");
            }

            return new HttpResponseMessage(_last.StatusCode)
            {
                Content = new StringContent(_last.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// One handler behind every named client, which is all the module asks for. Deliberately does
    /// not dispose it: the handler outlives each <see cref="HttpClient"/> the code under test
    /// creates and disposes, exactly as the real factory's pooled pipeline does.
    /// </summary>
    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://portal.test") };
    }
}

/// <summary>
/// A log a test can read, at warning and worse.
/// <para>
/// Written down rather than taken from a package for the reason the integration suite's
/// <c>RecordedLogs</c> is: the whole of what is needed is a level and a formatted message. Not
/// shared with that one either — it lives in another assembly and is an <c>ILoggerProvider</c> for
/// a whole host, where this is one logger handed to one class. It is here at all because a seat
/// report that the portal declined changes nothing a caller can see —
/// the line the reporter writes about it <em>is</em> the behaviour, so a test that cannot read one
/// cannot tell the fixed code from the code that never warned.
/// </para>
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<string> _complaints = [];
    private readonly Lock _gate = new();

    /// <summary>Every line written at warning or worse, formatted as a log would render it.</summary>
    internal IReadOnlyList<string> Complaints
    {
        get
        {
            lock (_gate)
            {
                return [.. _complaints];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel < LogLevel.Warning)
        {
            return;
        }

        lock (_gate)
        {
            _complaints.Add(formatter(state, exception));
        }
    }
}

/// <summary>A clock the test moves. Nothing in the Licensing module reads the wall clock.</summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    internal void Advance(TimeSpan by) => _now += by;
}
