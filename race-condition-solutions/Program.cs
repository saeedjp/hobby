using System.Diagnostics;
using RaceConditionSolutions;

// Usage:
//   dotnet run                    run every scenario
//   dotnet run -- app             only application-level scenarios (no Docker needed)
//   dotnet run -- db              only PostgreSQL scenarios
//   dotnet run -- dist            only Redis scenarios
//   dotnet run -- db-optimistic   a single scenario by key

var all = Scenarios.All();
var selector = args.FirstOrDefault() ?? "all";

var selected = selector switch
{
    "all" => all,
    "app" or "db" or "dist" => all.Where(s => s.Level == selector).ToList(),
    _ => all.Where(s => s.Key == selector).ToList(),
};

if (selected.Count == 0)
{
    Console.WriteLine($"Unknown scenario '{selector}'. Use: all, app, db, dist, or one of:");
    foreach (var s in all) Console.WriteLine($"  {s.Key}");
    return 1;
}

if (selected.Any(s => s.Level != "app"))
    await Pg.EnsureSchemaAsync();

Console.WriteLine($"{Config.Requests} concurrent withdrawals of {Config.Amount} from a balance of {Config.InitialBalance}.");
Console.WriteLine($"Correct result: {Config.ExpectedSucceeded} succeed, {Config.Requests - Config.ExpectedSucceeded} rejected, balance {Config.ExpectedBalance}.");
Console.WriteLine("(db-idempotency sends only 10 distinct request ids: 10 applied, balance 900.)");
Console.WriteLine();

var failures = 0;

foreach (var s in selected)
{
    var sw = Stopwatch.StartNew();
    var o = await s.Run();
    sw.Stop();

    var correct = o.Succeeded == s.ExpectedSucceeded && o.FinalBalance == s.ExpectedBalance;

    // Baselines are SUPPOSED to be wrong. Seeing them break is the point of the demo.
    string label;
    if (s.Baseline) label = correct ? "no race this run" : "RACE (expected)";
    else label = correct ? "OK" : "WRONG";

    if (!s.Baseline && !correct) failures++;

    var note = o.Note is null ? "" : "  " + o.Note;
    Console.WriteLine(
        $"{s.Key,-18} succeeded={o.Succeeded,-4} rejected={o.Rejected,-4} balance={o.FinalBalance,-7} {sw.ElapsedMilliseconds,5} ms  {label}{note}");
}

return failures == 0 ? 0 : 1;
