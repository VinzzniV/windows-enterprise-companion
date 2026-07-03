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
using Wec.Core.Messaging;
using Wec.Core.Privileges;
using Wec.Host.Bridge;
using Wec.Host.Dialogs;
using Wec.Host.Options;
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
using Wec.Infrastructure.Directory;
using Wec.Modules.ActiveDirectory;
using Wec.Modules.Diagnostics;
using Wec.Modules.Inventory;
using Wec.Modules.Reporting;
using Wec.Modules.Security;

using HostFactory = Microsoft.Extensions.Hosting.Host;

namespace Wec.Host;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            using IHost host = BuildHost(args);
            host.Start();
            ValidateActionHandlerRegistrations(host.Services);
            ApplyDatabaseMigrations(host.Services);
            try
            {
                Application.Run(host.Services.GetRequiredService<MainWindow>());
            }
            finally
            {
                host.StopAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception) when (exception is OptionsValidationException or HostAbortedException)
        {
            MessageBox.Show(
                $"Configuration is invalid:{Environment.NewLine}{exception.Message}",
                "Windows Enterprise Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
            .AddOptions<ActiveDirectoryOptions>()
            .Bind(builder.Configuration.GetSection(ActiveDirectoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        IModule[] modules =
        [
            new InventoryModule(),
            new SecurityModule(),
            new DiagnosticsModule(),
            new ReportingModule(),
            new ActiveDirectoryModule(),
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
        builder.Services.AddSingleton<IRegistryReader, WindowsRegistryReader>();
        builder.Services.AddSingleton<INetworkInfoProvider, SystemNetworkInfoProvider>();
        builder.Services.AddSingleton<IPingProbe, SystemPingProbe>();
        builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        builder.Services.AddSingleton<IEventLogReader, SystemEventLogReader>();
        builder.Services.AddSingleton<IDirectoryReader, LdapDirectoryReader>();
        builder.Services.AddSingleton<IShellLauncher, ShellLauncher>();
        builder.Services.AddSingleton<ISaveFileDialogService, WinFormsSaveFileDialogService>();
        builder.Services.AddSingleton<IPrivilegeContext, WindowsPrivilegeContext>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IActionHandler, PingHandler>();
        builder.Services.AddSingleton<IActionHandler, GetAppInfoHandler>();
        builder.Services.AddSingleton<IActionHandler, OpenLogsFolderHandler>();
        builder.Services.AddSingleton<IElevatedProcessLauncher, ShellElevatedProcessLauncher>();
        builder.Services.AddSingleton<IAppShutdown, MainWindowShutdown>();
        builder.Services.AddSingleton<IActionHandler, RestartElevatedHandler>();
        builder.Services.AddSingleton<ActionDispatcher>();
        builder.Services.AddSingleton<WebViewBridge>();
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
