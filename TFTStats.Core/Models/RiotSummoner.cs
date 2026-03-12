
using System.Text.Json.Serialization;

namespace TFTStats.Core.Models
{
    public record RiotTFTSummoner(
        [property: JsonPropertyName("id")] string Id,               
        [property: JsonPropertyName("accountId")] string AccountId, 
        [property: JsonPropertyName("puuid")] string Puuid,                
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("profileIconId")] int ProfileIconId,
        [property: JsonPropertyName("revisionDate")] long RevisionDate,
        [property: JsonPropertyName("summonerLevel")] int SummonerLevel
    );
}
