using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFTStats.Core.Entities
{
    [Table("match")]
    public class Match
    {
        [Key, Column("match_id")] public string MatchId { get; set; } = null!;
        [Column("game_creation")] public long GameCreation { get; set; }
        [Column("game_datetime")] public DateTime GameDateTime { get; set; }
        [Column("game_length")] public float GameLength { get; set; }
        [Column("game_version")] public string? GameVersion { get; set; }
        [Column("set_number")] public int SetNumber { get; set; }
        [Column("queue_id")] public int QueueId { get; set; }

        public List<Participant> Participants { get; set; } = [];
    }
}
