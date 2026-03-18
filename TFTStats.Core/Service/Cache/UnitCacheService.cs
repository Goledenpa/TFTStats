using TFTStats.Core.Entities;

namespace TFTStats.Core.Service.Cache
{
    public class UnitCacheService
    {
        private readonly Dictionary<UnitKey, int> _cache = [];

        public void Initialize(IEnumerable<UnitReference> units)
        {
            _cache.Clear();
            foreach (var unit in units)
            {
                Add(unit.CharacterId, unit.Tier, unit.Rarity, unit.Id);
            }
        }

        public int? GetId(string characterId, int tier, int rarity)
        {
            var key = new UnitKey(characterId, tier, rarity);
            return _cache.TryGetValue(key, out int id) ? id : null;
        }

        public void Add(string charId, int tier, int rarity, int dbId)
        {
            _cache[new UnitKey(charId, tier, rarity)] = dbId;
        }
    }
}
