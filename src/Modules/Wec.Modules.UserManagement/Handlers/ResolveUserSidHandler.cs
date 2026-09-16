using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Microsoft365;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Application;

namespace Wec.Modules.UserManagement.Handlers;

public sealed record ResolveUserSidRequest(string SecurityIdentifier, string DirectoryScope,
    UserDirectoryConnectionRequest? Connection = null);

internal sealed class ResolveUserSidHandler(IDirectoryUserSnapshotProvider directory) : IActionHandler<ResolveUserSidRequest, ObjectReference>
{
    public string Module => "usermanagement";
    public string Action => "resolveSid";

    public async Task<Result<ObjectReference>> HandleAsync(ResolveUserSidRequest payload, CancellationToken cancellationToken)
    {
        string? sid = Microsoft365QueryValidation.AccountSid(payload.SecurityIdentifier);
        string? scope = payload.DirectoryScope?.Trim().TrimEnd('.').ToLowerInvariant();
        if (sid is null || scope is null || scope.Length > 253 || Uri.CheckHostName(scope) != UriHostNameType.Dns)
        {
            return Result.Failure<ObjectReference>(new(ErrorCode.InvalidRequest, "An exact account SID and directory DNS scope are required."));
        }
        Result<DirectoryUserReadConnection> connection = (payload.Connection ?? new()).ToConnection();
        if (connection.IsFailure) { return Result.Failure<ObjectReference>(connection.Error!); }
        var query = new DirectoryUserLookup(connection.Value with { Domain = connection.Value.Domain ?? scope }, scope, SecurityIdentifier: sid);
        Result<DirectoryUserIdentityResult> result = await directory.ReadIdentityAsync(query, cancellationToken);
        if (result.IsFailure) { return Result.Failure<ObjectReference>(result.Error!); }
        DirectoryUserRecord? user = result.Value.User;
        if (user is null)
        {
            return Result.Failure<ObjectReference>(new(ErrorCode.NotFound, "This bounded directory query returned no user. The observation may be historical or outside the selected directory; no account was selected."));
        }
        if (user.ObjectId == Guid.Empty || Microsoft365QueryValidation.AccountSid(user.Sid) != sid
            || !string.Equals(user.DirectoryScope, scope, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ObjectReference>(new(ErrorCode.DirectoryUnavailable, "The returned directory identity did not match the selected scope and SID."));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(new ObjectReference(ObjectKind.User, ObjectSource.ActiveDirectory, scope, user.ObjectId.ToString("D")));
    }
}
