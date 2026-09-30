using System.Text.RegularExpressions;
using Xunit;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Rules about how the tests in this repository are written, checked against their own source.
/// One so far.
/// <para>
/// Here rather than in the architecture suite, which is about the product — the layers, the
/// modules, the persisted model — and reads compiled assemblies. Reflection over IL cannot see the
/// shape of a call inside a lambda, and the rule below is about a shape only a project referencing
/// <c>WebApplicationFactory</c> can write. Whoever meets the failure should also land one file
/// away from the worked example it points at.
/// </para>
/// </summary>
public sealed class TestConventionTests
{
    /// <summary>
    /// An xunit throw assertion whose lambda starts the host. <c>CreateClient</c>,
    /// <c>CreateDefaultClient</c>, <c>Server</c> and <c>Services</c> all boot it on first touch.
    /// <para>
    /// Matched as text on purpose: what is being forbidden is a shape somebody copies, and it is
    /// copied as text rather than as a type. This file must therefore never spell that shape out
    /// itself, in a comment or anywhere else.
    /// </para>
    /// </summary>
    private static readonly Regex ABootExpectedToThrow = new(
        @"(Assert\s*\.\s*Throws\w*|Record\s*\.\s*Exception\w*)\s*(<[^>]*>)?\s*\(\s*(async\s*)?\(\s*\)\s*=>\s*[^;]*?\b(CreateClient|CreateDefaultClient|Server|Services)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// No test boots the application through <see cref="TodoWerkWebApplicationFactory"/> and then
    /// asserts that the boot threw.
    /// <para>
    /// Under <c>WebApplicationFactory</c> the entry point runs on a thread of its own and startup
    /// validation runs there, inside <c>RunAsync</c> — which disposes the host in its
    /// <c>finally</c>. Whether the boot throws, and what it throws, is therefore decided by
    /// scheduling rather than by the configuration under test.
    /// <c>ConfigurationValidationTests</c> writes it down at length and asks
    /// <c>IStartupValidator</c> instead.
    /// </para>
    /// <para>
    /// This is in the build because a note is not enough. CONTRIBUTING § Testing states the rule,
    /// and a rule that only prose carries is a rule that gets walked past.
    /// </para>
    /// </summary>
    [Fact]
    public void NoTestBootsTheApplicationAndExpectsTheBootToThrow()
    {
        var root = RepositoryRoot();

        var offenders = TestSources(root)
            .Select(path => (Path: path, Match: ABootExpectedToThrow.Match(File.ReadAllText(path))))
            .Where(candidate => candidate.Match.Success)
            .Select(candidate =>
                $"{Path.GetRelativePath(root, candidate.Path)}: {candidate.Match.Value.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A test expects a boot through WebApplicationFactory to throw, which is decided by "
            + "scheduling rather than by the configuration under test. Ask the startup validator "
            + "instead: "
            + "ConfigurationValidationTests.ApplicationServicesWith is the worked example, and "
            + "CONTRIBUTING § Testing says why."
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Every C# file under <c>tests/</c>, rather than only this project's: nothing else references
    /// the factory today, and the day something does is not the day this should start passing
    /// because it looked in the wrong place.
    /// </summary>
    private static List<string> TestSources(string root)
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .ToList();

        Assert.NotEmpty(files);

        return files;
    }

    /// <summary>
    /// The checkout, found by walking up from the test binaries to the solution file. A test that
    /// reads source can only run inside one, and saying so out loud beats scanning nothing and
    /// reporting no offenders.
    /// </summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TodoWerk.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "TodoWerk.slnx was not found above the test binaries. This test reads the test sources "
            + "and needs a checkout to read them from.");
    }
}
