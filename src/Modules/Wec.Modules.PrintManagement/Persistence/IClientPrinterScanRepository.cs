using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Persistence;

public interface IClientPrinterScanRepository
{
    Task<ClientPrinterScan?> GetLatestAsync(string hostKey, CancellationToken cancellationToken);

    Task SaveAsync(ClientPrinterScan scan, CancellationToken cancellationToken);
}
