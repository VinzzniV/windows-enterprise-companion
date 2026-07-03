using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record TestDirectoryConnectionRequest(DirectoryConnectionRequest? Connection = null);

public sealed record TestDirectoryConnectionResult(
    bool DomainJoined,
    string? DomainName,
    string? DefaultNamingContext);

/// <summary>
/// Cheap bind check before a full analysis: resolves the domain and reads
/// the RootDSE with the given connection — the same first step every AD
/// action performs, so a passing test means overview/hygiene can connect.
/// </summary>
internal sealed class TestDirectoryConnectionHandler
    : IActionHandler<TestDirectoryConnectionRequest, TestDirectoryConnectionResult>
{
    private readonly DomainContextService _domainContextService;

    public TestDirectoryConnectionHandler(DomainContextService domainContextService)
    {
        _domainContextService = domainContextService;
    }

    public string Module => "activedirectory";

    public string Action => "testConnection";

    public async Task<Result<TestDirectoryConnectionResult>> HandleAsync(
        TestDirectoryConnectionRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryConnection> connection =
            (payload.Connection ?? new DirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Result.Failure<TestDirectoryConnectionResult>(connection.Error!);
        }

        Result<DomainContext> context =
            await _domainContextService.GetContextAsync(connection.Value, cancellationToken);
        return context.IsFailure
            ? Result.Failure<TestDirectoryConnectionResult>(context.Error!)
            : Result.Success(new TestDirectoryConnectionResult(
                context.Value.DomainJoined,
                context.Value.DomainName,
                context.Value.DefaultNamingContext));
    }
}
