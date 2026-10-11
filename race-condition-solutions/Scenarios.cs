namespace RaceConditionSolutions;

public static class Scenarios
{
    private const int DistinctRequestIds = 10;

    public static List<Scenario> All() => new List<Scenario>
    {
        // ------------------------- application level -------------------------
        new Scenario("app-unsafe", "app", "check-then-act, no protection", true,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            async () =>
            {
                var wallet = new UnsafeWallet(Config.InitialBalance);
                var (ok, rejected) = await Harness.RunAsync(Config.Requests, _ => wallet.WithdrawAsync(Config.Amount));
                return new Outcome(ok, rejected, wallet.Balance);
            }),

        new Scenario("app-mutex", "app", "mutual exclusion (lock / semaphore)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            async () =>
            {
                var wallet = new MutexWallet(Config.InitialBalance);
                var (ok, rejected) = await Harness.RunAsync(Config.Requests, _ => wallet.WithdrawAsync(Config.Amount));
                return new Outcome(ok, rejected, wallet.Balance);
            }),

        new Scenario("app-cas", "app", "atomic compare-and-swap", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            async () =>
            {
                var wallet = new CasWallet(Config.InitialBalance);
                var (ok, rejected) = await Harness.RunAsync(Config.Requests, _ => Task.FromResult(wallet.TryWithdraw(Config.Amount)));
                return new Outcome(ok, rejected, wallet.Balance);
            }),

        new Scenario("app-actor", "app", "single writer (actor / mailbox)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            async () =>
            {
                var wallet = new ActorWallet(Config.InitialBalance);
                var (ok, rejected) = await Harness.RunAsync(Config.Requests, _ => wallet.WithdrawAsync(Config.Amount));
                return new Outcome(ok, rejected, wallet.Balance);
            }),

        // --------------------------- database level --------------------------
        new Scenario("db-naive", "db", "SELECT then UPDATE, no protection", true,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(_ => WalletDb.NaiveWithdrawAsync(Config.Amount))),

        new Scenario("db-atomic-update", "db", "atomic UPDATE ... WHERE balance >= x", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(_ => WalletDb.AtomicUpdateWithdrawAsync(Config.Amount))),

        new Scenario("db-pessimistic", "db", "pessimistic lock (SELECT ... FOR UPDATE)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(_ => WalletDb.PessimisticWithdrawAsync(Config.Amount))),

        new Scenario("db-optimistic", "db", "optimistic lock (version column + retry)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(_ => WalletDb.OptimisticWithdrawAsync(Config.Amount))),

        new Scenario("db-constraint", "db", "CHECK constraint (balance >= 0)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(_ => WalletDb.ConstraintWithdrawAsync(Config.Amount), table: "guarded_wallets")),

        new Scenario("db-serializable", "db", "serializable isolation + retry", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(_ => WalletDb.SerializableWithdrawAsync(Config.Amount))),

        new Scenario("db-advisory-lock", "db", "advisory lock (pg_advisory_xact_lock)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(_ => WalletDb.AdvisoryLockWithdrawAsync(Config.Amount))),

        // 150 requests but only 10 distinct request ids: each id must be applied exactly once.
        new Scenario("db-idempotency", "db", "idempotency key (UNIQUE request id)", false,
            DistinctRequestIds, Config.InitialBalance - DistinctRequestIds * Config.Amount,
            () => RunDb(i => WalletDb.IdempotentWithdrawAsync($"request-{i % DistinctRequestIds}", Config.Amount))),

        // ------------------------ distributed (Redis) level ------------------
        new Scenario("dist-redis-lua", "dist", "atomic script inside Redis (Lua)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            async () =>
            {
                await RedisOps.ResetWalletAsync(Config.InitialBalance);
                var (ok, rejected) = await Harness.RunAsync(Config.Requests, _ => RedisOps.LuaWithdrawAsync(Config.Amount));
                return new Outcome(ok, rejected, await RedisOps.GetWalletAsync());
            }),

        // The unsafe SELECT-then-UPDATE code from db-naive, made safe by a Redis lock around it.
        new Scenario("dist-redis-lock", "dist", "distributed lock (Redis SET NX PX + Lua release)", false,
            Config.ExpectedSucceeded, Config.ExpectedBalance,
            () => RunDb(async _ =>
            {
                await using var redisLock = await RedisLock.AcquireAsync(
                    RedisOps.Db, "lock:wallet:1", ttl: TimeSpan.FromSeconds(10), wait: TimeSpan.FromSeconds(60));

                if (redisLock is null)
                    throw new TimeoutException("Could not acquire the lock in time.");

                return await WalletDb.NaiveWithdrawAsync(Config.Amount);
            })),
    };

    private static async Task<Outcome> RunDb(Func<int, Task<bool>> operation, string table = "wallets")
    {
        await Pg.ResetAsync(Config.InitialBalance);
        Interlocked.Exchange(ref WalletDb.Retries, 0);

        var (ok, rejected) = await Harness.RunAsync(Config.Requests, operation);
        var balance = await Pg.GetBalanceAsync(table);

        var retries = WalletDb.Retries;
        return new Outcome(ok, rejected, balance, retries > 0 ? $"retries={retries}" : null);
    }
}
