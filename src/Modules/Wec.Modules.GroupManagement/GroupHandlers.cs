using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Objects;
using Wec.Core.Results;

namespace Wec.Modules.GroupManagement;

internal sealed class GroupProfileHandler(GroupProfileService service) : IActionHandler<GroupProfileRequest, GroupProfileResult>
{
    public string Module => "groups";
    public string Action => "getProfile";
    public Task<Result<GroupProfileResult>> HandleAsync(GroupProfileRequest payload, CancellationToken cancellationToken) => service.GetAsync(payload, cancellationToken);
}
internal sealed class ResolveGroupHandler(GroupProfileService service) : IActionHandler<ResolveGroupRequest, ObjectReference>
{
    public string Module => "groups";
    public string Action => "resolve";
    public Task<Result<ObjectReference>> HandleAsync(ResolveGroupRequest payload, CancellationToken cancellationToken) => service.ResolveAsync(payload, cancellationToken);
}
internal sealed class DirectoryGroupPageHandler(GroupProfileService service) : IActionHandler<DirectoryGroupPageRequest, CachedDirectoryGroupPage>
{
    public string Module => "groups";
    public string Action => "readDirectoryPage";
    public Task<Result<CachedDirectoryGroupPage>> HandleAsync(DirectoryGroupPageRequest payload, CancellationToken cancellationToken) => service.PageAsync(payload, cancellationToken);
}
