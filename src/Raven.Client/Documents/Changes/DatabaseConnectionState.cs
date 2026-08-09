using System;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations;

namespace Raven.Client.Documents.Changes
{
    internal sealed class DatabaseConnectionState : AbstractDatabaseConnectionState, IChangesConnectionState<DocumentChange>, IChangesConnectionState<IndexChange>, IChangesConnectionState<OperationStatusChange>, IChangesConnectionState<CounterChange>, IChangesConnectionState<TimeSeriesChange>, IChangesConnectionState<CacheInvalidationChange>
    {
        private event Action<DocumentChange> OnDocumentChangeNotification;

        private event Action<CounterChange> OnCounterChangeNotification;

        private event Action<IndexChange> OnIndexChangeNotification;

        private event Action<OperationStatusChange> OnOperationStatusChangeNotification;

        private event Action<CacheInvalidationChange> OnCacheInvalidationNotification;

        private event Action<TimeSeriesChange> OnTimeSeriesChangeNotification;

        public DatabaseConnectionState(Func<Task> onConnect, Func<Task> onDisconnect)
            : base(onConnect, onDisconnect)
        {
        }

        public void Send(DocumentChange documentChange)
        {
            CallEventInternal(OnDocumentChangeNotification, documentChange);
        }

        public void Send(CounterChange counterChange)
        {
            CallEventInternal(OnCounterChangeNotification, counterChange);
        }

        public void Send(TimeSeriesChange timeSeriesChange)
        {
            CallEventInternal(OnTimeSeriesChangeNotification, timeSeriesChange);
        }

        public void Send(IndexChange indexChange)
        {
            CallEventInternal(OnIndexChangeNotification, indexChange);
        }

        public void Send(OperationStatusChange operationStatusChange)
        {
            CallEventInternal(OnOperationStatusChangeNotification, operationStatusChange);
        }

        public void Send(CacheInvalidationChange cacheInvalidationChange)
        {
            CallEventInternal(OnCacheInvalidationNotification, cacheInvalidationChange);
        }

        void IChangesConnectionState<TimeSeriesChange>.RegisterEvents(Action<TimeSeriesChange> onChangeNotification, Action<Exception> onError)
        {
            RegisterEventsInternal(ref OnTimeSeriesChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<TimeSeriesChange>.UnregisterEvents(Action<TimeSeriesChange> onChangeNotification, Action<Exception> onError)
        {
            UnregisterEventsInternal(ref OnTimeSeriesChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<CounterChange>.RegisterEvents(Action<CounterChange> onChangeNotification, Action<Exception> onError)
        {
            RegisterEventsInternal(ref OnCounterChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<CounterChange>.UnregisterEvents(Action<CounterChange> onChangeNotification, Action<Exception> onError)
        {
            UnregisterEventsInternal(ref OnCounterChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<OperationStatusChange>.RegisterEvents(Action<OperationStatusChange> onChangeNotification, Action<Exception> onError)
        {
            RegisterEventsInternal(ref OnOperationStatusChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<OperationStatusChange>.UnregisterEvents(Action<OperationStatusChange> onChangeNotification, Action<Exception> onError)
        {
            UnregisterEventsInternal(ref OnOperationStatusChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<IndexChange>.RegisterEvents(Action<IndexChange> onChangeNotification, Action<Exception> onError)
        {
            RegisterEventsInternal(ref OnIndexChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<IndexChange>.UnregisterEvents(Action<IndexChange> onChangeNotification, Action<Exception> onError)
        {
            UnregisterEventsInternal(ref OnIndexChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<DocumentChange>.RegisterEvents(Action<DocumentChange> onChangeNotification, Action<Exception> onError)
        {
            RegisterEventsInternal(ref OnDocumentChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<DocumentChange>.UnregisterEvents(Action<DocumentChange> onChangeNotification, Action<Exception> onError)
        {
            UnregisterEventsInternal(ref OnDocumentChangeNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<CacheInvalidationChange>.RegisterEvents(Action<CacheInvalidationChange> onChangeNotification, Action<Exception> onError)
        {
            RegisterEventsInternal(ref OnCacheInvalidationNotification, onChangeNotification, onError);
        }

        void IChangesConnectionState<CacheInvalidationChange>.UnregisterEvents(Action<CacheInvalidationChange> onChangeNotification, Action<Exception> onError)
        {
            UnregisterEventsInternal(ref OnCacheInvalidationNotification, onChangeNotification, onError);
        }

        public override void Dispose()
        {
            lock (_eventLock)
            {
                base.Dispose();

                OnDocumentChangeNotification = null;
                OnCounterChangeNotification = null;
                OnTimeSeriesChangeNotification = null;
                OnIndexChangeNotification = null;
                OnCacheInvalidationNotification = null;
                OnOperationStatusChangeNotification = null;
            }
        }
    }
}
