using System;
using System.Collections.Generic;
using System.Linq;
using ObserveThing;

namespace FofX.Stateful
{
    public class RecursiveChildrenObservable : IDisposable
    {
        private class EntryData
        {
            public uint id;
            public IDisposable subscription;
        }

        private IDisposable _stream;
        private ISetObserver<IStateNode> _receiver;
        private CollectionIdProvider _idProvider;
        private Dictionary<IStateNode, EntryData> _entries = new Dictionary<IStateNode, EntryData>();
        private bool _disposed;

        public RecursiveChildrenObservable(IStateNode source, ISetObserver<IStateNode> receiver)
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

            entryData.subscription = node.ObservableChildren().Subscribe(
                onAdd: SubscribeRecursive,
                onRemove: UnsubscribeRecursive,
                onError: _receiver.OnError,
                immediate: true
            );

            _receiver.OnAdd(entryData.id, node);
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
}