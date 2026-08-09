# Cache consistency incident

## Ticket

After a document is updated, most clients invalidate their local aggressive-cache entry immediately, but occasionally one or more clients continue returning the previous value until the cache lifetime expires.

Reported server monitoring shows that the Changes API fan-out completed and no transport error was reported. The issue is more frequent when many clients are connected and document changes occur concurrently. Disabling aggressive caching avoids stale reads, but it is not an acceptable permanent workaround for the affected workload.

## Impact

Applications that rely on long aggressive-cache windows can read a stale document version after another client has already observed the committed update. The data is committed correctly on the server; the visible failure is inconsistent cache invalidation across clients connected to the same database.

## What was tried

- Re-running the same write/read loop without aggressive caching always returns the committed value.
- Lowering concurrency reduces how often the symptom appears but does not explain it.
- Disabling aggressive caching avoids the symptom but removes the intended latency benefit.
- Changes API frames were captured during the workload; see `EVIDENCE/` for the raw observations.

## What the workload does

The workload uses public RavenDB client APIs:

1. Creates or reuses a local database.
2. Opens many independent client stores against the same database.
3. Writes a document with an old value.
4. Primes every client through aggressive caching.
5. Commits a new value through a writer store.
6. Re-reads through aggressive caching until either all clients observe the new value or the iteration deadline expires.

An incident is counted only when at least one aggressive-cache client has observed the committed new value while at least one other aggressive-cache client still returns the old value at the deadline.

The workload can also open raw Changes API WebSocket subscribers to capture the notification payloads observed during the same run. Those subscribers use the public `watch-aggressive-caching` command and do not use test-only hooks. The capture is client-side frame evidence, not server-internal tracing.

## Expected evidence

After a run, `assessment/EVIDENCE/` contains:

- `notification-payloads.txt` — captured cache-invalidation payload shapes per capture connection.
- `changes-connection-capture.log` — Changes API frames and local frame classifications observed by capture sockets.
- `client-cache.log` — accepted/ignored notification classifications and aggressive-cache read outcomes.
- `runtime-and-counters.txt` — runtime settings, work units, incident count and diagnostic counters.
- `support-notes.md` — notes about how to read the evidence.

## How to run

From the repository root:

```powershell
.\assessment\scripts\setup-check.ps1
```

In a second PowerShell window, start an unsecured local server:

```powershell
.\assessment\scripts\start-server.ps1 -Url http://127.0.0.1:8081
```

Then run the workload:

```powershell
.\assessment\scripts\run-incident.ps1 -Url http://127.0.0.1:8081 -Clients 48 -Iterations 20
```

The script exits with code `0` if it observes at least one stale-read incident. Increase `-Clients` and `-Iterations` for a stronger distribution run on slower or less contended machines.
