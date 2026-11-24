using MediCare.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace MediCare.Infrastructure.Analytics;

/// <summary>In-process cache for closed historical analytics. Cache misses and eviction are always safe: results are recomputed.</summary>
public sealed class MemoryAnalyticsResultCache(IMemoryCache memoryCache) : IAnalyticsResultCache
{
    public bool TryGet<T>(string key, out T? value) where T : class
    {
        if (memoryCache.TryGetValue(key, out var stored) && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan timeToLive) where T : class =>
        memoryCache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive });
}
