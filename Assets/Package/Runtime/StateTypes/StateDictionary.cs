using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;

using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public interface IReadOnlyStateDictionary : IEnumerable, IStateNode, IDictionaryObservable
    {
        Type keyType { get; }
        Type valueType { get; }
        int count { get; }
        IStateNode this[object key] { get; }
        IEnumerable keys { get; }
        IEnumerable<IStateNode> values { get; }
        bool TryGetValue(object key, out IStateNode value);
    }

    public interface IStateDictionary : IReadOnlyStateDictionary
    {
        IStateNode Add(object key);
        bool Remove(object key);
        IStateNode GetOrAdd(object key);
        void Clear();
    }

    public interface IStateDictionaryViewMutator<TKey, TValue>
    {
        TValue Add(TKey key);
        bool Remove(TKey key);
        TValue GetOrAdd(TKey key);
        void Clear();
    }

    public class StateDictionaryView<TKey, TValue> : ReadOnlyStateDictionary<TKey, TValue> where TValue : IStateNode, new()
    {
        public override bool isView => true;
        private Mutator _mutator;
        private bool _viewInitialized;
        private IDisposable _subscription;

        private class Mutator : IStateDictionaryViewMutator<TKey, TValue>
        {
            public Func<TKey, TValue> add;
            public Func<TKey, bool> remove;
            public Func<TKey, TValue> getOrAdd;
            public Action clear;

            public TValue Add(TKey key)
                => add(key);

            public bool Remove(TKey key)
                => remove(key);

            public TValue GetOrAdd(TKey key)
                => getOrAdd(key);

            public void Clear()
                => clear();
        }

        public StateDictionaryView() : base()
        {
            _mutator = new Mutator()
            {
                add = AddChild,
                remove = RemoveChild,
                getOrAdd = GetOrAdd,
                clear = ClearInternal
            };
        }

        public void InitializeView(Action<IStateDictionaryViewMutator<TKey, TValue>> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public void InitializeView(Func<IStateDictionaryViewMutator<TKey, TValue>, IDisposable> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            _subscription = initialize(_mutator);
        }

        private TValue GetOrAdd(TKey key)
        {
            if (TryGetValue(key, out var value))
                return value;

            return AddChild(key);
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

            foreach (var child in GetValuesInternal())
                child.Reset();
        }

        protected override void DisposeInternal()
        {
            _subscription?.Dispose();
            base.DisposeInternal();
        }
    }

    public class StateDictionary<TKey, TValue> : ReadOnlyStateDictionary<TKey, TValue>, IStateDictionary where TValue : IStateNode, new()
    {
        public override bool isView => false;
        private Action<StateDictionary<TKey, TValue>> _initializer;

        public StateDictionary() : this(default) { }
        public StateDictionary(Action<StateDictionary<TKey, TValue>> initializer = default) : base()
        {
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            _initializer?.Invoke(this);
        }

        public TValue Add(TKey key)
            => AddChild(key);

        public bool Remove(TKey key)
            => RemoveChild(key);

        public TValue GetOrAdd(TKey key)
        {
            if (TryGetValue(key, out var value))
                return value;

            return AddChild(key);
        }

        public void Clear()
            => ClearInternal();

        public IStateNode GetOrAdd(object key)
            => GetOrAdd((TKey)key);

        public IStateNode Add(object key)
            => AddChild((TKey)key);

        bool IStateDictionary.Remove(object key)
            => RemoveChild((TKey)key);

        protected override void Reset()
        {
            logger.Generic(LogLevel.Trace, $"Resetting {nodePath}");
            Clear();
            _initializer?.Invoke(this);
        }

        public override void FromJSON(JSONNode json)
        {
            if (json == null)
            {
                Reset();
                return;
            }

            JSONObject dict = (JSONObject)json;
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            Reset();

            foreach (var value in dict)
                AddChild(serializer.fromJSON(value.Key)).FromJSON(value.Value);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateDictionary<TKey, TValue>)copyTo;

            var toRemove = target.keys.Except(keys).ToArray();

            foreach (var keyToRemove in toRemove)
                target.Remove(keyToRemove);

            foreach (var kvpToCopy in ElementsInternal())
            {
                if (!target.TryGetValue(kvpToCopy.Key, out var child))
                    child = target.Add(kvpToCopy.Key);

                kvpToCopy.Value.value.CopyTo(child);
            }
        }
    }

    public abstract class ReadOnlyStateDictionary<TKey, TValue> : ObservableDictionaryBase<TKey, TValue>,
        IReadOnlyStateDictionary,
        IEnumerable<KeyValuePair<TKey, TValue>>
        where TValue : IStateNode, new()
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public abstract bool isView { get; }

        public int count => GetCountInternal();
        public TValue this[TKey key] => GetValue(key);
        public IEnumerable<TKey> keys => GetKeysInternal();
        public IEnumerable<TValue> values => GetValuesInternal();

        public Type keyType => typeof(TKey);
        public Type valueType => typeof(TValue);

        IEnumerable IReadOnlyStateDictionary.keys => keys;
        IEnumerable<IStateNode> IReadOnlyStateDictionary.values => GetValuesInternal().Select(x => (IStateNode)x);

        IStateNode IReadOnlyStateDictionary.this[object key] => GetValue((TKey)key);

        int IStateNode.childCount => count;
        IEnumerable<IStateNode> IStateNode.children => GetValuesInternal().Select(x => (IStateNode)x);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => ElementsInternal().Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value.value)).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ElementsInternal().Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value.value)).GetEnumerator();

        public ReadOnlyStateDictionary() : base(null) { }

        protected virtual void InitializeInternal() { }

        protected override void SendOperation(IDictionaryObserver<TKey, TValue> observer, DictionaryOp<TKey, TValue> operation)
        {
            logger.Trace($"Notifying {Utility.FormatOperationLog(operation.isRemove ? OpType.Remove : OpType.Add, this, operation.key, operation.elementId, operation.value)}");
            base.SendOperation(observer, operation);
        }

        protected TValue AddChild(TKey key)
        {
            TValue value = new TValue();

            if (value is IKeyedStateNode<TKey> keyedNode)
                keyedNode.AssignKey(key);

            value.Initialize(this, key.ToString());
            value.PostInitialize();

            AddInternal(key, value);

            return value;
        }

        protected bool RemoveChild(TKey key)
        {
            if (!TryGetValueInternal(key, out var value))
                return false;

            value.Dispose();

            RemoveInternal(key);

            return true;
        }

        public bool ContainsKey(TKey key)
            => ContainsKeyInternal(key);

        public bool ContainsValue(TValue value)
            => ContainsValueInternal(value);

        public bool TryGetValue(TKey key, out TValue value)
            => TryGetValueInternal(key, out value);

        bool IReadOnlyStateDictionary.TryGetValue(object key, out IStateNode value)
        {
            if (TryGetValue((TKey)key, out var stateValue))
            {
                value = stateValue;
                return true;
            }

            value = default;
            return false;
        }

        protected abstract void Reset();
        protected override void DisposeInternal()
        {
            foreach (var child in GetValuesInternal())
                child.Dispose();
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

        void IStateNode.PostInitialize()
        {
            foreach (var child in GetValuesInternal())
                child.PostInitialize();
        }

        void IStateNode.Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
            foreach (var child in ElementsInternal())
                child.Value.value.Rename(child.Value.value.nodeName);
        }

        IStateNode IStateNode.GetChild(string name)
            => ElementsInternal().First(x => x.Key.ToString() == name).Value.value;

        bool IStateNode.TryGetChild(string name, out IStateNode child)
        {
            child = ElementsInternal().FirstOrDefault(x => x.Key.ToString() == name).Value.value;
            return child != null;
        }

        public abstract void CopyTo(IStateNode copyTo);
        public abstract void FromJSON(JSONNode json);

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONObject dict = new JSONObject();
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            foreach (var kvp in ElementsInternal().Where(x => filter(x.Value.value)))
                dict.Add(serializer.toJSON(kvp.Key), kvp.Value.value.ToJSON(filter));

            return dict;
        }

        void IStateNode.Reset()
            => Reset();

        // IObserver<StateOperation>
        public IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = kvp.Key, child = kvp.Value, elementId = id }),
                onRemove: (id, kvp) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = kvp.Key, child = kvp.Value, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}