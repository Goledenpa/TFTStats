namespace TFTStats.Core.Service.Cache
{
    public class TraitCacheService
    {
        private readonly Dictionary<string, int> _cache = [];

        public void Initialize(IEnumerable<(string Name, int Id)> traits)
        {
            _cache.Clear();

            foreach (var (Name, Id) in traits)
            {
                Add(Name, Id);
            }
        }

        public int? GetId(string name)
        {
            return _cache.TryGetValue(name, out int id) ? id : null;
        }

        public void Add(string name, int id)
        {
            _cache[name] = id;
        }
    }
}
