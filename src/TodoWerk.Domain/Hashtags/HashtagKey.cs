using System.Text;

namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// The folded form that decides whether two Spellings are the same Hashtag, per ADR-0005:
/// <c>Key = NFC(ToUpperInvariant(NFC(name)))</c>.
/// <para>
/// The fold lives here, in one testable place, rather than in a SQL collation — SQL Server's
/// default collation and .NET disagree about which strings are equal (`ß` versus `ss` above all),
/// and a restore onto an instance with a different default collation would otherwise change what
/// the product considers one Hashtag. The column that stores this is binary-collated so the
/// database compares bytes and holds no opinion of its own.
/// </para>
/// <para>
/// The key is never displayed — the Canonical Spelling is what the Workbench shows — so its case
/// is free, and upper is the direction that folds Greek final sigma onto sigma rather than
/// leaving two Hashtags where a reader sees one.
/// </para>
/// </summary>
public static class HashtagKey
{
    /// <summary>
    /// Longest key the index stores. Graph accepts a 255-character title, so a name cannot
    /// exceed that; the column is sized to the same bound rather than to <c>nvarchar(max)</c>,
    /// which cannot carry an index.
    /// </summary>
    public const int MaxLength = 255;

    /// <summary>
    /// Folds one Spelling — the name only, without the leading marker — onto its Hashtag's key.
    /// </summary>
    public static string Fold(string spelling)
    {
        ArgumentNullException.ThrowIfNull(spelling);

        // The inner pass makes case mapping see composed characters; the outer one recomposes
        // whatever the mapping decomposed, so the key is always NFC and two spellings that fold
        // to the same characters always produce the same bytes.
        return spelling
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormC);
    }
}
