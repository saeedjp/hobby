# Race condition solutions (C#, PostgreSQL, Redis)

Companion code for the LinkedIn post about preventing race conditions at the
application, database and distributed level.

Every solution is tested against the same scenario, so they are directly comparable:

> A wallet holds 1000. 150 requests try to withdraw 10 at the same moment.
> Correct result: **100 succeed, 50 are rejected, final balance 0**
> (never negative, never lost money).

The baselines (`app-unsafe`, `db-naive`) are meant to break. Everything else must end with the correct result.

## Run it

Everything (Postgres + Redis + the demo app):

```
docker compose up --build --abort-on-container-exit
```

One scenario, or one level:

```
docker compose run --rm app db-optimistic
docker compose run --rm app app      # application level only
docker compose run --rm app db       # database level only
docker compose run --rm app dist     # Redis level only
```

Without Docker for the app itself (needs the Postgres/Redis containers running, e.g. `docker compose up postgres redis`):

```
dotnet run -- all
dotnet run -- app     # in-process scenarios, needs no infrastructure at all
```

## The solutions

| Key | Level | Idea | Main trade-off |
|---|---|---|---|
| `app-unsafe` | app | baseline: check, then act | breaks |
| `app-mutex` | app | one request at a time in the critical section | one process only, serializes everything |
| `app-cas` | app | atomic compare-and-swap | only when check + change fit in ONE atomic operation |
| `app-actor` | app | one writer processes messages in order | extra latency, mailbox to manage |
| `db-naive` | db | baseline: SELECT, check, UPDATE | breaks |
| `db-atomic-update` | db | `UPDATE ... WHERE balance >= x` | the check must fit in one statement |
| `db-pessimistic` | db | `SELECT ... FOR UPDATE` | waiting, deadlock risk |
| `db-optimistic` | db | `version` column + retry | many retries under heavy contention (shown in the output) |
| `db-constraint` | db | `CHECK (balance >= 0)` | last line of defense, not a flow control |
| `db-serializable` | db | serializable isolation + retry | aborted transactions must be retried |
| `db-advisory-lock` | db | `pg_advisory_xact_lock` | a lock again, but managed by the database |
| `db-idempotency` | db | UNIQUE request id | solves duplicates, not concurrent different requests |
| `dist-redis-lua` | dist | check + change in one Redis Lua script | state must live in Redis |
| `dist-redis-lock` | dist | `SET NX PX` lock + atomic release | TTL can expire mid-operation (see below) |

`db-idempotency` is a different kind of race: the same request arriving several
times (double click, client retry, message redelivery). It sends 150 requests
with only 10 distinct ids, so the correct result is 10 applied and balance 900.

## Things the code does not cover (but you should know)

- **Lock expiry.** If the work takes longer than the lock TTL, two owners can
  be inside at the same time. A fencing token (the storage layer rejects
  writes carrying an older token) is the real fix; the TTL only limits the damage.
- **Redis failover.** A single Redis instance lock is not safe if a failover can
  lose the key. That is what Redlock (and its criticism) is about.
- **Partitioning by key.** Sending all operations for one wallet to the same
  consumer (for example a Kafka partition key) removes the race by design,
  like the actor, but across processes. Not implemented here.

## Notes

- `db-naive` and `app-unsafe` can occasionally look correct on a lucky run.
  That is exactly why race conditions are dangerous: tests pass, production fails.
- The numbers in the output (milliseconds, retries) depend on your machine.
  They are for comparing the shape of the trade-offs, not a benchmark.
