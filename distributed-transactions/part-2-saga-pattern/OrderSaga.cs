// Part 2/3 — Distributed Transactions
// Orchestration-based Saga: instead of one coordinator locking everyone
// (2PC), each step is a real local transaction. If a later step fails,
// we run compensating actions for everything that already succeeded —
// undoing forward, not rolling back a shared lock.

public record OrderContext(Guid OrderId, decimal Amount, string CustomerId)
{
    public bool InventoryReserved { get; set; }
    public bool PaymentCaptured { get; set; }
    public bool OrderConfirmed { get; set; }
}

public interface ISagaStep
{
    string Name { get; }
    Task ExecuteAsync(OrderContext context);
    Task CompensateAsync(OrderContext context);
}

public class ReserveInventoryStep : ISagaStep
{
    public string Name => nameof(ReserveInventoryStep);

    public Task ExecuteAsync(OrderContext context)
    {
        Console.WriteLine($"[{Name}] Reserving inventory for order {context.OrderId}");
        context.InventoryReserved = true;
        return Task.CompletedTask;
    }

    public Task CompensateAsync(OrderContext context)
    {
        if (!context.InventoryReserved) return Task.CompletedTask;
        Console.WriteLine($"[{Name}] Releasing reserved inventory for order {context.OrderId}");
        context.InventoryReserved = false;
        return Task.CompletedTask;
    }
}

public class CapturePaymentStep : ISagaStep
{
    private readonly bool _simulateFailure;
    public string Name => nameof(CapturePaymentStep);

    public CapturePaymentStep(bool simulateFailure = false) => _simulateFailure = simulateFailure;

    public Task ExecuteAsync(OrderContext context)
    {
        if (_simulateFailure)
        {
            Console.WriteLine($"[{Name}] Payment capture FAILED for order {context.OrderId}");
            throw new InvalidOperationException("Payment gateway declined the charge.");
        }

        Console.WriteLine($"[{Name}] Captured {context.Amount:C} for order {context.OrderId}");
        context.PaymentCaptured = true;
        return Task.CompletedTask;
    }

    public Task CompensateAsync(OrderContext context)
    {
        if (!context.PaymentCaptured) return Task.CompletedTask;
        Console.WriteLine($"[{Name}] Refunding {context.Amount:C} for order {context.OrderId}");
        context.PaymentCaptured = false;
        return Task.CompletedTask;
    }
}

public class ConfirmOrderStep : ISagaStep
{
    public string Name => nameof(ConfirmOrderStep);

    public Task ExecuteAsync(OrderContext context)
    {
        Console.WriteLine($"[{Name}] Confirming order {context.OrderId}");
        context.OrderConfirmed = true;
        return Task.CompletedTask;
    }

    public Task CompensateAsync(OrderContext context)
    {
        if (!context.OrderConfirmed) return Task.CompletedTask;
        Console.WriteLine($"[{Name}] Un-confirming order {context.OrderId}");
        context.OrderConfirmed = false;
        return Task.CompletedTask;
    }
}

// The orchestrator: runs steps in order, and on failure walks backwards
// through everything that already succeeded, compensating each one.
public class SagaOrchestrator
{
    private readonly List<ISagaStep> _steps;

    public SagaOrchestrator(List<ISagaStep> steps) => _steps = steps;

    public async Task<bool> RunAsync(OrderContext context)
    {
        var completed = new Stack<ISagaStep>();

        foreach (var step in _steps)
        {
            try
            {
                await step.ExecuteAsync(context);
                completed.Push(step);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Step '{step.Name}' failed: {ex.Message}. Starting compensation.");

                while (completed.Count > 0)
                {
                    var doneStep = completed.Pop();
                    await doneStep.CompensateAsync(context);
                }

                return false;
            }
        }

        return true;
    }
}

// Example usage:
//
// var orchestrator = new SagaOrchestrator(new List<ISagaStep>
// {
//     new ReserveInventoryStep(),
//     new CapturePaymentStep(simulateFailure: true), // force the failure path
//     new ConfirmOrderStep(),
// });
//
// var context = new OrderContext(Guid.NewGuid(), 149.99m, "customer-123");
// var success = await orchestrator.RunAsync(context);
// -> Inventory gets reserved, payment fails, inventory reservation gets
//    released automatically. No shared lock, no frozen coordinator —
//    just an explicit undo path for each step.
