using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using FofX.Serialization;

using ObserveThing;

using SimpleJSON;

namespace FofX.Stateful
{
    public struct StateSetOperation<T> : IStateOperation
    {
        public IStateNode source { get; set; }
        public OpType opType { get; set; }
        public T element { get; set; }
        public uint elementId { get; set; }

        object IStateOperation.param => element;
        public IStateNode child => null;

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={element}";
        }
    }

    public interface IStateValueSet : IEnumerable, IStateNode, ICollectionObservable
    {
        Type elementType { get; }
        int Count { get; }
        bool Add(object element);
        bool Remove(object element);
        bool Contains(object element);
        void Clear();
    }

    public class StateValueSet<T> : StateNode<ISetObserver<T>, StateSetOperation<T>>,
        IStateValueSet,
        ISetObservable<T>,
        IEnumerable<T>
    {
        public int Count => _set.Count;
        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();
        public override bool derived => _deriveStream != null;

        Type IStateValueSet.elementType => throw new NotImplementedException();

        private IDisposable _deriveStream;

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ((IEnumerable<T>)_set).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_set).GetEnumerator();

        private Dictionary<T, uint> _set = new Dictionary<T, uint>();
        private CollectionIdProvider _idProvider;
        private Func<T[]> _getInitialValue;

        public StateValueSet() : this(default(Func<T[]>)) { }

        public StateValueSet(params T[] elements) : this(() => elements) { }
        public StateValueSet(Func<T[]> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
            _idProvider = new CollectionIdProvider(_set.ContainsValue);
        }

        protected override void InitializeInternal()
        {
            if (_getInitialValue == null)
                return;

            foreach (var element in _getInitialValue())
                _set.Add(element, _idProvider.GetUnusedId());
        }

        protected override IEnumerable<StateSetOperation<T>> GetInitializationOperations()
        {
            foreach (var kvp in _set)
                yield return new() { source = this, elementId = kvp.Value, element = kvp.Key, opType = OpType.Add };
        }

        protected override void SendStateOperation(ISetObserver<T> observer, StateSetOperation<T> operation)
        {
            if (operation.opType == OpType.Add)
            {
                observer.OnAdd(operation.elementId, operation.element);
            }
            else if (operation.opType == OpType.Remove)
            {
                observer.OnRemove(operation.elementId, operation.element);
            }
            else
            {
                throw new Exception($"Unhandled op type {operation.opType}");
            }
        }

        protected override IStateNode GetChildInternal(string childName)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = default;
            return false;
        }

        private bool AddInternal(T element)
        {
            if (_set.ContainsKey(element))
                return false;

            var id = _idProvider.GetUnusedId();
            _set.Add(element, id);
            EnqueuePendingStateOperation(new() { source = this, elementId = id, element = element, opType = OpType.Add });
            return true;
        }

        private bool RemoveInternal(T element)
        {
            if (!_set.TryGetValue(element, out var id))
                return false;

            _set.Remove(element);
            EnqueuePendingStateOperation(new() { source = this, elementId = id, element = element, opType = OpType.Remove });
            return true;
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateValueSet<T>)copyTo;

            foreach (var toRemove in target.Except(_set.Keys).ToArray())
                target.Remove(toRemove);

            foreach (var toAdd in _set.Keys.Except(target).ToArray())
                target.Add(toAdd);
        }

        public bool Add(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return AddInternal(element);
        }

        public bool Remove(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return RemoveInternal(element);
        }

        public bool Contains(T element)
            => _set.ContainsKey(element);

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            foreach (var element in _set.Keys.ToArray())
                Remove(element);
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            Clear();

            if (_getInitialValue != null)
            {
                foreach (var element in _getInitialValue())
                    AddInternal(element);
            }

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public override void FromJSON(JSONNode json)
        {
            if (derived)
            {
                logger.Warning($"Attempted to write to derived state from JSON. This will be ignored. Path: {nodePath}");
                return;
            }

            if (json == null)
            {
                Reset();
                return;
            }

            JSONArray array = (JSONArray)json;
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            Clear();

            foreach (var value in array.Values)
                Add(serializer.fromJSON(value));
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            foreach (T value in _set.Keys)
                array.Add(serializer.toJSON(value));

            return array;
        }

        protected override void DisposeInternal()
        {
            _deriveStream?.Dispose();
        }

        public void Derive(IListObservable<T> source)
        {
            _deriveStream = source.Subscribe(
                onAdd: item => AddInternal(item),
                onRemove: item => RemoveInternal(item),
                immediate: true
            );
        }

        bool IStateValueSet.Add(object element)
        {
            throw new NotImplementedException();
        }

        bool IStateValueSet.Remove(object element)
        {
            throw new NotImplementedException();
        }

        bool IStateValueSet.Contains(object element)
        {
            throw new NotImplementedException();
        }

        public override IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnNext(new StateSetOperation<T>() { source = this, elementId = id, element = element, opType = OpType.Add }),
                onRemove: (id, element) => observer.OnNext(new StateSetOperation<T>() { source = this, elementId = id, element = element, opType = OpType.Remove }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ISetObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnAdd(id, element),
                onRemove: (id, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver observer, bool immediate, uint? priority)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnAdd(id, element),
                onRemove: (id, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver<T> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnAdd(id, element),
                onRemove: (id, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}