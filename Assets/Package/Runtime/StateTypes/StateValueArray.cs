using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FofX.Serialization;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateValueArray : IStateNode, IEnumerable
    {
        int count { get; }
        Type elementType { get; }

        void SetValue(IEnumerable values);
        void Clear();
    }

    public interface IStateValueArray<T> : IValueObservable<IReadOnlyList<T>>, IStateValueArray, IEnumerable<T>
    {
        Type IStateValueArray.elementType => typeof(T);

        void SetValue(IReadOnlyList<T> values);

        void IStateValueArray.SetValue(IEnumerable values)
            => SetValue(values.Cast<T>());
    }

    public class StateValueArray<T> : StateNode, IStateValueArray<T>
    {
        public int count => _values.Count;
        public T this[int index] => _values[index];

        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();
        public override bool derived => _deriveStream != null;

        private IDisposable _deriveStream;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _values.GetEnumerator();

        private List<T> _values = new List<T>();
        private ValueObservable<IReadOnlyList<T>> _value = new ValueObservable<IReadOnlyList<T>>();
        private Func<IReadOnlyList<T>> _getInitialValue;

        public StateValueArray() { }

        public StateValueArray(IReadOnlyList<T> value) : this(() => value) { }
        public StateValueArray(Func<IReadOnlyList<T>> getInitialValue)
        {
            _getInitialValue = getInitialValue;
        }

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        protected override void InitializeInternal()
        {
            _value = _getInitialValue == null ?
                new ValueObservable<IReadOnlyList<T>>(context) : new ValueObservable<IReadOnlyList<T>>(_getInitialValue(), context);
        }

        protected override IStateNode GetChildInternal(string childName)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = default;
            return false;
        }

        protected override void CopyToInternal(IStateNode copyTo)
            => CopyTo((IStateValueArray<T>)copyTo);

        private void SetValueInternal(IEnumerable<T> value)
        {
            _values.Clear();

            if (value != null)
                _values.AddRange(value);

            _value.value = _values.AsReadOnly(); //doing this creates a new wrapper around _values which will trigger _value's OnNext()
        }

        public void SetValue(IReadOnlyList<T> value)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            SetValueInternal(value);
        }

        public void Clear()
        {
            SetValueInternal(null);
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            SetValueInternal(_getInitialValue == null ? null : _getInitialValue());

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public void CopyTo(IStateValueArray<T> copyTo)
        {
            copyTo.SetValue(_value.value);
        }

        public IDisposable Subscribe(IValueObserver<IReadOnlyList<T>> observer)
            => _value.Subscribe(observer);

        public override IDisposable Subscribe(IObserver observer)
            => _value.Subscribe(observer);

        public override IDisposable Subscribe(IStateOpObserver observer)
            => _value.Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnOperation(new StateOpArgs() { opType = OpType.Set, param = x, source = this }),
                onError: observer.OnError,
                onDispose: () =>
                {
                    if (disposed)
                        observer.OnOperation(new StateOpArgs() { opType = OpType.Dispose, source = this });

                    observer.OnDispose();
                }
            ));

        protected override void DisposeInternal()
        {
            _value.Dispose();
            _deriveStream?.Dispose();
        }

        public void Derive(IValueObservable<IReadOnlyList<T>> source)
        {
            _deriveStream = source.Subscribe(
                SetValueInternal,
                immediate: true
            );
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            if (_value.value == null)
                return JSONNull.CreateOrGet();

            JSONArray json = new JSONArray();
            var serializer = JSONSerialization.GetSerializer<T>();

            foreach (var item in _value.value)
                json.Add(serializer.toJSON(item));

            return json;
        }

        public override void FromJSON(JSONNode json)
        {
            if (json.IsNull)
            {
                _value.value = null;
                return;
            }

            var serializer = JSONSerialization.GetSerializer<T>();
            SetValueInternal(((JSONArray)json).Linq.Select(x => serializer.fromJSON(x)));
        }
    }
}