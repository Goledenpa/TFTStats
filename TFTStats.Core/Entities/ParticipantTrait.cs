using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFTStats.Core.Entities
{
    [Table("participant_trait")]
    public class ParticipantTrait
    {
        [Key, Column("id")] public int Id { get; set; }
        [Column("participant_id")] public int ParticipantId { get; set; }
        [Column("trait_name")] public string? TraitName { get; set; }
        [Column("num_units")] public int NumUnits { get; set; }
        [Column("tier_current")] public int TierCurrent{ get; set; }
        [Column("tier_total")] public int TierTotal{ get; set; }
    }
}
