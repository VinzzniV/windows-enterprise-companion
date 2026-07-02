using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Host.Bridge;
using Wec.Host.Options;
using Wec.Infrastructure.Logging;
using Wec.Infrastructure.Time;

using HostFactory = Microsoft.Extensions.Hosting.Host;

namespace Wec.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            using IHost host = BuildHost(args);
            host.Start();
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

    private static IHost BuildHost(string[] args)
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

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IActionHandler, PingHandler>();
        builder.Services.AddSingleton<ActionDispatcher>();
        builder.Services.AddSingleton<WebViewBridge>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
