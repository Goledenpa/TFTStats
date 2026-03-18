using TFTStats.Core.Entities;
using TFTStats.Core.Models;

namespace TFTStats.Core.Mappings
{
    public static class RiotMapper
    {
        public static Match ToEntity(this RiotTFTMatch dto)
        {
            var matchId = dto.Metadata.MatchId;

            return new Match
            {
                MatchId = matchId,
                GameDateTime = DateTimeOffset.FromUnixTimeMilliseconds(dto.Info.GameDatetime).UtcDateTime,
                GameLength = dto.Info.GameLength,
                GameVersion = dto.Info.GameVersion,
                SetNumber = dto.Info.TftSetNumber,
                QueueId = dto.Info.QueueId,
                Participants = dto.Info.Participants
                .Where(x => x.Puuid != "BOT")
                .Select(p => new Participant
                {
                    Id = $"{matchId}_{p.Puuid}",
                    MatchId = matchId,
                    Puuid = p.Puuid,
                    Placement = p.Placement,
                    Level = p.Level,
                    GoldLeft = p.GoldLeft,
                    LastRound = p.LastRound,
                    TimeEliminated = p.TimeEliminated,
                    PlayersEliminated = p.PlayersEliminated,
                    TotalDamageToPlayers = p.TotalDamageToPlayers,
                    CompanionSpecies = p.Companion.Species,
                    CompanionSkinId = p.Companion.SkinId,
                    Units = p.Units.Select(u => new ParticipantUnit
                    {
                        CharacterId = u.CharacterId,
                        Tier = u.Tier,
                        Rarity = u.Rarity,
                        Items = u.ItemNames?.OrderBy(x => x).ToArray() ?? []
                    }).ToList(),
                    Traits = p.Traits.Select(t => new ParticipantTrait
                    {
                        TraitName = t.Name,
                        NumUnits = t.NumUnits,
                        TierCurrent = t.TierCurrent,
                        TierTotal = t.TierTotal,
                    }).ToList(),
                    RiotGameName = p.RiotGameName,
                    RiotTagLine = p.RiotTagline
                }).ToList()
            };
        }
    }
}
