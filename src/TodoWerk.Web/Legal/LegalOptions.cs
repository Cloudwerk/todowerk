namespace TodoWerk.Web.Legal;

/// <summary>
/// Who stands behind the terms of use this deployment serves. TodoWerk is free software, so the
/// same terms are served by CloudWerk's deployment and by every Self-Host — what differs is the
/// organisation the person signing in is agreeing with, and that organisation is named here rather
/// than written into the document.
/// <para>
/// Each value is substituted into a sentence in <c>TERMS.md</c>, and each has a fallback that
/// completes that sentence in English — so an installation that has configured none of them serves
/// a coherent document rather than a page of braces. A deployment that is going to point an app
/// registration or a Store listing at these pages should set all three;
/// <see cref="Endpoints.LegalEndpoints"/> says so in the log at startup when it finds them unset
/// outside Development.
/// </para>
/// </summary>
public sealed class LegalOptions
{
    public const string SectionName = "Legal";

    /// <summary>
    /// The organisation operating this installation, as it should be named in a legal document —
    /// "CloudWerk GmbH", not "CloudWerk". It is the party the person signing in is agreeing with.
    /// </summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>
    /// Where questions about the terms go. An email address is rendered as a link; anything else is
    /// rendered as written, which is what makes the unconfigured fallback readable.
    /// </summary>
    public string OperatorContact { get; set; } = string.Empty;

    /// <summary>
    /// The law the terms are governed by, named so that it completes the sentence "governed by the
    /// law of —". "Germany", not "German law".
    /// </summary>
    public string GoverningLaw { get; set; } = string.Empty;

    /// <summary>
    /// The settings nobody has filled in, fully qualified. All three, not just the operator's name:
    /// each falls back independently, so a deployment that named itself and left the governing law
    /// alone still serves "governed by the law of the place where the Operator is established" on
    /// the page a consent dialog links to — and would have been told it was fine.
    /// <para>
    /// Blank rather than absent is the state worth asking about, because the shipped
    /// <c>appsettings.json</c> carries the section with empty values so that the knobs can be found
    /// without reading the source.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> UnsetSettings =>
    [
        .. new (string Key, string Value)[]
            {
                ($"{SectionName}:{nameof(Operator)}", Operator),
                ($"{SectionName}:{nameof(OperatorContact)}", OperatorContact),
                ($"{SectionName}:{nameof(GoverningLaw)}", GoverningLaw),
            }
            .Where(setting => string.IsNullOrWhiteSpace(setting.Value))
            .Select(setting => setting.Key),
    ];

    internal string OperatorOrFallback => Or(Operator, "the organisation that runs it");

    internal string OperatorContactOrFallback => Or(OperatorContact, "whoever runs this installation");

    internal string GoverningLawOrFallback => Or(GoverningLaw, "the place where the Operator is established");

    private static string Or(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
