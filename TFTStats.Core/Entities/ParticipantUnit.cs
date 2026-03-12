using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFTStats.Core.Entities
{
    [Table("participant_unit")]
    public class ParticipantUnit
    {
        [Key, Column("id")] public int Id { get; set; }
        [Column("participant_id")] public int ParticipantId { get; set; }
        [Column("character_id")] public string? CharacterId { get; set; }
        [Column("tier")] public int Tier { get; set; }
        [Column("rarity")] public int Rarity { get; set; }
        [Column("item_1")] public string? Item1 { get; set; }
        [Column("item_2")] public string? Item2 { get; set; }
        [Column("item_3")] public string? Item3 { get; set; }
    }
}
