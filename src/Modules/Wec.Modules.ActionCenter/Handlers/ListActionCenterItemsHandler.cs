using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActionCenter.Application;

namespace Wec.Modules.ActionCenter.Handlers;

internal sealed class ListActionCenterItemsHandler(ActionCenterService service)
    : IActionHandler<ListActionCenterItemsRequest, ActionCenterPage>
{
    public string Module => "actioncenter";

    public string Action => "listItems";

    public Task<Result<ActionCenterPage>> HandleAsync(
        ListActionCenterItemsRequest payload,
        CancellationToken cancellationToken) => service.GetPageAsync(payload, cancellationToken);
}
