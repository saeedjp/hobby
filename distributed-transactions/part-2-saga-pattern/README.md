# Part 2 — The Saga Pattern

Related LinkedIn post: [`../posts/post-2.txt`](../posts/post-2.txt)

## The idea

Instead of one coordinator locking every participant until everyone
agrees (2PC), a Saga runs a sequence of **local** transactions, one
per service. Each step is fully committed the moment it succeeds —
there's no shared lock across services.

If a later step fails, the Saga doesn't roll back a lock — it runs
**compensating actions** for every step that already completed,
undoing the work forward instead of reversing a transaction.

## Orchestration vs. choreography

This example uses **orchestration**: a central `SagaOrchestrator`
calls each step in order and decides what to compensate. The
alternative is **choreography**, where each service reacts to events
from the previous one and there's no central coordinator — better
decoupling, harder to trace end-to-end.

For most teams starting out, orchestration is easier to reason about
and debug, which is why the example uses it.

## What the code shows

`SagaOrchestrator.RunAsync`:
- Executes `ReserveInventoryStep` → `CapturePaymentStep` → `ConfirmOrderStep`
- Pushes each successful step onto a stack
- On failure, pops the stack and calls `CompensateAsync` on each
  already-completed step, in reverse order

Run it with `CapturePaymentStep(simulateFailure: true)` and you'll see
inventory reserved, then payment fail, then inventory automatically
released — no distributed lock involved anywhere.

## Trade-off to be upfront about

Sagas give you availability and no cross-service locking, but you give
up strict consistency — there's a window where inventory is reserved
but payment hasn't happened yet. Whether that's acceptable depends on
the business, not the pattern.
