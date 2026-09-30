using System.Reflection;
using TodoWerk.Domain.Failures;
using TodoWerk.Infrastructure.Graph;
using TodoWerk.SharedKernel;
using Xunit;

namespace TodoWerk.UnitTests.Indexing;

/// <summary>
/// The translation from what Graph did to the code the Workbench switches on.
/// </summary>
public sealed class GraphErrorsTests
{
    /// <summary>
    /// Every error this class declares, so a member added later is tested by having been written.
    /// </summary>
    public static TheoryData<string> DeclaredErrors()
    {
        var data = new TheoryData<string>();

        foreach (var error in Declared())
        {
            data.Add(error.Code);
        }

        return data;
    }

    /// <summary>
    /// <see cref="GraphErrors.CodeFor"/> ends in a fall-through to
    /// <see cref="FailureCode.Unknown"/>, and one of this class's own errors reaching it would
    /// store a failure Graph had named precisely as "we do not know", under a sentence written for
    /// a different path. The fall-through is for an <see cref="Error"/> from somewhere else; nothing
    /// declared here may land in it, and that is what this pins rather than the individual arms,
    /// because the likely defect is a missing arm and not a wrong one.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeclaredErrors))]
    public void EveryDeclaredError_ClassifiesAsSomethingOtherThanUnknown(string code)
    {
        var error = Declared().Single(candidate => candidate.Code == code);

        Assert.NotEqual(FailureCode.Unknown, GraphErrors.CodeFor(error));
    }

    /// <summary>An error from outside this class is exactly what the fall-through is for.</summary>
    [Fact]
    public void AnErrorFromElsewhere_IsUnknown()
    {
        var elsewhere = Error.Failure("Licensing.PortalUnreachable", "The portal could not be reached.");

        Assert.Equal(FailureCode.Unknown, GraphErrors.CodeFor(elsewhere));
    }

    [Fact]
    public void NoError_IsNone() => Assert.Equal(FailureCode.None, GraphErrors.CodeFor(Error.None));

    /// <summary>
    /// The two 404s share a code and differ in their sentence: one is a task a Change found gone,
    /// the other a list Graph listed and would not read. What the reader is told differs; what the
    /// client may offer about it does not.
    /// </summary>
    [Fact]
    public void BothKindsOf404_AreNotFound()
    {
        Assert.Equal(FailureCode.NotFound, GraphErrors.CodeFor(GraphErrors.NotFound));
        Assert.Equal(FailureCode.NotFound, GraphErrors.CodeFor(GraphErrors.ListNotReadable));

        Assert.NotEqual(GraphErrors.NotFound.Description, GraphErrors.ListNotReadable.Description);
    }

    /// <summary>
    /// A list Graph enumerates has not been deleted, whatever a read of it answers, so its
    /// sentence must not say it has. Worth pinning as a sentence rather than only as a code: the
    /// code is what the client switches on, but the sentence is what a person reads and acts on.
    /// </summary>
    [Fact]
    public void TheUnreadableListSentence_DoesNotClaimTheListIsGone()
    {
        var sentence = GraphErrors.ListNotReadable.Description;

        Assert.DoesNotContain("no longer", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deleted", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("try again", sentence, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Error> Declared() =>
        typeof(GraphErrors)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.FieldType == typeof(Error))
            .Select(field => (Error)field.GetValue(null)!);
}
