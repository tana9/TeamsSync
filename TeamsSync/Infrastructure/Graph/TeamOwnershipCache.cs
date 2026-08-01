using System.Collections.Concurrent;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>ユーザーごとのチーム所有権判定結果を、アプリのセッション内でキャッシュする。</summary>
internal sealed class TeamOwnershipCache
{
    private readonly ConcurrentDictionary<(string UserId, string TeamId), bool> _cache = new();

    /// <summary>キャッシュ済みの判定結果を取得する。</summary>
    public bool TryGet(string userId, string teamId, out bool isOwner) =>
        _cache.TryGetValue((userId, teamId), out isOwner);

    /// <summary>判定結果をキャッシュへ書き込む。</summary>
    public void Set(string userId, string teamId, bool isOwner) => _cache[(userId, teamId)] = isOwner;

    /// <summary>キャッシュを消去する。ユーザーIDを指定するとそのユーザー分のみ消去する。</summary>
    public void Clear(string? userId = null)
    {
        foreach (var key in _cache.Keys)
            if (userId is null || string.Equals(key.UserId, userId, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
    }
}
