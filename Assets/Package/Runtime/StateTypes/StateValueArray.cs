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

    public class StateValueArray<T> : ObservableValueBase<IReadOnlyList<T>>,
        IStateValueArray,
        IValueObservable<IReadOnlyList<T>>,
        IEnumerable<T>
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public bool derived => _deriveSubscription != null;

        public int count => _value.Count;
        public T this[int index] => _value[index];

        int IStateNode.childCount => 0;
        IEnumerable<IStateNode> IStateNode.children => EmptyChildren();

        Type IStateValueArray.elementType => typeof(T);

        private IDisposable _deriveSubscription;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => _value.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _value.GetEnumerator();

        private Action<StateValueArray<T>> _initializer;

        public StateValueArray() : this(default(Action<StateValueArray<T>>)) { }

        public StateValueArray(T[] value) : this(x => x.SetValue(value)) { }
        public StateValueArray(Action<StateValueArray<T>> initializer) : base(null)
        {
            SetValueInternal(new T[0]);
            _initializer = initializer;
        }

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
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

        public void SetValue(T[] value)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            SetValueInternal(value);
        }

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            SetValueInternal(new T[0]);
        }

        public void CopyTo(IStateNode copyTo)
        {
            var target = (StateValueArray<T>)copyTo;
            target.SetValue(_value.ToArray());
        }

        public void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            SetValueInternal(new T[0]);
            _initializer?.Invoke(this);

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        protected override void DisposeInternal()
        {
            _deriveSubscription?.Dispose();
        }

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray json = new JSONArray();
            var serializer = JSONSerialization.GetSerializer<T>();

            foreach (var item in _value)
                json.Add(serializer.toJSON(item));

            return json;
        }

        public void FromJSON(JSONNode json)
        {
            if (derived)
            {
                logger.Warning($"Attempted to write to derived state from JSON. This will be ignored. Path: {nodePath}");
                return;
            }

            var serializer = JSONSerialization.GetSerializer<T>();
            SetValueInternal(((JSONArray)json).Linq.Select(x => serializer.fromJSON(x)).ToArray());
        }

        public void Derive(IDisposable subscription)
        {
            _deriveSubscription = subscription;
        }

        public void Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
        }

        public IStateNode GetChild(string name)
            => throw new NotImplementedException();

        public bool TryGetChild(string name, out IStateNode child)
        {
            child = default;
            return false;
        }

        void IStateValueArray.SetValue(IEnumerable values)
            => SetValue(values.Cast<T>().ToArray());

        public IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnNext(new StateValueOperation<IReadOnlyList<T>>() { source = this, value = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}