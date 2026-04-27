using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using FofX.Serialization;

using ObserveThing;

using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateValueSet : IEnumerable, IStateNode
    {
        Type elementType { get; }
        int Count { get; }
        bool Add(object element);
        bool Remove(object element);
        bool Contains(object element);
        void Clear();
        void CopyTo(IStateList copyTo);
    }

    public interface IStateValueSet<T> : ISetObservable<T>, IStateValueSet, IEnumerable<T>
    {
        Type IStateValueSet.elementType => typeof(T);
        bool Add(T element);
        bool Remove(T element);
        bool Contains(T element);
        void CopyTo(IStateValueSet<T> copyTo);

        bool IStateValueSet.Add(object element)
            => Add((T)element);

        bool IStateValueSet.Remove(object element)
            => Remove((T)element);

        bool IStateValueSet.Contains(object element)
            => Contains((T)element);

        void IStateValueSet.CopyTo(IStateList copyTo)
            => CopyTo((IStateValueSet<T>)copyTo);
    }

    public class StateValueSet<T> : StateNode<T>, IStateValueSet<T>
    {
        public int Count => _set.Count;
        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();
        public override bool derived => _deriveStream != null;
        private IDisposable _deriveStream;

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ((IEnumerable<T>)_set).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_set).GetEnumerator();

        private ObservableSet<T> _set;
        private Func<T[]> _getInitialValue;
        private List<StateOpArgs<T>> _initOps = new List<StateOpArgs<T>>();

        public StateValueSet() : this(default(Func<T[]>)) { }

        public StateValueSet(params T[] elements) : this(() => elements) { }
        public StateValueSet(Func<T[]> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
        }

        protected override void InitializeInternal()
        {
            _set = _getInitialValue == null ?
                new ObservableSet<T>(context) : new ObservableSet<T>(context, _getInitialValue());

            _set.Subscribe(HandleInternalOperation, immediate: true);
        }

        protected override IReadOnlyList<StateOpArgs<T>> GetInitializationOperations()
        {
            _initOps.Clear();
            _initOps.AddRange(_set.ElementsWithIds.Select(x => new StateOpArgs<T>(this, OpType.Add, x.element, x.id)));
            return _initOps;
        }

        private void HandleInternalOperation(IReadOnlyList<SetOpArgs<T>> ops)
        {
            if (ops == null)
                return;

            foreach (var op in ops)
            {
                EnqueuePendingOperation(new StateOpArgs<T>(
                    source: this,
                    opType: op.isRemove ? OpType.Remove : OpType.Add,
                    param: op.element,
                    collectionElementId: op.id
                ));
            }
        }

        protected override IStateNode GetChildInternal(string childName)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = default;
            return false;
        }

        protected override void CopyToInternal(IStateNode copyTo)
            => CopyTo((IStateValueSet<T>)copyTo);

        public bool Add(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            LogOperation(OpType.Add, element);

            return _set.Add(element);
        }

        public bool Remove(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            LogOperation(OpType.Remove, element);

            return _set.Remove(element);
        }

        public bool Contains(T element)
            => _set.Contains(element);

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            _set.Clear();
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            _set.Clear();

            if (_getInitialValue != null)
                _set.AddRange(_getInitialValue());

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public void CopyTo(IStateValueSet<T> copyTo)
        {
            foreach (var toRemove in copyTo.Except(_set).ToArray())
                copyTo.Remove(toRemove);

            foreach (var toAdd in _set.Except(copyTo).ToArray())
                copyTo.Add(toAdd);
        }

        public override void FromJSON(JSONNode json)
        {
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

            foreach (T value in _set)
                array.Add(serializer.toJSON(value));

            return array;
        }

        protected override void DisposeInternal()
        {
            _set.Dispose();
            _deriveStream?.Dispose();
        }

        public void Derive(IListObservable<T> source)
        {
            _deriveStream = source.Subscribe(
                onAdd: item => _set.Add(item),
                onRemove: item => _set.Remove(item),
                immediate: true
            );
        }

        public IDisposable Subscribe(ISetObserver<T> observer)
            => Subscribe(new Observer<StateOpArgs<T>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        int index = 0;
                        foreach (var pair in _set.ElementsWithIds)
                        {
                            observer.OnAdd(pair.id, pair.element);
                            index++;
                        }

                        return;
                    }

                    foreach (var op in ops)
                    {
                        if (op.opType == OpType.Add)
                        {
                            observer.OnAdd(op.collectionElementId, op.param);
                        }
                        else if (op.opType == OpType.Remove)
                        {
                            observer.OnRemove(op.collectionElementId, op.param);
                        }
                    }
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));

        public IDisposable Subscribe(ICollectionObserver<T> observer)
            => Subscribe(new Observer<StateOpArgs<T>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        int index = 0;
                        foreach (var pair in _set.ElementsWithIds)
                        {
                            observer.OnAdd(pair.id, pair.element);
                            index++;
                        }

                        return;
                    }

                    foreach (var op in ops)
                    {
                        if (op.opType == OpType.Add)
                        {
                            observer.OnAdd(op.collectionElementId, op.param);
                        }
                        else if (op.opType == OpType.Remove)
                        {
                            observer.OnRemove(op.collectionElementId, op.param);
                        }
                    }
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));
    }
}