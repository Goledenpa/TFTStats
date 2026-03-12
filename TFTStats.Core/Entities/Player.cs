using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFTStats.Core.Entities
{
    [Table("player")]
    public class Player
    {
        [Key, Column("puuid")] public string Puuid { get; set; } = null!;
        [Column("game_name")] public string? GameName { get; set; } = null!;
        [Column("tagline")] public string? Tagline { get; set; } = null!;
    }
}
