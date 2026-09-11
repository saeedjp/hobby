# Part 1 — Why Two-Phase Commit Breaks Down

Related LinkedIn post: [`../posts/post-1.txt`](../posts/post-1.txt)

## The problem

A single database gives you ACID for free. The moment a "transaction"
spans multiple services (Order, Payment, Inventory, each with their own
database), you lose that guarantee — there's no single engine that can
commit or roll back everything atomically.

## Two-Phase Commit (2PC)

The classic textbook answer is a coordinator that runs two phases:

1. **Prepare** — every participant locks its resource and confirms
   "yes, I can commit this if you ask me to."
2. **Commit** — if everyone said yes, the coordinator tells everyone
   to commit. If anyone said no, everyone rolls back.

## Why it doesn't hold up in practice

- Every participant holds a lock for the entire Prepare→Commit window.
  Under load, that's a lot of contention.
- If the coordinator dies **after** Prepare but **before** Commit,
  every participant is stuck holding its lock indefinitely — see
  `simulateCoordinatorCrashAfterPrepare` in the code.
- It assumes all participants are reachable and fast. One slow
  participant blocks the whole transaction.
- Most message brokers and many databases don't support the XA
  protocol 2PC needs, so it often isn't even available as an option
  across the exact services you have.

Run the code with `simulateCoordinatorCrashAfterPrepare: true` and
watch every participant print "Prepared... LOCKED" and then nothing —
that's the failure mode in one paragraph.

This is why most real systems use the **Saga pattern** (Part 2) or the
**Outbox pattern** (Part 3) instead of trying to force a distributed
ACID transaction.
