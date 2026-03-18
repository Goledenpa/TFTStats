using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFTStats.Core.Entities
{
    [Table("participant_unit")]
    public class ParticipantUnit
    {
        [Key, Column("id")] public int Id { get; set; }
        public int UnitRefId { get; set; }
        [Column("participant_id")] public string ParticipantId { get; set; } = null!;
        [Column("character_id")] public string CharacterId { get; set; } = null!;
        [Column("tier")] public int Tier { get; set; }
        [Column("rarity")] public int Rarity { get; set; }
        public string[] Items { get; set; } = [];
    }
}
