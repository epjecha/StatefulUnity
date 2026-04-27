using System;
using System.Collections.Generic;
using ObserveThing;

namespace FofX.Stateful
{
    public class ChildrenObservable : IDisposable
    {
        private IDisposable _sourceSubscription;
        private ICollectionObserver<IStateNode> _receiver;
        private CollectionIdProvider _idProvider;
        private Dictionary<IStateNode, uint> _children = new Dictionary<IStateNode, uint>();
        private bool _initializing;
        private bool _disposed;

        public ChildrenObservable(IStateNode source, ICollectionObserver<IStateNode> receiver)
        {
            _idProvider = new CollectionIdProvider(_children.ContainsValue);
            _receiver = receiver;

            _initializing = true;
            _sourceSubscription = source.Subscribe(onOperation: HandleSourceChanged, onDispose: Dispose, immediate: true);
            _initializing = false;

            foreach (var child in source.children)
            {
                var id = _idProvider.GetUnusedId();
                _children.Add(child, id);
                receiver.OnAdd(id, child);
            }
        }

        private void HandleSourceChanged(IReadOnlyList<IStateOperation> operations)
        {
            if (_initializing)
                return;

            foreach (var op in operations)
            {
                if (op.opType == OpType.Dispose)
                {
                    Dispose();
                    break;
                }

                if (op.child == null)
                    continue;

                if (op.opType == OpType.Add)
                {
                    var id = _idProvider.GetUnusedId();
                    _children.Add(op.child, id);
                    _receiver.OnAdd(id, op.child);
                }
                else if (op.opType == OpType.Remove)
                {
                    var id = _children[op.child];
                    _children.Remove(op.child);
                    _receiver.OnRemove(id, op.child);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _sourceSubscription?.Dispose();
            _receiver.OnDispose();
        }
    }
}