using System;
using System.Diagnostics;
using System.Threading;
using Raven.Client.Documents.Changes;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Changes
{
    public sealed class DocumentsChanges : DocumentsChangesBase<ChangesClientConnection, DocumentsOperationContext>
    {
        public event Action<DocumentChange> OnDocumentChange;

        public event Action<CounterChange> OnCounterChange;

        public event Action<TimeSeriesChange> OnTimeSeriesChange;

        public event Action<IndexChange> OnIndexChange;

        private long _cacheInvalidationGeneration;
        private int _aggressiveCacheConnections;

        public void RaiseNotifications(IndexChange indexChange)
        {
            OnIndexChange?.Invoke(indexChange);

            var cacheInvalidation = HasAggressiveCacheConnections && CacheInvalidationChange.ShouldInvalidateCache(indexChange)
                ? CreateCacheInvalidationEnvelope("Index:" + indexChange.Type)
                : null;

            foreach (var connection in Connections)
                connection.Value.SendIndexChanges(indexChange, cacheInvalidation);
        }

        public void RaiseNotifications(DocumentChange documentChange)
        {
            OnDocumentChange?.Invoke(documentChange);

            var cacheInvalidation = HasAggressiveCacheConnections && CacheInvalidationChange.ShouldInvalidateCache(documentChange)
                ? CreateCacheInvalidationEnvelope("Document:" + documentChange.Type)
                : null;

            foreach (var connection in Connections)
            {
                if (!connection.Value.IsDisposed)
                    connection.Value.SendDocumentChanges(documentChange, cacheInvalidation);
            }
        }

        public void RaiseNotifications(CounterChange counterChange)
        {
            OnCounterChange?.Invoke(counterChange);

            foreach (var connection in Connections)
            {
                if (!connection.Value.IsDisposed)
                    connection.Value.SendCounterChanges(counterChange);
            }
        }

        public void RaiseNotifications(TimeSeriesChange timeSeriesChange)
        {
            OnTimeSeriesChange?.Invoke(timeSeriesChange);

            foreach (var connection in Connections)
            {
                if (!connection.Value.IsDisposed)
                    connection.Value.SendTimeSeriesChanges(timeSeriesChange);
            }
        }

        private CacheInvalidationEnvelope CreateCacheInvalidationEnvelope(string reason)
        {
            var generation = Interlocked.Increment(ref _cacheInvalidationGeneration);
            return new CacheInvalidationEnvelope(reason, generation);
        }

        private bool HasAggressiveCacheConnections => Volatile.Read(ref _aggressiveCacheConnections) > 0;

        internal void IncrementAggressiveCacheConnectionCount()
        {
            Interlocked.Increment(ref _aggressiveCacheConnections);
        }

        internal void DecrementAggressiveCacheConnectionCount()
        {
            int count = Interlocked.Decrement(ref _aggressiveCacheConnections);
            Debug.Assert(count >= 0);
        }
    }
}
