# Support notes

This evidence folder is produced by the cache-consistency workload against a local unsecured single-node server.

The workload uses public RavenDB client APIs for document writes and aggressive-cache reads. The payload capture uses separate public Changes API WebSocket subscribers that issue `watch-aggressive-caching`; it does not use test hooks.

A cache invalidation notification is treated as accepted when its `Value` object contains a non-empty `Reason` and a positive `Generation`. Missing or invalid required fields are counted as `notification_contract_rejected_total`.

Duplicate JSON properties, if they appear, are preserved in `notification-payloads.txt` as captured payload shape. They are not counted as rejected unless a required field is missing or invalid.

`changes-connection-capture.log` and the capture counters are client-side capture observations, not server internals. They are intended to show which notification frames reached independent Changes API subscribers and how those payloads classify under the client-side contract.