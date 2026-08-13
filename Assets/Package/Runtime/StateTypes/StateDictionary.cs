using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;

using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public interface IStateDictionary : IEnumerable, IStateNode, ICollectionObservable
    {
        Type keyType { get; }
        Type valueType { get; }
        int Count { get; }
        IStateNode this[object key] { get; }
        IEnumerable keys { get; }
        IEnumerable<IStateNode> values { get; }
        IStateNode Add(object key);
        bool Remove(object key);
        bool TryGetValue(object key, out IStateNode value);
        IStateNode GetOrAdd(object key);
        void Clear();
    }

    public struct StateDictionaryOperation<TKey, TValue> : IStateOperation where TValue : IStateNode
    {
        public IStateNode source { get; set; }
        public OpType opType { get; set; }
        public TKey key { get; set; }
        public TValue value { get; set; }
        public uint elementId { get; set; }

        object IStateOperation.param => key;
        public IStateNode child => value;

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={key}";
        }
    }

    public interface IDerivedDictionaryAccess<TKey, TValue>
    {
        TValue Add(TKey key);
        bool Remove(TKey key);
        void Clear();
    }

    public class StateDictionary<TKey, TValue> : ObservableDictionaryBase<TKey, TValue>,
        IStateDictionary,
        IEnumerable<KeyValuePair<TKey, TValue>>
        where TValue : IStateNode, new()
    {
        private class DerivedDictionaryAccess : IDerivedDictionaryAccess<TKey, TValue>
        {
            public Func<TKey, TValue> add;
            public Func<TKey, bool> remove;
            public Action clear;

            public TValue Add(TKey key)
                => add(key);

            public bool Remove(TKey key)
                => remove(key);

            public void Clear()
                => clear();
        }

        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public bool derived => _deriveStream != null;

        public int Count => GetCountInternal();
        public TValue this[TKey key] => GetValue(key);
        public IEnumerable<TKey> keys => GetKeysInternal();
        public IEnumerable<TValue> values => GetValuesInternal();

        public Type keyType => typeof(TKey);
        public Type valueType => typeof(TValue);

        IEnumerable IStateDictionary.keys => keys;
        IEnumerable<IStateNode> IStateDictionary.values => GetValuesInternal().Select(x => (IStateNode)x);

        IStateNode IStateDictionary.this[object key] => GetValue((TKey)key);

        int IStateNode.childCount => Count;
        IEnumerable<IStateNode> IStateNode.children => GetValuesInternal().Select(x => (IStateNode)x);

        private IDisposable _deriveStream;

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => ElementsInternal().Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value.value)).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ElementsInternal().Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value.value)).GetEnumerator();

        private Action<StateDictionary<TKey, TValue>> _initializer;

        public StateDictionary() : this(default) { }

        public StateDictionary(Action<StateDictionary<TKey, TValue>> initializer) : base(null)
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

        void IStateNode.PostInitialize()
        {
            foreach (var child in GetValuesInternal())
                child.PostInitialize();
        }

        private TValue Add_NoCheck(TKey key)
        {
            TValue value = new TValue();

            if (value is IKeyedStateNode<TKey> keyedNode)
                keyedNode.AssignKey(key);

            value.Initialize(this, key.ToString());
            value.PostInitialize();

            AddInternal(key, value);

            return value;
        }

        private bool Remove_NoCheck(TKey key)
        {
            if (!TryGetValueInternal(key, out var value))
                return false;

            value.Dispose();

            RemoveInternal(key);

            return true;
        }

        public TValue Add(TKey key)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return Add_NoCheck(key);
        }

        public bool Remove(TKey key)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return Remove_NoCheck(key);
        }

        public bool ContainsKey(TKey key)
            => ContainsKeyInternal(key);

        public bool ContainsValue(TValue value)
            => ContainsValueInternal(value);

        public bool TryGetValue(TKey key, out TValue value)
            => TryGetValueInternal(key, out value);

        public TValue GetOrAdd(TKey key)
        {
            if (TryGetValue(key, out var value))
                return value;

            return Add(key);
        }

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            ClearInternal();
        }

        public void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");

            Clear();

            _initializer?.Invoke(this);
        }

        public void CopyTo(IStateNode copyTo)
        {
            var target = (StateDictionary<TKey, TValue>)copyTo;

            var toRemove = target.keys.Except(keys).ToArray();

            foreach (var keyToRemove in toRemove)
                target.Remove(keyToRemove);

            foreach (var kvpToCopy in ElementsInternal())
                kvpToCopy.Value.value.CopyTo(target.GetOrAdd(kvpToCopy.Key));
        }

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

            JSONObject dict = (JSONObject)json;
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            Clear();

            foreach (var value in dict)
                Add(serializer.fromJSON(value.Key)).FromJSON(value.Value);
        }

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONObject dict = new JSONObject();
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            foreach (var kvp in ElementsInternal().Where(x => filter(x.Value.value)))
                dict.Add(serializer.toJSON(kvp.Key), kvp.Value.value.ToJSON(filter));

            return dict;
        }

        protected override void DisposeInternal()
        {
            foreach (var child in GetValuesInternal())
                child.Dispose();

            _deriveStream?.Dispose();
        }

        public void Derive(Func<IDerivedDictionaryAccess<TKey, TValue>, IDisposable> derive)
        {
            _deriveStream = derive(new DerivedDictionaryAccess() { add = Add_NoCheck, remove = Remove_NoCheck, clear = ClearInternal });
        }

        public void Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
            foreach (var child in ElementsInternal())
                child.Value.value.Rename(child.Value.value.nodeName);
        }

        public IStateNode GetChild(string name)
            => ElementsInternal().First(x => x.Key.ToString() == name).Value.value;

        public bool TryGetChild(string name, out IStateNode child)
        {
            child = ElementsInternal().FirstOrDefault(x => x.Key.ToString() == name).Value.value;
            return child != null;
        }

        IStateNode IStateDictionary.Add(object key)
            => Add((TKey)key);

        bool IStateDictionary.Remove(object key)
            => Remove((TKey)key);

        bool IStateDictionary.TryGetValue(object key, out IStateNode value)
        {
            if (TryGetValue((TKey)key, out var stateValue))
            {
                value = stateValue;
                return true;
            }

            value = default;
            return false;
        }

        IStateNode IStateDictionary.GetOrAdd(object key)
            => GetOrAdd((TKey)key);

        public IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnNext(new StateDictionaryOperation<TKey, TValue>() { source = this, opType = OpType.Add, key = kvp.Key, value = kvp.Value, elementId = id }),
                onRemove: (id, kvp) => observer.OnNext(new StateDictionaryOperation<TKey, TValue>() { source = this, opType = OpType.Remove, key = kvp.Key, value = kvp.Value, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}