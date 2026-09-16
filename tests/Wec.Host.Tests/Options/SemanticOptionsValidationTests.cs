using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics;
using Wec.Modules.EmployeeLifecycle;
using Wec.Modules.Clients;
using Wec.Modules.NetworkScan;

namespace Wec.Host.Tests.Options;

public sealed class SemanticOptionsValidationTests
{
    [Fact]
    public void RejectsNonPositiveProviderDurations()
    {
        var options = new RemoteScanOptions { ConnectionTimeout = TimeSpan.Zero };

        Assert.Contains(Validate(options), result => result.MemberNames.Contains(nameof(options.ConnectionTimeout)));
    }

    [Fact]
    public void RejectsEmptyRequiredDiagnosticLists()
    {
        var options = new DiagnosticsOptions
        {
            EventLogNames = [],
            MonitoredServices = [],
        };

        IReadOnlyList<ValidationResult> results = Validate(options);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(options.EventLogNames)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(options.MonitoredServices)));
    }

    [Fact]
    public void RejectsOutOfRangeNetworkPorts()
    {
        var options = new NetworkScanOptions { ScanPorts = [0, 443, 65_536] };

        Assert.Contains(Validate(options), result => result.MemberNames.Contains(nameof(options.ScanPorts)));
    }

    [Fact]
    public void RejectsInvalidNestedKasperskyProviderSettings()
    {
        var options = new ItLifecycleOptions
        {
            Kaspersky = new KasperskyOptions { Port = 0, RequestTimeout = TimeSpan.Zero },
        };

        Assert.Equal(2, Validate(options).Count);
    }

    [Fact]
    public void RejectsNonPositiveClientOverviewMaximumAges()
    {
        var options = new ClientOverviewOptions
        {
            MaximumInventoryAge = TimeSpan.Zero,
            MaximumSoftwareAge = TimeSpan.Zero,
            MaximumHealthAge = TimeSpan.Zero,
            MaximumSecurityAge = TimeSpan.Zero,
        };

        Assert.Equal(4, Validate(options).Count);
    }

    [Fact]
    public async Task ShippedConfiguration_PassesValidateOnStart()
    {
        using IHost host = Program.BuildHost([]);

        await host.StartAsync();
        await host.StopAsync();
    }

    private static List<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
