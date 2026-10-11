using System.Threading.Channels;

namespace RaceConditionSolutions;

// ---------------------------------------------------------------------------
// Application level: shared state inside ONE process.
// ---------------------------------------------------------------------------

// Baseline: check-then-act with no protection.
// Many requests pass the balance check before any of them subtracts.
public sealed class UnsafeWallet
{
    private decimal _balance;

    public UnsafeWallet(decimal initial) => _balance = initial;

    public decimal Balance => _balance;

    public async Task<bool> WithdrawAsync(decimal amount)
    {
        if (_balance < amount) return false;   // check

        await Task.Delay(1);                   // real code does work here (fraud check, I/O...)

        _balance -= amount;                    // act (the state may have changed since the check)
        return true;
    }
}

// Solution 1: mutual exclusion.
// Only one request at a time is inside the critical section.
// (SemaphoreSlim instead of lock, because we await inside the critical section.)
public sealed class MutexWallet
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private decimal _balance;

    public MutexWallet(decimal initial) => _balance = initial;

    public decimal Balance => _balance;

    public async Task<bool> WithdrawAsync(decimal amount)
    {
        await _mutex.WaitAsync();
        try
        {
            if (_balance < amount) return false;
            await Task.Delay(1);
            _balance -= amount;
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }
}

// Solution 2: atomic compare-and-swap (lock-free).
// Works only when "check + change" fits into ONE atomic operation on ONE value.
// Money is stored as cents in a long because Interlocked does not support decimal.
public sealed class CasWallet
{
    private long _cents;

    public CasWallet(decimal initial) => _cents = (long)(initial * 100);

    public decimal Balance => Volatile.Read(ref _cents) / 100m;

    public bool TryWithdraw(decimal amount)
    {
        var amountCents = (long)(amount * 100);

        while (true)
        {
            var current = Volatile.Read(ref _cents);
            if (current < amountCents) return false;

            // Succeeds only if nobody changed the value since we read it.
            if (Interlocked.CompareExchange(ref _cents, current - amountCents, current) == current)
                return true;

            // Someone else won the race: read again and retry.
        }
    }
}

// Solution 3: single writer (actor / mailbox).
// Nobody touches the state directly. Everybody sends a message,
// and ONE loop processes the messages one at a time.
public sealed class ActorWallet
{
    private readonly Channel<(decimal Amount, TaskCompletionSource<bool> Reply)> _inbox =
        Channel.CreateUnbounded<(decimal Amount, TaskCompletionSource<bool> Reply)>(
            new UnboundedChannelOptions { SingleReader = true });

    private decimal _balance;

    public ActorWallet(decimal initial)
    {
        _balance = initial;
        _ = Task.Run(ProcessAsync);
    }

    public decimal Balance => _balance;

    public Task<bool> WithdrawAsync(decimal amount)
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inbox.Writer.TryWrite((amount, reply));
        return reply.Task;
    }

    private async Task ProcessAsync()
    {
        await foreach (var (amount, reply) in _inbox.Reader.ReadAllAsync())
        {
            if (_balance < amount)
            {
                reply.SetResult(false);
                continue;
            }

            await Task.Delay(1);
            _balance -= amount;
            reply.SetResult(true);
        }
    }
}
