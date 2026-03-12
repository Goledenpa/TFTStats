using System.Text.Json.Serialization;

namespace TFTStats.Core.Models
{
    public record RiotAccount(
        [property: JsonPropertyName("puuid")] string Puuid,
        [property: JsonPropertyName("gameName")] string GameName,
        [property: JsonPropertyName("tagLine")] string TagLine
    );
}
