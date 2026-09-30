using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace TodoWerk.ArchitectureTests;

/// <summary>
/// Vertical modules (folders repeated per layer) must not reach into each other.
/// Shared plumbing lives in the layer's Abstractions namespace, which every module may use.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly string[] Modules = ["Changes", "Indexing", "Jobs", "Licensing", "Markers", "Onboarding"];

    /// <summary>
    /// Namespaces that sit outside every module because more than one of them needs what is in
    /// there. They are allowed to be depended <em>on</em> by anybody, and are allowed to depend on
    /// nobody — which is the second half of the bargain and the thing this file also checks.
    /// </summary>
    private static readonly string[] SharedNamespaces = ["TodoWerk.Domain.Hashtags", "TodoWerk.Infrastructure.Graph"];

    private static readonly Assembly[] LayerAssemblies =
    [
        typeof(Domain.AssemblyReference).Assembly,
        typeof(TodoWerk.Application.ApplicationServiceCollectionExtensions).Assembly,
        typeof(Infrastructure.AssemblyReference).Assembly,
    ];

    public static TheoryData<string, string> ModulePairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var module in Modules)
        {
            foreach (var other in Modules.Where(other => other != module))
            {
                data.Add(module, other);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModulePairs))]
    public void Modules_DoNotDependOnEachOther(string module, string other)
    {
        foreach (var assembly in LayerAssemblies)
        {
            var layerRoot = assembly.GetName().Name!;

            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceStartingWith($"{layerRoot}.{module}")
                .ShouldNot()
                .HaveDependencyOnAny($"{layerRoot}.{other}")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{layerRoot}: module '{module}' must not depend on '{other}'. "
                + LayerDependencyTests.FailureMessage(result));
        }
    }

    /// <summary>
    /// The modules that exist. <c>Jobs</c> is named in <see cref="Modules"/> and holds no types
    /// yet, which is fine — but a boundary check over an empty namespace passes for the wrong
    /// reason, so the modules that are built say so here. Onboarding joined in M3, Licensing in M5
    /// and Markers in M7, and without these lines the theory above would report them clean whether
    /// or not they reached into Indexing.
    /// </summary>
    private static readonly string[] BuiltModules = ["Changes", "Indexing", "Licensing", "Markers", "Onboarding"];

    [Theory]
    [MemberData(nameof(BuiltModuleNames))]
    public void EveryBuiltModule_HasTypesInEveryLayer(string module)
    {
        foreach (var assembly in LayerAssemblies)
        {
            var layerRoot = assembly.GetName().Name!;

            Assert.True(
                assembly.GetTypes().Any(type =>
                    type.Namespace?.StartsWith($"{layerRoot}.{module}", StringComparison.Ordinal) == true),
                $"'{layerRoot}.{module}' holds no types, so every boundary check over it passes "
                + "vacuously. Either the module is not built in this layer — in which case take it "
                + $"out of {nameof(BuiltModules)} — or something has been moved out of it.");
        }
    }

    public static TheoryData<string> BuiltModuleNames()
    {
        var data = new TheoryData<string>();

        foreach (var module in BuiltModules)
        {
            data.Add(module);
        }

        return data;
    }

    /// <summary>
    /// The Hashtag grammar and the Graph gateway left the Indexing module in M2 so that a Change
    /// could use them without reaching into it. That only holds if they stay module-free: a shared
    /// namespace that grows a dependency on one module has quietly made every other module depend
    /// on it too, through the back door this file exists to keep shut.
    /// </summary>
    [Fact]
    public void SharedNamespaces_DependOnNoModule()
    {
        foreach (var shared in SharedNamespaces)
        {
            var assembly = LayerAssemblies.Single(candidate => shared.StartsWith(candidate.GetName().Name!, StringComparison.Ordinal));
            var layerRoot = assembly.GetName().Name!;

            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceStartingWith(shared)
                .ShouldNot()
                .HaveDependencyOnAny([.. Modules.Select(module => $"{layerRoot}.{module}")])
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"'{shared}' is shared by every module and must depend on none of them. "
                + LayerDependencyTests.FailureMessage(result));
        }
    }
}
