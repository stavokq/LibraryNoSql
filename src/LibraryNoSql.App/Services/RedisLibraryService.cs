using System.Text.Json;
using LibraryNoSql.App.Models;
using LibraryNoSql.App.Repositories;
using StackExchange.Redis;

namespace LibraryNoSql.App.Services;

public class RedisLibraryService : IAsyncDisposable
{
    private readonly string _configuration;
    private ConnectionMultiplexer? _mux;

    public RedisLibraryService(string configuration)
    {
        _configuration = configuration;
    }

    private async Task<IDatabase> GetDbAsync()
    {
        _mux ??= await ConnectionMultiplexer.ConnectAsync(_configuration);
        return _mux.GetDatabase();
    }

    public async Task RunDemoAsync(IMediaCatalogRepository sourceRepo, string sampleItemId)
    {
        var _db = await GetDbAsync();
        int readerId = 17;
        string sessionKey = $"lib:session:s{readerId}:v1";
        string cacheKey = $"lib:card:{sampleItemId}:v1";
        string cartKey = $"lib:holdcart:u{readerId}:v1";
        string recentKey = $"lib:recent:u{readerId}:v1";
        string tagsKeyA = $"lib:tags:{sampleItemId}:v1";
        string tagsKeyB = "lib:tags:item-0000002:v1";
        string ratingKey = "lib:rating:weekly:v1";
        string rateKey = $"lib:ratelimit:u{readerId}:{DateTime.UtcNow:yyyyMMddHHmm}:v1";

        await _db.KeyDeleteAsync([sessionKey, cacheKey, cartKey, recentKey, tagsKeyA, tagsKeyB, ratingKey, rateKey]);

        await _db.StringSetAsync(
            sessionKey,
            JsonSerializer.Serialize(new { readerId, cardNumber = "LIB-2026-017", role = "reader" }),
            TimeSpan.FromMinutes(30));
        var sessionVal = await _db.StringGetAsync(sessionKey);
        var sessionTtl = await _db.KeyTimeToLiveAsync(sessionKey);
        Console.WriteLine($"Сесія: {sessionKey} -> {sessionVal} (TTL: {sessionTtl?.TotalSeconds:F0} с)");

        var firstRead = await GetCardCacheAsideAsync(cacheKey, sampleItemId, sourceRepo);
        var secondRead = await GetCardCacheAsideAsync(cacheKey, sampleItemId, sourceRepo);
        Console.WriteLine($"Кеш картки: 1-й запит = {(firstRead.FromCache ? "HIT" : "MISS")}, 2-й запит = {(secondRead.FromCache ? "HIT" : "MISS")} ({secondRead.Card?.Title})");

        await _db.HashSetAsync(cartKey,
        [
            new HashEntry(sampleItemId, 1),
            new HashEntry("item-0000002", 2),
            new HashEntry("version", 1)
        ]);
        await _db.KeyExpireAsync(cartKey, TimeSpan.FromMinutes(20));

        var txOk = _db.CreateTransaction();
        txOk.AddCondition(Condition.HashEqual(cartKey, "version", 1));
        _ = txOk.HashIncrementAsync(cartKey, sampleItemId, 1);
        _ = txOk.HashIncrementAsync(cartKey, "version", 1);
        bool okCommitted = await txOk.ExecuteAsync();

        var txStale = _db.CreateTransaction();
        txStale.AddCondition(Condition.HashEqual(cartKey, "version", 1));
        _ = txStale.HashDeleteAsync(cartKey, sampleItemId);
        bool staleCommitted = await txStale.ExecuteAsync();

        var cartEntries = await _db.HashGetAllAsync(cartKey);
        string cartDump = string.Join(", ", cartEntries.Select(e => $"{e.Name}={e.Value}"));
        Console.WriteLine($"Кошик {cartKey}: [{cartDump}] | оновлення v1: {okCommitted}, повтор зі старою v1: {staleCommitted}");

        await _db.ListLeftPushAsync(recentKey, ["item-0000005", "item-0000003", sampleItemId]);
        await _db.ListTrimAsync(recentKey, 0, 9);
        var recent = await _db.ListRangeAsync(recentKey, 0, 9);
        Console.WriteLine($"Останні перегляди: {string.Join(" -> ", recent.Select(x => x.ToString()))}");

        await _db.SetAddAsync(tagsKeyA, ["комп-науки", "практикум", "бестселер"]);
        await _db.SetAddAsync(tagsKeyB, ["комп-науки", "алгоритми", "практикум"]);
        bool hasTag = await _db.SetContainsAsync(tagsKeyA, "бестселер");
        var commonTags = await _db.SetCombineAsync(SetOperation.Intersect, tagsKeyA, tagsKeyB);
        Console.WriteLine($"Теги: наявність 'бестселер' = {hasTag} | спільні: {string.Join(", ", commonTags.Select(x => x.ToString()))}");

        await _db.SortedSetAddAsync(ratingKey,
        [
            new SortedSetEntry(sampleItemId, 120),
            new SortedSetEntry("item-0000002", 95),
            new SortedSetEntry("item-0000003", 110)
        ]);
        await _db.SortedSetIncrementAsync(ratingKey, "item-0000002", 40);
        var top = await _db.SortedSetRangeByRankWithScoresAsync(ratingKey, 0, 2, Order.Descending);
        Console.WriteLine($"Рейтинг: {string.Join(" | ", top.Select((e, idx) => $"#{idx + 1} {e.Element} ({e.Score})"))}");

        int allowed = 0;
        int blocked = 0;
        for (int req = 0; req < 12; req++)
        {
            if (await AllowRequestAsync(rateKey, limitPerMinute: 10))
            {
                allowed++;
            }
            else
            {
                blocked++;
            }
        }
        Console.WriteLine($"Ліміт запитів (10/хв, надіслано 12): дозволено = {allowed}, відхилено = {blocked}");
    }

    private async Task<(CatalogCard? Card, bool FromCache)> GetCardCacheAsideAsync(
        string cacheKey,
        string itemId,
        IMediaCatalogRepository repo)
    {
        var _db = await GetDbAsync();
        var cached = await _db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            var deserialized = JsonSerializer.Deserialize<CatalogCard>((string)cached!);
            return (deserialized, true);
        }

        var card = await repo.GetByIdAsync(itemId);
        if (card is not null)
        {
            int ttlWithJitterSec = 300 + Random.Shared.Next(0, 61);
            await _db.StringSetAsync(cacheKey, JsonSerializer.Serialize(card), TimeSpan.FromSeconds(ttlWithJitterSec));
        }
        return (card, false);
    }

    private async Task<bool> AllowRequestAsync(string rateKey, int limitPerMinute)
    {
        var _db = await GetDbAsync();
        long count = await _db.StringIncrementAsync(rateKey);
        if (count == 1)
        {
            await _db.KeyExpireAsync(rateKey, TimeSpan.FromSeconds(60));
        }
        return count <= limitPerMinute;
    }

    public async ValueTask DisposeAsync()
    {
        if (_mux is not null)
        {
            await _mux.DisposeAsync();
        }
    }
}
