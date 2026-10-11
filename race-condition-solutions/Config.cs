namespace RaceConditionSolutions;

// One scenario for every solution, so the approaches are directly comparable:
// a wallet with 1000, and 150 concurrent requests that each try to withdraw 10.
// Only 100 of them can succeed. The other 50 must be rejected, and the balance
// must end at exactly 0 (never negative, never "lost" money).
public static class Config
{
    public const decimal InitialBalance = 1000m;
    public const decimal Amount = 10m;
    public const int Requests = 150;

    public const int ExpectedSucceeded = 100;
    public const decimal ExpectedBalance = 0m;
}

public sealed record Outcome(int Succeeded, int Rejected, decimal FinalBalance, string? Note = null);

public sealed record Scenario(
    string Key,
    string Level,
    string Title,
    bool Baseline,
    int ExpectedSucceeded,
    decimal ExpectedBalance,
    Func<Task<Outcome>> Run);

public static class Harness
{
    // Releases all requests at (almost) the same moment, so the race is real
    // and not just "tasks that happened to start one after another".
    public static async Task<(int Succeeded, int Rejected)> RunAsync(int requests, Func<int, Task<bool>> operation)
    {
        var start = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, requests)
            .Select(i => Task.Run(async () =>
            {
                await start.Task;
                return await operation(i);
            }))
            .ToArray();

        start.SetResult();

        var results = await Task.WhenAll(tasks);
        var succeeded = results.Count(r => r);
        return (succeeded, results.Length - succeeded);
    }
}
