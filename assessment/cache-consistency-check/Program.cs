using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

namespace CacheConsistencyCheck;

internal static class Program
{
    private const string DocumentPrefix = "cache-consistency-check/users/";
    private const string NotificationType = "CacheInvalidation";
    private const string ReasonFieldName = "Reason";
    private const string GenerationFieldName = "Generation";

    public static async Task<int> Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Options.PrintUsage();
            return 64;
        }

        Console.WriteLine("Cache consistency workload");
        Console.WriteLine($"url={options.Url}");
        Console.WriteLine($"database={options.Database}");
        Console.WriteLine($"clients={options.Clients}");
        Console.WriteLine($"iterations={options.Iterations}");
        Console.WriteLine($"deadlineMs={options.DeadlineMs}");
        Console.WriteLine($"pollMs={options.PollMs}");
        Console.WriteLine($"captureSockets={options.CaptureSockets}");
        Console.WriteLine($"evidenceDir={options.EvidenceDir ?? "<disabled>"}");
        Console.WriteLine($"expectMinIncidents={options.ExpectMinIncidents}");
        Console.WriteLine();

        using CancellationTokenSource cts = new(options.Timeout);
        using EvidenceRecorder? evidence = options.EvidenceDir == null ? null : new EvidenceRecorder(options);
        evidence?.RecordStartup();

        CaptureConnection[] captureConnections = Array.Empty<CaptureConnection>();

        using DocumentStore adminStore = CreateStore(options.Url, null);
        await EnsureDatabaseAsync(adminStore, options.Database, cts.Token).ConfigureAwait(false);

        using DocumentStore writerStore = CreateStore(options.Url, options.Database);
        DocumentStore[] clientStores = Enumerable.Range(0, options.Clients)
            .Select(_ => CreateStore(options.Url, options.Database))
            .ToArray();

        try
        {
            if (evidence != null && options.CaptureSockets > 0)
            {
                captureConnections = await StartCaptureConnectionsAsync(options, evidence, cts.Token).ConfigureAwait(false);
                Console.WriteLine($"connected capture sockets={captureConnections.Length}");
                Console.WriteLine();
            }

            WorkloadSummary summary = await RunWorkloadAsync(writerStore, clientStores, options, evidence, cts.Token).ConfigureAwait(false);
            evidence?.RecordSummary(summary);

            Console.WriteLine();
            Console.WriteLine("summary");
            Console.WriteLine($"iterations={summary.Iterations}");
            Console.WriteLine($"committedWrites={summary.CommittedWrites}");
            Console.WriteLine($"aggressiveReads={summary.AggressiveReads}");
            Console.WriteLine($"incidents={summary.Incidents}");
            Console.WriteLine($"inconclusiveAllOld={summary.InconclusiveAllOld}");
            Console.WriteLine($"allUpdated={summary.AllUpdated}");

            if (summary.Incidents == 0)
            {
                Console.WriteLine();
                Console.WriteLine("  No stale-read incident was observed on this run.");
                Console.WriteLine();
                Console.WriteLine("  If you have not changed any code yet, this stand did not surface the problem on");
                Console.WriteLine("  your machine on this run. Try a few more times, and raise --clients and");
                Console.WriteLine("  --iterations. If it keeps coming back clean, stop retrying and tell us - we will");
                Console.WriteLine("  sort it out with you.");
                Console.WriteLine();
                Console.WriteLine("  If you have already changed code, this run is consistent with your change having");
                Console.WriteLine("  removed the problem. It is not proof on its own. Make that case in your write-up");
                Console.WriteLine("  and back it with the regression test you are asked to supply, not with this exit");
                Console.WriteLine("  code.");
            }

            if (summary.Incidents < options.ExpectMinIncidents)
                return 2;

            return 0;
        }
        catch (ServerMismatchException e)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  The server at {e.Message} did not send any cache-invalidation notification.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  That server is not running the code from this repository, so this workload");
            Console.Error.WriteLine("  cannot tell you anything. It is easy to hit if you have another RavenDB");
            Console.Error.WriteLine("  already listening on that port.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  Stop it, then start the server from this repository:");
            Console.Error.WriteLine("    pwsh assessment/scripts/start-server.ps1");
            Console.Error.WriteLine();
            return 3;
        }
        finally
        {
            foreach (CaptureConnection captureConnection in captureConnections)
                await captureConnection.DisposeAsync().ConfigureAwait(false);

            foreach (DocumentStore clientStore in clientStores)
                clientStore.Dispose();
        }
    }

    private static async Task<CaptureConnection[]> StartCaptureConnectionsAsync(Options options, EvidenceRecorder evidence, CancellationToken token)
    {
        Uri changesUrl = BuildChangesWebSocketUrl(options.Url, options.Database);
        List<CaptureConnection> connections = new(options.CaptureSockets);

        try
        {
            for (int i = 0; i < options.CaptureSockets; i++)
            {
                CaptureConnection connection = new($"capture-{i + 1}", changesUrl, evidence);
                await connection.StartAsync(token).ConfigureAwait(false);
                connections.Add(connection);
            }

            return connections.ToArray();
        }
        catch
        {
            foreach (CaptureConnection connection in connections)
                await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static Uri BuildChangesWebSocketUrl(string serverUrl, string database)
    {
        Uri httpUri = new($"{serverUrl.TrimEnd('/')}/databases/{Uri.EscapeDataString(database)}/changes?throttleConnection=false");
        UriBuilder builder = new(httpUri)
        {
            Scheme = httpUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws"
        };

        return builder.Uri;
    }

    private static async Task<WorkloadSummary> RunWorkloadAsync(DocumentStore writerStore, DocumentStore[] clientStores, Options options, EvidenceRecorder? evidence, CancellationToken token)
    {
        WorkloadSummary summary = new();

        for (int iteration = 1; iteration <= options.Iterations; iteration++)
        {
            token.ThrowIfCancellationRequested();

            string id = DocumentPrefix + iteration;
            string oldName = $"old-{iteration}";
            string newName = $"new-{iteration}";

            await PutUserAsync(writerStore, id, oldName, token).ConfigureAwait(false);
            await PrimeAggressiveCachesAsync(clientStores, id, oldName, token).ConfigureAwait(false);

            await PutUserAsync(writerStore, id, newName, token).ConfigureAwait(false);
            summary.CommittedWrites++;

            string committed = await ReadWithoutAggressiveCacheAsync(writerStore, id, token).ConfigureAwait(false);
            if (committed != newName)
                throw new InvalidOperationException($"Committed control read for {id} returned '{committed}', expected '{newName}'.");

            IterationObservation observation = await ObserveAggressiveCachesAsync(clientStores, id, oldName, newName, options, token).ConfigureAwait(false);
            summary.AggressiveReads += observation.AggressiveReads;
            evidence?.RecordIteration(iteration, id, oldName, newName, observation);

            // The behaviour under investigation lives on the server, so a server built from a
            // different tree produces a perfectly clean run and tells you nothing. The capture
            // sockets subscribe with the public watch command, so by the end of the first
            // iteration at least one notification of this contract must have arrived.
            if (iteration == 1 && evidence != null && options.CaptureSockets > 0 && evidence.CacheInvalidationNotificationsObserved == 0)
                throw new ServerMismatchException(options.Url);

            if (observation.Incident)
            {
                summary.Incidents++;
                Console.WriteLine($"incident iteration={iteration} staleClients={observation.OldValuesAtDeadline} updatedClients={observation.NewValuesAtDeadline} aggressiveReads={observation.AggressiveReads}");
            }
            else if (observation.AllOldAtDeadline)
            {
                summary.InconclusiveAllOld++;
                Console.WriteLine($"inconclusive iteration={iteration} reason=all-clients-still-old aggressiveReads={observation.AggressiveReads}");
            }
            else
            {
                summary.AllUpdated++;
            }

            summary.Iterations++;
        }

        return summary;
    }

    private static async Task<IterationObservation> ObserveAggressiveCachesAsync(DocumentStore[] clientStores, string id, string oldName, string newName, Options options, CancellationToken token)
    {
        Stopwatch sw = Stopwatch.StartNew();
        bool sawNew = false;
        string[] lastValues = Array.Empty<string>();
        int aggressiveReads = 0;

        while (sw.ElapsedMilliseconds < options.DeadlineMs)
        {
            token.ThrowIfCancellationRequested();

            lastValues = await Task.WhenAll(clientStores.Select(store => ReadWithAggressiveCacheAsync(store, id, token))).ConfigureAwait(false);
            aggressiveReads += lastValues.Length;

            if (lastValues.Any(value => value == newName))
                sawNew = true;

            if (sawNew && lastValues.All(value => value == newName))
            {
                return new IterationObservation(
                    Incident: false,
                    AllOldAtDeadline: false,
                    OldValuesAtDeadline: 0,
                    NewValuesAtDeadline: lastValues.Length,
                    AggressiveReads: aggressiveReads);
            }

            await Task.Delay(options.PollInterval, token).ConfigureAwait(false);
        }

        int oldValues = lastValues.Count(value => value == oldName);
        int newValues = lastValues.Count(value => value == newName);

        return new IterationObservation(
            Incident: sawNew && oldValues > 0,
            AllOldAtDeadline: sawNew == false && oldValues == lastValues.Length,
            OldValuesAtDeadline: oldValues,
            NewValuesAtDeadline: newValues,
            AggressiveReads: aggressiveReads);
    }

    private static async Task PrimeAggressiveCachesAsync(DocumentStore[] clientStores, string id, string expectedName, CancellationToken token)
    {
        string[] names = await Task.WhenAll(clientStores.Select(store => ReadWithAggressiveCacheAsync(store, id, token))).ConfigureAwait(false);
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] != expectedName)
                throw new InvalidOperationException($"Prime read for client {i} returned '{names[i]}', expected '{expectedName}'.");
        }
    }

    private static async Task<string> ReadWithAggressiveCacheAsync(DocumentStore store, string id, CancellationToken token)
    {
        using (await store.AggressivelyCacheForAsync(TimeSpan.FromHours(1)).ConfigureAwait(false))
        using (IAsyncDocumentSession session = store.OpenAsyncSession())
        {
            User? user = await session.LoadAsync<User>(id, token).ConfigureAwait(false);
            return user?.Name ?? "<missing>";
        }
    }

    private static async Task<string> ReadWithoutAggressiveCacheAsync(DocumentStore store, string id, CancellationToken token)
    {
        using (store.DisableAggressiveCaching())
        using (IAsyncDocumentSession session = store.OpenAsyncSession())
        {
            User? user = await session.LoadAsync<User>(id, token).ConfigureAwait(false);
            return user?.Name ?? "<missing>";
        }
    }

    private static async Task PutUserAsync(DocumentStore store, string id, string name, CancellationToken token)
    {
        using IAsyncDocumentSession session = store.OpenAsyncSession();
        await session.StoreAsync(new User
        {
            Name = name
        }, id, token).ConfigureAwait(false);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private static async Task EnsureDatabaseAsync(DocumentStore adminStore, string database, CancellationToken token)
    {
        try
        {
            await adminStore.Maintenance.Server.SendAsync(new CreateDatabaseOperation(new DatabaseRecord(database)), token).ConfigureAwait(false);
            Console.WriteLine($"created database '{database}'");
        }
        catch (ConcurrencyException)
        {
            Console.WriteLine($"using existing database '{database}'");
        }
    }

    private static DocumentStore CreateStore(string url, string? database)
    {
        DocumentStore store = new()
        {
            Urls = new[] { url },
            Database = database
        };

        store.Initialize();
        return store;
    }

    private sealed class CaptureConnection : IAsyncDisposable
    {
        private readonly string _name;
        private readonly Uri _url;
        private readonly EvidenceRecorder _evidence;
        private readonly ClientWebSocket _socket = new();
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly TaskCompletionSource _confirmed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _receiveTask = Task.CompletedTask;

        public CaptureConnection(string name, Uri url, EvidenceRecorder evidence)
        {
            _name = name;
            _url = url;
            _evidence = evidence;
        }

        public async Task StartAsync(CancellationToken token)
        {
            await _socket.ConnectAsync(_url, token).ConfigureAwait(false);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_disposeCts.Token), _disposeCts.Token);
            await SendSubscribeCommandAsync(token).ConfigureAwait(false);
            await _confirmed.Task.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
        }

        private async Task SendSubscribeCommandAsync(CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"CommandId\":1,\"Command\":\"watch-aggressive-caching\",\"Param\":null}");
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken: token).ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[64 * 1024];

            try
            {
                while (token.IsCancellationRequested == false && _socket.State == WebSocketState.Open)
                {
                    using MemoryStream ms = new();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (result.EndOfMessage == false);

                    if (ms.Length == 0)
                        continue;

                    string frame = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessFrame(frame);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (WebSocketException e) when (token.IsCancellationRequested || _socket.State != WebSocketState.Open)
            {
                _evidence.RecordCaptureConnectionClosed(_name, e.Message);
            }
            catch (Exception e)
            {
                _evidence.RecordCaptureConnectionClosed(_name, e.ToString());
            }
        }

        private void ProcessFrame(string frame)
        {
            _evidence.RecordFrame(_name, frame);

            using JsonDocument document = JsonDocument.Parse(frame);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (item.TryGetProperty("Type", out JsonElement typeElement) == false)
                    continue;

                string? type = typeElement.GetString();
                if (type == "Confirm" && item.TryGetProperty("CommandId", out JsonElement commandId) && commandId.TryGetInt32(out int id) && id == 1)
                {
                    _confirmed.TrySetResult();
                    continue;
                }

                if (type == NotificationType)
                    _evidence.RecordNotification(_name, item);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposeCts.Cancel();

            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch
            {
            }

            _socket.Dispose();
            _disposeCts.Dispose();
        }
    }

    private sealed class EvidenceRecorder : IDisposable
    {
        private readonly Options _options;
        private readonly string _notificationPayloadsPath;
        private readonly string _connectionCapturePath;
        private readonly string _clientCachePath;
        private readonly string _runtimeAndCountersPath;
        private readonly string _supportNotesPath;
        private readonly object _gate = new();
        private long _frames;
        private long _cacheInvalidationNotificationsObserved;

        public long CacheInvalidationNotificationsObserved => Interlocked.Read(ref _cacheInvalidationNotificationsObserved);
        private long _notificationContractRejected;

        public EvidenceRecorder(Options options)
        {
            _options = options;

            string evidenceDir = Path.GetFullPath(options.EvidenceDir!);
            Directory.CreateDirectory(evidenceDir);

            _notificationPayloadsPath = Path.Combine(evidenceDir, "notification-payloads.txt");
            _connectionCapturePath = Path.Combine(evidenceDir, "changes-connection-capture.log");
            _clientCachePath = Path.Combine(evidenceDir, "client-cache.log");
            _runtimeAndCountersPath = Path.Combine(evidenceDir, "runtime-and-counters.txt");
            _supportNotesPath = Path.Combine(evidenceDir, "support-notes.md");

            WriteFile(_notificationPayloadsPath, "# Captured cache-invalidation notification payloads\n");
            WriteFile(_connectionCapturePath, "# Changes API frames observed by capture sockets\n");
            WriteFile(_clientCachePath, "# Cache and notification observations\n");
            WriteFile(_runtimeAndCountersPath, string.Empty);
            WriteSupportNotes();
        }

        public void RecordStartup()
        {
            AppendLine(_runtimeAndCountersPath, "run_started_utc=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "url=" + _options.Url);
            AppendLine(_runtimeAndCountersPath, "database=" + _options.Database);
            AppendLine(_runtimeAndCountersPath, "clients=" + _options.Clients.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "iterations=" + _options.Iterations.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "deadline_ms=" + _options.DeadlineMs.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "poll_ms=" + _options.PollMs.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "capture_sockets=" + _options.CaptureSockets.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "process_id=" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "framework=" + RuntimeInformation.FrameworkDescription);
            AppendLine(_runtimeAndCountersPath, "os=" + RuntimeInformation.OSDescription);
            AppendLine(_runtimeAndCountersPath, "architecture=" + RuntimeInformation.ProcessArchitecture);
        }

        public void RecordFrame(string connectionName, string frame)
        {
            long frameNumber = Interlocked.Increment(ref _frames);
            AppendLine(_connectionCapturePath, $"{Timestamp()} frame_received frame={frameNumber} connection={connectionName} bytes={Encoding.UTF8.GetByteCount(frame)}");
        }

        public void RecordNotification(string connectionName, JsonElement notification)
        {
            long notificationNumber = Interlocked.Increment(ref _cacheInvalidationNotificationsObserved);

            NotificationClassification classification = Classify(notification);
            string generation = classification.Generation.HasValue
                ? classification.Generation.Value.ToString(CultureInfo.InvariantCulture)
                : "unknown";
            string fieldSet = classification.Fields.Count == 0 ? "<empty>" : string.Join("|", classification.Fields);
            string contract = classification.IsValid ? "accepted" : "ignored";

            AppendLine(_connectionCapturePath, $"{Timestamp()} frame_classified notification={notificationNumber} connection={connectionName} sequence={generation} fields={fieldSet} contract={contract}");
            AppendLine(_notificationPayloadsPath, $"{Timestamp()} connection={connectionName} sequence={generation} fields={fieldSet} contract={contract} payload={notification.GetRawText()}");

            if (classification.IsValid)
            {
                AppendLine(_clientCachePath, $"{Timestamp()} capture-client={connectionName} cache-invalidation=accepted sequence={generation} reason=\"{classification.Reason}\"");
            }
            else
            {
                Interlocked.Increment(ref _notificationContractRejected);
                AppendLine(_clientCachePath, $"{Timestamp()} capture-client={connectionName} cache-invalidation=ignored sequence={generation} reason=\"{classification.RejectionReason}\"");
            }
        }

        public void RecordCaptureConnectionClosed(string connectionName, string reason)
        {
            AppendLine(_connectionCapturePath, $"{Timestamp()} capture_connection_closed connection={connectionName} reason=\"{reason.Replace("\"", "'")}\"");
        }

        public void RecordIteration(int iteration, string id, string oldName, string newName, IterationObservation observation)
        {
            string result = observation.Incident ? "incident" : observation.AllOldAtDeadline ? "inconclusive-all-old" : "all-updated";
            AppendLine(
                _clientCachePath,
                $"{Timestamp()} aggressive-clients iteration={iteration} document={id} old={oldName} new={newName} result={result} staleClients={observation.OldValuesAtDeadline} updatedClients={observation.NewValuesAtDeadline} aggressiveReads={observation.AggressiveReads}");
        }

        public void RecordSummary(WorkloadSummary summary)
        {
            AppendLine(_runtimeAndCountersPath, "run_completed_utc=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "committed_writes=" + summary.CommittedWrites.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "aggressive_reads=" + summary.AggressiveReads.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "incidents=" + summary.Incidents.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "inconclusive_all_old=" + summary.InconclusiveAllOld.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "all_updated=" + summary.AllUpdated.ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "websocket_frames_observed_total=" + Interlocked.Read(ref _frames).ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "cache_invalidation_notifications_observed_total=" + Interlocked.Read(ref _cacheInvalidationNotificationsObserved).ToString(CultureInfo.InvariantCulture));
            AppendLine(_runtimeAndCountersPath, "notification_contract_rejected_total=" + Interlocked.Read(ref _notificationContractRejected).ToString(CultureInfo.InvariantCulture));
        }

        private static NotificationClassification Classify(JsonElement notification)
        {
            List<string> fields = new();
            string? reason = null;
            long? generation = null;
            string? rejectionReason = null;

            if (notification.TryGetProperty("Value", out JsonElement value) == false || value.ValueKind != JsonValueKind.Object)
            {
                return new NotificationClassification(fields, null, null, false, "missing payload");
            }

            foreach (JsonProperty property in value.EnumerateObject())
            {
                fields.Add(property.Name);

                if (property.NameEquals(ReasonFieldName))
                    reason = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                else if (property.NameEquals(GenerationFieldName) && property.Value.TryGetInt64(out long parsedGeneration))
                    generation = parsedGeneration;
            }

            if (string.IsNullOrEmpty(reason))
                rejectionReason = $"missing or empty '{ReasonFieldName}'";
            else if (generation is null or <= 0)
                rejectionReason = $"missing or invalid '{GenerationFieldName}'";

            return new NotificationClassification(fields, reason, generation, rejectionReason == null, rejectionReason);
        }

        private void WriteSupportNotes()
        {
            WriteFile(
                _supportNotesPath,
                """
                # Support notes

                This evidence folder is produced by the cache-consistency workload against a local unsecured single-node server.

                The workload uses public RavenDB client APIs for document writes and aggressive-cache reads. The payload capture uses separate public Changes API WebSocket subscribers that issue `watch-aggressive-caching`; it does not use test hooks.

                A cache invalidation notification is treated as accepted when its `Value` object contains a non-empty `Reason` and a positive `Generation`. Missing or invalid required fields are counted as `notification_contract_rejected_total`.

                Duplicate JSON properties, if they appear, are preserved in `notification-payloads.txt` as captured payload shape. They are not counted as rejected unless a required field is missing or invalid.

                `changes-connection-capture.log` and the capture counters are client-side capture observations, not server internals. They are intended to show which notification frames reached independent Changes API subscribers and how those payloads classify under the client-side contract.
                """);
        }

        private static string Timestamp()
        {
            return DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        private void AppendLine(string path, string line)
        {
            lock (_gate)
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static void WriteFile(string path, string contents)
        {
            File.WriteAllText(path, contents.ReplaceLineEndings(Environment.NewLine), Encoding.UTF8);
        }

        public void Dispose()
        {
        }
    }

    private sealed record NotificationClassification(List<string> Fields, string? Reason, long? Generation, bool IsValid, string? RejectionReason);

    private sealed class User
    {
        public string? Name { get; set; }
    }

    private sealed class WorkloadSummary
    {
        public int Iterations;
        public int CommittedWrites;
        public int AggressiveReads;
        public int Incidents;
        public int InconclusiveAllOld;
        public int AllUpdated;
    }

    private sealed class ServerMismatchException : Exception
    {
        public ServerMismatchException(string url) : base(url)
        {
        }
    }

    private sealed record IterationObservation(bool Incident, bool AllOldAtDeadline, int OldValuesAtDeadline, int NewValuesAtDeadline, int AggressiveReads);

    private sealed class Options
    {
        public string Url { get; private init; } = "http://127.0.0.1:8081";
        public string Database { get; private init; } = "CacheConsistencyCheck";
        public int Clients { get; private init; } = 48;
        public int Iterations { get; private init; } = 20;
        public int DeadlineMs { get; private init; } = 3000;
        public int PollMs { get; private init; } = 25;
        public int CaptureSockets { get; private init; } = 4;
        public string? EvidenceDir { get; private init; }
        public int ExpectMinIncidents { get; private init; } = 0;
        public TimeSpan Timeout { get; private init; } = TimeSpan.FromMinutes(5);
        public TimeSpan PollInterval => TimeSpan.FromMilliseconds(PollMs);

        public static Options Parse(string[] args)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg is "-h" or "--help" or "/?")
                    throw new InvalidOperationException("Usage requested.");

                if (arg.StartsWith("--", StringComparison.Ordinal) == false)
                    throw new InvalidOperationException($"Unexpected argument '{arg}'.");

                string key = arg[2..];
                if (i + 1 >= args.Length)
                    throw new InvalidOperationException($"Missing value for '{arg}'.");

                values[key] = args[++i];
            }

            Options defaults = new();
            return new Options
            {
                Url = GetString(values, "url", defaults.Url),
                Database = GetString(values, "database", defaults.Database),
                Clients = GetInt(values, "clients", defaults.Clients, min: 2),
                Iterations = GetInt(values, "iterations", defaults.Iterations, min: 1),
                DeadlineMs = GetInt(values, "deadline-ms", defaults.DeadlineMs, min: 100),
                PollMs = GetInt(values, "poll-ms", defaults.PollMs, min: 1),
                CaptureSockets = GetInt(values, "capture-sockets", defaults.CaptureSockets, min: 0),
                EvidenceDir = GetOptionalString(values, "evidence-dir"),
                ExpectMinIncidents = GetInt(values, "expect-min-incidents", defaults.ExpectMinIncidents, min: 0),
                Timeout = TimeSpan.FromSeconds(GetInt(values, "timeout-sec", (int)defaults.Timeout.TotalSeconds, min: 1))
            };
        }

        public static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project assessment/cache-consistency-check/CacheConsistencyCheck.csproj -- --url http://127.0.0.1:8081 --database CacheConsistencyCheck --clients 48 --iterations 20 --evidence-dir assessment/EVIDENCE");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --url <url>");
            Console.WriteLine("  --database <name>");
            Console.WriteLine("  --clients <n>");
            Console.WriteLine("  --iterations <n>");
            Console.WriteLine("  --deadline-ms <n>");
            Console.WriteLine("  --poll-ms <n>");
            Console.WriteLine("  --capture-sockets <n>");
            Console.WriteLine("  --evidence-dir <path>");
            Console.WriteLine("  --expect-min-incidents <n>");
            Console.WriteLine("  --timeout-sec <n>");
        }

        private static string GetString(Dictionary<string, string> values, string key, string defaultValue)
        {
            return values.TryGetValue(key, out string? value) ? value : defaultValue;
        }

        private static string? GetOptionalString(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string? value) && string.IsNullOrWhiteSpace(value) == false ? value : null;
        }

        private static int GetInt(Dictionary<string, string> values, string key, int defaultValue, int min)
        {
            if (values.TryGetValue(key, out string? value) == false)
                return defaultValue;

            if (int.TryParse(value, out int parsed) == false || parsed < min)
                throw new InvalidOperationException($"'{key}' must be an integer >= {min}.");

            return parsed;
        }
    }
}
