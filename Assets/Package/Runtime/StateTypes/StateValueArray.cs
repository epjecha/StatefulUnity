using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FofX.Serialization;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IReadOnlyStateValueArray : IStateNode, IEnumerable, IValueObservable
    {
        int count { get; }
        Type elementType { get; }
        object this[int index] { get; }
    }

    public interface IStateValueArray : IReadOnlyStateValueArray
    {
        void SetValue(IEnumerable values);
        void Clear();
    }

    public interface IStateValueArrayViewMutator<T>
    {
        void SetValue(IReadOnlyList<T> value);
        void Clear();
    }

    public class StateValueArrayView<T> : ReadOnlyStateValueArray<T>
    {
        public override bool isView => true;
        private Mutator _mutator;
        private bool _viewInitialized;
        private IDisposable _subscription;

        private class Mutator : IStateValueArrayViewMutator<T>
        {
            public Action<IReadOnlyList<T>> setValue;
            public Action clear;

            public void SetValue(IReadOnlyList<T> value)
                => setValue(value);

            public void Clear()
                => clear();
        }

        public StateValueArrayView() : base()
        {
            _mutator = new Mutator()
            {
                setValue = SetValueInternal,
                clear = () => SetValueInternal(new T[0])
            };
        }

        public void InitializeView(Action<IStateValueArrayViewMutator<T>> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public void InitializeView(Func<IStateValueArrayViewMutator<T>, IDisposable> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            logger.Warning($"{nodePath} is a view. \'CopyTo\' will be ignored.");
        }

        public override void FromJSON(JSONNode json)
        {
            logger.Warning($"{nodePath} is a view. \'FromJSON\' will be ignored.");
        }

        public override void Reset()
        {
            logger.Warning($"{nodePath} is a view. \'Reset\' will be ignored for this object. Children will be reset.");
        }

        protected override void DisposeInternal()
        {
            base.DisposeInternal();
            _subscription?.Dispose();
        }
    }

    public class StateValueArray<T> : ReadOnlyStateValueArray<T>,
        IStateValueArray,
        IValueObservable<IReadOnlyList<T>>
    {
        public override bool isView => false;
        private Action<StateValueArray<T>> _initializer;

        public StateValueArray() : this(default(Action<StateValueArray<T>>)) { }
        public StateValueArray(T[] value) : this(x => x.SetValue(value)) { }
        public StateValueArray(Action<StateValueArray<T>> initializer) : base()
        {
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            base.InitializeInternal();
            _initializer?.Invoke(this);
        }

        public void SetValue(T[] value)
            => SetValueInternal(value);

        public void Clear()
            => SetValueInternal(new T[0]);

        void IStateValueArray.SetValue(IEnumerable values)
            => SetValue(values.Cast<T>().ToArray());

        public override void Reset()
        {
            logger.Generic(LogLevel.Trace, $"Resetting {nodePath}");
            SetValueInternal(new T[0]);
            _initializer?.Invoke(this);
        }

        public override void FromJSON(JSONNode json)
        {
            var serializer = JSONSerialization.GetSerializer<T>();
            SetValueInternal(((JSONArray)json).Linq.Select(x => serializer.fromJSON(x)).ToArray());
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateValueArray<T>)copyTo;
            target.SetValueInternal(GetValueInternal().ToArray());
        }
    }

    public abstract class ReadOnlyStateValueArray<T> : StateNode,
        IReadOnlyStateValueArray,
        IValueObservable<IReadOnlyList<T>>,
        IEnumerable<T>
    {
        public int count => _value.value.Count;
        public T this[int index] => _value.value[index];

        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();

        object IReadOnlyStateValueArray.this[int index] => this[index];
        Type IReadOnlyStateValueArray.elementType => typeof(T);

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => _value.value.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _value.value.GetEnumerator();

        private ObservableValue<IReadOnlyList<T>> _value;

        public ReadOnlyStateValueArray() : base() { }

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        protected override void InitializeInternal()
        {
            _value = new ObservableValue<IReadOnlyList<T>>(context, new T[0]);
        }

        protected IReadOnlyList<T> GetValueInternal()
            => _value.value;

        protected void SetValueInternal(IReadOnlyList<T> value)
        {
            value = value ?? new T[0];
            logger.Trace(Utility.FormatOperationLog(OpType.Set, this, $"{typeof(T).Name}[{string.Join(", ", value.Select(x => x.ToString()))}]"));
            _value.value = value;
        }

        protected override IStateNode GetChildInternal(string name)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string name, out IStateNode child)
        {
            child = default;
            return false;
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray json = new JSONArray();
            var serializer = JSONSerialization.GetSerializer<T>();

            foreach (var item in _value.value)
                json.Add(serializer.toJSON(item));

            return json;
        }

        protected override void DisposeInternal()
        {
            _value.Dispose();
        }

        // IObserver<StateOperation>
        public override IDisposable Subscribe(ObserveThing.IObserver<IOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public override IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IValueObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IValueObserver<IReadOnlyList<T>> observer, bool immediate = false, uint? priority = null)
            => _value.Subscribe(observer, immediate, priority);
    }
}