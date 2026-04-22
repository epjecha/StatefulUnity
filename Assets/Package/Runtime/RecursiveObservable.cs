using System;
using System.Collections.Generic;
using System.Linq;
using ObserveThing;

namespace FofX.Stateful
{
    public class DeepChildrenObservable : IDisposable
    {
        private class EntryData
        {
            public uint id;
            public IDisposable subscription;
        }

        private IDisposable _stream;
        private ICollectionObserver<IStateNode> _receiver;
        private CollectionIdProvider _idProvider;
        private Dictionary<IStateNode, EntryData> _entries = new Dictionary<IStateNode, EntryData>();
        private bool _disposed;

        public DeepChildrenObservable(IStateNode source, ICollectionObserver<IStateNode> receiver)
        {
            _idProvider = new CollectionIdProvider(x => _entries.Values.Select(x => x.id).Contains(x));
            _receiver = receiver;
            _stream = source.Subscribe(onDispose: Dispose, immediate: true);
            SubscribeRecursive(source);
        }

        private void SubscribeRecursive(IStateNode node)
        {
            var entryData = new EntryData() { id = _idProvider.GetUnusedId() };
            _entries.Add(node, entryData);

            if (node is IStateList || node is IStateDictionary)
            {
                entryData.subscription = node.Subscribe(
                    onOperation: HandleSourceChanged,
                    onError: _receiver.OnError,
                    immediate: true
                );
            }

            _receiver.OnAdd(entryData.id, node);

            foreach (var child in node.children)
                SubscribeRecursive(child);
        }

        private void UnsubscribeRecursive(IStateNode node)
        {
            var entryData = _entries[node];
            _entries.Remove(node);
            entryData.subscription?.Dispose();

            _receiver.OnRemove(entryData.id, node);

            foreach (var child in node.children)
                UnsubscribeRecursive(child);
        }

        private void HandleSourceChanged(IReadOnlyList<IStateOperation> operations)
        {
            if (operations == null)
                return;

            foreach (var op in operations)
            {
                if (op.opType == OpType.Add)
                {
                    SubscribeRecursive(op.child);
                }
                else if (op.opType == OpType.Remove)
                {
                    UnsubscribeRecursive(op.child);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _stream.Dispose();

            foreach (var entry in _entries.Values)
                entry.subscription?.Dispose();

            _receiver.OnDispose();
        }
    }

    public class CombineOperationsObservable : IPendingObserver, IDisposable
    {
        public uint priority { get; }
        public bool immediate => _receiver.immediate;
        public bool disposed { get; private set; }

        private bool _pending;
        private List<IStateOperation> _pendingOperations;
        private List<IStateOperation> _pendingOperations1 = new List<IStateOperation>();
        private List<IStateOperation> _pendingOperations2 = new List<IStateOperation>();

        private ObservationContext _context;
        private IDisposable _sourceStream;

        private ObserveThing.IObserver<IStateOperation> _receiver;
        private Dictionary<IStateNode, IDisposable> _elementSubscriptions = new Dictionary<IStateNode, IDisposable>();

        public CombineOperationsObservable(ObservationContext context, ISetObservable<IStateNode> source, ObserveThing.IObserver<IStateOperation> receiver)
        {
            _context = context ?? Settings.DefaultObservationContext;

            priority = context.AllocateObserverPriority();

            SwitchPendingOperationsList();

            _receiver = receiver;

            _sourceStream = source.Subscribe(
                onAdd: HandleSourceStateAdded,
                onRemove: HandleSourceStateRemoved,
                onError: receiver.OnError,
                onDispose: Dispose,
                immediate: true
            );

            receiver.OnOperation(null);
        }

        private void HandleSourceStateAdded(IStateNode node)
        {
            _elementSubscriptions.Add(
                node,
                node.Subscribe(
                    onOperation: HandleSourceChanged,
                    onError: _receiver.OnError,
                    onDispose: () => HandleSourceStateRemoved(node),
                    immediate: true
                )
            );
        }

        private void HandleSourceStateRemoved(IStateNode node)
        {
            if (disposed)
                return;

            var subscription = _elementSubscriptions[node];
            subscription.Dispose();
            _elementSubscriptions.Remove(node);
        }

        private void HandleSourceChanged(IReadOnlyList<IStateOperation> ops)
        {
            if (ops == null)
                return;

            foreach (var op in ops)
                EnqueuePendingOperation(op.Clone());
        }

        private void SwitchPendingOperationsList()
        {
            if (_pendingOperations == _pendingOperations1)
            {
                _pendingOperations = _pendingOperations2;
            }
            else
            {
                _pendingOperations = _pendingOperations1;
            }
        }

        protected void EnqueuePendingOperation(IStateOperation operation)
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().Name);

            _pendingOperations.Add(operation);

            if (!_pending)
                _context.RegisterPendingObserver(this);

            _context.NotifyPendingObserversIfNecessary();
        }

        public void SendNext()
        {
            if (_pendingOperations.Count == 0)
                return;

            var ops = _pendingOperations;
            SwitchPendingOperationsList();
            _pending = false;

            try
            {
                _receiver.OnOperation(ops);
            }
            catch (Exception exc)
            {
                _receiver.OnError(exc);
            }

            ops.Clear();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            _sourceStream.Dispose();

            foreach (var subscription in _elementSubscriptions.Values)
                subscription.Dispose();

            _context.DeallocateObserverPriority(priority);

            _receiver.OnDispose();
        }
    }
}