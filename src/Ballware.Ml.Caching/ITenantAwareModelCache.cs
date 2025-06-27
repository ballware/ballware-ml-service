namespace Ballware.Ml.Caching;

public interface ITenantAwareModelCache
{
    TItem? GetItem<TItem>(Guid tenantId, string identifier) where TItem : class;
    bool TryGetItem<TItem>(Guid tenantId, string identifier, out TItem? item) where TItem : class;
    void SetItem<TItem>(Guid tenantId, string identifier, TItem value) where TItem : class;
    void PurgeItem(Guid tenantId, string identifier);
}