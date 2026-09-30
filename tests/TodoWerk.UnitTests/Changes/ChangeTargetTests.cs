using TodoWerk.Domain.Changes;
using Xunit;

namespace TodoWerk.UnitTests.Changes;

/// <summary>
/// A target Spelling is accepted when it round-trips: prepend <c>#</c>, run it through the
/// extractor, and require exactly one Hashtag back whose Spelling is the target. That single test
/// is what enforces ADR-0005's second compatibility property — every title a write produces still
/// highlights in the To Do clients — and it inherits the extractor's conservatism deliberately.
/// </summary>
public sealed class ChangeTargetTests
{
    [Theory]
    [InlineData("Priority1")]
    [InlineData("kunde-nord")]
    [InlineData("Prüfung")]
    [InlineData("q1_2026")]
    [InlineData("Übersicht")]
    public void ANameTheGrammarReadsBackWhole_IsAccepted(string target) =>
        Assert.True(ChangeTarget.RoundTrips(target));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    // Two Hashtags, not one: the target names a single Hashtag or it is not a target.
    [InlineData("kunde nord")]
    // The marker is not part of a Spelling, and "##x" extracts nothing at all.
    [InlineData("#kunde")]
    // Punctuation ends the name, so what comes back is shorter than what was asked for.
    [InlineData("kunde.")]
    [InlineData("kunde,nord")]
    [InlineData("(kunde)")]
    public void ANameTheGrammarWouldNotReadBack_IsRefused(string? target) =>
        Assert.False(ChangeTarget.RoundTrips(target));

    /// <summary>
    /// Long enough that the extractor calls it a pasted blob rather than a Hashtag. Refusing it
    /// here is what stops a Change writing tags the index would then decline to count.
    /// </summary>
    [Fact]
    public void ANameTooLongToIndex_IsRefused() =>
        Assert.False(ChangeTarget.RoundTrips(new string('a', 256)));

    /// <summary>
    /// Leading and trailing whitespace is not trimmed on the user's behalf: the target is the
    /// Spelling that will be written into somebody's task titles, and quietly writing something
    /// other than what was typed is the surprise this product cannot afford.
    /// </summary>
    [Theory]
    [InlineData(" kunde")]
    [InlineData("kunde ")]
    public void ANameWithWhitespaceAroundIt_IsRefused(string target) =>
        Assert.False(ChangeTarget.RoundTrips(target));
}
