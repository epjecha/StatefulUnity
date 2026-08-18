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

        protected override void Reset()
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
            SetValueInternal(new T[0]);
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            _initializer?.Invoke(this);
        }

        public void SetValue(T[] value)
            => SetValueInternal(value);

        public void Clear()
            => SetValueInternal(new T[0]);

        void IStateValueArray.SetValue(IEnumerable values)
            => SetValue(values.Cast<T>().ToArray());

        protected override void Reset()
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
            target.SetValue(_value.ToArray());
        }
    }

    public abstract class ReadOnlyStateValueArray<T> : ObservableValueBase<IReadOnlyList<T>>,
        IReadOnlyStateValueArray,
        IValueObservable<IReadOnlyList<T>>,
        IEnumerable<T>
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public abstract bool isView { get; }

        public int count => _value.Count;
        public T this[int index] => _value[index];

        int IStateNode.childCount => 0;
        IEnumerable<IStateNode> IStateNode.children => EmptyChildren();

        object IReadOnlyStateValueArray.this[int index] => this[index];
        Type IReadOnlyStateValueArray.elementType => typeof(T);

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => _value.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _value.GetEnumerator();

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        public ReadOnlyStateValueArray() : base(default) { }

        protected virtual void InitializeInternal() { }

        protected override void SendOperation(IValueObserver<IReadOnlyList<T>> observer, ValueOp<IReadOnlyList<T>> operation)
        {
            logger.Trace($"Notifying {Utility.FormatOperationLog(OpType.Set, this, $"[{string.Join(", ", operation.value)}]")}");
            base.SendOperation(observer, operation);
        }

        protected abstract void Reset();

        // IStateNode
        public void Initialize(ObservationContext context, ILogger logger, string name = "root")
        {
            this.context = context;
            root = this;
            this.logger = logger;
            nodeName = name;
            nodePath = name;
            InitializeInternal();
            initialized = true;
            ((IStateNode)this).PostInitialize();
        }

        public void Initialize(IStateNode parent, string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (initialized)
                throw new Exception($"{nodePath} has already been initialized");

            context = parent.context;
            root = parent.root;
            this.parent = parent;
            logger = parent.logger;
            nodeName = name;
            nodePath = $"{parent.nodePath}/{nodeName}";
            InitializeInternal();
            initialized = true;
        }

        void IStateNode.PostInitialize() { }

        public void Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
        }

        IStateNode IStateNode.GetChild(string name)
            => throw new NotImplementedException();

        bool IStateNode.TryGetChild(string name, out IStateNode child)
        {
            child = default;
            return false;
        }

        public abstract void CopyTo(IStateNode copyTo);
        public abstract void FromJSON(JSONNode json);

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray json = new JSONArray();
            var serializer = JSONSerialization.GetSerializer<T>();

            foreach (var item in _value)
                json.Add(serializer.toJSON(item));

            return json;
        }

        void IStateNode.Reset()
            => Reset();

        // IObserver<StateOperation>
        public IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}