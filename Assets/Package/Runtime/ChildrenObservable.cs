using System;
using ObserveThing;

namespace FofX.Stateful
{
    public class ChildrenObservable : IDisposable
    {
        private IDisposable _sourceSubscription;
        private ObservableSet<IStateNode> _operand;
        private bool _initializing;
        private bool _disposed;

        public ChildrenObservable(IStateNode source, ObservableSet<IStateNode> operand)
        {
            _operand = operand;

            _initializing = true;
            _sourceSubscription = source.Subscribe(
                onNext: HandleSourceChanged,
                onError: operand.OnError,
                onDispose: Dispose,
                immediate: true
            );

            _initializing = false;

            foreach (var child in source.children)
                _operand.Add(child);
        }

        private void HandleSourceChanged(StateOperation operation)
        {
            if (_initializing)
                return;

            if (operation.child == null)
                return;

            if (operation.opType == OpType.Add)
            {
                _operand.Add(operation.child);
            }
            else if (operation.opType == OpType.Remove)
            {
                _operand.Remove(operation.child);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _sourceSubscription?.Dispose();
            _operand.Dispose();
        }
    }
}