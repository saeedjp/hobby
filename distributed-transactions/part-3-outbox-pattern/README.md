# Part 3 — The Outbox Pattern

Related LinkedIn post: [`../posts/post-3.txt`](../posts/post-3.txt)

## The problem it solves

You save an `Order` to your database, then need to publish an
`OrderPlaced` event to a message broker so other services can react.
Two separate systems (DB + broker), so you can't wrap both in one
transaction:

- Save to DB, then publish → if publish fails, the event is lost even
  though the order exists.
- Publish, then save to DB → if the DB write fails, other services
  react to an order that doesn't exist.

## The fix

Write the event into an `OutboxMessages` table in the **same local
transaction** as the business write (`Order`). Both rows commit
together or not at all — that's a single database transaction, not a
distributed one.

A separate background process (`OutboxDispatcher`) polls
`OutboxMessages` for unprocessed rows, publishes them to the broker,
and marks them processed.

## What the code shows

- `OrderService.PlaceOrderAsync` — adds an `Order` and an
  `OutboxMessage` in the same `SaveChangesAsync` call.
- `OutboxDispatcher.DispatchPendingAsync` — reads unprocessed rows,
  calls the injected `publish` function, marks each row processed on
  success, increments a retry count on failure.

## Important caveat

The dispatcher can crash **after** publishing but **before** marking
a row processed — on the next run it will publish that message again.
This pattern only works if consumers are idempotent (dedupe incoming
events by their `Id`). That's a property of the consumer, not
something the outbox table gives you for free.
