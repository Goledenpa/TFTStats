using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TFTStats.Core.Base;

namespace TFTStats.Presentation
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
                    applyThemeToRedirectedOutput: true)
                .WriteTo.File("logs/crawler-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
            try
            {
                Log.Information("Starting TFT Stats Crawler System (Set 16)");

                using IHost host = Host.CreateDefaultBuilder(args)
                    .UseSerilog()
                    .ConfigureServices((context, services) =>
                    {
                        string connectionString = context.Configuration.GetConnectionString("TftDatabase")
                            ?? throw new InvalidOperationException("Connection string 'TftDatabase' not found.");

                        services.AddCoreServices(connectionString);
                        services.AddTransient<Crawler>();
                    })
                    .Build();

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    Log.Warning("Shutdown requested. Finishing current batch and closing safely...");
                    cts.Cancel();
                };
                var crawler = host.Services.GetRequiredService<Crawler>();

                // Config for March 2026
                string cluster = "europe";
                int setNumber = 16;

                try
                {
                    await crawler.RunAsync(cluster, setNumber, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log.Information("Crawler stopped gracefully via user cancellation.");
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Crawler crashed due to an unhandled exception");
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "The crawler terminated unexpectedly");
            }
            finally
            {
                Log.Information("Shutting down...");
                Log.CloseAndFlush();
            }
        }
    }
}
