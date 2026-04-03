using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using FofX.Serialization;

using ObserveThing;

using SimpleJSON;
using UnityEditorInternal;

namespace FofX.Stateful
{
    public interface IStateValueSet : IEnumerable, IStateNode
    {
        Type elementType { get; }
        int count { get; }
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

    public class StateValueSet<T> : StateNode, IStateValueSet<T>
    {
        public int count => _set.count;
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

        private SetObservable<T> _set;
        private Func<T[]> _getInitialValue;

        public StateValueSet() { }

        public StateValueSet(params T[] elements) : this(() => elements) { }
        public StateValueSet(Func<T[]> getInitialValue)
        {
            _getInitialValue = getInitialValue;
        }

        public StateValueSet(SynchronizationContext context, ILogger logger, string name = "root", params T[] elements) : this(context, logger, name, () => elements) { }
        public StateValueSet(SynchronizationContext context, ILogger logger, string name = "root", Func<T[]> getInitialValue = default) : base(context, logger, name)
        {
            _getInitialValue = getInitialValue;
        }

        protected override void InitializeInternal()
        {
            _set = _getInitialValue == null ?
                new SetObservable<T>(parent.context) : new SetObservable<T>(_getInitialValue(), parent.context);
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

        public IDisposable Subscribe(ISetObserver<T> observer)
            => _set.Subscribe(observer);

        public IDisposable Subscribe(ICollectionObserver<T> observer)
            => _set.Subscribe(observer);

        public override IDisposable Subscribe(IObserver observer)
            => _set.Subscribe(observer);

        public override IDisposable Subscribe(IStateOpObserver observer)
            => _set.Subscribe(new SetObserver<T>(
                onAdd: (_, element) => observer.OnOperation(new StateOpArgs() { opType = OpType.Add, param = element, source = this }),
                onRemove: (_, element) => observer.OnOperation(new StateOpArgs() { opType = OpType.Remove, param = element, source = this }),
                onError: observer.OnError,
                onDispose: () =>
                {
                    if (disposed)
                        observer.OnOperation(new StateOpArgs() { opType = OpType.Dispose, source = this });

                    observer.OnDispose();
                }
            ));

        public override void FromJSON(string json)
        {
            if (json == null)
            {
                Reset();
                return;
            }

            JSONArray array = (JSONArray)JSONNode.Parse(json);
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            Clear();

            foreach (var value in array.Values)
                Add(serializer.fromJSON(value));
        }

        public override string ToJSON(Func<IStateNode, bool> filter)
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
    }
}