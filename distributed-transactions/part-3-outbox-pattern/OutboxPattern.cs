// Part 3/3 — Distributed Transactions
// The Outbox pattern: solves "how do I atomically update my DB AND
// publish an event" without a distributed transaction across DB + broker.
// The trick: write the event into a table in the SAME local transaction
// as your business data, then a separate background process reads that
// table and publishes to the broker, marking rows as dispatched.

using Microsoft.EntityFrameworkCore;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    public string Payload { get; set; } = default!;   // JSON-serialized event
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int RetryCount { get; set; }
}

public class Order
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Status { get; set; } = default!;
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}

// The key part: business write + outbox write happen in ONE SaveChanges
// call, so they either both succeed or both fail together. No distributed
// transaction needed — it's a single local transaction against one DB.
public class OrderService
{
    private readonly AppDbContext _db;

    public OrderService(AppDbContext db) => _db = db;

    public async Task PlaceOrderAsync(string customerId, decimal amount)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Amount = amount,
            Status = "Placed"
        };

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "OrderPlaced",
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                order.Id,
                order.CustomerId,
                order.Amount
            }),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Orders.Add(order);
        _db.OutboxMessages.Add(outboxMessage);

        // Single atomic write: order row + outbox row commit together
        // or not at all. The event can never be "lost" relative to the
        // business data, and it can never be published for data that
        // was never actually saved.
        await _db.SaveChangesAsync();
    }
}

// Background dispatcher: polls unprocessed outbox rows and publishes
// them. Runs independently of request handling, and is safe to retry —
// publishing needs to be idempotent on the consumer side (dedupe by
// message Id), since this dispatcher can crash after publish but before
// marking a row processed.
public class OutboxDispatcher
{
    private readonly AppDbContext _db;
    private readonly Func<OutboxMessage, Task> _publish;

    public OutboxDispatcher(AppDbContext db, Func<OutboxMessage, Task> publish)
    {
        _db = db;
        _publish = publish;
    }

    public async Task DispatchPendingAsync(int batchSize = 50)
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync();

        foreach (var message in pending)
        {
            try
            {
                await _publish(message);
                message.ProcessedAtUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                Console.WriteLine($"Failed to publish {message.Id} (attempt {message.RetryCount}): {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();
    }
}

// Example usage (e.g. in a Hangfire recurring job or a hosted background service):
//
// var dispatcher = new OutboxDispatcher(dbContext, async message =>
// {
//     await messageBus.PublishAsync(message.Type, message.Payload);
// });
//
// await dispatcher.DispatchPendingAsync();
