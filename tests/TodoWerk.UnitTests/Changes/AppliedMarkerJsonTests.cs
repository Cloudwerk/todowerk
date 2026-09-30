using System.Text.Json;
using TodoWerk.Domain.Changes;
using Xunit;

namespace TodoWerk.UnitTests.Changes;

/// <summary>
/// The Markers a Change carries are one JSON column, and a column outlives the shape that wrote
/// it. A row written before deleted rules were kept says nothing about <c>abandoned</c>, and has
/// to read back as the ordinary rule it was — the reader must not choke on the absence, and must
/// not read it as anything but false.
/// </summary>
public sealed class AppliedMarkerJsonTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ARowWrittenBeforeAbandonedExisted_ReadsAsNotAbandoned()
    {
        const string written = """
            [{"key":"BREAD","spelling":"bread","marker":"🍞","retiredMarker":null,"position":10}]
            """;

        var read = JsonSerializer.Deserialize<List<AppliedMarker>>(written, Json);

        var marker = Assert.Single(read!);

        Assert.False(marker.Abandoned);
        Assert.Equal("BREAD", marker.Key);
        Assert.Equal("🍞", marker.Marker);
    }

    [Fact]
    public void WhatIsWrittenToday_ReadsBackWhole()
    {
        var markers = new List<AppliedMarker>
        {
            new("BREAD", "bread", "🥐", "🍞", 10),
            new(string.Empty, string.Empty, "☕", null, int.MaxValue, Abandoned: true),
        };

        var read = JsonSerializer.Deserialize<List<AppliedMarker>>(JsonSerializer.Serialize(markers, Json), Json);

        Assert.Equal(markers, read);
    }
}
