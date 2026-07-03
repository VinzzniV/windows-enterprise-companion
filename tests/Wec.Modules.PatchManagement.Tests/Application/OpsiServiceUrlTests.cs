using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class OpsiServiceUrlTests
{
    [Theory]
    [InlineData("opsi.kauth.local", "https://opsi.kauth.local:4447/")]
    [InlineData("opsi.kauth.local:4448", "https://opsi.kauth.local:4448/")]
    [InlineData("https://opsi.kauth.local", "https://opsi.kauth.local:4447/")]
    // Uri.ToString() hides the scheme-default port; the Uri still carries 443
    [InlineData("https://opsi.kauth.local:443", "https://opsi.kauth.local/")]
    [InlineData("  opsi.kauth.local/  ", "https://opsi.kauth.local:4447/")]
    [InlineData("http://testserver", "http://testserver:4447/")]
    public void Normalize_AcceptsAdminInput(string input, string expected)
    {
        Result<Uri> result = OpsiServiceUrl.Normalize(input, 4447);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://opsi.kauth.local")]
    [InlineData("https://")]
    public void Normalize_RejectsInvalidInput(string input)
    {
        Result<Uri> result = OpsiServiceUrl.Normalize(input, 4447);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
    }
}
