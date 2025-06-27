using Ballware.Ml.Caching.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballware.Ml.Caching.Internal;

public class InMemoryTenantAwareModelCache : ITenantAwareModelCache
{
    private ILogger<InMemoryTenantAwareModelCache> Logger { get; }
    private IMemoryCache Cache { get; }
    private CacheOptions Options { get; }
    
    private static string BuildKey(Guid tenantId, string identifier)
    {
        return $"{tenantId}_{identifier}".ToLowerInvariant();
    }
    
    public InMemoryTenantAwareModelCache(ILogger<InMemoryTenantAwareModelCache> logger, IMemoryCache cache, IOptions<CacheOptions> options)
    {
        Logger = logger;
        Cache = cache;
        Options = options.Value;
    }

    public TItem? GetItem<TItem>(Guid tenantId, string identifier) where TItem : class
    {
        var cachedItem = Cache.Get<TItem>(BuildKey(tenantId, identifier));
        
        if (cachedItem != null)
        {
            Logger.LogDebug("Cache hit for {BuildKey}", BuildKey(tenantId, identifier));
            return cachedItem;
        }
        
        Logger.LogDebug("Cache miss for {BuildKey}", BuildKey(tenantId, identifier));
        return null;
    }

    public bool TryGetItem<TItem>(Guid tenantId, string identifier, out TItem? item) where TItem : class
    {
        item = GetItem<TItem>(tenantId, identifier);

        return item != null;
    }

    public void SetItem<TItem>(Guid tenantId, string identifier, TItem value) where TItem : class
    {
        Cache.Set(BuildKey(tenantId, identifier), value, new MemoryCacheEntryOptions()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Options.CacheExpirationHours)
        });
        
        Logger.LogDebug("Cache update for {BuildKey}", BuildKey(tenantId, identifier));
    }

    public void PurgeItem(Guid tenantId, string identifier)
    {
        Cache.Remove(BuildKey(tenantId, identifier));
        
        Logger.LogDebug("Cache purge for {BuildKey}", BuildKey(tenantId, identifier));
    }
}