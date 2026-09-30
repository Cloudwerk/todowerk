namespace TodoWerk.IntegrationTests;

/// <summary>
/// What the fake cloud saw. Separate from <see cref="FakeEntraAndGraphHandler"/> so it can
/// span hosts: each host gets — and is free to dispose — its own handler instance, while the
/// test asserts against the one recorder threaded through all of them. A restart test lives
/// on that split.
/// </summary>
internal sealed class EntraAndGraphRecorder
{
    private readonly Lock _gate = new();

    private readonly List<string> _tokenGrantTypes = [];

    private readonly List<string?> _graphAuthorizationHeaders = [];

    /// <summary>Every <c>grant_type</c> the token endpoint saw, in order.</summary>
    public IReadOnlyList<string> TokenGrantTypes
    {
        get
        {
            lock (_gate)
            {
                return [.. _tokenGrantTypes];
            }
        }
    }

    /// <summary>Every <c>Authorization</c> header Graph saw, in order.</summary>
    public IReadOnlyList<string?> GraphAuthorizationHeaders
    {
        get
        {
            lock (_gate)
            {
                return [.. _graphAuthorizationHeaders];
            }
        }
    }

    internal void RecordTokenGrant(string grantType)
    {
        lock (_gate)
        {
            _tokenGrantTypes.Add(grantType);
        }
    }

    internal void RecordGraphAuthorization(string? authorizationHeader)
    {
        lock (_gate)
        {
            _graphAuthorizationHeaders.Add(authorizationHeader);
        }
    }
}
