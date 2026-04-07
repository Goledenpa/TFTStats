using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TFTStats.Core.Base;
using TFTStats.Core.Repositories.Interfaces;

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
                Log.Information("Starting TFT Stats Two-Phase Crawler System (Set 16)");

                using IHost host = Host.CreateDefaultBuilder(args)
                    .UseSerilog()
                    .ConfigureServices((context, services) =>
                    {
                        string connectionString = context.Configuration.GetConnectionString("TftDatabase")
                            ?? throw new InvalidOperationException("Connection string 'TftDatabase' not found.");

                        services.AddCoreServices(connectionString);
                        services.AddTransient<Crawler>();
                        services.AddTransient<Harvester>();
                    })
                    .Build();

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    Log.Warning("Shutdown requested. Finishing current batch and closing safely...");
                    cts.Cancel();
                };
                
                // Phase 1: Harvest all match IDs first (exits when no more players)
                var harvester = ActivatorUtilities.CreateInstance<Harvester>(
                    host.Services, 10000, 5000, true);
                var crawler = host.Services.GetRequiredService<Crawler>();

                // Sync the pending counter at startup (takes ~15s but only runs once)
                var harvestRepo = host.Services.GetRequiredService<IHarvesterRepository>();
                Log.Information("Syncing pending match counter...");
                await harvestRepo.SyncPendingCounterAsync();

                string cluster = "europe";
                int setNumber = 16;

                Log.Information("=== PHASE 1: HARVESTING MATCH IDs ===");
                try
                {
                    await harvester.RunAsync(cluster, setNumber, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log.Information("Harvester stopped gracefully.");
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Harvester crashed due to an unhandled exception");
                }

                if (cts.IsCancellationRequested)
                {
                    Log.Information("Shutdown requested. Skipping Phase 2.");
                    return;
                }

                // Phase 2: Crawl and import match details
                Log.Information("=== PHASE 2: CRAWLING MATCH DETAILS ===");
                try
                {
                    await crawler.RunAsync(cluster, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log.Information("Crawler stopped gracefully.");
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
