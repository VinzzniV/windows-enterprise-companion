using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Application;

namespace Wec.Modules.UserManagement.Handlers;

internal sealed class ScopedUserProfileHandler(ScopedUserProfileService service) : IActionHandler<ScopedUserProfileRequest, ScopedUserProfile>
{
    public string Module => "usermanagement";
    public string Action => "getProfile";
    public Task<Result<ScopedUserProfile>> HandleAsync(ScopedUserProfileRequest payload, CancellationToken cancellationToken) =>
        service.GetAsync(payload, cancellationToken);
}
