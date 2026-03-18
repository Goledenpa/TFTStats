using System.Text.Json.Serialization;

namespace TFTStats.Core.Models
{
    public record RiotTFTAccount(
        [property: JsonPropertyName("puuid")] string Puuid,
        [property: JsonPropertyName("gameName")] string GameName,
        [property: JsonPropertyName("tagLine")] string TagLine
    );
}
