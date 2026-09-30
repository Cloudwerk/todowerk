namespace TodoWerk.Application.Indexing;

/// <summary>
/// Bounds shared by the wire contract and the storage schema. Owned here so the layer that
/// validates a request and the layer that sizes a column agree by construction.
/// </summary>
public static class IndexingLimits
{
    /// <summary>
    /// The longest Microsoft Graph identifier TodoWerk stores. Real ids are far shorter; a value
    /// past this is not a bigger id, it is not an id.
    /// </summary>
    public const int GraphIdLength = 512;
}
