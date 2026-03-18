using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFTStats.Core.Entities
{
    [Table("participant")]
    public class Participant
    {
        [Key, Column("id")] public string Id { get; set; } = null!;
        [Column("match_id")] public string MatchId { get; set; } = null!;
        [Column("puuid")] public string Puuid { get; set; } = null!;
        [Column("placement")] public int Placement { get; set; }
        [Column("level")] public int Level { get; set; }
        [Column("gold_left")] public int GoldLeft { get; set; }
        [Column("last_round")] public int LastRound { get; set; }
        [Column("time_eliminated")] public float TimeEliminated{ get; set; }
        [Column("platers_eliminated")] public int PlayersEliminated{ get; set; }
        [Column("total_damage_to_players")] public int TotalDamageToPlayers{ get; set; }
        [Column("companion_species")] public string? CompanionSpecies{ get; set; }
        [Column("companion_skin_id")] public int CompanionSkinId{ get; set; }

        public string RiotGameName { get; set; } = null!;
        public string RiotTagLine { get; set; } = null!;


        public List<ParticipantUnit> Units { get; set; } = [];
        public List<ParticipantTrait> Traits { get; set; } = [];
    }
}
