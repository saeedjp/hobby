using StackExchange.Redis;

namespace RaceConditionSolutions;

// ---------------------------------------------------------------------------
// Distributed level: many application instances, coordination through Redis.
// ---------------------------------------------------------------------------
public static class RedisOps
{
    private static readonly Lazy<ConnectionMultiplexer> Mux = new(() =>
        ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379"));

    public static IDatabase Db => Mux.Value.GetDatabase();

    private const string WalletKey = "wallet:1";

    // Solution 8: the data store does check + change atomically.
    // Redis runs a Lua script as ONE uninterruptible step.
    private const string WithdrawScript = """
        local balance = tonumber(redis.call('GET', KEYS[1]))
        local amount = tonumber(ARGV[1])
        if balance >= amount then
            redis.call('DECRBY', KEYS[1], amount)
            return 1
        end
        return 0
        """;

    public static async Task ResetWalletAsync(decimal balance)
    {
        await Db.StringSetAsync(WalletKey, (long)balance);
    }

    public static async Task<decimal> GetWalletAsync()
    {
        var value = await Db.StringGetAsync(WalletKey);
        return (long)value;
    }

    public static async Task<bool> LuaWithdrawAsync(decimal amount)
    {
        var result = await Db.ScriptEvaluateAsync(
            WithdrawScript,
            new RedisKey[] { WalletKey },
            new RedisValue[] { (long)amount });

        return (long)result == 1;
    }
}

// Solution 9: distributed lock.
// Acquire = SET key token NX PX ttl  (only one caller can create the key)
// Release = compare the token and delete, in ONE atomic Lua script,
//           so we never delete a lock that expired and now belongs to someone else.
public sealed class RedisLock : IAsyncDisposable
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    private readonly IDatabase _db;
    private readonly string _key;
    private readonly string _token;

    private RedisLock(IDatabase db, string key, string token)
    {
        _db = db;
        _key = key;
        _token = token;
    }

    public static async Task<RedisLock?> AcquireAsync(IDatabase db, string key, TimeSpan ttl, TimeSpan wait)
    {
        var token = Guid.NewGuid().ToString("N"); // unique per owner
        var deadline = DateTime.UtcNow + wait;

        while (DateTime.UtcNow < deadline)
        {
            if (await db.StringSetAsync(key, token, expiry: ttl, when: When.NotExists))
                return new RedisLock(db, key, token);

            await Task.Delay(Random.Shared.Next(5, 20)); // small random pause, then try again
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _db.ScriptEvaluateAsync(
            ReleaseScript,
            new RedisKey[] { _key },
            new RedisValue[] { _token });
    }
}
