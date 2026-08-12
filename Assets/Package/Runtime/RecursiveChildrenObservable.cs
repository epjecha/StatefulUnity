using System;
using System.Collections.Generic;
using ObserveThing;

namespace FofX.Stateful
{
    public class RecursiveChildrenObservable : IDisposable
    {
        private IDisposable _stream;
        private ISetOperand<IStateNode> _operand;
        private Dictionary<IStateNode, IDisposable> _subscriptions = new Dictionary<IStateNode, IDisposable>();
        private bool _disposed;
        private uint _priority;

        public RecursiveChildrenObservable(IStateNode source, ISetOperand<IStateNode> operand)
        {
            _operand = operand;
            _stream = source.Subscribe(onDispose: Dispose, immediate: true);
            _priority = source.context.AllocateObserverPriority();
            SubscribeRecursive(source);
        }

        private void SubscribeRecursive(IStateNode node)
        {
            _subscriptions.Add(node, null);
            _operand.Add(node);

            _subscriptions[node] = node.ObservableChildren().Subscribe(
                onAdd: SubscribeRecursive,
                onRemove: UnsubscribeRecursive,
                onError: _operand.OnError,
                immediate: true,
                priority: _priority
            );
        }

        private void UnsubscribeRecursive(IStateNode node)
        {
            var subscription = _subscriptions[node];
            _subscriptions.Remove(node);
            subscription?.Dispose();

            _operand.Remove(node);

            foreach (var child in node.children)
                UnsubscribeRecursive(child);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _stream.Dispose();

            foreach (var entry in _subscriptions.Values)
                entry?.Dispose();

            _operand.OnDisposed();
        }
    }
}