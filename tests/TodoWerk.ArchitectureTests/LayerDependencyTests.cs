using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace TodoWerk.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly SharedKernelAssembly = typeof(SharedKernel.Error).Assembly;
    private static readonly Assembly DomainAssembly = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly ApplicationAssembly =
        typeof(TodoWerk.Application.ApplicationServiceCollectionExtensions).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.AssemblyReference).Assembly;

    [Fact]
    public void SharedKernel_DependsOnNoOtherLayer()
    {
        var result = Types.InAssembly(SharedKernelAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "TodoWerk.Domain",
                "TodoWerk.Application",
                "TodoWerk.Infrastructure",
                "TodoWerk.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Domain_DependsOnlyOnSharedKernel()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "TodoWerk.Application",
                "TodoWerk.Infrastructure",
                "TodoWerk.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Application_DoesNotDependOnOuterLayers()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "TodoWerk.Infrastructure",
                "TodoWerk.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnWeb()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("TodoWerk.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    internal static string FailureMessage(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Offending types: " + string.Join(", ", result.FailingTypes.Select(t => t.FullName));
}
