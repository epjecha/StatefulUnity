using System;
using System.Collections.Generic;

using ObserveThing;
using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public interface IReadOnlyStateValue : IStateNode, IValueObservable
    {
        object value { get; }
        Type valueType { get; }
    }

    public interface IStateValue : IReadOnlyStateValue
    {
        new object value { get; set; }

        object IReadOnlyStateValue.value => value;
    }

    public interface IStateValueViewMutator<T>
    {
        T value { get; set; }
    }

    public class StateValueView<T> : ReadOnlyStateValue<T>
    {
        public override bool isView => true;
        private Mutator _mutator;
        private bool _viewInitialized;
        private IDisposable _subscription;

        private class Mutator : IStateValueViewMutator<T>
        {
            public Func<T> get;
            public Action<T> set;

            public T value
            {
                get => get();
                set => set(value);
            }
        }

        public StateValueView()
        {
            _mutator = new Mutator() { get = () => _value, set = SetValueInternal };
        }

        public void InitializeView(Action<IStateValueViewMutator<T>> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public void InitializeView(Func<IStateValueViewMutator<T>, IDisposable> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            _subscription = initialize(_mutator);
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
            _subscription?.Dispose();
            base.DisposeInternal();
        }
    }

    public class StateValue<T> : ReadOnlyStateValue<T>
    {
        public override bool isView => false;

        new public T value
        {
            get => _value;
            set => SetValueInternal(value);
        }

        private Action<StateValue<T>> _initializer;

        public StateValue() : this(default) { }
        public StateValue(Action<StateValue<T>> initializer) : base()
        {
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            _initializer?.Invoke(this);
        }

        public override void Reset()
        {
            logger.Generic(LogLevel.Trace, $"Resetting {nodePath}");
            value = default;
            _initializer?.Invoke(this);
        }

        public override void FromJSON(JSONNode json)
        {
            if (json == null)
            {
                Reset();
                return;
            }

            SetValueInternal(JSONSerialization.FromJSON<T>(json));
        }

        public override void CopyTo(IStateNode copyTo)
            => ((StateValue<T>)copyTo).SetValueInternal(value);
    }

    public abstract class ReadOnlyStateValue<T> : ObservableValueBase<T>,
        IReadOnlyStateValue,
        IValueObservable<T>
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public IStateNode root { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public abstract bool isView { get; }

        public T value => _value;
        public Type valueType => typeof(T);

        object IReadOnlyStateValue.value => value;

        int IStateNode.childCount => 0;
        IEnumerable<IStateNode> IStateNode.children => EmptyChildren();

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        public ReadOnlyStateValue() : base(default) { }

        protected virtual void InitializeInternal() { }

        protected override void SendOperation(IValueObserver<T> observer, ValueOp<T> operation)
        {
            logger.Trace($"Notifying {Utility.FormatOperationLog(OpType.Set, this, operation.value)}");
            base.SendOperation(observer, operation);
        }

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

        void IStateNode.Rename(string name)
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
            => JSONSerialization.ToJSON(value);

        public abstract void Reset();

        // IObserver<StateOperation>
        public IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<T>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}