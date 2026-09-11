# Distributed Transactions — 3-Part Series

Companion code for a 3-post LinkedIn series on handling transactions across
service boundaries in a microservices/.NET backend.

| Part | Topic | Code |
|------|-------|------|
| 1 | Why naive Two-Phase Commit (2PC) breaks down | [`part-1-two-phase-commit/TwoPhaseCommitDemo.cs`](./part-1-two-phase-commit/TwoPhaseCommitDemo.cs) |
| 2 | The Saga pattern (orchestration + compensation) | [`part-2-saga-pattern/OrderSaga.cs`](./part-2-saga-pattern/OrderSaga.cs) |
| 3 | The Outbox pattern (reliable event publishing) | [`part-3-outbox-pattern/OutboxPattern.cs`](./part-3-outbox-pattern/OutboxPattern.cs) |

Each folder has its own README with the full write-up and a runnable
code sample. The `posts/` folder has the exact text used for each
LinkedIn post in the series.

## Why this order

- **Part 1** sets up the problem: why you can't just wrap a distributed
  operation in a transaction the way you would inside a single database.
- **Part 2** shows the most common answer for multi-step business
  workflows: a Saga that moves forward step by step and compensates
  backwards on failure.
- **Part 3** shows the answer to a narrower but very common problem:
  atomically writing to your DB and publishing an event, without a
  two-phase commit across DB and message broker.

None of this code is production-hardened (no retries policies, no
idempotency keys shown beyond a comment, no real message broker) — it's
written to make the mechanics visible for a LinkedIn-post-sized example.
