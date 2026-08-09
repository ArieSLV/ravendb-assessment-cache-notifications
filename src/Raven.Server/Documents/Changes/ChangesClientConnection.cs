using System;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Changes;
using Raven.Server.ServerWide.Context;
using Sparrow.Collections;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Changes
{
    public sealed class ChangesClientConnection : AbstractChangesClientConnection<DocumentsOperationContext>
    {
        private readonly ConcurrentSet<string> _matchingDocuments = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingIndexes = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingDocumentPrefixes = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingDocumentsInCollection = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingCounters = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingDocumentCounters = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<DocumentIdAndNamePair> _matchingDocumentCounter = new();

        private readonly ConcurrentSet<string> _matchingTimeSeries = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingAllDocumentTimeSeries = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<DocumentIdAndNamePair> _matchingDocumentTimeSeries = new();

        private int _watchAllDocuments;
        private int _watchAllIndexes;
        private int _watchAllCounters;
        private int _watchAllTimeSeries;
        private int _aggressiveChanges;
        private readonly DocumentDatabase _database;
        private readonly WebSocket _webSocket;

        internal bool WatchesAggressiveCaching => Volatile.Read(ref _aggressiveChanges) == 1;

        public ChangesClientConnection(WebSocket webSocket, DocumentDatabase database, bool throttleConnection, bool fromStudio)
            : base(webSocket, database.DocumentsStorage.ContextPool, database.DatabaseShutdown, throttleConnection, fromStudio)
        {
            _database = database;
            _webSocket = webSocket;
        }

        public void SendCounterChanges(CounterChange change)
        {
            if (IsDisposed)
                return;

            if (_watchAllCounters > 0)
            {
                Send(change);
                return;
            }

            if (change.Name != null && _matchingCounters.Contains(change.Name))
            {
                Send(change);
                return;
            }

            if (change.DocumentId != null && _matchingDocumentCounters.Contains(change.DocumentId))
            {
                Send(change);
                return;
            }

            if (change.DocumentId != null && change.Name != null && _matchingDocumentCounter.IsEmpty == false)
            {
                var parameters = new DocumentIdAndNamePair(change.DocumentId, change.Name);
                if (_matchingDocumentCounter.Contains(parameters))
                {
                    Send(change);
                    return;
                }
            }
        }

        public void SendTimeSeriesChanges(TimeSeriesChange change)
        {
            if (IsDisposed)
                return;

            if (_watchAllTimeSeries > 0)
            {
                Send(change);
                return;
            }

            if (change.Name != null && _matchingTimeSeries.Contains(change.Name))
            {
                Send(change);
                return;
            }

            if (change.DocumentId != null && _matchingAllDocumentTimeSeries.Contains(change.DocumentId))
            {
                Send(change);
                return;
            }

            if (change.DocumentId != null && change.Name != null && _matchingDocumentTimeSeries.IsEmpty == false)
            {
                var parameters = new DocumentIdAndNamePair(change.DocumentId, change.Name);
                if (_matchingDocumentTimeSeries.Contains(parameters))
                {
                    Send(change);
                    return;
                }
            }
        }

        internal void SendDocumentChanges(DocumentChange change, CacheInvalidationEnvelope cacheInvalidation)
        {
            // this is a precaution, in order to overcome an observed race condition between change client disconnection and raising changes
            if (IsDisposed)
                return;

            if (WatchesAggressiveCaching)
            {
                Debug.Assert(_watchAllDocuments == 0 && (_matchingDocuments == null || _matchingDocuments.Count == 0));

                if (cacheInvalidation != null)
                    SendCacheInvalidation(cacheInvalidation);
                return;
            }

            if (_watchAllDocuments > 0)
            {
                Send(change);
                return;
            }

            if (change.Id != null && _matchingDocuments.Contains(change.Id))
            {
                Send(change);
                return;
            }

            var hasPrefix = change.Id != null && HasItemStartingWith(_matchingDocumentPrefixes, change.Id);
            if (hasPrefix)
            {
                Send(change);
                return;
            }

            var hasCollection = change.CollectionName != null && HasItemEqualsTo(_matchingDocumentsInCollection, change.CollectionName);
            if (hasCollection)
            {
                Send(change);
                return;
            }

            if (change.Id == null && change.CollectionName == null)
            {
                Send(change);
            }
        }

        internal void SendIndexChanges(IndexChange change, CacheInvalidationEnvelope cacheInvalidation)
        {
            if (WatchesAggressiveCaching)
            {
                Debug.Assert(_watchAllIndexes == 0 && (_matchingIndexes == null || _matchingIndexes.Count == 0));

                if (cacheInvalidation != null)
                    SendCacheInvalidation(cacheInvalidation);
                return;
            }

            if (_watchAllIndexes > 0)
            {
                Send(change);
                return;
            }

            if (change.Name != null && _matchingIndexes.Contains(change.Name))
            {
                Send(change);
            }
        }

        private void SendCacheInvalidation(CacheInvalidationEnvelope cacheInvalidation)
        {
            AddToQueue(new SendQueueItem
            {
                AllowSkip = true,
                ValueToSend = cacheInvalidation
            }, addIfEmpty: true);
        }

        private void Send(CounterChange change)
        {
            var value = CreateValueToSend(nameof(CounterChange), change.ToJson());

            AddToQueue(new SendQueueItem
            {
                ValueToSend = value,
                AllowSkip = true
            });
        }

        private void Send(TimeSeriesChange change)
        {
            var value = CreateValueToSend(nameof(TimeSeriesChange), change.ToJson());

            AddToQueue(new SendQueueItem
            {
                ValueToSend = value,
                AllowSkip = true
            });
        }

        private void Send(DocumentChange change)
        {
            var value = CreateValueToSend(nameof(DocumentChange), change.ToJson());

            AddToQueue(new SendQueueItem
            {
                ValueToSend = value,
                AllowSkip = true
            });
        }

        private void Send(IndexChange change)
        {
            var value = CreateValueToSend(nameof(IndexChange), change.ToJson());

            AddToQueue(new SendQueueItem
            {
                ValueToSend = value,
                AllowSkip = change.Type == IndexChangeTypes.BatchCompleted
            });
        }

        private static bool HasItemStartingWith(ConcurrentSet<string> set, string value)
        {
            if (set.IsEmpty)
                return false;
            foreach (string item in set)
            {
                if (value.StartsWith(item, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasItemEqualsTo(ConcurrentSet<string> set, string value)
        {
            if (set.IsEmpty)
                return false;
            foreach (string item in set)
            {
                if (value.Equals(item, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        protected override ValueTask WatchDocumentAsync(string docId, CancellationToken token)
        {
            _matchingDocuments.TryAdd(docId);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchDocumentAsync(string docId, CancellationToken token)
        {
            _matchingDocuments.TryRemove(docId);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchAllDocumentsAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _watchAllDocuments);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchAllDocumentsAsync(CancellationToken token)
        {
            Interlocked.Decrement(ref _watchAllDocuments);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchCounterAsync(string name, CancellationToken token)
        {
            _matchingCounters.TryAdd(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchCounterAsync(string name, CancellationToken token)
        {
            _matchingCounters.TryRemove(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchDocumentCountersAsync(string docId, CancellationToken token)
        {
            _matchingDocumentCounters.TryAdd(docId);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchDocumentCountersAsync(string docId, CancellationToken token)
        {
            _matchingDocumentCounters.TryRemove(docId);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchDocumentCounterAsync(BlittableJsonReaderArray parameters, CancellationToken token)
        {
            var val = GetParameters(parameters);

            _matchingDocumentCounter.TryAdd(val);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchDocumentCounterAsync(BlittableJsonReaderArray parameters, CancellationToken token)
        {
            var val = GetParameters(parameters);

            _matchingDocumentCounter.TryRemove(val);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchAllCountersAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _watchAllCounters);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchAllCountersAsync(CancellationToken token)
        {
            Interlocked.Decrement(ref _watchAllCounters);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchTimeSeriesAsync(string name, CancellationToken token)
        {
            _matchingTimeSeries.TryAdd(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchTimeSeriesAsync(string name, CancellationToken token)
        {
            _matchingTimeSeries.TryRemove(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchAllDocumentTimeSeriesAsync(string docId, CancellationToken token)
        {
            _matchingAllDocumentTimeSeries.TryAdd(docId);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchAllDocumentTimeSeriesAsync(string docId, CancellationToken token)
        {
            _matchingAllDocumentTimeSeries.TryRemove(docId);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchDocumentTimeSeriesAsync(BlittableJsonReaderArray parameters, CancellationToken token)
        {
            var val = GetParameters(parameters);

            _matchingDocumentTimeSeries.TryAdd(val);

            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchDocumentTimeSeriesAsync(BlittableJsonReaderArray parameters, CancellationToken token)
        {
            var val = GetParameters(parameters);

            _matchingDocumentTimeSeries.TryRemove(val);

            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchAllTimeSeriesAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _watchAllTimeSeries);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchAllTimeSeriesAsync(CancellationToken token)
        {
            Interlocked.Decrement(ref _watchAllTimeSeries);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchDocumentPrefixAsync(string name, CancellationToken token)
        {
            _matchingDocumentPrefixes.TryAdd(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchDocumentPrefixAsync(string name, CancellationToken token)
        {
            _matchingDocumentPrefixes.TryRemove(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchDocumentInCollectionAsync(string name, CancellationToken token)
        {
            _matchingDocumentsInCollection.TryAdd(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchDocumentInCollectionAsync(string name, CancellationToken token)
        {
            _matchingDocumentsInCollection.TryRemove(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchAllIndexesAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _watchAllIndexes);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchAllIndexesAsync(CancellationToken token)
        {
            Interlocked.Decrement(ref _watchAllIndexes);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchIndexAsync(string name, CancellationToken token)
        {
            _matchingIndexes.TryAdd(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchIndexAsync(string name, CancellationToken token)
        {
            _matchingIndexes.TryRemove(name);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask WatchAggressiveCachingAsync(CancellationToken token)
        {
            if (IsDisposed)
                return ValueTask.CompletedTask;

            if (Interlocked.Exchange(ref _aggressiveChanges, 1) == 0)
            {
                _database.Changes.IncrementAggressiveCacheConnectionCount();

                if (IsDisposed)
                    UnregisterAggressiveCachingWatcher();
            }

            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnwatchAggressiveCachingAsync(CancellationToken token)
        {
            UnregisterAggressiveCachingWatcher();
            return ValueTask.CompletedTask;
        }

        private void UnregisterAggressiveCachingWatcher()
        {
            if (Interlocked.Exchange(ref _aggressiveChanges, 0) == 1)
                _database.Changes.DecrementAggressiveCacheConnectionCount();
        }

        public override void Dispose()
        {
            base.Dispose();
            UnregisterAggressiveCachingWatcher();
        }

        public override DynamicJsonValue GetDebugInfo()
        {
            var djv = base.GetDebugInfo();

            djv["WatchAllDocuments"] = _watchAllDocuments > 0;
            djv["WatchAllIndexes"] = _watchAllIndexes > 0;
            djv["WatchAllCounters"] = _watchAllCounters > 0;
            djv["WatchAllTimeSeries"] = _watchAllTimeSeries > 0;
            djv["WatchDocumentPrefixes"] = _matchingDocumentPrefixes.ToArray();
            djv["WatchDocumentsInCollection"] = _matchingDocumentsInCollection.ToArray();
            djv["WatchIndexes"] = _matchingIndexes.ToArray();
            djv["WatchDocuments"] = _matchingDocuments.ToArray();
            djv["WatchCounters"] = _matchingCounters.ToArray();
            djv["WatchCounterOfDocument"] = _matchingDocumentCounter.Select(x => x.ToJson()).ToArray();
            djv["WatchCountersOfDocument"] = _matchingDocumentCounters.ToArray();
            djv["WatchTimeSeries"] = _matchingTimeSeries.ToArray();
            djv["WatchTimeSeriesOfDocument"] = _matchingDocumentTimeSeries.Select(x => x.ToJson()).ToArray();
            djv["WatchAllTimeSeriesOfDocument"] = _matchingAllDocumentTimeSeries.ToArray();

            return djv;
        }

        protected override bool CanWriteSpecialMessage(object message)
        {
            return message is CacheInvalidationEnvelope;
        }

        protected override bool TryWriteSpecialMessage(JsonOperationContext context, AsyncBlittableJsonTextWriter writer, object message)
        {
            if (message is not CacheInvalidationEnvelope cacheInvalidation)
                return false;

            cacheInvalidation.WriteTo(context, writer, Id, _webSocket, _database.ForTestingPurposes?.OnCacheInvalidationMessageFieldWritten);
            return true;
        }
    }

    internal sealed class CacheInvalidationEnvelope
    {
        private readonly string _reason;
        private readonly long _generation;
        private int _fieldCursor;

        public CacheInvalidationEnvelope(string reason, long generation)
        {
            _reason = reason;
            _generation = generation;
        }

        public void WriteTo(JsonOperationContext context, IBlittableJsonTextWriter writer, long connectionId, WebSocket webSocket, Action<CacheInvalidationMessageFieldWritten> onFieldWritten)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("Type");
            writer.WriteString(CacheInvalidationChange.NotificationType);
            writer.WriteComma();

            writer.WritePropertyName("Value");
            writer.WriteStartObject();

            BeginPayloadWalk();
            var written = 0;
            while (TryMoveToNextField(out int fieldIndex))
            {
                if (written > 0)
                    writer.WriteComma();

                string fieldName = WriteField(writer, fieldIndex);
                written++;

                onFieldWritten?.Invoke(new CacheInvalidationMessageFieldWritten(
                    connectionId,
                    webSocket,
                    _reason,
                    _generation,
                    fieldIndex,
                    fieldName));
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private void BeginPayloadWalk()
        {
            _fieldCursor = 0;
        }

        private bool TryMoveToNextField(out int fieldIndex)
        {
            fieldIndex = _fieldCursor;
            if (fieldIndex >= 2)
                return false;

            _fieldCursor = fieldIndex + 1;
            return true;
        }

        private string WriteField(IBlittableJsonTextWriter writer, int fieldIndex)
        {
            switch (fieldIndex)
            {
                case 0:
                    writer.WritePropertyName(CacheInvalidationChange.GenerationFieldName);
                    writer.WriteInteger(_generation);
                    return CacheInvalidationChange.GenerationFieldName;
                case 1:
                    writer.WritePropertyName(CacheInvalidationChange.ReasonFieldName);
                    writer.WriteString(_reason);
                    return CacheInvalidationChange.ReasonFieldName;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fieldIndex), fieldIndex, "Unknown cache invalidation payload field.");
            }
        }
    }

    internal sealed class CacheInvalidationMessageFieldWritten
    {
        public readonly long ConnectionId;
        public readonly WebSocket WebSocket;
        public readonly string Reason;
        public readonly long Generation;
        public readonly int FieldIndex;
        public readonly string FieldName;

        public CacheInvalidationMessageFieldWritten(long connectionId, WebSocket webSocket, string reason, long generation, int fieldIndex, string fieldName)
        {
            ConnectionId = connectionId;
            WebSocket = webSocket;
            Reason = reason;
            Generation = generation;
            FieldIndex = fieldIndex;
            FieldName = fieldName;
        }
    }
}
