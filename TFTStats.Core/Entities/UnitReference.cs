namespace TFTStats.Core.Entities
{
    public record UnitKey(string CharacterId, int Tier, int Rarity);

    public class UnitReference
    {
        public int Id { get; set; }
        public string CharacterId { get; set; } = null!;
        public int Tier { get; set; }
        public int Rarity { get; set; }
    }
}
