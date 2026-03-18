using System.Text.Json.Serialization;

namespace TFTStats.Core.Models;

public record RiotTFTMatch(
    [property: JsonPropertyName("metadata")] RiotTFTMatchMetadata Metadata,
    [property: JsonPropertyName("info")] RiotTFTMatchInfo Info
);

public record RiotTFTMatchMetadata(
    [property: JsonPropertyName("data_version")] string DataVersion,
    [property: JsonPropertyName("match_id")] string MatchId,
    [property: JsonPropertyName("participants")] List<string> Participants // List of PUUIDs
);

public record RiotTFTMatchInfo(
    [property: JsonPropertyName("game_creation")] long GameCreation,
    [property: JsonPropertyName("game_datetime")] long GameDatetime,
    [property: JsonPropertyName("game_length")] float GameLength,
    [property: JsonPropertyName("game_version")] string GameVersion,
    [property: JsonPropertyName("participants")] List<RiotTFTParticipant> Participants,
    [property: JsonPropertyName("tft_set_number")] int TftSetNumber,
    [property: JsonPropertyName("queue_id")] int QueueId

);

public record RiotTFTParticipant(
    [property: JsonPropertyName("placement")] int Placement,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("gold_left")] int GoldLeft,
    [property: JsonPropertyName("last_round")] int LastRound,
    [property: JsonPropertyName("time_eliminated")] float TimeEliminated,
    [property: JsonPropertyName("players_eliminated")] int PlayersEliminated,
    [property: JsonPropertyName("total_damage_to_players")] int TotalDamageToPlayers,
    [property: JsonPropertyName("puuid")] string Puuid,
    [property: JsonPropertyName("companion")] RiotCompanion Companion,
    [property: JsonPropertyName("traits")] List<RiotTFTTrait> Traits,
    [property: JsonPropertyName("units")] List<RiotTFTUnit> Units,
    [property: JsonPropertyName("riotIdGameName")] string RiotGameName,
    [property: JsonPropertyName("riotIdTagline")] string RiotTagline

);

public record RiotCompanion(
    [property: JsonPropertyName("content_ID")] string ContentId,
    [property: JsonPropertyName("skin_ID")] int SkinId,
    [property: JsonPropertyName("species")] string Species
);

public record RiotTFTTrait(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("num_units")] int NumUnits,
    [property: JsonPropertyName("style")] int Style, // 0=None, 1=Bronze, 2=Silver, 3=Gold, 4=Prismatic
    [property: JsonPropertyName("tier_current")] int TierCurrent,
    [property: JsonPropertyName("tier_total")] int TierTotal
);

public record RiotTFTUnit(
    [property: JsonPropertyName("character_id")] string CharacterId, // e.g. "TFT9_Aatrox"
    [property: JsonPropertyName("itemNames")] List<string> ItemNames,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rarity")] int Rarity,
    [property: JsonPropertyName("tier")] int Tier // Star level (1, 2, or 3)
);