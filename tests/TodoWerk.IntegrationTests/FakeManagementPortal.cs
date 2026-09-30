using System.Net;
using System.Text;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Stands in for ManagementPortal's First-Party Path, and hands everything else to the fake that
/// already answers for Entra ID and Graph.
/// <para>
/// A composition rather than a second handler, because the factory routes <em>every</em> outbound
/// client through one: a test that stood in for the portal alone would break the sign-in it needs
/// in order to have anybody to resolve a Licence for.
/// </para>
/// </summary>
internal sealed class FakeManagementPortal : HttpMessageHandler
{
    internal const string Host = "portal.todowerk.test";

    internal const string BaseAddress = $"https://{Host}";

    internal const string ApplicationKey = "integration-test-application-key";

    internal const string SolutionSlug = "todowerk";

    private readonly HttpMessageHandler _ownedElsewhere;
    private readonly HttpMessageInvoker _elsewhere;
    private readonly Func<string, PortalAnswer> _answer;
    private readonly List<Recorded> _calls = [];
    private readonly Lock _gate = new();

    /// <param name="answer">
    /// What to answer one resolve call, given its request body. A function rather than a fixed
    /// reply because the point of the per-person model is that two people get two answers.
    /// </param>
    internal FakeManagementPortal(Func<string, PortalAnswer> answer, HttpMessageHandler? elsewhere = null)
    {
        _answer = answer;
        _ownedElsewhere = elsewhere ?? new FakeEntraAndGraphHandler(new EntraAndGraphRecorder());
        _elsewhere = new HttpMessageInvoker(_ownedElsewhere, disposeHandler: false);
    }

    /// <summary>Every first-party call the application made, in order.</summary>
    internal IReadOnlyList<Recorded> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    internal sealed record Recorded(string Path, string Body, string? ApplicationKey);

    /// <summary>A status code and a body, which for a resolve call is always 200 in the contract.</summary>
    internal sealed record PortalAnswer(HttpStatusCode StatusCode, string Body)
    {
        internal static PortalAnswer Ok(string body) => new(HttpStatusCode.OK, body);

        internal static PortalAnswer Unavailable() => new(HttpStatusCode.ServiceUnavailable, "{}");
    }

    /// <summary>
    /// A valid answer of one kind. The field names follow the licensing contract this fake stands
    /// in for; the shape is asserted against here rather than assumed anywhere in the product.
    /// </summary>
    internal static string Valid(string kind, string endsAt, string? purchaseUrl = null) => $$"""
        {
          "valid": true,
          "status": "active",
          "expiresUtc": "{{endsAt}}",
          "message": "License is valid.",
          "recheckSeconds": 300,
          "failOpenSeconds": 14400,
          "kind": "{{kind}}",
          "licenseType": "{{(kind == "trial" ? "trial" : "paid")}}",
          "channel": "portal",
          "licenseId": "3b6f2c9e-1d4a-4e8b-9f0c-7a5d2e1b8c34",
          "usageSecret": "us_integration_secret"
          {{(purchaseUrl is null ? string.Empty : $$""", "purchaseUrl": "{{purchaseUrl}}" """)}}
        }
        """;

    /// <summary>
    /// A confirmed negative: <c>valid: false</c> over HTTP 200, and the only thing that denies.
    /// <para>
    /// It takes a purchase URL for the same reason a valid answer does: the licensing service
    /// withholds its client configuration document on every non-valid answer, so the answer
    /// itself has to carry the link for the one screen that most wants it — the ended card.
    /// </para>
    /// </summary>
    internal static string Expired(string message, string? purchaseUrl = null) => $$"""
        {
          "valid": false,
          "status": "expired",
          "message": "{{message}}",
          "recheckSeconds": 300,
          "kind": "trial"
          {{(purchaseUrl is null ? string.Empty : $$""", "purchaseUrl": "{{purchaseUrl}}" """)}}
        }
        """;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri
            ?? throw new InvalidOperationException("Outbound request without a URI.");

        if (!string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return await _elsewhere.SendAsync(request, cancellationToken);
        }

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (_gate)
        {
            _calls.Add(new Recorded(
                uri.AbsolutePath,
                body,
                request.Headers.TryGetValues("X-Application-Key", out var key) ? key.FirstOrDefault() : null));
        }

        // A seat the portal stored, in the shape the real one answers — a per-seat-metered
        // solution, one seat, no limit. The answer says whether anything was stored, so the body
        // is read, and a fake that said `unlimited` while claiming to have recorded one would be a
        // shape the portal never sends (an Unlimited-metered solution never meters, and answers
        // `recorded: false`).
        var answer = uri.AbsolutePath.EndsWith("/usage", StringComparison.Ordinal)
            ? PortalAnswer.Ok("""{"recorded": true, "seatStatus": "within", "seatsUsed": 1, "seatLimit": null}""")
            : _answer(body);

        return new HttpResponseMessage(answer.StatusCode)
        {
            Content = new StringContent(answer.Body, Encoding.UTF8, "application/json"),
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _elsewhere.Dispose();
            _ownedElsewhere.Dispose();
        }

        base.Dispose(disposing);
    }
}
