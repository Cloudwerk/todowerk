using TodoWerk.SharedKernel;
using Xunit;

namespace TodoWerk.UnitTests.SharedKernel;

public sealed class ErrorTests
{
    [Fact]
    public void Failure_CreatesErrorWithFailureType()
    {
        var error = Error.Failure("Code", "Description");

        Assert.Equal("Code", error.Code);
        Assert.Equal("Description", error.Description);
        Assert.Equal(ErrorType.Failure, error.Type);
    }

    [Fact]
    public void Validation_CreatesErrorWithValidationType()
    {
        var error = Error.Validation("Code", "Description");

        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void NotFound_CreatesErrorWithNotFoundType()
    {
        var error = Error.NotFound("Code", "Description");

        Assert.Equal(ErrorType.NotFound, error.Type);
    }

    [Fact]
    public void Conflict_CreatesErrorWithConflictType()
    {
        var error = Error.Conflict("Code", "Description");

        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    [Fact]
    public void Unauthorized_CreatesErrorWithUnauthorizedType()
    {
        var error = Error.Unauthorized("Code", "Description");

        Assert.Equal(ErrorType.Unauthorized, error.Type);
    }

    [Fact]
    public void None_HasEmptyCodeAndDescription()
    {
        Assert.Equal(string.Empty, Error.None.Code);
        Assert.Equal(string.Empty, Error.None.Description);
    }

    [Fact]
    public void Errors_WithSameValues_AreEqual()
    {
        Assert.Equal(Error.Failure("A", "B"), Error.Failure("A", "B"));
    }
}
