# Cache-change notifications — field investigation

A full RavenDB 7.2 source tree plus one customer incident to investigate.

A fleet of .NET clients uses aggressive caching together with long-lived Changes API connections.
After a document is updated, most clients invalidate their local cache immediately — but occasionally
one or more clients keep returning the previous value until the cache lifetime expires. Server
monitoring says the notification fan-out completed and reports no transport error. It gets worse the
more clients are connected and the more concurrent the writes are. Turning aggressive caching off
avoids the stale reads and is not an acceptable answer.

Something is being delivered and not applied. Your job is to find out what, fix it, and prove the fix.

---

## Prerequisites

- **.NET 10 SDK and runtime**
- **PowerShell 7+** (`pwsh`)
- **Windows.** This is what we build and run the stand on, and the only platform we have verified.
  Nothing here is deliberately Windows-only, but we have not tried it anywhere else — if you want to
  work on another platform, tell us first.
- **A RavenDB license** — step 1 below
- ~2 GB free disk for build output

You do **not** need to build `RavenDB.sln` or run the test suites to start.

---

## Quick start

**1 — Get a license and drop it in.** The server will not start without one. A free developer license
is enough: request one at <https://ravendb.net/license/request/dev> and save the file you receive as
`license.json` in the root of this repository, next to `RavenDB.sln`.

That is the only place the scripts look. `setup-check.ps1` tells you if it is missing and
`start-server.ps1` refuses to start without it, so you will find out immediately rather than halfway
through. `license.json` is git-ignored — you will not commit yours by accident.

**2 — Read the incident.**

[`assessment/INCIDENT.md`](assessment/INCIDENT.md) is the ticket: the reported symptom, the impact,
what was already tried, and the runbook.

**3 — Preflight.** Checks your toolchain and the license, and builds the server and the workload.

```powershell
pwsh assessment/scripts/setup-check.ps1
```

**4 — Start a server.** Leave this running in its own terminal. It listens on
`http://127.0.0.1:8081`, unsecured, no setup wizard.

```powershell
pwsh assessment/scripts/start-server.ps1
```

**5 — Run the workload.** In another terminal:

```powershell
pwsh assessment/scripts/run-incident.ps1
```

Every run rewrites `assessment/EVIDENCE/` from what it observed. If a run reports no incident, it
prints what that does and does not tell you, depending on whether you have changed anything yet.

---

## The reproduction is not deterministic

This is deliberate, and it matches how the problem behaves in production. Each iteration writes a
document, waits for every client to catch up, and counts an **incident** only when at least one client
has already observed the committed new value while another still returns the old one at the deadline.

Which clients go stale, and how many, differs on every run. Turn the dial up for a stronger sample or
on a slower machine:

```powershell
pwsh assessment/scripts/run-incident.ps1 -Clients 96 -Iterations 40
```

If the default run comes back with zero incidents before you have changed anything, raise `-Clients`
and `-Iterations` first. If it keeps coming back clean, stop retrying and tell us — we will sort it
out with you.

---

## What is in the package

| Path | What it is |
| --- | --- |
| [`assessment/INCIDENT.md`](assessment/INCIDENT.md) | The ticket, the impact, what was already tried, and the runbook |
| `assessment/EVIDENCE/notification-payloads.txt` | Cache-invalidation payloads captured per subscriber connection |
| `assessment/EVIDENCE/changes-connection-capture.log` | Changes API frames and their local classification, per capture socket |
| `assessment/EVIDENCE/client-cache.log` | Which notifications were applied or ignored, and the aggressive-cache read outcome per iteration |
| `assessment/EVIDENCE/runtime-and-counters.txt` | Runtime, workload settings, work units, incident count, diagnostic counters |
| `assessment/EVIDENCE/support-notes.md` | What each artifact does and does not establish — read this before drawing conclusions from the counters |
| `assessment/cache-consistency-check/` | The workload. A plain console application using public client APIs only |
| `assessment/scripts/` | Preflight, server, and the reproduction workload |
| `src/`, `test/` | The RavenDB source tree, unmodified except for what the incident touches |

The workload builds against `Raven.Client` **from this tree**, so a change you make to the client is
picked up by the next run. Its payload capture uses ordinary Changes API WebSocket subscribers issuing
the public `watch-aggressive-caching` command — there are no test hooks in the reproduction path.

---

## What we need from you

1. **Reproduce.** Record the observed result and the exact command you used.
2. **Investigate.** Trace notification construction, serialization, fan-out, transport completion,
   client processing and cache invalidation. Say which subsystem owns each transition.
3. **Diagnose.** Explain how successful transport delivery can coexist with a client that keeps stale
   data. Separate what you confirmed from what you inferred, and say what is still uncertain.
4. **Fix.** The smallest production-quality change you can justify. It must preserve parallel
   notification fan-out and must not disable aggressive caching.
5. **Prove.** Automated proof covering multiple recipients, repeated delivery, parallel execution and
   failure isolation. Assert the semantic cache consequence, not just that a message was sent.
6. **Communicate.** A professional reply to the customer: what was delivered, what was ignored, the
   resulting risk, and the next verification step.

The diagnostic counters in `EVIDENCE/` are leads, not conclusions. Part of the exercise is explaining
what they support, what they do not prove, and which additional observation would discriminate between
competing explanations.

There is no time limit. AI tools, web search, documentation and public source may be used; briefly
state what you used and how you verified it. A technical follow-up conversation is part of the
process.

---

## Notes and troubleshooting

- The scripts use `dotnet` by default. To use a specific SDK, pass `-DotNetPath` or set `RAVEN_DOTNET`
  for the current shell.
- If `MSBuildSDKsPath` is set in your environment, the scripts clear it **for their own process only**
  and print why. Your environment is not modified.
- Build output (`bin/`, `obj/`) and server data are not shipped; the scripts create them.
- Step 4 rewrites `assessment/EVIDENCE/` from your own run. The captures we shipped are there as a
  starting point, not as ground truth. Pass `-EvidenceDir` to write elsewhere and keep ours intact.
