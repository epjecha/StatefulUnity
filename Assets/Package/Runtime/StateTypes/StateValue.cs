using System;
using System.Collections.Generic;

using ObserveThing;
using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public struct StateValueOperation<T> : IStateOperation
    {
        public IStateNode source { get; set; }
        public OpType opType => OpType.Set;
        public T value { get; set; }

        uint IStateOperation.elementId => 0;
        object IStateOperation.param => value;
        public IStateNode child => null;

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={value}";
        }
    }

    public interface IStateValue : IStateNode, IValueObservable
    {
        object value { get; set; }
        Type valueType { get; }
    }

    public class StateValue<T> : ObservableValueBase<T>,
        IStateValue,
        IValueObservable<T>
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public IStateNode root { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public bool derived => _deriveStream != null;

        public T value
        {
            get => _value;
            set
            {
                if (derived)
                    throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

                SetValueInternal(value);
            }
        }

        public Type valueType => typeof(T);

        object IStateValue.value { get => value; set => this.value = (T)value; }

        int IStateNode.childCount => 0;
        IEnumerable<IStateNode> IStateNode.children => EmptyChildren();

        private IDisposable _deriveStream;

        private Action<StateValue<T>> _initializer;

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        public StateValue() : this(default(Action<StateValue<T>>)) { }

        public StateValue(T value) : this(x => x.value = value) { }
        public StateValue(Action<StateValue<T>> initializer) : base(null)
        {
            _initializer = initializer;
        }

        public void Initialize(ObservationContext context, ILogger logger, string name = "root")
        {
            this.context = context;
            root = this;
            this.logger = logger;
            nodeName = name;
            nodePath = name;
            _initializer?.Invoke(this);
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
            _initializer?.Invoke(this);
            initialized = true;
        }

        void IStateNode.PostInitialize() { }

        public void CopyTo(IStateNode copyTo)
        {
            ((StateValue<T>)copyTo).value = value;
        }

        public void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            value = default;
            _initializer?.Invoke(this);

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
            => JSONSerialization.ToJSON(value);

        public void FromJSON(JSONNode json)
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

            value = JSONSerialization.FromJSON<T>(json);
        }

        protected override void DisposeInternal()
        {
            _deriveStream?.Dispose();
        }

        public void Derive(IValueObservable<T> source)
        {
            _deriveStream = source.Subscribe(
                x => value = x,
                immediate: true
            );
        }

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

        public IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<T>(
                onNext: x => observer.OnNext(new StateValueOperation<T>() { source = this, value = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}