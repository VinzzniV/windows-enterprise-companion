using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Privileges;
using Wec.Host.Bridge;
using Wec.Host.Dialogs;
using Wec.Host.Options;
using Wec.Host.Runtime;
using Wec.Infrastructure.Logging;
using Wec.Infrastructure.Persistence;
using Wec.Infrastructure.Privileges;
using Wec.Infrastructure.Registry;
using Wec.Infrastructure.Shell;
using Wec.Core.Modules;
using Wec.Infrastructure.Time;
using Wec.Infrastructure.Wmi;
using Wec.Infrastructure.EventLog;
using Wec.Infrastructure.Network;
using Wec.Infrastructure.SoftwareUpdates;
using Wec.Infrastructure.Security;
using Wec.Infrastructure.RemoteExecution;
using Wec.Infrastructure.Directory;
using Wec.Modules.ActiveDirectory;
using Wec.Modules.Diagnostics;
using Wec.Modules.EmployeeLifecycle;
using Wec.Modules.Inventory;
using Wec.Modules.NetworkScan;
using Wec.Modules.PatchManagement;
using Wec.Modules.PrintManagement;
using Wec.Modules.Reporting;
using Wec.Modules.Security;
using Wec.Modules.Targets;
using Wec.Modules.VulnerabilityManagement;

using HostFactory = Microsoft.Extensions.Hosting.Host;

namespace Wec.Host;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        IHost? host = null;
        bool hostStarted = false;

        try
        {
            host = BuildHost(args);
            host.Start();
            hostStarted = true;
            ValidateActionHandlerRegistrations(host.Services);
            ApplyDatabaseMigrations(host.Services);
            Application.Run(host.Services.GetRequiredService<MainWindow>());
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application startup failed");
            StartupFailurePresentation presentation = StartupFailurePresentation.From(
                exception,
                StartupPhase.Host);
            MessageBox.Show(
                presentation.Message,
                presentation.Title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (hostStarted && host is not null)
            {
                try
                {
                    host.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Application host shutdown failed");
                }
            }

            host?.Dispose();
            Log.CloseAndFlush();
        }
    }

    // Internal so the composition-root test can build the real host
    internal static IHost BuildHost(string[] args)
    {
        HostApplicationBuilder builder = HostFactory.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        string userSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wec",
            "usersettings.json");
        builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: false);
        builder.Services.AddSingleton(new UserSettingsStore(userSettingsPath));

        RuntimeInstanceProfile runtimeProfile = RuntimeInstanceProfile.Resolve(
            builder.Configuration,
            builder.Environment.EnvironmentName,
            builder.Environment.ContentRootPath,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable(RuntimeInstanceProfile.EnvironmentVariableName));
        builder.Configuration.AddInMemoryCollection(runtimeProfile.ConfigurationOverrides);
        builder.Services.AddSingleton(runtimeProfile);

        builder.Services
            .AddOptions<LoggingOptions>()
            .Bind(builder.Configuration.GetSection(LoggingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<WebViewOptions>()
            .Bind(builder.Configuration.GetSection(WebViewOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<FrontendOptions>()
            .Bind(builder.Configuration.GetSection(FrontendOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSerilog((serviceProvider, loggerConfiguration) =>
            SerilogConfiguration.Configure(
                loggerConfiguration,
                serviceProvider.GetRequiredService<IOptions<LoggingOptions>>().Value));

        builder.Services
            .AddOptions<DatabaseOptions>()
            .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<Wec.Core.Targets.RemoteScanOptions>()
            .Bind(builder.Configuration.GetSection(Wec.Core.Targets.RemoteScanOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<InventoryOptions>()
            .Bind(builder.Configuration.GetSection(InventoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<DiagnosticsOptions>()
            .Bind(builder.Configuration.GetSection(DiagnosticsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<SecurityOptions>()
            .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<ReportingOptions>()
            .Bind(builder.Configuration.GetSection(ReportingOptions.SectionName))
            .Validate(options => options.MaximumInventoryAge > TimeSpan.Zero,
                "The Reporting inventory freshness window must be positive.")
            .Validate(options => options.MaximumSecurityScanAge > TimeSpan.Zero,
                "The Reporting Security freshness window must be positive.")
            .ValidateOnStart();

        builder.Services
            .AddOptions<ActiveDirectoryOptions>()
            .Bind(builder.Configuration.GetSection(ActiveDirectoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<PatchManagementOptions>()
            .Bind(builder.Configuration.GetSection(PatchManagementOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<PrintManagementOptions>()
            .Bind(builder.Configuration.GetSection(PrintManagementOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<NetworkScanOptions>()
            .Bind(builder.Configuration.GetSection(NetworkScanOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<ItLifecycleOptions>()
            .Bind(builder.Configuration.GetSection(ItLifecycleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<VulnerabilityManagementOptions>()
            .Bind(builder.Configuration.GetSection(VulnerabilityManagementOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.StaleCriticalDays > options.StaleWarningDays,
                "The Nessus critical stale threshold must be greater than the warning threshold.")
            .ValidateOnStart();

        IModule[] modules =
        [
            new InventoryModule(),
            new SecurityModule(),
            new DiagnosticsModule(),
            new ReportingModule(),
            new ActiveDirectoryModule(),
            new PatchManagementModule(),
            new PrintManagementModule(),
            new NetworkScanModule(),
            new TargetsModule(),
            new EmployeeLifecycleModule(),
            new VulnerabilityManagementModule(),
        ];
        foreach (IModule module in modules)
        {
            module.RegisterServices(builder.Services);
        }

        builder.Services.AddSingleton(new ModelAssemblyRegistry(
            [.. modules.Select(module => module.GetType().Assembly)]));
        builder.Services.AddDbContext<WecDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            string databasePath = Environment.ExpandEnvironmentVariables(databaseOptions.DatabasePath);
            options.UseSqlite($"Data Source={databasePath}");
        });
        // Modules depend on the DbContext base type only (dependency rule 2)
        builder.Services.AddScoped<DbContext>(serviceProvider => serviceProvider.GetRequiredService<WecDbContext>());

        builder.Services.AddSingleton<IWmiQueryService, CimWmiQueryService>();
        builder.Services.AddSingleton<IDiskEncryptionStatusReader, Wec.Infrastructure.Storage.WmiDiskEncryptionStatusReader>();
        builder.Services.AddSingleton<Wec.Core.Opsi.IOpsiClient, Wec.Infrastructure.Opsi.JsonRpcOpsiClient>();
        builder.Services.AddSingleton<Wec.Core.SoftwareUpdates.IVendorVersionClient, HttpVendorVersionClient>();
        builder.Services.AddSingleton<Wec.Core.RemoteExecution.IRemoteCommandExecutor, OpenSshRemoteCommandExecutor>();
        builder.Services.AddSingleton<Wec.Core.RemoteExecution.IRemoteArtifactStager, Wec.Infrastructure.RemoteExecution.HttpOpenSshArtifactStager>();
        builder.Services.AddSingleton<Wec.Core.Snmp.ISnmpReader, Wec.Infrastructure.Snmp.SnmpV2cReader>();
        builder.Services.AddSingleton<Wec.Core.Ccrx.ICcrxClient, Wec.Infrastructure.Ccrx.CcrxHttpClient>();
        builder.Services.AddSingleton<Wec.Core.Dhcp.IDhcpReader, Wec.Infrastructure.Dhcp.PowerShellDhcpReader>();
        builder.Services.AddSingleton<Wec.Core.Printing.IPrinterPortRemover, Wec.Infrastructure.Printing.PowerShellPrinterPortRemover>();
        builder.Services.AddSingleton<Wec.Core.Network.INetworkScanner, Wec.Infrastructure.Network.NmapScanner>();
        builder.Services.AddSingleton<IRegistryReader, WindowsRegistryReader>();
        builder.Services.AddSingleton<ILocalAccountPolicyReader, Wec.Infrastructure.Accounts.SamAccountPolicyReader>();
        builder.Services.AddSingleton<INetworkInfoProvider, SystemNetworkInfoProvider>();
        builder.Services.AddSingleton<IPingProbe, SystemPingProbe>();
        builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        builder.Services.AddSingleton<IEventLogReader, SystemEventLogReader>();
        builder.Services.AddSingleton<IDirectoryReader, LdapDirectoryReader>();
        builder.Services.AddSingleton<IShellLauncher, ShellLauncher>();
        builder.Services.AddSingleton<ISaveFileDialogService, WinFormsSaveFileDialogService>();
        builder.Services.AddSingleton<IPrivilegeContext, WindowsPrivilegeContext>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IServiceCredentialStore, WindowsCredentialStore>();
        builder.Services.AddSingleton<IActionHandler, PingHandler>();
        builder.Services.AddSingleton<IActionHandler, ProbeHostsHandler>();
        builder.Services.AddSingleton<IPowerShellSessionLauncher, ShellPowerShellSessionLauncher>();
        builder.Services.AddSingleton<IActionHandler, OpenPsSessionHandler>();
        builder.Services.AddSingleton<IActionHandler, RecentLogEntriesHandler>();
        builder.Services.AddSingleton<IActionHandler, ClearRecentLogEntriesHandler>();
        builder.Services.AddSingleton<IActionHandler, GetAppInfoHandler>();
        builder.Services.AddSingleton<IActionHandler, OpenLogsFolderHandler>();
        builder.Services.AddSingleton<IActionHandler, GetItLifecycleSettingsHandler>();
        builder.Services.AddSingleton<IActionHandler, SaveItLifecycleSettingsHandler>();
        builder.Services.AddSingleton<IActionHandler, GetOpsiSettingsHandler>();
        builder.Services.AddSingleton<IActionHandler, SaveOpsiSettingsHandler>();
        builder.Services.AddSingleton<IActionHandler, GetNessusSettingsHandler>();
        builder.Services.AddSingleton<IActionHandler, SaveNessusSettingsHandler>();
        builder.Services.AddSingleton<IActionHandler, GetServiceCredentialStatusesHandler>();
        builder.Services.AddSingleton<IActionHandler, SaveServiceCredentialHandler>();
        builder.Services.AddSingleton<IActionHandler, DeleteServiceCredentialHandler>();
        builder.Services.AddSingleton<IElevatedProcessLauncher, ShellElevatedProcessLauncher>();
        builder.Services.AddSingleton<IAppShutdown, MainWindowShutdown>();
        builder.Services.AddSingleton<IActionHandler, RestartElevatedHandler>();
        builder.Services.AddSingleton<IBridgeExecutionTimeoutPolicy, BridgeExecutionTimeoutPolicy>();
        builder.Services.AddSingleton<ActionDispatcher>();
        builder.Services.AddSingleton<WebViewBridge>();
        builder.Services.AddSingleton<WebViewBridgeEventPublisher>();
        builder.Services.AddSingleton<IBridgeEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<WebViewBridgeEventPublisher>());
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    internal static void ValidateActionHandlerRegistrations(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        var duplicateRegistrations = scope.ServiceProvider
            .GetServices<IActionHandler>()
            .GroupBy(handler => (handler.Module, handler.Action))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Module}/{group.Key.Action}")
            .ToList();

        if (duplicateRegistrations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate action handler registrations: {string.Join(", ", duplicateRegistrations)}");
        }
    }

    private static void ApplyDatabaseMigrations(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();

        var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        string databasePath = Environment.ExpandEnvironmentVariables(databaseOptions.DatabasePath);
        string? databaseDirectory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<WecDbContext>();
        var stopwatch = Stopwatch.StartNew();
        dbContext.Database.Migrate();
        stopwatch.Stop();

        var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<WecDbContext>>();
        LogMigrationsApplied(migrationLogger, stopwatch.ElapsedMilliseconds, databasePath);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Database migrations applied in {ElapsedMilliseconds} ms ({DatabasePath})")]
    private static partial void LogMigrationsApplied(
        Microsoft.Extensions.Logging.ILogger logger,
        long elapsedMilliseconds,
        string databasePath);
}
