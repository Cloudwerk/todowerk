namespace TodoWerk.Application.Markers;

/// <summary>Bounds the Markers module holds to, in the layer that validates a request.</summary>
public static class MarkerLimits
{
    /// <summary>
    /// How many rules one person may hold. Not a storage bound: the block is written at the front
    /// of a 255-character title, and a person with two hundred rules would eventually meet an Apply
    /// that skips every task it touches for being too long. Fifty is far more Hashtags than anybody
    /// marks and low enough that the block stays something a reader can take in.
    /// </summary>
    public const int MaxRulesPerUser = 50;
}
