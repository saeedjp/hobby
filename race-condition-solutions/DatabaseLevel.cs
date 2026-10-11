using System.Data;
using Npgsql;

namespace RaceConditionSolutions;

// ---------------------------------------------------------------------------
// Database level: shared state inside PostgreSQL, many connections / instances.
// Every method returns true if the withdrawal was applied, false if rejected.
// ---------------------------------------------------------------------------
public static class WalletDb
{
    // Number of optimistic / serializable retries (shown next to the result).
    public static int Retries;

    private static NpgsqlCommand Cmd(
        NpgsqlConnection conn,
        string sql,
        NpgsqlTransaction? tx = null,
        params (string Name, object Value)[] parameters)
    {
        var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    // Baseline: SELECT, check in application code, then UPDATE.
    // Between the SELECT and the UPDATE other connections change the row.
    public static async Task<bool> NaiveWithdrawAsync(decimal amount)
    {
        await using var conn = await Pg.OpenAsync();

        decimal balance;
        await using (var read = Cmd(conn, "SELECT balance FROM wallets WHERE id = 1"))
            balance = (decimal)(await read.ExecuteScalarAsync())!;

        if (balance < amount) return false;

        await using var write = Cmd(conn, "UPDATE wallets SET balance = @b WHERE id = 1", null, ("b", balance - amount));
        await write.ExecuteNonQueryAsync();
        return true;
    }

    // Solution 1: one atomic statement. The check lives inside the UPDATE.
    // PostgreSQL locks the row, re-evaluates the WHERE clause, then updates.
    public static async Task<bool> AtomicUpdateWithdrawAsync(decimal amount)
    {
        await using var conn = await Pg.OpenAsync();
        await using var cmd = Cmd(
            conn,
            "UPDATE wallets SET balance = balance - @a WHERE id = 1 AND balance >= @a",
            null,
            ("a", amount));

        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    // Solution 2: pessimistic locking. SELECT ... FOR UPDATE locks the row
    // until the transaction ends, so everybody else waits in line.
    public static async Task<bool> PessimisticWithdrawAsync(decimal amount)
    {
        await using var conn = await Pg.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        decimal balance;
        await using (var read = Cmd(conn, "SELECT balance FROM wallets WHERE id = 1 FOR UPDATE", tx))
            balance = (decimal)(await read.ExecuteScalarAsync())!;

        if (balance < amount)
        {
            await tx.RollbackAsync();
            return false;
        }

        await using (var write = Cmd(conn, "UPDATE wallets SET balance = @b WHERE id = 1", tx, ("b", balance - amount)))
            await write.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return true;
    }

    // Solution 3: optimistic locking. No lock; a version column detects that
    // somebody else changed the row, and we retry from the beginning.
    public static async Task<bool> OptimisticWithdrawAsync(decimal amount)
    {
        await using var conn = await Pg.OpenAsync();

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            decimal balance;
            int version;

            await using (var read = Cmd(conn, "SELECT balance, version FROM wallets WHERE id = 1"))
            await using (var reader = await read.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                balance = reader.GetDecimal(0);
                version = reader.GetInt32(1);
            }

            if (balance < amount) return false;

            await using var write = Cmd(
                conn,
                "UPDATE wallets SET balance = @b, version = version + 1 WHERE id = 1 AND version = @v",
                null,
                ("b", balance - amount),
                ("v", version));

            if (await write.ExecuteNonQueryAsync() == 1) return true;

            // 0 rows updated: the version moved on. Retry with a small random pause.
            Interlocked.Increment(ref Retries);
            await Task.Delay(Random.Shared.Next(1, 8));
        }

        throw new InvalidOperationException("Too many optimistic retries.");
    }

    // Solution 4: let the database enforce the invariant (CHECK balance >= 0).
    // Last line of defense: even buggy code cannot make the balance negative.
    public static async Task<bool> ConstraintWithdrawAsync(decimal amount)
    {
        await using var conn = await Pg.OpenAsync();

        try
        {
            await using var cmd = Cmd(
                conn,
                "UPDATE guarded_wallets SET balance = balance - @a WHERE id = 1",
                null,
                ("a", amount));

            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return false;
        }
    }

    // Solution 5: serializable isolation. The database detects conflicting
    // transactions and aborts one; the application retries it.
    public static async Task<bool> SerializableWithdrawAsync(decimal amount)
    {
        await using var conn = await Pg.OpenAsync();

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                decimal balance;
                await using (var read = Cmd(conn, "SELECT balance FROM wallets WHERE id = 1", tx))
                    balance = (decimal)(await read.ExecuteScalarAsync())!;

                if (balance < amount)
                {
                    await tx.RollbackAsync();
                    return false;
                }

                await using (var write = Cmd(conn, "UPDATE wallets SET balance = @b WHERE id = 1", tx, ("b", balance - amount)))
                    await write.ExecuteNonQueryAsync();

                await tx.CommitAsync();
                return true;
            }
            catch (PostgresException ex) when (
                ex.SqlState == PostgresErrorCodes.SerializationFailure ||
                ex.SqlState == PostgresErrorCodes.DeadlockDetected)
            {
                Interlocked.Increment(ref Retries);
                await Task.Delay(Random.Shared.Next(1, 8));
            }
        }

        throw new InvalidOperationException("Too many serialization retries.");
    }

    // Solution 6: advisory lock. A named lock managed by PostgreSQL itself,
    // released automatically at the end of the transaction. Works across
    // many application instances without needing Redis.
    public static async Task<bool> AdvisoryLockWithdrawAsync(decimal amount)
    {
        await using var conn = await Pg.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var lockCmd = Cmd(conn, "SELECT pg_advisory_xact_lock(@key)", tx, ("key", 42L)))
            await lockCmd.ExecuteNonQueryAsync();

        // From here on we are alone: the naive read-modify-write is safe.
        decimal balance;
        await using (var read = Cmd(conn, "SELECT balance FROM wallets WHERE id = 1", tx))
            balance = (decimal)(await read.ExecuteScalarAsync())!;

        if (balance < amount)
        {
            await tx.RollbackAsync();
            return false;
        }

        await using (var write = Cmd(conn, "UPDATE wallets SET balance = @b WHERE id = 1", tx, ("b", balance - amount)))
            await write.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return true;
    }

    // Solution 7: idempotency key. A different kind of race: the SAME request
    // arriving more than once (double click, client retry, message redelivery).
    // The UNIQUE request id guarantees it is applied at most once.
    public static async Task<bool> IdempotentWithdrawAsync(string requestId, decimal amount)
    {
        await using var conn = await Pg.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var claim = Cmd(
            conn,
            "INSERT INTO processed_requests (request_id) VALUES (@id) ON CONFLICT DO NOTHING",
            tx,
            ("id", requestId)))
        {
            if (await claim.ExecuteNonQueryAsync() == 0)
            {
                await tx.RollbackAsync();
                return false; // duplicate: already processed (or being processed)
            }
        }

        await using var update = Cmd(
            conn,
            "UPDATE wallets SET balance = balance - @a WHERE id = 1 AND balance >= @a",
            tx,
            ("a", amount));

        if (await update.ExecuteNonQueryAsync() == 0)
        {
            await tx.RollbackAsync(); // also releases the request id, so the client may retry later
            return false;
        }

        await tx.CommitAsync();
        return true;
    }
}
