using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Handlers;

namespace Wec.Modules.UserManagement.Tests;

public sealed class ResolveUserSidHandlerTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private readonly IDirectoryUserSnapshotProvider _directory = Substitute.For<IDirectoryUserSnapshotProvider>();
    private static DirectoryUserRecord User() => new(Guid.NewGuid(), Sid, "Name", null, null, null, null, null, null,
        null, "CN=Name,DC=example,DC=test", "DC=example,DC=test", null, null, null, null, null, null, null, [],
        new(DirectoryUserAccessCoverage.NotEvaluated, "Unknown", []), "example.test");

    [Fact]
    public async Task ExactSidProducesScopedGuidReference()
    {
        var user = User();
        _directory.ReadIdentityAsync(Arg.Any<DirectoryUserLookup>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DirectoryUserIdentityResult("example.test", DateTimeOffset.UtcNow, user)));
        var result = await new ResolveUserSidHandler(_directory).HandleAsync(new(Sid, "EXAMPLE.TEST."), CancellationToken.None);
        Assert.Equal(new ObjectReference(ObjectKind.User, ObjectSource.ActiveDirectory, "example.test", user.ObjectId.ToString("D")), result.Value);
        await _directory.Received(1).ReadIdentityAsync(Arg.Is<DirectoryUserLookup>(query => query.SecurityIdentifier == Sid
            && query.ObjectId == null && query.DirectoryScope == "example.test" && query.Connection.Domain == "example.test"), CancellationToken.None);
    }

    [Theory]
    [InlineData("S-1-5-32-544", "example.test")]
    [InlineData(Sid, "bad/scope")]
    public async Task InvalidIdentityOrScopeCannotQueryDirectory(string sid, string scope)
    {
        Assert.True((await new ResolveUserSidHandler(_directory).HandleAsync(new(sid, scope), CancellationToken.None)).IsFailure);
        Assert.Empty(_directory.ReceivedCalls());
    }

    [Fact]
    public async Task MissingOrMismatchedNativeEvidenceCannotSelectAnAccount()
    {
        _directory.ReadIdentityAsync(Arg.Any<DirectoryUserLookup>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DirectoryUserIdentityResult("example.test", DateTimeOffset.UtcNow, null)),
                Result.Success(new DirectoryUserIdentityResult("example.test", DateTimeOffset.UtcNow, User() with { DirectoryScope = "other.test" })));
        var handler = new ResolveUserSidHandler(_directory);
        Assert.Equal(ErrorCode.NotFound, (await handler.HandleAsync(new(Sid, "example.test"), CancellationToken.None)).Error!.Code);
        Assert.Equal(ErrorCode.DirectoryUnavailable, (await handler.HandleAsync(new(Sid, "example.test"), CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task AmbiguousDirectoryFailureRemainsFailure()
    {
        var error = new Error(ErrorCode.DirectoryUnavailable, "Ambiguous identity");
        _directory.ReadIdentityAsync(Arg.Any<DirectoryUserLookup>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<DirectoryUserIdentityResult>(error));
        Assert.Same(error, (await new ResolveUserSidHandler(_directory).HandleAsync(new(Sid, "example.test"), CancellationToken.None)).Error);
    }
}
