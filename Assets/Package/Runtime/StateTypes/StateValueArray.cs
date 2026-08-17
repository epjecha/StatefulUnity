using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FofX.Serialization;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateValueArray : IReadOnlyStateValueArray
    {
        void SetValue(IEnumerable values);
        void Clear();
    }

    public interface IReadOnlyStateValueArray : IStateNode, IEnumerable, IValueObservable
    {
        int count { get; }
        Type elementType { get; }
        object this[int index] { get; }
    }

    public class StateValueArrayView<T> : ReadOnlyStateValueArray<T>
    {
        public override bool isView => true;

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
    }

    public class StateValueArray<T> : ReadOnlyStateValueArray<T>, IStateValueArray
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

        public void SetValue(T[] value)
            => SetValueInternal(value);

        public void Clear()
            => SetValueInternal(new T[0]);

        void IStateValueArray.SetValue(IEnumerable values)
            => SetValue(values.Cast<T>().ToArray());

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateValueArray<T>)copyTo;
            target.SetValue(_value.ToArray());
        }

        protected override void Reset()
        {
            SetValueInternal(new T[0]);
            _initializer?.Invoke(this);
            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public override void FromJSON(JSONNode json)
        {
            var serializer = JSONSerialization.GetSerializer<T>();
            SetValueInternal(((JSONArray)json).Linq.Select(x => serializer.fromJSON(x)).ToArray());
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

        protected virtual void InitializeInternal() { }

        void IStateNode.PostInitialize() { }

        protected override void SendOperation(IValueObserver<IReadOnlyList<T>> observer, ValueOp<IReadOnlyList<T>> operation)
        {
            logger.Trace($"Notifying {Utility.FormatOperationLog(OpType.Set, this, $"[{string.Join(", ", operation.value)}]")}");
            base.SendOperation(observer, operation);
        }

        public abstract void CopyTo(IStateNode copyTo);

        protected abstract void Reset();

        void IStateNode.Reset()
            => Reset();

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray json = new JSONArray();
            var serializer = JSONSerialization.GetSerializer<T>();

            foreach (var item in _value)
                json.Add(serializer.toJSON(item));

            return json;
        }

        public abstract void FromJSON(JSONNode json);

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

        public IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<IReadOnlyList<T>>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}