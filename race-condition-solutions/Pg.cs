using Npgsql;

namespace RaceConditionSolutions;

// Connection + schema helpers for the PostgreSQL scenarios.
public static class Pg
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
        ?? "Host=localhost;Port=5432;Username=demo;Password=demo;Database=demo;Maximum Pool Size=50";

    public static async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    public static async Task EnsureSchemaAsync()
    {
        await using var conn = await OpenAsync();

        // wallets:         plain table. The baseline and most solutions use it.
        // guarded_wallets: same table plus a CHECK constraint (the "let the database enforce it" solution).
        // processed_requests: UNIQUE request ids for the idempotency solution.
        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS wallets (
                id INT PRIMARY KEY,
                balance NUMERIC NOT NULL,
                version INT NOT NULL DEFAULT 0
            )
            """);

        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS guarded_wallets (
                id INT PRIMARY KEY,
                balance NUMERIC NOT NULL,
                CONSTRAINT balance_non_negative CHECK (balance >= 0)
            )
            """);

        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS processed_requests (
                request_id TEXT PRIMARY KEY
            )
            """);
    }

    public static async Task ResetAsync(decimal balance)
    {
        await using var conn = await OpenAsync();

        await ExecuteAsync(conn, "TRUNCATE processed_requests");

        await ExecuteAsync(conn, """
            INSERT INTO wallets (id, balance, version) VALUES (1, @b, 0)
            ON CONFLICT (id) DO UPDATE SET balance = EXCLUDED.balance, version = 0
            """, ("b", balance));

        await ExecuteAsync(conn, """
            INSERT INTO guarded_wallets (id, balance) VALUES (1, @b)
            ON CONFLICT (id) DO UPDATE SET balance = EXCLUDED.balance
            """, ("b", balance));
    }

    public static async Task<decimal> GetBalanceAsync(string table = "wallets")
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT balance FROM {table} WHERE id = 1", conn);
        return (decimal)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
