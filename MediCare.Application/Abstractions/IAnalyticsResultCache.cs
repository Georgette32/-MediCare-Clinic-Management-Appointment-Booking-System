namespace MediCare.Application.Abstractions;

/// <summary>
/// Short-lived cache for finished historical analytics. Only closed (before-today) periods may be stored; live results are never cached.
/// </summary>
public interface IAnalyticsResultCache
{
    bool TryGet<T>(string key, out T? value) where T : class;
    void Set<T>(string key, T value, TimeSpan timeToLive) where T : class;
}
