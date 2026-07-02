using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wec.Core.Messaging;

namespace Wec.Host.Tests;

/// <summary>
/// Builds the REAL host (same composition as production). Catches wiring bugs
/// unit tests structurally cannot see — e.g. a registered service without a
/// public constructor, or a handler with an unresolvable dependency.
/// </summary>
public sealed class CompositionRootTests
{
    [Fact]
    public void AllActionHandlersResolveFromTheProductionComposition()
    {
        using IHost host = Program.BuildHost([]);
        using IServiceScope scope = host.Services.CreateScope();

        List<IActionHandler> handlers = [.. scope.ServiceProvider.GetServices<IActionHandler>()];

        Assert.NotEmpty(handlers);
        // Every module must contribute at least one handler
        string[] expectedModules = ["system", "inventory", "security", "diagnostics", "activedirectory", "reporting"];
        foreach (string module in expectedModules)
        {
            Assert.Contains(handlers, handler => handler.Module == module);
        }
    }

    [Fact]
    public void HandlerRegistrationsContainNoDuplicates()
    {
        using IHost host = Program.BuildHost([]);

        Program.ValidateActionHandlerRegistrations(host.Services);
    }
}
