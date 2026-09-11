// Part 1/3 — Distributed Transactions
// Minimal illustration of why a naive Two-Phase Commit (2PC) blocks and
// fails badly under partial failure. This is intentionally simplified —
// no real distributed transaction coordinator would look exactly like this,
// but the failure mode it demonstrates (coordinator crash after Prepare,
// before Commit) is the real reason 2PC is rarely used across microservices.

public interface IResourceParticipant
{
    string Name { get; }
    Task<bool> PrepareAsync();   // "Can you commit if asked?"
    Task CommitAsync();
    Task RollbackAsync();
}

public class InMemoryParticipant : IResourceParticipant
{
    public string Name { get; }
    private bool _prepared;
    private readonly bool _failOnPrepare;

    public InMemoryParticipant(string name, bool failOnPrepare = false)
    {
        Name = name;
        _failOnPrepare = failOnPrepare;
    }

    public Task<bool> PrepareAsync()
    {
        if (_failOnPrepare)
        {
            Console.WriteLine($"[{Name}] Prepare FAILED (simulated).");
            return Task.FromResult(false);
        }

        _prepared = true;
        Console.WriteLine($"[{Name}] Prepared. Resource is now LOCKED until Commit/Rollback.");
        return Task.FromResult(true);
    }

    public Task CommitAsync()
    {
        if (!_prepared) throw new InvalidOperationException("Cannot commit without prepare.");
        Console.WriteLine($"[{Name}] Committed. Lock released.");
        return Task.CompletedTask;
    }

    public Task RollbackAsync()
    {
        Console.WriteLine($"[{Name}] Rolled back. Lock released.");
        return Task.CompletedTask;
    }
}

// The coordinator every 2PC implementation needs. This is the single
// point of failure the pattern depends on.
public class TwoPhaseCommitCoordinator
{
    private readonly List<IResourceParticipant> _participants;

    public TwoPhaseCommitCoordinator(List<IResourceParticipant> participants)
    {
        _participants = participants;
    }

    public async Task<bool> ExecuteAsync(bool simulateCoordinatorCrashAfterPrepare = false)
    {
        // Phase 1: Prepare — every participant locks its resource and
        // reports whether it CAN commit. Nothing is final yet.
        var prepareResults = new List<bool>();
        foreach (var p in _participants)
        {
            prepareResults.Add(await p.PrepareAsync());
        }

        if (prepareResults.Any(ok => !ok))
        {
            Console.WriteLine("At least one participant failed to prepare. Rolling back all.");
            foreach (var p in _participants) await p.RollbackAsync();
            return false;
        }

        if (simulateCoordinatorCrashAfterPrepare)
        {
            // This is the failure mode that makes naive 2PC dangerous:
            // every participant is now holding a lock, waiting for a
            // Commit or Rollback message that will never arrive because
            // the coordinator process died. Resources stay locked until
            // a recovery/timeout mechanism kicks in — if one exists at all.
            Console.WriteLine("!! Coordinator crashed after Prepare, before Commit !!");
            Console.WriteLine("!! Every participant is now blocked holding its lock  !!");
            return false;
        }

        // Phase 2: Commit — only reached if every participant said yes.
        foreach (var p in _participants) await p.CommitAsync();
        return true;
    }
}

// Example usage:
//
// var coordinator = new TwoPhaseCommitCoordinator(new List<IResourceParticipant>
// {
//     new InMemoryParticipant("OrderService"),
//     new InMemoryParticipant("PaymentService"),
//     new InMemoryParticipant("InventoryService"),
// });
//
// await coordinator.ExecuteAsync(simulateCoordinatorCrashAfterPrepare: true);
// -> All three services stay "prepared" (locked) forever. That's the core
//    problem: 2PC trades "eventual inconsistency" for "the whole system
//    can freeze if the coordinator dies at the wrong moment."
