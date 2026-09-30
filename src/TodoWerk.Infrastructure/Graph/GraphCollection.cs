using System.Text.Json.Serialization;

namespace TodoWerk.Infrastructure.Graph;

/// <summary>
/// Graph's collection envelope. The two links are what paging and delta are made of:
/// <c>@odata.nextLink</c> while pages remain, then <c>@odata.deltaLink</c> once on the last page
/// of a delta response.
/// </summary>
internal sealed record GraphCollection<T>(
    [property: JsonPropertyName("value")] IReadOnlyList<T>? Value,
    [property: JsonPropertyName("@odata.nextLink")] string? NextLink = null,
    [property: JsonPropertyName("@odata.deltaLink")] string? DeltaLink = null);
