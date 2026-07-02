using Wec.Core.Privileges;
using Wec.Core.Results;

namespace Wec.Core.Tests.Results;

public class ResultTests
{
    [Fact]
    public void Success_ExposesValue()
    {
        Result<int> result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_ExposesError()
    {
        var error = Error.NotFound("snapshot not found");

        Result<int> result = Result.Failure<int>(error);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Failure_ReadingValue_Throws()
    {
        Result<int> result = Result.Failure<int>(Error.NotFound("missing"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void AccessDeniedError_CarriesRequiredPrivilege()
    {
        var error = Error.AccessDenied("security log requires elevation", PrivilegeLevel.Administrator);

        Assert.Equal(ErrorCode.AccessDenied, error.Code);
        Assert.Equal(PrivilegeLevel.Administrator, error.RequiredPrivilege);
    }
}
