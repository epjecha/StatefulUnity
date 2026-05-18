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

    public interface IStateValueArray<T> : IValueObservable<IReadOnlyList<T>>, IStateValueArray, IEnumerable<T>
    {
        Type IStateValueArray.elementType => typeof(T);

        void SetValue(IReadOnlyList<T> values);

        void IStateValueArray.SetValue(IEnumerable values)
            => SetValue(values.Cast<T>());
    }

    public class StateValueArray<T> : StateNode<IReadOnlyList<T>>, IStateValueArray<T>
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
        private ObservableValue<IReadOnlyList<T>> _value = new ObservableValue<IReadOnlyList<T>>();
        private Func<IReadOnlyList<T>> _getInitialValue;
        private List<StateOpArgs<IReadOnlyList<T>>> _initOps = new List<StateOpArgs<IReadOnlyList<T>>>();

        public StateValueArray() : this(default(Func<IReadOnlyList<T>>)) { }

        public StateValueArray(IReadOnlyList<T> value) : this(() => value) { }
        public StateValueArray(Func<IReadOnlyList<T>> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
            _initOps = new List<StateOpArgs<IReadOnlyList<T>>>() { new StateOpArgs<IReadOnlyList<T>>(this, OpType.Set, _values) };
        }

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        protected override void InitializeInternal()
        {
            _value = _getInitialValue == null ?
                new ObservableValue<IReadOnlyList<T>>(context) : new ObservableValue<IReadOnlyList<T>>(context, _getInitialValue());

            _value.Subscribe(HandleInternalOperation, immediate: true);
        }

        protected override IReadOnlyList<StateOpArgs<IReadOnlyList<T>>> GetInitializationOperations()
            => _initOps;

        private void HandleInternalOperation(IReadOnlyList<IReadOnlyList<T>> ops)
        {
            if (ops == null)
                return;

            foreach (var op in ops)
            {
                EnqueuePendingOperation(new StateOpArgs<IReadOnlyList<T>>(
                    source: this,
                    opType: OpType.Set,
                    param: op
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

        protected override void DisposeInternal()
        {
            _value.Dispose();
            _deriveStream?.Dispose();
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
            if (derived)
            {
                logger.Warning($"Attempted to write to derived state from JSON. This will be ignored. Path: {nodePath}");
                return;
            }

            if (json.IsNull)
            {
                _value.value = null;
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

        public IDisposable Subscribe(IValueObserver<IReadOnlyList<T>> observer)
            => Subscribe(new Observer<StateOpArgs<IReadOnlyList<T>>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        observer.OnNext(_value.value);
                        return;
                    }

                    foreach (var op in ops)
                        observer.OnNext(op.param);
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));

        public IDisposable Subscribe(IValueObserver observer)
            => Subscribe(new Observer<StateOpArgs<IReadOnlyList<T>>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        observer.OnNext(_value.value);
                        return;
                    }

                    foreach (var op in ops)
                        observer.OnNext(op.param);
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));
    }
}