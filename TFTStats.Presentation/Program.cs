using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TFTStats.Core.Base;
using TFTStats.Core.Models;
using TFTStats.Core.Service;

namespace TFTStats.Presentation
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            using IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                {
                    string myApiKey = "RGAPI-96eed502-94bb-4026-a110-ad88285bee56";
                    string connectionString = "Host=79.72.28.58;Database=TFTStats;Username=postgres;Password=!Adv31295.-9";
                    services.AddCoreServices(myApiKey, connectionString);
                })
                .Build();


            var riotAccountService = host.Services.GetRequiredService<RiotAccountService>();
            var riotSummonerService = host.Services.GetRequiredService<RiotTFTSummonerService>();
            var riotMatchService = host.Services.GetRequiredService<RiotTFTMatchService>();

            Console.WriteLine("=== TFT STATS TRIAL ===");

            string cluster = "europe";
            string region = "euw1";
            string riotName = "pablito";
            string tagLine = "adc";

            try
            {
                // STEP 1: Get Account
                Console.WriteLine($"\n[1/4] Fetching Account: {riotName}#{tagLine}...");
                var account = await riotAccountService.GetAccountByRiotId(cluster, riotName, tagLine);

                if (account == null) { Console.WriteLine("Account not found."); return; }
                Console.WriteLine($"Found! PUUID: {account.Puuid}");

                // STEP 2: Get Summoner Level
                Console.WriteLine($"\n[2/4] Fetching Summoner Details for {region}...");
                var summoner = await riotSummonerService.GetSummonerByPuuidAsync(region, account.Puuid);

                if (summoner == null) { Console.WriteLine("Summoner data not found."); return; }
                Console.WriteLine($"Level: {summoner.SummonerLevel}");

                // STEP 3: Get Match List
                Console.WriteLine($"\n[3/4] Fetching last 5 Match IDs...");
                var matchIds = await riotMatchService.GetMatches(cluster, account.Puuid, 0, 100);

                if (!matchIds.Any()) { Console.WriteLine("No matches found."); return; }
                Console.WriteLine($"Latest Match ID: {matchIds[0]}");

                RiotTFTMatch match = default;

                for (int i = 0; i < matchIds.Count; i++)
                {
                    // STEP 4: Get Match Detail
                    Console.WriteLine($"\n[4/4] Fetching details for match {matchIds[i]}...");
                    match = await riotMatchService.GetMatch(cluster, matchIds[i]);

                    if (match != null)
                    {
                        // Find our player in the match participants
                        var me = match.Info.Participants.FirstOrDefault(p => p.Puuid == account.Puuid);

                        Console.WriteLine("\n--- LATEST MATCH RESULTS ---");
                        Console.WriteLine($"Placement: #{me?.Placement}");
                        Console.WriteLine($"Level: {me?.Level}");
                        Console.WriteLine($"Units:");
                        foreach (var unit in me?.Units ?? [])
                        {
                            Console.WriteLine($" - {unit.Tier}* {unit.CharacterId.Replace("TFT_Unit_", "")}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
            }

            Console.WriteLine("\nTrial Complete. Press any key to exit.");
            Console.ReadKey();
        }
    }
}
