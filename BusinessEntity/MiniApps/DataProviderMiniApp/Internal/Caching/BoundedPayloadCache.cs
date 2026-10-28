
namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;

// Хранит только неизменяемые raw payload с двумя жёсткими лимитами и LRU-вытеснением.
internal sealed class BoundedPayloadCache
{
    private readonly object _sync = new();
    private readonly Dictionary<PayloadCacheKey, LinkedListNode<(PayloadCacheKey Key, CachedPayload Value)>> _entries = new();
    private readonly LinkedList<(PayloadCacheKey Key, CachedPayload Value)> _lru = new();
    private long _bytes, _hits, _misses, _evictions;
    public long LimitBytes { get; }
    public int LimitEntries { get; }
    // Лимиты задаются явно; нулевое значение полностью выключает удержание payload.
    public BoundedPayloadCache(IConfiguration configuration)
    {
        LimitBytes = configuration.GetValue<long>("DataProvider:PayloadCacheMaxBytes", 32L * 1024 * 1024);
        LimitEntries = configuration.GetValue("DataProvider:PayloadCacheMaxEntries", 128);
        if (LimitBytes < 0 || LimitEntries < 0) throw new ArgumentOutOfRangeException(nameof(configuration), "Лимиты кэша не могут быть отрицательными.");
    }
    // Возвращает raw record, который вызывающий код заново материализует в собственный runtime-объект.
    public CachedPayload? Get(PayloadCacheKey key)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry)) { _misses++; return null; }
            _lru.Remove(entry); _lru.AddFirst(entry); _hits++; return entry.Value.Value;
        }
    }
    // Не удерживает слишком большой объект и не допускает превышения суммарного бюджета.
    public void Put(PayloadCacheKey key, CachedPayload value)
    {
        lock (_sync)
        {
            if (value.SizeBytes > LimitBytes || LimitEntries == 0) return;
            if (_entries.Remove(key, out var previous)) { _lru.Remove(previous); _bytes -= previous.Value.Value.SizeBytes; }
            var entry = _lru.AddFirst((key, value)); _entries.Add(key, entry); _bytes += value.SizeBytes;
            while (_entries.Count > LimitEntries || _bytes > LimitBytes)
            {
                var last = _lru.Last!; _lru.RemoveLast(); _entries.Remove(last.Value.Key);
                _bytes -= last.Value.Value.SizeBytes; _evictions++;
            }
        }
    }
    // Убирает записи изменённых владельцев после коммита; generation key защищает от позднего attach.
    public void Invalidate(IReadOnlySet<Guid> ids)
    {
        lock (_sync)
            foreach (var key in _entries.Keys.Where(x => ids.Contains(x.EntityId)).ToArray())
            {
                var entry = _entries[key]; _entries.Remove(key); _lru.Remove(entry); _bytes -= entry.Value.Value.SizeBytes;
            }
    }
    // Возвращает согласованные показатели удерживаемой памяти и обращений.
    public (int Entries, long Bytes, long Hits, long Misses, long Evictions) Statistics()
    { lock (_sync) return (_entries.Count, _bytes, _hits, _misses, _evictions); }
}
// Поколение относится к владельцу: позднее чтение v1 mutable-типа не может вернуть к жизни старый cache entry.
internal readonly record struct PayloadCacheKey(Guid EntityId, int Version, long Generation);
// Raw snapshot содержит только данные строки; указатель публикации берётся из актуального графа.
internal sealed record CachedPayload(string Json, string Name, int Version, DateTime CreatedDate, DateTime LastModifiedDate)
{
    public long SizeBytes => ((long)Json.Length + Name.Length) * 2L + 320L;
}
