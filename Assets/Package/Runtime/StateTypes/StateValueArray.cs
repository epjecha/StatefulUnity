using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FofX.Serialization;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateValueArray : IStateNode, IEnumerable, IValueObservable
    {
        int count { get; }
        Type elementType { get; }

        void SetValue(IEnumerable values);
        void Clear();
    }

    public class StateValueArray<T> : StateNode<IValueObserver<IReadOnlyList<T>>, StateValueOperation<IReadOnlyList<T>>>,
        IStateValueArray,
        IValueObservable<IReadOnlyList<T>>,
        IEnumerable<T>
    {
        public int count => _values.Count;
        public T this[int index] => _values[index];

        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();
        public override bool derived => _deriveStream != null;

        Type IStateValueArray.elementType => typeof(T);

        private IDisposable _deriveStream;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _values.GetEnumerator();

        private List<T> _values = new List<T>();
        private IReadOnlyList<T> _externalList;
        private Func<T[]> _getInitialValue;

        public StateValueArray() : this(default(Func<T[]>)) { }

        public StateValueArray(T[] value) : this(() => value) { }
        public StateValueArray(Func<T[]> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
            _externalList = _values.AsReadOnly();
        }

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        protected override void InitializeInternal()
        {
            if (_getInitialValue == null)
                return;

            _values.AddRange(_getInitialValue());
        }

        protected override IEnumerable<StateValueOperation<IReadOnlyList<T>>> GetInitializationOperations()
        {
            yield return new StateValueOperation<IReadOnlyList<T>>() { source = this, value = _externalList };
        }

        protected override void SendStateOperation(IValueObserver<IReadOnlyList<T>> observer, StateValueOperation<IReadOnlyList<T>> operation)
        {
            observer.OnNext(operation.value);
        }

        protected override IStateNode GetChildInternal(string childName)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = default;
            return false;
        }

        private void SetValueInternal(IEnumerable<T> value)
        {
            _values.Clear();

            if (value != null)
                _values.AddRange(value);

            EnqueuePendingStateOperation(new() { source = this, value = _externalList });
        }

        public void SetValue(IEnumerable<T> value)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            SetValueInternal(value);
        }

        public void Clear()
        {
            SetValueInternal(null);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateValueArray<T>)copyTo;
            target.SetValue(_externalList);
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            SetValueInternal(_getInitialValue == null ? null : _getInitialValue());

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        protected override void DisposeInternal()
        {
            _deriveStream?.Dispose();
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray json = new JSONArray();
            var serializer = JSONSerialization.GetSerializer<T>();

            foreach (var item in _values)
                json.Add(serializer.toJSON(item));

            return json;
        }

        public override void FromJSON(JSONNode json)
        {
            if (derived)
            {
                logger.Warning($"Attempted to write to derived state from JSON. This will be ignored. Path: {nodePath}");
                return;
            }

            var serializer = JSONSerialization.GetSerializer<T>();
            SetValueInternal(((JSONArray)json).Linq.Select(x => serializer.fromJSON(x)));
        }

        public void Derive(IValueObservable<IReadOnlyList<T>> source)
        {
            _deriveStream = source.Subscribe(
                SetValueInternal,
                immediate: true
            );
        }

        void IStateValueArray.SetValue(IEnumerable values)
            => SetValue(values.Cast<T>().ToArray());

        public override IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnNext(new StateValueOperation<IReadOnlyList<T>>() { source = this, value = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IValueObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: observer.OnNext,
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}