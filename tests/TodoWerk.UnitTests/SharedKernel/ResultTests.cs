using TodoWerk.SharedKernel;
using Xunit;

namespace TodoWerk.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void Success_IsSuccessWithNoneError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsFailureCarryingTheError()
    {
        var error = Error.NotFound("Thing.NotFound", "Thing was not found.");

        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Failure_WithNoneError_Throws()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void SuccessOfT_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void FailureOfT_AccessingValue_Throws()
    {
        var result = Result.Failure<int>(Error.Failure("Code", "Description"));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void FailureOfT_WithNoneError_Throws()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure<int>(Error.None));
    }
}
