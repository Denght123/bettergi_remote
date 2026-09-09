namespace BetterGI.RemoteLite.Security;

public sealed class RequestReplayGuard(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public bool TryAccept(string requestId)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            foreach (var expired in _seen.Where(item => item.Value <= now).Select(item => item.Key).ToArray())
            {
                _seen.Remove(expired);
            }
            if (_seen.ContainsKey(requestId))
            {
                return false;
            }
            _seen[requestId] = now.AddMinutes(10);
            if (_seen.Count > 2048)
            {
                var oldest = _seen.OrderBy(item => item.Value).Take(_seen.Count - 2048).Select(item => item.Key).ToArray();
                foreach (var key in oldest)
                {
                    _seen.Remove(key);
                }
            }
            return true;
        }
    }
}

