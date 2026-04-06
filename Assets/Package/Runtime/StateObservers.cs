using System;
using System.Collections.Generic;
using System.Linq;
using ObserveThing;

namespace FofX.Stateful
{
    public static class StateObservers
    {
        public static IDisposable Subscribe(this IStateNode target, Action<StateOpArgs> onOperation = default, Action<Exception> onError = default, Action onDispose = default)
            => target.Subscribe(new StateOpObserver(onOperation, onError, onDispose));

        public static IDisposable Subscribe(Action<StateOpArgs> onOperation, params IStateNode[] nodes)
            => Subscribe(onOperation, null, null, nodes);

        public static IDisposable Subscribe(Action<StateOpArgs> onOperation, Action<Exception> onError, Action onDispose, params IStateNode[] nodes)
        {
            var observer = new StateOpObserver(onOperation, onError, onDispose);
            return new ComposedDisposable(nodes.Select(x => x.Subscribe(observer)).ToArray());
        }

        public static IDisposable SubscribeAll(this IStateNode target, StateOpObserver observer, bool muteInitializations = true)
            => new SubscribeRecursive(target, observer, muteInitializations);

        public static IDisposable SubscribeAll(this IStateNode target, Action<StateOpArgs> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool muteInitializations = true)
            => target.SubscribeAll(new StateOpObserver(onOperation, onError, onDispose), muteInitializations);

        public static IDisposable SubscribeAll(Action<StateOpArgs> onOperation = default, params IStateNode[] nodes)
            => SubscribeAll(onOperation, null, null, true, nodes);

        public static IDisposable SubscribeAll(Action<StateOpArgs> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool muteInitializations = true, params IStateNode[] nodes)
        {
            var observer = new StateOpObserver(onOperation, onError, onDispose);
            return new ComposedDisposable(nodes.Select(x => x.SubscribeAll(observer, muteInitializations)).ToArray());
        }

        private class SubscribeRecursive : IDisposable
        {
            private IStateNode _target;
            private Dictionary<IStateNode, IDisposable> _streams = new Dictionary<IStateNode, IDisposable>();

            private StateOpObserver _observer;
            private StateOpObserver _internalObserver;
            private bool _muteInitializations;
            private bool _initializing = false;
            private bool _disposed;

            public SubscribeRecursive(IStateNode target, StateOpObserver observer, bool muteInitializations = true)
            {
                _target = target;
                _observer = observer;
                _muteInitializations = muteInitializations;
                _internalObserver = new StateOpObserver(
                    onOperation: HandleOperation,
                    onError: observer.OnError
                );

                SubscribeToNode(target);
            }

            private void HandleOperation(StateOpArgs args)
            {
                if (_initializing && _muteInitializations)
                    return;

                if (args.opType == OpType.Add)
                {
                    if (args.child != null)
                        SubscribeToNode(args.child);
                }
                else if (args.opType == OpType.Remove)
                {
                    if (args.child != null)
                        UnsubscribeFromNode(args.child);
                }
                else if (args.source == _target && args.opType == OpType.Dispose)
                {
                    Dispose();
                }

                _observer.OnOperation(args);
            }

            private void SubscribeToNode(IStateNode node)
            {
                bool wasInitializing = _initializing;
                _initializing = true;
                _streams.Add(node, node.Subscribe(_internalObserver));
                _initializing = wasInitializing;

                if (node is StateObject) //collections will automatically send Adds for all their elements
                {
                    foreach (var child in node.children)
                        SubscribeToNode(child);
                }
            }

            private void UnsubscribeFromNode(IStateNode node)
            {
                if (_streams.TryGetValue(node, out var stream))
                {
                    _streams.Remove(node);
                    stream.Dispose();
                }

                foreach (var child in node.children)
                    UnsubscribeFromNode(child);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;

                foreach (var stream in _streams.Values)
                    stream.Dispose();

                _streams.Clear();

                _observer.OnDispose();
            }
        }
    }
}